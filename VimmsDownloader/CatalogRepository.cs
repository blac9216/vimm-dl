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

    public async Task ReplaceSystemGamesAsync(long systemId, IReadOnlyList<DatGame> games, string? datVersion, CancellationToken ct)
    {
        await using var db = await OpenAsync();
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);

        // Clear the system's existing rows so a re-sync replaces rather than appends.
        await using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM catalog_rom WHERE game_id IN (SELECT id FROM catalog_game WHERE system_id = $sid);
                DELETE FROM catalog_game WHERE system_id = $sid;
                """;
            del.Parameters.AddWithValue("$sid", systemId);
            await del.ExecuteNonQueryAsync(ct);
        }

        // 1G1R: title key per game + which variant is the parent (preferred) of its group.
        var titleKeys = new string[games.Count];
        var isParent = new bool[games.Count];
        var groups = new Dictionary<string, List<int>>();
        for (int i = 0; i < games.Count; i++)
        {
            var key = Dedup.TitleKey(games[i].Name);
            titleKeys[i] = key;
            if (!groups.TryGetValue(key, out var members)) groups[key] = members = [];
            members.Add(i);
        }
        foreach (var members in groups.Values)
        {
            var variants = members.Select(idx => (games[idx].Name, games[idx].Region)).ToList();
            isParent[members[Dedup.SelectParent(variants)]] = true;
        }

        await using (var ig = db.CreateCommand())
        await using (var ir = db.CreateCommand())
        {
            ig.Transaction = tx;
            ig.CommandText = """
                INSERT INTO catalog_game (system_id, name, region, serial, serial_key, languages, title_key, is_parent)
                VALUES ($sid, $name, $region, $serial, $skey, $langs, $tkey, $parent) RETURNING id
                """;
            ig.Parameters.AddWithValue("$sid", systemId);
            var gName = ig.Parameters.Add("$name", SqliteType.Text);
            var gRegion = ig.Parameters.Add("$region", SqliteType.Text);
            var gSerial = ig.Parameters.Add("$serial", SqliteType.Text);
            var gSkey = ig.Parameters.Add("$skey", SqliteType.Text);
            var gLangs = ig.Parameters.Add("$langs", SqliteType.Text);
            var gTkey = ig.Parameters.Add("$tkey", SqliteType.Text);
            var gParent = ig.Parameters.Add("$parent", SqliteType.Integer);

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

            for (int i = 0; i < games.Count; i++)
            {
                var g = games[i];
                gName.Value = g.Name;
                gRegion.Value = (object?)g.Region ?? DBNull.Value;
                gSerial.Value = (object?)g.Serial ?? DBNull.Value;
                // Normalize with the SAME function the compat table uses (RpcsCompat.NormalizeSerial),
                // so catalog_game.serial_key and catalog_compat.serial_key join symmetrically (#48).
                gSkey.Value = string.IsNullOrEmpty(g.Serial)
                    ? DBNull.Value
                    : RpcsCompat.NormalizeSerial(g.Serial);
                gLangs.Value = g.Languages.Count > 0 ? string.Join(',', g.Languages) : DBNull.Value;
                gTkey.Value = titleKeys[i];
                gParent.Value = isParent[i] ? 1 : 0;
                var gid = Convert.ToInt64(await ig.ExecuteScalarAsync(ct));

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
        }

        await using (var upd = db.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE catalog_system SET dat_version = $v, game_count = $gc, synced_at = datetime('now')
                WHERE id = $sid
                """;
            upd.Parameters.AddWithValue("$v", (object?)datVersion ?? DBNull.Value);
            upd.Parameters.AddWithValue("$gc", games.Count);
            upd.Parameters.AddWithValue("$sid", systemId);
            await upd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
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

    /// <summary>Replace an emulator's compatibility entries wholesale (normalized serial → status).</summary>
    public async Task ReplaceCompatAsync(string emulator, IReadOnlyList<(string Serial, string Status)> entries, CancellationToken ct)
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
            ins.CommandText = "INSERT OR REPLACE INTO catalog_compat (emulator, serial_key, status) VALUES ($e, $s, $st)";
            ins.Parameters.AddWithValue("$e", emulator);
            var ps = ins.Parameters.Add("$s", SqliteType.Text);
            var pst = ins.Parameters.Add("$st", SqliteType.Text);
            foreach (var (serial, status) in entries)
            {
                ps.Value = serial;
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
    /// <c>owned</c> flag, a best-effort emulator <c>compat</c> status, and a <c>verified</c> result.
    /// </summary>
    public async Task<(int Total, List<CatalogGameDto> Games)> GetGamesAsync(
        string? console, string? query, string local, bool dedupe, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(0, page);
        var like = string.IsNullOrWhiteSpace(query) ? null : "%" + query.Trim() + "%";
        local = local is "owned" or "remote" ? local : "all";

        const string where = """
            WHERE ($console IS NULL OR s.console = $console)
              AND ($like IS NULL OR g.name LIKE $like)
              AND ($local = 'all'
                   OR ($local = 'owned'  AND     EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id))
                   OR ($local = 'remote' AND NOT EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id)))
              AND ($dedupe = 0 OR g.is_parent = 1)
            """;

        await using var db = await OpenAsync();

        int total;
        await using (var cnt = db.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id {where}";
            cnt.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cnt.Parameters.AddWithValue("$like", (object?)like ?? DBNull.Value);
            cnt.Parameters.AddWithValue("$local", local);
            cnt.Parameters.AddWithValue("$dedupe", dedupe ? 1 : 0);
            total = Convert.ToInt32(await cnt.ExecuteScalarAsync());
        }

        var games = new List<CatalogGameDto>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT g.id, g.name, s.console, g.region, g.serial, g.languages,
                       (SELECT COALESCE(SUM(r.size), 0) FROM catalog_rom r WHERE r.game_id = g.id) AS size,
                       EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id) AS owned,
                       (SELECT c.status FROM catalog_compat c WHERE c.serial_key = g.serial_key LIMIT 1) AS compat,
                       (SELECT o.verified FROM catalog_owned o WHERE o.game_id = g.id) AS verified,
                       g.vimm_match AS vimm_match
                FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id
                {where}
                ORDER BY g.name
                LIMIT $limit OFFSET $offset
                """;
            cmd.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$like", (object?)like ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$local", local);
            cmd.Parameters.AddWithValue("$dedupe", dedupe ? 1 : 0);
            cmd.Parameters.AddWithValue("$limit", pageSize);
            cmd.Parameters.AddWithValue("$offset", page * pageSize);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                games.Add(new CatalogGameDto(
                    r.GetInt32(0), r.GetString(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetInt64(6),
                    r.GetInt32(7) != 0,
                    r.IsDBNull(8) ? null : r.GetString(8),
                    r.IsDBNull(9) ? null : r.GetInt32(9) != 0,
                    r.IsDBNull(10) ? null : r.GetString(10)));
        }
        return (total, games);
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
