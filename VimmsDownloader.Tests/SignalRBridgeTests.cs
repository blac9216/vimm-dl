using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Pipeline;

namespace VimmsDownloader.Tests;

/// <summary>
/// #151 / A: the PS3 pipeline bridge stamps the live <c>ConvertStatus</c> payload with the catalog
/// identity (game_id / format) — resolved the same way the events table is — so the Active panel can
/// group conversions by game. Exercises the REAL <see cref="SignalRPs3PipelineBridge"/> against a temp DB
/// + a capturing <see cref="IHubContext{T}"/>; the filename stays the display / abort key.
///
/// Also covers #274: both console bridges (<see cref="SignalRPs3PipelineBridge"/> and
/// <see cref="SignalRWiiUPipelineBridge"/>) stamp their own <c>platform</c>, since it's the only
/// reliable way to tell a PS3 conversion apart from a Wii U one — the phase strings alone are
/// ambiguous ("queued"/"extracting" are shared by both pipelines).
/// </summary>
[TestClass]
public class SignalRBridgeTests
{
    private string _dir = null!;
    private string _connStr = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"bridge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task ConvertStatus_CarriesGameIdAndFormat_ForMatchedItem()
    {
        var (repo, gameId) = await SeededRepoBoundToVault(1001, format: 1);
        var hub = new CapturingHubContext();
        var bridge = new SignalRPs3PipelineBridge(hub, repo);

        await bridge.SendAsync(new PipelineStatusEvent("Game.7z", "done", "ISO ready", OutputFilename: "Game.iso"));

        var payload = hub.ConvertStatusPayload();
        Assert.AreEqual(gameId, payload.GetProperty("gameId").GetInt64());
        Assert.AreEqual(1, payload.GetProperty("format").GetInt32());
        Assert.AreEqual("Game.7z", payload.GetProperty("itemName").GetString()); // display / abort key unchanged
    }

    [TestMethod]
    public async Task ConvertStatus_NullIdentity_ForUnmatchedItem()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        var hub = new CapturingHubContext();
        var bridge = new SignalRPs3PipelineBridge(hub, repo);

        await bridge.SendAsync(new PipelineStatusEvent("Unknown.7z", "converting", "50%"));

        var payload = hub.ConvertStatusPayload();
        Assert.IsTrue(IsNullOrAbsent(payload, "gameId"), "unmatched item carries no game identity");
        Assert.IsTrue(IsNullOrAbsent(payload, "format"));
        Assert.AreEqual("Unknown.7z", payload.GetProperty("itemName").GetString());
    }

    [TestMethod]
    public async Task ConvertStatus_Ps3Bridge_StampsPs3Platform()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        var hub = new CapturingHubContext();
        var bridge = new SignalRPs3PipelineBridge(hub, repo);

        await bridge.SendAsync(new PipelineStatusEvent("Game.7z", "extracting", "Extracting 10%"));

        var payload = hub.ConvertStatusPayload();
        Assert.AreEqual(Module.Core.Platforms.PS3, payload.GetProperty("platform").GetString());
    }

    /// <summary>
    /// #274: the Jobs tab's stop button routes a conversion's abort to the right backend pipeline by
    /// reading this stamped <c>platform</c> field — otherwise "extracting"/"queued" are the same string
    /// for both consoles and the frontend has no way to tell them apart.
    /// </summary>
    [TestMethod]
    public async Task ConvertStatus_WiiUBridge_StampsWiiUPlatform()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        var hub = new CapturingHubContext();
        var bridge = new SignalRWiiUPipelineBridge(hub, repo);

        await bridge.SendAsync(new PipelineStatusEvent("0005000010144000", "extracting", "Extracting files…"));

        var payload = hub.ConvertStatusPayload();
        Assert.AreEqual(Module.Core.Platforms.WiiU, payload.GetProperty("platform").GetString());
    }

    private static bool IsNullOrAbsent(JsonElement obj, string prop)
        => !obj.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null;

    /// <summary>
    /// Seed a catalog game bound to <paramref name="vaultId"/> and a completed Vimm download for it at
    /// <paramref name="format"/>, so <c>ResolveEventIdentityAsync("Game.7z")</c> → (gameId, format, "vimm").
    /// </summary>
    private async Task<(QueueRepository Repo, long GameId)> SeededRepoBoundToVault(long vaultId, int format)
    {
        long gameId;
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
            await Exec(db, "INSERT INTO catalog_system (dat_name, console, source) VALUES ('Test', 'ps3', 'redump')");
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO catalog_game (system_id, name, vault_id) VALUES (1, 'Bound', $v); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$v", vaultId);
            gameId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        await repo.AddToQueueAsync($"https://vimm.net/vault/{vaultId}", format);
        var next = (await repo.GetNextQueueItemAsync())!.Value;
        await repo.CompleteItemAsync(next.Id, next.Url, "Game.7z",
            Path.Combine(_dir, "downloads", "completed", "Game.7z"), next.Format);
        return (repo, gameId);
    }

    private static async Task Exec(SqliteConnection db, string sql)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Minimal capturing <see cref="IHubContext{DownloadHub}"/> — records every <c>SendAsync</c>.</summary>
sealed class CapturingHubContext : IHubContext<DownloadHub>
{
    public List<(string Method, object?[] Args)> Sent { get; } = [];
    public IHubClients Clients { get; }
    public IGroupManager Groups => throw new NotSupportedException();

    public CapturingHubContext() => Clients = new CapturingClients(this);

    /// <summary>The payload (a JsonElement) of the single ConvertStatus broadcast.</summary>
    public JsonElement ConvertStatusPayload()
        => (JsonElement)Sent.Single(s => s.Method == "ConvertStatus").Args[0]!;

    private sealed class CapturingClients(CapturingHubContext owner) : IHubClients
    {
        private readonly IClientProxy _proxy = new CapturingProxy(owner);
        public IClientProxy All => _proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Client(string connectionId) => _proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
        public IClientProxy Group(string groupName) => _proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
        public IClientProxy User(string userId) => _proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
    }

    private sealed class CapturingProxy(CapturingHubContext owner) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Sent.Add((method, args));
            return Task.CompletedTask;
        }
    }
}
