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

        await using (var ig = db.CreateCommand())
        await using (var ir = db.CreateCommand())
        {
            ig.Transaction = tx;
            ig.CommandText = """
                INSERT INTO catalog_game (system_id, name, region, serial, languages)
                VALUES ($sid, $name, $region, $serial, $langs) RETURNING id
                """;
            ig.Parameters.AddWithValue("$sid", systemId);
            var gName = ig.Parameters.Add("$name", SqliteType.Text);
            var gRegion = ig.Parameters.Add("$region", SqliteType.Text);
            var gSerial = ig.Parameters.Add("$serial", SqliteType.Text);
            var gLangs = ig.Parameters.Add("$langs", SqliteType.Text);

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

            foreach (var g in games)
            {
                gName.Value = g.Name;
                gRegion.Value = (object?)g.Region ?? DBNull.Value;
                gSerial.Value = (object?)g.Serial ?? DBNull.Value;
                gLangs.Value = g.Languages.Count > 0 ? string.Join(',', g.Languages) : DBNull.Value;
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
        cmd.CommandText = "SELECT console, SUM(game_count) FROM catalog_system GROUP BY console ORDER BY console";
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CatalogConsole>();
        while (await r.ReadAsync())
            list.Add(new CatalogConsole(r.GetString(0), r.GetInt32(1)));
        return list;
    }

    /// <summary>Paged game list, filtered by console and/or a case-insensitive name substring.</summary>
    public async Task<(int Total, List<CatalogGameDto> Games)> GetGamesAsync(string? console, string? query, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(0, page);
        var like = string.IsNullOrWhiteSpace(query) ? null : "%" + query.Trim() + "%";

        await using var db = await OpenAsync();

        int total;
        await using (var cnt = db.CreateCommand())
        {
            cnt.CommandText = """
                SELECT COUNT(*) FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id
                WHERE ($console IS NULL OR s.console = $console)
                  AND ($like IS NULL OR g.name LIKE $like)
                """;
            cnt.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cnt.Parameters.AddWithValue("$like", (object?)like ?? DBNull.Value);
            total = Convert.ToInt32(await cnt.ExecuteScalarAsync());
        }

        var games = new List<CatalogGameDto>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT g.id, g.name, s.console, g.region, g.serial, g.languages,
                       (SELECT COALESCE(SUM(r.size), 0) FROM catalog_rom r WHERE r.game_id = g.id) AS size
                FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id
                WHERE ($console IS NULL OR s.console = $console)
                  AND ($like IS NULL OR g.name LIKE $like)
                ORDER BY g.name
                LIMIT $limit OFFSET $offset
                """;
            cmd.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$like", (object?)like ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$limit", pageSize);
            cmd.Parameters.AddWithValue("$offset", page * pageSize);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                games.Add(new CatalogGameDto(
                    r.GetInt32(0), r.GetString(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetInt64(6)));
        }
        return (total, games);
    }
}
