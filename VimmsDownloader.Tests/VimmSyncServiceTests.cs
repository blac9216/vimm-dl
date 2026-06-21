using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Integration test for the real <see cref="VimmSyncService"/> against a temp-file catalog DB and a
/// stubbed Vimm (fake HttpClient serving fixtures). Covers the single-file inline-hash match, the
/// multi-disc hashes2.php match, format capture, and "no Vimm match" flagging — end to end through
/// the real parsers + repository binding.
/// </summary>
[TestClass]
public class VimmSyncServiceTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmsync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task SyncAsync_BindsByInlineHashAndHashes2_AndFlagsUnmatched()
    {
        // snes: ActRaiser matches the inline SNES fixture by SHA1; "Unowned" has no Vimm entry.
        var snes = await Seed("snes");
        var actRaiser = await AddGame(snes, "ActRaiser");
        await AddRom(actRaiser, "ActRaiser (USA).sfc", "eac3358d", "635d5d7dd2aad4768412fbae4a32fd6e",
            "e8365852cc20178d42c93cd188a7ae9af45369d7");
        var unowned = await AddGame(snes, "No Vimm Entry");
        await AddRom(unowned, "No Vimm Entry.sfc", "11111111", "22222222", "33333333");
        // psx: FF7 matches the multi-disc fixture via hashes2.php (Disc 1 .bin SHA1).
        var ps = await Seed("psx");
        var ff7 = await AddGame(ps, "Final Fantasy VII");
        await AddRom(ff7, "Final Fantasy VII (Europe) (Disc 1).bin", "900e6a9e",
            "95daa58e45d71bfe6ce3c699c87652a0", "6b0e68ff27c636d560d4575fd990091ab09bed80");

        var svc = new VimmSyncService(_repo, new FakeHttpClientFactory(new StubHandler(Route)),
            NullLogger<VimmSyncService>.Instance) { PoliteDelayMs = 0 };
        await svc.SyncAsync(null, default);

        // ActRaiser bound by inline SHA1, with the single-file format labelled by ROM extension.
        Assert.AreEqual((1009L, "sha1"), await Binding(actRaiser));
        var formats = await Formats(actRaiser);
        Assert.HasCount(1, formats);
        Assert.AreEqual((0, ".sfc", 658L), formats[0]);

        // FF7 bound by hashes2 SHA1 (multi-disc — no inline hash on the page).
        Assert.AreEqual((50602L, "sha1"), await Binding(ff7));

        // The catalog game Vimm doesn't carry is flagged "no Vimm match".
        Assert.AreEqual((0L, "none"), await BindingRaw(unowned));   // vault_id NULL → 0 sentinel, match 'none'
    }

    // --- stubbed Vimm ---

    private static string? Route(string url)
    {
        if (url.Contains("p=list"))
        {
            if (url.Contains("system=SNES") && url.Contains("section=A")) return SnesListA;
            if (url.Contains("system=PS1") && url.Contains("section=F")) return Ps1ListF;
            return ""; // any other section → empty list (200)
        }
        if (url.EndsWith("/vault/1009")) return SnesMedia;
        if (url.EndsWith("/vault/50602")) return Ff7Media;
        if (url.Contains("hashes2.php?id=43962")) return Ff7Hashes2;
        return null; // 404
    }

    private const string SnesListA =
        """<tr><td><a href="/vault/999999"></a><a href= "/vault/1009">ActRaiser</a></td></tr>""";
    private const string Ps1ListF =
        """<tr><td><a href="/vault/999999"></a><a href= "/vault/50602">Final Fantasy VII</a></td></tr>""";
    private const string SnesMedia =
        """<script>let media=[{"ID":989,"GoodTitle":"QWN0UmFpc2VyIChVU0EpLnNmYw==","Serial":"SNS-AR-USA","Zipped":"658","AltZipped":"0","AltZipped2":"0","GoodHash":"EAC3358D","GoodMd5":"635D5D7DD2AAD4768412FBAE4A32FD6E","GoodSha1":"E8365852CC20178D42C93CD188A7AE9AF45369D7","ZippedText":"658 KB"}];</script>""";
    // Multi-disc: media entries carry NO inline hash → service must call hashes2.php per media ID.
    private const string Ff7Media =
        """<script>let media=[{"ID":43962,"GoodTitle":"","Zipped":"700000","AltZipped":"0","AltZipped2":"0","ZippedText":"700 MB"},{"ID":43963,"GoodTitle":"","Zipped":"700000","AltZipped":"0","AltZipped2":"0","ZippedText":"700 MB"}];</script>""";
    private const string Ff7Hashes2 =
        """<div style="grid-column:span 2">Final Fantasy VII (Europe) (Disc 1).bin</div><div>Crc</div><div>900e6a9e</div><div>Md5</div><div>95daa58e45d71bfe6ce3c699c87652a0</div><div>Sha1</div><div>6b0e68ff27c636d560d4575fd990091ab09bed80</div>""";

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = route(req.RequestUri!.ToString());
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    // --- seeding / verification ---

    private async Task<long> Seed(string console) =>
        await ScalarLong($"INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT {console}', '{console}', 'no-intro') RETURNING id");

    private async Task<long> AddGame(long systemId, string name) =>
        await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({systemId}, '{name}') RETURNING id");

    private async Task AddRom(long gameId, string name, string crc, string md5, string sha1) =>
        await Exec($"INSERT INTO catalog_rom (game_id, name, size, crc, md5, sha1) VALUES ({gameId}, '{name}', 1, '{crc}', '{md5}', '{sha1}')");

    private async Task<(long Vault, string Match)> Binding(long gameId)
    {
        var (v, m) = await BindingRaw(gameId);
        return (v, m!);
    }

    private async Task<(long Vault, string? Match)> BindingRaw(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT vault_id, vimm_match FROM catalog_game WHERE id = {gameId}";
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.IsDBNull(0) ? 0L : r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1));
    }

    private async Task<List<(int Alt, string Label, long Size)>> Formats(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT alt, label, size_bytes FROM catalog_vimm_format WHERE game_id = {gameId} ORDER BY alt";
        var list = new List<(int, string, long)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task Exec(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
