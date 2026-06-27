using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Module.Catalog;

/// <summary>
/// SQLite persistence for the canonical catalog. Implements <see cref="ICatalogStore"/> so the
/// web-free <see cref="CatalogSyncService"/> can populate it. Shares <c>queue.db</c> with
/// <see cref="QueueRepository"/> (the catalog tables are created by migration 012).
/// </summary>
class CatalogRepository : ICatalogStore
{
    private string _connStr = "Data Source=queue.db";

    public void Configure(string? connStr)
    {
        if (!string.IsNullOrEmpty(connStr)) _connStr = connStr;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<long> UpsertSystemAsync(string datName, string console, string source, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_system (dat_name, console, source) VALUES ($n, $c, $s)
            ON CONFLICT(dat_name) DO UPDATE SET console = $c, source = $s
            RETURNING id
            """;
        cmd.Parameters.AddWithValue("$n", datName);
        cmd.Parameters.AddWithValue("$c", console);
        cmd.Parameters.AddWithValue("$s", source);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task MergeSystemGamesAsync(long systemId, string origin, IReadOnlyList<DatGame> games, string? datVersion, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);

        // Snapshot the system's current games: the content key → row map is the dedup anchor (a keyed
        // incoming game whose key already exists MERGES onto that row), and the set of rows THIS origin
        // currently sources lets us drop only this origin's stale games on a re-sync (never another's).
        var keyedExisting = new Dictionary<string, long>(StringComparer.Ordinal);
        var originGameIds = new HashSet<long>();
        await using (var sel = db.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = """
                SELECT g.id, g.canonical_key,
                       EXISTS(SELECT 1 FROM catalog_game_source s WHERE s.game_id = g.id AND s.origin = $origin) AS mine
                FROM catalog_game g WHERE g.system_id = $sid
                """;
            sel.Parameters.AddWithValue("$sid", systemId);
            sel.Parameters.AddWithValue("$origin", origin);
            await using var r = await sel.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var gid = r.GetInt64(0);
                if (!r.IsDBNull(1)) keyedExisting[r.GetString(1)] = gid;
                if (r.GetInt32(2) != 0) originGameIds.Add(gid);
            }
        }

        var seen = new HashSet<long>();
        await using (var ig = db.CreateCommand())
        await using (var ir = db.CreateCommand())
        await using (var isrc = db.CreateCommand())
        {
            ig.Transaction = tx;
            ig.CommandText = """
                INSERT INTO catalog_game (system_id, name, region, serial, serial_key, languages, title_key, is_parent, canonical_key)
                VALUES ($sid, $name, $region, $serial, $skey, $langs, $tkey, 0, $ckey) RETURNING id
                """;
            ig.Parameters.AddWithValue("$sid", systemId);
            var gName = ig.Parameters.Add("$name", SqliteType.Text);
            var gRegion = ig.Parameters.Add("$region", SqliteType.Text);
            var gSerial = ig.Parameters.Add("$serial", SqliteType.Text);
            var gSkey = ig.Parameters.Add("$skey", SqliteType.Text);
            var gLangs = ig.Parameters.Add("$langs", SqliteType.Text);
            var gTkey = ig.Parameters.Add("$tkey", SqliteType.Text);
            var gCkey = ig.Parameters.Add("$ckey", SqliteType.Text);

            ir.Transaction = tx;
            ir.CommandText = """
                INSERT INTO catalog_rom (game_id, name, size, crc, md5, sha1)
                VALUES ($gid, $name, $size, $crc, $md5, $sha1)
                """;
            var rGid = ir.Parameters.Add("$gid", SqliteType.Integer);
            var rName = ir.Parameters.Add("$name", SqliteType.Text);
            var rSize = ir.Parameters.Add("$size", SqliteType.Integer);
            var rCrc = ir.Parameters.Add("$crc", SqliteType.Text);
            var rMd5 = ir.Parameters.Add("$md5", SqliteType.Text);
            var rSha1 = ir.Parameters.Add("$sha1", SqliteType.Text);

            // Bind (or refresh) this origin onto the game — idempotent on the (game_id, origin) unique.
            isrc.Transaction = tx;
            isrc.CommandText = """
                INSERT INTO catalog_game_source (game_id, origin, dat_version) VALUES ($gid, $origin, $ver)
                ON CONFLICT(game_id, origin) DO UPDATE SET dat_version = excluded.dat_version
                """;
            var sGid = isrc.Parameters.Add("$gid", SqliteType.Integer);
            isrc.Parameters.AddWithValue("$origin", origin);
            var sVer = isrc.Parameters.Add("$ver", SqliteType.Text);

            foreach (var g in games)
            {
                // Cross-source content identity (D2 / #129): hash-derived, name-independent. A keyed game
                // already in the system is the SAME game from another origin → merge onto it; an unkeyed
                // game (CRC-only/empty) can't be deduped, so it's always inserted (stale prior copies from
                // this origin are pruned below) — accepting some row churn for these rare entries.
                var key = CanonicalKey.Compute(g.Roms);
                long gid;
                if (key is not null && keyedExisting.TryGetValue(key, out var existing))
                {
                    gid = existing;   // merge: keep the existing row + roms, just bind this origin
                }
                else
                {
                    gName.Value = g.Name;
                    gRegion.Value = (object?)g.Region ?? DBNull.Value;
                    gSerial.Value = (object?)g.Serial ?? DBNull.Value;
                    // Normalize with the SAME function the compat sources use (CompatKeys.NormalizeSerial),
                    // so catalog_game.serial_key and a serial-keyed catalog_compat.match_key join symmetrically (#48).
                    gSkey.Value = string.IsNullOrEmpty(g.Serial)
                        ? DBNull.Value
                        : CompatKeys.NormalizeSerial(g.Serial);
                    gLangs.Value = g.Languages.Count > 0 ? string.Join(',', g.Languages) : DBNull.Value;
                    gTkey.Value = Dedup.TitleKey(g.Name);   // is_parent is set in the 1G1R post-pass below
                    gCkey.Value = (object?)key ?? DBNull.Value;
                    gid = Convert.ToInt64(await ig.ExecuteScalarAsync(ct));
                    if (key is not null) keyedExisting[key] = gid;   // dedup repeats within this same DAT too

                    foreach (var rom in g.Roms)
                    {
                        rGid.Value = gid;
                        rName.Value = rom.Name;
                        rSize.Value = rom.Size;
                        rCrc.Value = (object?)rom.Crc ?? DBNull.Value;
                        rMd5.Value = (object?)rom.Md5 ?? DBNull.Value;
                        rSha1.Value = (object?)rom.Sha1 ?? DBNull.Value;
                        await ir.ExecuteNonQueryAsync(ct);
                    }
                }

                sGid.Value = gid;
                sVer.Value = (object?)datVersion ?? DBNull.Value;
                await isrc.ExecuteNonQueryAsync(ct);
                seen.Add(gid);
            }
        }

        // Drop this origin from games it no longer lists; then delete any game left with no origins at
        // all (gone from every source). Scoped to this system + this origin — other origins' games and
        // other systems' (not-yet-resynced) games are untouched.
        var stale = originGameIds.Where(id => !seen.Contains(id)).ToList();
        if (stale.Count > 0)
        {
            await using var dropSrc = db.CreateCommand();
            dropSrc.Transaction = tx;
            dropSrc.CommandText = "DELETE FROM catalog_game_source WHERE game_id = $gid AND origin = $origin";
            dropSrc.Parameters.AddWithValue("$origin", origin);
            var dGid = dropSrc.Parameters.Add("$gid", SqliteType.Integer);
            foreach (var id in stale) { dGid.Value = id; await dropSrc.ExecuteNonQueryAsync(ct); }
        }
        await using (var orphan = db.CreateCommand())
        {
            orphan.Transaction = tx;
            orphan.CommandText = """
                DELETE FROM catalog_rom WHERE game_id IN (
                    SELECT id FROM catalog_game g WHERE g.system_id = $sid
                      AND NOT EXISTS(SELECT 1 FROM catalog_game_source s WHERE s.game_id = g.id));
                DELETE FROM catalog_game WHERE system_id = $sid
                      AND NOT EXISTS(SELECT 1 FROM catalog_game_source s WHERE s.game_id = catalog_game.id);
                """;
            orphan.Parameters.AddWithValue("$sid", systemId);
            await orphan.ExecuteNonQueryAsync(ct);
        }

        await RecomputeParentsAsync(db, tx, systemId, ct);

        await using (var upd = db.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE catalog_system
                SET dat_version = $v,
                    game_count = (SELECT COUNT(*) FROM catalog_game WHERE system_id = $sid),
                    synced_at = datetime('now')
                WHERE id = $sid
                """;
            upd.Parameters.AddWithValue("$v", (object?)datVersion ?? DBNull.Value);
            upd.Parameters.AddWithValue("$sid", systemId);
            await upd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Recompute 1G1R parent selection for every game now in the system: group by title key and mark one
    /// variant per group <c>is_parent</c>. Runs after a merge so newly-added (or removed) variants from a
    /// second origin are reflected. Title keys are stable per name, so only <c>is_parent</c> moves.
    /// </summary>
    private static async Task RecomputeParentsAsync(SqliteConnection db, SqliteTransaction tx, long systemId, CancellationToken ct)
    {
        var ids = new List<long>();
        var variants = new List<(string Name, string? Region)>();
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        await using (var sel = db.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT id, name, region FROM catalog_game WHERE system_id = $sid";
            sel.Parameters.AddWithValue("$sid", systemId);
            await using var r = await sel.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                ids.Add(r.GetInt64(0));
                variants.Add((r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
                var key = Dedup.TitleKey(r.GetString(1));
                if (!groups.TryGetValue(key, out var members)) groups[key] = members = [];
                members.Add(ids.Count - 1);
            }
        }

        var isParent = new bool[ids.Count];
        foreach (var members in groups.Values)
            isParent[members[Dedup.SelectParent(members.Select(i => variants[i]).ToList())]] = true;

        await using var upd = db.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE catalog_game SET is_parent = $p WHERE id = $id";
        var pP = upd.Parameters.Add("$p", SqliteType.Integer);
        var pId = upd.Parameters.Add("$id", SqliteType.Integer);
        for (int i = 0; i < ids.Count; i++)
        {
            pP.Value = isParent[i] ? 1 : 0;
            pId.Value = ids[i];
            await upd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<List<CatalogSystemStatus>> GetSystemsAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT dat_name, console, source, dat_version, game_count, synced_at
            FROM catalog_system ORDER BY console
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CatalogSystemStatus>();
        while (await r.ReadAsync())
            list.Add(new CatalogSystemStatus(
                r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    public async Task<List<CatalogConsole>> GetConsolesAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        // Total from the cheap stored count; owned via the small catalog_owned table.
        cmd.CommandText = """
            SELECT s.console, SUM(s.game_count) AS total,
                   (SELECT COUNT(*) FROM catalog_owned o
                      JOIN catalog_game g ON g.id = o.game_id
                      JOIN catalog_system s2 ON s2.id = g.system_id
                      WHERE s2.console = s.console) AS owned
            FROM catalog_system s GROUP BY s.console ORDER BY s.console
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CatalogConsole>();
        while (await r.ReadAsync())
            list.Add(new CatalogConsole(r.GetString(0), r.GetInt32(1), r.GetInt32(2)));
        return list;
    }

    /// <summary>All games as (id, console, name) — the key set for matching local files.</summary>
    public async Task<List<(long Id, string Console, string Name)>> GetGameKeysAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT g.id, s.console, g.name FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id";
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<(long, string, string)>();
        while (await r.ReadAsync())
            list.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    /// <summary>Replace the owned set wholesale (game_id → local filepath).</summary>
    public async Task ReplaceOwnedAsync(IReadOnlyDictionary<long, string> owned, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);

        await using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM catalog_owned";
            await del.ExecuteNonQueryAsync(ct);
        }
        await using (var ins = db.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR REPLACE INTO catalog_owned (game_id, filepath, matched_at) VALUES ($g, $p, datetime('now'))";
            var pg = ins.Parameters.Add("$g", SqliteType.Integer);
            var pp = ins.Parameters.Add("$p", SqliteType.Text);
            foreach (var (gid, path) in owned)
            {
                pg.Value = gid;
                pp.Value = path;
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Replace an emulator's compatibility entries wholesale (normalized match key → status). The
    /// <paramref name="matchKind"/> (serial | title_id | name) records how these keys join to a catalog
    /// game, so emulators keyed differently can coexist in the one table. <paramref name="consoles"/> is
    /// the CSV of consoles a name-keyed emulator targets — the name join is gated to them because titles
    /// collide across consoles (null for serial-keyed emulators, whose keys are globally unique).
    /// </summary>
    public async Task ReplaceCompatAsync(string emulator, string matchKind, IReadOnlyList<CompatEntry> entries,
        CancellationToken ct, string? consoles = null)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);

        await using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM catalog_compat WHERE emulator = $e";
            del.Parameters.AddWithValue("$e", emulator);
            await del.ExecuteNonQueryAsync(ct);
        }
        await using (var ins = db.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR REPLACE INTO catalog_compat (emulator, match_kind, match_key, status, consoles) VALUES ($e, $k, $mk, $st, $cons)";
            ins.Parameters.AddWithValue("$e", emulator);
            ins.Parameters.AddWithValue("$k", matchKind);
            ins.Parameters.AddWithValue("$cons", (object?)consoles ?? DBNull.Value);
            var pmk = ins.Parameters.Add("$mk", SqliteType.Text);
            var pst = ins.Parameters.Add("$st", SqliteType.Text);
            foreach (var (matchKey, status) in entries)
            {
                pmk.Value = matchKey;
                pst.Value = status;
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Hash-based owned detection (Phase C / C3): mark each matched game owned + verified, recording
    /// which hash confirmed it. Name-independent — a match may add a game that name-scanning missed.
    /// Clears <c>verified</c> on all owned rows first so the flag means "hash-confirmed this run"
    /// (games we couldn't confirm — archives, mismatches — fall back to verified=0). Upsert by game_id.
    /// </summary>
    public async Task MarkOwnedByHashAsync(IReadOnlyDictionary<long, (string Path, string Hash)> matched, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);

        await using (var reset = db.CreateCommand())
        {
            reset.Transaction = tx;
            reset.CommandText = "UPDATE catalog_owned SET verified = 0, verified_hash = NULL";
            await reset.ExecuteNonQueryAsync(ct);
        }
        await using (var ins = db.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO catalog_owned (game_id, filepath, matched_at, verified, verified_hash)
                VALUES ($g, $p, datetime('now'), 1, $h)
                ON CONFLICT(game_id) DO UPDATE SET
                    filepath = excluded.filepath, matched_at = excluded.matched_at,
                    verified = 1, verified_hash = excluded.verified_hash
                """;
            var pg = ins.Parameters.Add("$g", SqliteType.Integer);
            var pp = ins.Parameters.Add("$p", SqliteType.Text);
            var ph = ins.Parameters.Add("$h", SqliteType.Text);
            foreach (var (gid, (path, hash)) in matched)
            {
                pg.Value = gid;
                pp.Value = path;
                ph.Value = hash;
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    // --- Local import (catalog hash lookup + single-game owned; epic #118) ---

    /// <summary>A catalog game matched to a local file by content hash, plus which hash confirmed it.</summary>
    public sealed record CatalogHashMatch(long GameId, string Console, string MatchKind);

    /// <summary>
    /// Find the catalog game whose <c>catalog_rom</c> carries this file's hash, searched across <b>all</b>
    /// systems (import doesn't know the console up front) with SHA1 → MD5 → CRC32 precedence. Matching is
    /// case-insensitive because DAT hash case isn't normalized at store time; both case forms are passed so
    /// the SHA1 index is still used (hex within one source is uniformly cased). Null when no ROM matches.
    /// </summary>
    public async Task<CatalogHashMatch?> FindGameByHashAsync(FileHashes.Hashes h, CancellationToken ct)
    {
        if (await LookupByHashAsync("sha1", h.Sha1, ct) is { } a) return new CatalogHashMatch(a.GameId, a.Console, "sha1");
        if (await LookupByHashAsync("md5", h.Md5, ct) is { } b) return new CatalogHashMatch(b.GameId, b.Console, "md5");
        if (await LookupByHashAsync("crc", h.Crc, ct) is { } c) return new CatalogHashMatch(c.GameId, c.Console, "crc");
        return null;
    }

    /// <summary>One indexed hash lookup. <paramref name="column"/> is an internal literal (sha1/md5/crc), never user input.</summary>
    private async Task<(long GameId, string Console)?> LookupByHashAsync(string column, string? hash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"""
            SELECT g.id, s.console
            FROM catalog_rom r
            JOIN catalog_game g ON g.id = r.game_id
            JOIN catalog_system s ON s.id = g.system_id
            WHERE r.{column} IN ($lo, $hi)
            LIMIT 1
            """;
        var v = hash.Trim();
        cmd.Parameters.AddWithValue("$lo", v.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$hi", v.ToUpperInvariant());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return (r.GetInt64(0), r.GetString(1));
    }

    /// <summary>
    /// Mark a single game owned + hash-verified at <paramref name="filepath"/> (import placed it there),
    /// recording the hash that confirmed it. Upsert by game_id — re-importing refreshes the path; other
    /// owned rows are untouched (unlike the verify pass, which rebuilds the verified set wholesale).
    /// </summary>
    public async Task MarkOwnedAsync(long gameId, string filepath, string matchKind, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_owned (game_id, filepath, matched_at, verified, verified_hash)
            VALUES ($g, $p, datetime('now'), 1, $h)
            ON CONFLICT(game_id) DO UPDATE SET
                filepath = excluded.filepath, matched_at = excluded.matched_at,
                verified = 1, verified_hash = excluded.verified_hash
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$p", filepath);
        cmd.Parameters.AddWithValue("$h", matchKind);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // --- Vimm binding (catalog <-> Vimm by hash; migration 021) ---

    /// <summary>Per-console hash -> game_id maps from catalog_rom (lowercased) for in-memory Vimm matching.</summary>
    public sealed record VimmHashIndex(
        Dictionary<string, long> BySha1,
        Dictionary<string, long> ByMd5,
        Dictionary<string, long> ByCrc);

    /// <summary>One Vimm download format to persist for a bound game.</summary>
    public sealed record VimmFormatRow(int Alt, string Label, long SizeBytes, string? SizeText);

    /// <summary>
    /// Load the console's catalog ROM hashes as hash -> game_id lookups (CRC32/MD5/SHA1, lowercased)
    /// so the scrape can match a Vimm entry's hashes in memory without a query per title. On a hash
    /// shared by multiple ROMs the first wins (acceptable — ROM hashes are effectively unique).
    /// </summary>
    public async Task<VimmHashIndex> GetVimmHashIndexAsync(string console, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT r.crc, r.md5, r.sha1, r.game_id
            FROM catalog_rom r
            JOIN catalog_game g ON g.id = r.game_id
            JOIN catalog_system s ON s.id = g.system_id
            WHERE s.console = $console
            """;
        cmd.Parameters.AddWithValue("$console", console);
        var bySha1 = new Dictionary<string, long>();
        var byMd5 = new Dictionary<string, long>();
        var byCrc = new Dictionary<string, long>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var gid = rd.GetInt64(3);
            AddHash(byCrc, rd, 0, gid);
            AddHash(byMd5, rd, 1, gid);
            AddHash(bySha1, rd, 2, gid);
        }
        return new VimmHashIndex(bySha1, byMd5, byCrc);

        static void AddHash(Dictionary<string, long> map, System.Data.Common.DbDataReader rd, int col, long gid)
        {
            if (rd.IsDBNull(col)) return;
            var h = rd.GetString(col).Trim().ToLowerInvariant();
            if (h.Length > 0) map.TryAdd(h, gid);
        }
    }

    /// <summary>
    /// Bind a catalog game to a Vimm vault entry: set <c>vault_id</c> + the match kind
    /// (sha1/md5/crc) and replace its available formats. Idempotent — re-binding replaces the prior
    /// formats so a re-scrape stays clean.
    /// </summary>
    public async Task BindVimmAsync(long gameId, long vaultId, string matchKind,
        IReadOnlyList<VimmFormatRow> formats, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);
        await using (var upd = db.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE catalog_game SET vault_id = $v, vimm_match = $m WHERE id = $g";
            upd.Parameters.AddWithValue("$v", vaultId);
            upd.Parameters.AddWithValue("$m", matchKind);
            upd.Parameters.AddWithValue("$g", gameId);
            await upd.ExecuteNonQueryAsync(ct);
        }
        await using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM catalog_vimm_format WHERE game_id = $g";
            del.Parameters.AddWithValue("$g", gameId);
            await del.ExecuteNonQueryAsync(ct);
        }
        if (formats.Count > 0)
        {
            await using var ins = db.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO catalog_vimm_format (game_id, alt, label, size_bytes, size_text) VALUES ($g,$a,$l,$b,$t)";
            var pg = ins.Parameters.Add("$g", SqliteType.Integer);
            var pa = ins.Parameters.Add("$a", SqliteType.Integer);
            var pl = ins.Parameters.Add("$l", SqliteType.Text);
            var pb = ins.Parameters.Add("$b", SqliteType.Integer);
            var pt = ins.Parameters.Add("$t", SqliteType.Text);
            foreach (var f in formats)
            {
                pg.Value = gameId;
                pa.Value = f.Alt;
                pl.Value = f.Label;
                pb.Value = f.SizeBytes;
                pt.Value = (object?)f.SizeText ?? DBNull.Value;
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// After scraping a console, flag its still-unbound games (vault_id IS NULL, vimm_match IS NULL)
    /// as <c>'none'</c> so the UI can badge "no Vimm match". Returns how many were flagged.
    /// </summary>
    public async Task<int> MarkVimmUnmatchedAsync(string console, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE catalog_game SET vimm_match = 'none'
            WHERE vault_id IS NULL AND vimm_match IS NULL AND system_id IN (
                SELECT id FROM catalog_system WHERE console = $console
            )
            """;
        cmd.Parameters.AddWithValue("$console", console);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Paged game list, filtered by console and/or a case-insensitive name substring, and by
    /// local availability (<paramref name="local"/> = all | owned | remote). Each row carries an
    /// <c>owned</c> flag, its per-emulator <c>compat</c> statuses, and a <c>verified</c> result.
    /// Optionally filtered to games with a compat entry for <paramref name="emulator"/> (and, when
    /// given, that emulator's <paramref name="compatStatus"/>).
    /// </summary>
    public async Task<(int Total, List<CatalogGameDto> Games)> GetGamesAsync(
        string? console, string? query, string local, bool dedupe, bool english, bool excludeCategories,
        string searchMode, int page, int pageSize, string? emulator = null, string? compatStatus = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(0, page);
        local = local is "owned" or "remote" ? local : "all";
        searchMode = searchMode is "glob" or "regex" ? searchMode : "substring";
        var q = query?.Trim();
        bool hasQuery = !string.IsNullOrEmpty(q);
        var emu = string.IsNullOrWhiteSpace(emulator) ? null : emulator.Trim();
        var compatFilterStatus = string.IsNullOrWhiteSpace(compatStatus) ? null : compatStatus.Trim();

        // English-only (E3a): keep rows whose language list contains "en", or whose region — or the
        // region tag embedded in the name when region is empty — is Western. Built from
        // Dedup.EnglishRegionTokens so the SQL stays single-sourced with Dedup.IsEnglish. Region tags
        // are matched at a tag boundary (mirrors Dedup.HasRegionToken): the parenthesis/comma delimiters
        // are replaced with spaces and the token matched space-bounded, so the 2-letter "uk" no longer
        // substring-matches inside a name like "Sukeban" when the region column is empty (#197).
        const string engHaystack =
            "' ' || replace(replace(replace(lower(coalesce(g.region, g.name)), '(', ' '), ')', ' '), ',', ' ') || ' '";
        var englishClause = english
            ? " AND (instr(lower(coalesce(g.languages, '')), 'en') > 0"
              + string.Concat(Enumerable.Range(0, Dedup.EnglishRegionTokens.Length)
                    .Select(i => $" OR instr({engHaystack}, ' ' || $eng{i} || ' ') > 0"))
              + ")"
            : "";

        // Hide demos/protos (E3a): drop rows whose name carries a non-final category tag. Built from
        // Dedup.ExcludedCategoryTags — the same list Dedup.IsExcludedVariant uses.
        var categoryClause = excludeCategories
            ? " AND NOT ("
              + string.Join(" OR ", Enumerable.Range(0, Dedup.ExcludedCategoryTags.Length)
                    .Select(i => $"instr(lower(g.name), $cat{i}) > 0"))
              + ")"
            : "";

        // Name search (E3b): substring (LIKE %q%, unchanged), glob (*,? → LIKE %,_ with ESCAPE), or
        // regex (evaluated in C# below — SQLite has no REGEXP). Empty query → no name filter.
        string? nameParam = null;
        var nameClause = "";
        if (hasQuery && searchMode == "substring") { nameParam = "%" + q + "%"; nameClause = " AND g.name LIKE $name"; }
        else if (hasQuery && searchMode == "glob") { nameParam = GlobToLike(q!); nameClause = " AND g.name LIKE $name ESCAPE '\\'"; }

        // Emulator/status filter: keep rows with a compat entry for the chosen emulator (and, when
        // given, that exact status). Joins via the same CompatMatch fragment as the projection.
        var compatClause = emu is null
            ? ""
            : $" AND EXISTS(SELECT 1 FROM catalog_compat c WHERE ({CompatMatch}) AND c.emulator = $emu"
              + (compatFilterStatus is null ? "" : " AND c.status = $cstatus") + ")";

        var filterWhere = $"""
            WHERE ($console IS NULL OR s.console = $console)
              AND ($local = 'all'
                   OR ($local = 'owned'  AND     EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id))
                   OR ($local = 'remote' AND NOT EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id)))
              AND ($dedupe = 0 OR g.is_parent = 1){englishClause}{categoryClause}{compatClause}
            """;

        // Bind every WHERE parameter; the optional english/category/name/compat params only when present.
        void BindFilters(SqliteCommand cmd)
        {
            cmd.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$local", local);
            cmd.Parameters.AddWithValue("$dedupe", dedupe ? 1 : 0);
            if (nameParam != null) cmd.Parameters.AddWithValue("$name", nameParam);
            if (english)
                for (int i = 0; i < Dedup.EnglishRegionTokens.Length; i++)
                    cmd.Parameters.AddWithValue($"$eng{i}", Dedup.EnglishRegionTokens[i]);
            if (excludeCategories)
                for (int i = 0; i < Dedup.ExcludedCategoryTags.Length; i++)
                    cmd.Parameters.AddWithValue($"$cat{i}", Dedup.ExcludedCategoryTags[i]);
            if (emu is not null) cmd.Parameters.AddWithValue("$emu", emu);
            // $cstatus is only referenced inside the emu clause, so bind it only when that clause is present.
            if (emu is not null && compatFilterStatus is not null) cmd.Parameters.AddWithValue("$cstatus", compatFilterStatus);
        }

        await using var db = await OpenAsync();

        // Regex search runs in C# (AOT-safe interpreted Regex); the other filters still run in SQL.
        if (hasQuery && searchMode == "regex")
            return await QueryGamesByRegexAsync(db, filterWhere, BindFilters, q!, page, pageSize);

        var where = filterWhere + nameClause;

        int total;
        await using (var cnt = db.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id {where}";
            BindFilters(cnt);
            total = Convert.ToInt32(await cnt.ExecuteScalarAsync());
        }

        var games = new List<CatalogGameDto>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"SELECT {GameColumns} FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id {where} ORDER BY g.name LIMIT $limit OFFSET $offset";
            BindFilters(cmd);
            cmd.Parameters.AddWithValue("$limit", pageSize);
            cmd.Parameters.AddWithValue("$offset", page * pageSize);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) games.Add(MapGame(r));
        }
        return (total, games);
    }

    // How a catalog_compat row (aliased c) matches a catalog_game (aliased g), single-sourced so the
    // compat projection and the emulator/status filter join identically. Serial keys are globally
    // unique; name keys (titles) collide across consoles, so a name row is gated to the consoles its
    // emulator targets (the row's own `consoles` CSV) — per-emulator-correct even with several
    // name-keyed emulators (a NULL/empty consoles never matches). F3 adds a title_id branch here once
    // Nintendo systems carry a title_id column.
    private const string CompatMatch =
        "(c.match_kind = 'serial' AND c.match_key = g.serial_key) OR " +
        "(c.match_kind = 'name' AND c.match_key = g.title_key AND " +
        "instr(',' || c.consoles || ',', ',' || s.console || ',') > 0)";

    // The full per-row projection, shared by the normal paged query and the regex page hydration.
    // compat is GROUP_CONCAT'd as "emulator=status" pairs (joined by '|') so a game can carry a badge
    // per emulator; MapGame splits it back into a list.
    private const string GameColumns = $"""
        g.id, g.name, s.console, g.region, g.serial, g.languages,
        (SELECT COALESCE(SUM(r.size), 0) FROM catalog_rom r WHERE r.game_id = g.id) AS size,
        EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id) AS owned,
        (SELECT GROUP_CONCAT(c.emulator || '=' || c.status, '|') FROM catalog_compat c WHERE {CompatMatch}) AS compat,
        (SELECT o.verified FROM catalog_owned o WHERE o.game_id = g.id) AS verified,
        g.vimm_match AS vimm_match,
        (SELECT GROUP_CONCAT(f.alt) FROM catalog_vimm_format f WHERE f.game_id = g.id) AS avail_formats,
        (SELECT GROUP_CONCAT(DISTINCT cu.format) FROM completed_urls cu WHERE cu.game_id = g.id) AS owned_formats,
        (SELECT GROUP_CONCAT(DISTINCT cu.source) FROM completed_urls cu WHERE cu.game_id = g.id) AS owned_sources,
        (SELECT GROUP_CONCAT(DISTINCT gs.origin) FROM catalog_game_source gs WHERE gs.game_id = g.id) AS origins
        """;

    /// <summary>Build a <see cref="CatalogGameDto"/> from a row selected with <see cref="GameColumns"/>.</summary>
    private static CatalogGameDto MapGame(DbDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.GetInt64(6),
        r.GetInt32(7) != 0,
        ParseCompat(r.IsDBNull(8) ? null : r.GetString(8)),
        r.IsDBNull(9) ? null : r.GetInt32(9) != 0,
        r.IsDBNull(10) ? null : r.GetString(10),
        ParseIntCsv(r.IsDBNull(11) ? null : r.GetString(11)),
        ParseIntCsv(r.IsDBNull(12) ? null : r.GetString(12)),
        ParseStrCsv(r.IsDBNull(13) ? null : r.GetString(13)),
        ParseStrCsv(r.IsDBNull(14) ? null : r.GetString(14)));

    /// <summary>
    /// Translate a user glob (<c>*</c>, <c>?</c>) into a SQLite LIKE pattern (<c>%</c>, <c>_</c>),
    /// escaping LIKE's own metacharacters with <c>\</c> (paired with <c>ESCAPE '\'</c>). Case-insensitive,
    /// like substring search.
    /// </summary>
    private static string GlobToLike(string glob)
    {
        var sb = new StringBuilder(glob.Length + 4);
        foreach (var c in glob)
            switch (c)
            {
                case '*': sb.Append('%'); break;
                case '?': sb.Append('_'); break;
                case '%' or '_' or '\\': sb.Append('\\').Append(c); break;
                default: sb.Append(c); break;
            }
        return sb.ToString();
    }

    /// <summary>
    /// Regex search path: SQLite has no REGEXP, so the (AOT-safe, interpreted) Regex runs in C#. Pass 1
    /// streams (id, name) for every row passing the non-name filters and keeps the matches — with a
    /// per-row match timeout so a pathological pattern can't hang a row; pass 2 hydrates only the page.
    /// An invalid pattern yields an empty result rather than an error.
    /// </summary>
    private async Task<(int Total, List<CatalogGameDto> Games)> QueryGamesByRegexAsync(
        SqliteConnection db, string filterWhere, Action<SqliteCommand> bindFilters, string pattern, int page, int pageSize)
    {
        Regex rx;
        try { rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
        catch (ArgumentException) { return (0, []); }   // invalid pattern → graceful empty result

        var matchedIds = new List<long>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"SELECT g.id, g.name FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id {filterWhere} ORDER BY g.name";
            bindFilters(cmd);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                try { if (rx.IsMatch(r.GetString(1))) matchedIds.Add(r.GetInt64(0)); }
                catch (RegexMatchTimeoutException) { /* skip a row the pattern can't evaluate in time */ }
            }
        }

        int total = matchedIds.Count;
        var pageIds = matchedIds.Skip(page * pageSize).Take(pageSize).ToList();
        if (pageIds.Count == 0) return (total, []);

        var games = new List<CatalogGameDto>(pageIds.Count);
        await using (var cmd = db.CreateCommand())
        {
            var inList = string.Join(", ", Enumerable.Range(0, pageIds.Count).Select(i => $"$id{i}"));
            cmd.CommandText = $"SELECT {GameColumns} FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id WHERE g.id IN ({inList}) ORDER BY g.name";
            for (int i = 0; i < pageIds.Count; i++) cmd.Parameters.AddWithValue($"$id{i}", pageIds[i]);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) games.Add(MapGame(r));
        }
        return (total, games);
    }

    /// <summary>Parse a SQLite GROUP_CONCAT of ints (e.g. "0,1,1") into a sorted, distinct list.</summary>
    private static List<int> ParseIntCsv(string? csv) => string.IsNullOrEmpty(csv)
        ? []
        : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(int.Parse).Distinct().OrderBy(x => x).ToList();

    /// <summary>Parse a SQLite GROUP_CONCAT of strings into a distinct list.</summary>
    private static List<string> ParseStrCsv(string? csv) => string.IsNullOrEmpty(csv)
        ? []
        : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Distinct().ToList();

    /// <summary>Parse the compat projection ("emulator=status" pairs joined by '|') into a per-emulator list.</summary>
    private static List<CompatStatus> ParseCompat(string? concat)
    {
        if (string.IsNullOrEmpty(concat)) return [];
        var list = new List<CompatStatus>();
        foreach (var pair in concat.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && eq < pair.Length - 1)
                list.Add(new CompatStatus(pair[..eq], pair[(eq + 1)..]));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Emulator, b.Emulator));
        return list;
    }

    // --- download sets ---

    public async Task<long> AddSetAsync(string name, string console, IReadOnlyList<string> links)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync();
        long id;
        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            // The legacy source/identifier columns are NOT NULL but unused by the link model — fill
            // them with placeholders (migration 020 leaves them vestigial; links live in catalog_set_link).
            cmd.CommandText = """
                INSERT INTO catalog_set (name, console, source, identifier, created_at)
                VALUES ($n, $c, 'archive', '', datetime('now')) RETURNING id
                """;
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", console);
            id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await InsertLinksAsync(db, tx, id, links);
        await tx.CommitAsync();
        return id;
    }

    /// <summary>Replace a set's name/console/links wholesale. Returns false if the id is unknown.</summary>
    public async Task<bool> UpdateSetAsync(int id, string name, string console, IReadOnlyList<string> links)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync();
        int rows;
        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE catalog_set SET name = $n, console = $c WHERE id = $id";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", console);
            cmd.Parameters.AddWithValue("$id", id);
            rows = await cmd.ExecuteNonQueryAsync();
        }
        if (rows == 0) { await tx.RollbackAsync(); return false; }
        await using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM catalog_set_link WHERE set_id = $id";
            del.Parameters.AddWithValue("$id", id);
            await del.ExecuteNonQueryAsync();
        }
        await InsertLinksAsync(db, tx, id, links);
        await tx.CommitAsync();
        return true;
    }

    private static async Task InsertLinksAsync(SqliteConnection db, SqliteTransaction tx, long setId, IReadOnlyList<string> links)
    {
        await using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO catalog_set_link (set_id, url, position) VALUES ($s, $u, $p)";
        cmd.Parameters.AddWithValue("$s", setId);
        var pu = cmd.Parameters.Add("$u", SqliteType.Text);
        var pp = cmd.Parameters.Add("$p", SqliteType.Integer);
        int pos = 0;
        foreach (var raw in links)
        {
            var u = raw.Trim();
            if (u.Length == 0) continue;
            pu.Value = u;
            pp.Value = pos++;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<CatalogSetDto>> GetSetsAsync()
    {
        await using var db = await OpenAsync();
        return await ReadSetsAsync(db, null);
    }

    public async Task<List<CatalogSetDto>> GetSetsByConsoleAsync(string console)
    {
        await using var db = await OpenAsync();
        return await ReadSetsAsync(db, console);
    }

    public async Task<bool> DeleteSetAsync(int id)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync();
        await using (var dl = db.CreateCommand())
        {
            dl.Transaction = tx;
            dl.CommandText = "DELETE FROM catalog_set_link WHERE set_id = $id";
            dl.Parameters.AddWithValue("$id", id);
            await dl.ExecuteNonQueryAsync();
        }
        int rows;
        await using (var ds = db.CreateCommand())
        {
            ds.Transaction = tx;
            ds.CommandText = "DELETE FROM catalog_set WHERE id = $id";
            ds.Parameters.AddWithValue("$id", id);
            rows = await ds.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return rows > 0;
    }

    /// <summary>(console, name) for a catalog game, or null if the id is unknown.</summary>
    public async Task<(string Console, string Name)?> GetGameByIdAsync(int id)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT s.console, g.name FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id WHERE g.id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.GetString(0), r.GetString(1)) : null;
    }

    /// <summary>
    /// The Vimm vault binding for a game — its bound vault id and available download formats — or
    /// null if the game has no Vimm match. Used for the archive→Vimm download fallback.
    /// </summary>
    public async Task<(long VaultId, List<VimmFormatRow> Formats)?> GetVaultBindingAsync(int gameId)
    {
        await using var db = await OpenAsync();
        long vaultId;
        await using (var g = db.CreateCommand())
        {
            g.CommandText = "SELECT vault_id FROM catalog_game WHERE id = $id AND vault_id IS NOT NULL";
            g.Parameters.AddWithValue("$id", gameId);
            var v = await g.ExecuteScalarAsync();
            if (v is null || v == DBNull.Value) return null;
            vaultId = Convert.ToInt64(v);
        }
        var formats = new List<VimmFormatRow>();
        await using (var f = db.CreateCommand())
        {
            f.CommandText = "SELECT alt, label, size_bytes, size_text FROM catalog_vimm_format WHERE game_id = $id ORDER BY alt";
            f.Parameters.AddWithValue("$id", gameId);
            await using var r = await f.ExecuteReaderAsync();
            while (await r.ReadAsync())
                formats.Add(new VimmFormatRow(r.GetInt32(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return (vaultId, formats);
    }

    // --- catalog media (box art / title screens, epic #122 / M1) ---

    /// <summary>A game's libretro-thumbnails lookup key: its system DAT name, console, and exact name.</summary>
    public async Task<(string DatName, string Console, string Name)?> GetGameMediaKeyAsync(int gameId)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT s.dat_name, s.console, g.name
            FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id
            WHERE g.id = $id
            """;
        cmd.Parameters.AddWithValue("$id", gameId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetString(0), r.GetString(1), r.GetString(2));
    }

    /// <summary>The cached media record for a (game, type), or null when never fetched.</summary>
    public async Task<(string Status, string? Path)?> GetMediaAsync(int gameId, string type)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT status, path FROM catalog_media WHERE game_id = $id AND type = $type";
        cmd.Parameters.AddWithValue("$id", gameId);
        cmd.Parameters.AddWithValue("$type", type);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
    }

    /// <summary>Record a media fetch outcome — a cached image ('ok' + path) or a negative-cache miss ('missing').</summary>
    public async Task UpsertMediaAsync(int gameId, string type, string source, string status, string? path)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_media (game_id, type, source, status, path, fetched_at)
            VALUES ($id, $type, $source, $status, $path, datetime('now'))
            ON CONFLICT(game_id, type) DO UPDATE SET
                source = excluded.source, status = excluded.status,
                path = excluded.path, fetched_at = excluded.fetched_at
            """;
        cmd.Parameters.AddWithValue("$id", gameId);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$path", (object?)path ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- catalog descriptions (IGDB, epic #122 / M2) ---

    /// <summary>Every game on a console as (id, name) — the join input for the IGDB description match.</summary>
    public async Task<List<(long Id, string Name)>> GetGamesForConsoleAsync(string console)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT g.id, g.name FROM catalog_game g
            JOIN catalog_system s ON s.id = g.system_id
            WHERE s.console = $c
            """;
        cmd.Parameters.AddWithValue("$c", console);
        var list = new List<(long, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt64(0), r.GetString(1)));
        return list;
    }

    /// <summary>Store matched descriptions (game id → text) in one transaction.</summary>
    public async Task SetDescriptionsAsync(IReadOnlyList<(long Id, string Description)> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE catalog_game SET description = $d WHERE id = $id";
        var pDesc = cmd.Parameters.Add("$d", SqliteType.Text);
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        foreach (var (id, desc) in rows)
        {
            pDesc.Value = desc;
            pId.Value = id;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>A game's stored description (IGDB), or null when it has none.</summary>
    public async Task<string?> GetDescriptionAsync(int gameId)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT description FROM catalog_game WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", gameId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static async Task<List<CatalogSetDto>> ReadSetsAsync(SqliteConnection db, string? console)
    {
        var order = new List<(int Id, string Name, string Console)>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = console is null
                ? "SELECT id, name, console FROM catalog_set ORDER BY console, name, id"
                : "SELECT id, name, console FROM catalog_set WHERE console = $c ORDER BY name, id";
            if (console is not null) cmd.Parameters.AddWithValue("$c", console);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                order.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2)));
        }

        var linksBySet = new Dictionary<int, List<string>>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT set_id, url FROM catalog_set_link ORDER BY set_id, position, id";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var sid = r.GetInt32(0);
                if (!linksBySet.TryGetValue(sid, out var l)) linksBySet[sid] = l = [];
                l.Add(r.GetString(1));
            }
        }

        return order.Select(s => new CatalogSetDto(s.Id, s.Name, s.Console,
            linksBySet.TryGetValue(s.Id, out var l) ? l : [])).ToList();
    }
}
