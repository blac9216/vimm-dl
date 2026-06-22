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
