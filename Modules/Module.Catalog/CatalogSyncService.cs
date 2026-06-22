using System.Net;
using Microsoft.Extensions.Logging;
using Module.Core;

namespace Module.Catalog;

/// <summary>
/// Fetches the configured console DATs from the libretro-database mirror, parses them, and
/// persists each system's games via <see cref="ICatalogStore"/>. One system at a time; a failed
/// system (HTTP/parse error) is logged and skipped so a single failure never aborts the run.
///
/// <para>A full multi-console sync is ~one request per system against GitHub, whose anonymous cap is
/// low, so the fetch is <b>rate-limit-safe</b> (D3a / #130): a polite pause between systems paces the
/// run, and a rate-limited (HTTP 429, or 403 with <c>X-RateLimit-Remaining: 0</c>) or transient fetch
/// is retried with exponential backoff that honors a <c>Retry-After</c> header when the server sends
/// one. The fresher daily-bundle <i>source</i> is a separate slice (#130/D3b).</para>
/// </summary>
public sealed class CatalogSyncService(HttpClient http, ICatalogStore store, ILogger<CatalogSyncService> log)
{
    private const string BaseUrl = "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/";

    /// <summary>Total attempts (1 try + N retries) for a rate-limited/transient fetch before a system fails.</summary>
    internal int MaxFetchAttempts { get; set; } = 4;
    /// <summary>First-retry wait; doubles each retry, capped at <see cref="MaxBackoff"/>.</summary>
    internal TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Upper bound on a single backoff wait (also caps an over-long <c>Retry-After</c>).</summary>
    internal TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Polite pause between systems so a full multi-console run stays under the anonymous cap.</summary>
    internal TimeSpan InterSystemDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Delay seam — defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests substitute a no-op recorder.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>Sync every system, skipping any that fail. Returns a run summary.</summary>
    public async Task<CatalogSyncSummary> SyncAsync(IReadOnlyList<CatalogSystemInfo> systems, CancellationToken ct = default)
    {
        int synced = 0, failed = 0, games = 0;
        for (int i = 0; i < systems.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            // Pace the run: pause before every system after the first so we don't burst the cap.
            if (i > 0 && InterSystemDelay > TimeSpan.Zero) await Delay(InterSystemDelay, ct);
            var sys = systems[i];
            var r = await SyncSystemAsync(sys, ct);
            if (r.IsOk)
            {
                synced++;
                games += r.Value;
                log.LogInformation("Catalog: {System} → {Count} games", sys.DatName, r.Value);
            }
            else
            {
                failed++;
                log.LogWarning("Catalog: {System} failed — {Error}", sys.DatName, r.Error);
            }
        }
        log.LogInformation("Catalog sync done: {Synced} systems, {Games} games, {Failed} failed", synced, games, failed);
        return new CatalogSyncSummary(synced, failed, games);
    }

    /// <summary>Fetch, parse and persist a single system. Returns the game count or an error.</summary>
    public async Task<Result<int>> SyncSystemAsync(CatalogSystemInfo sys, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}{sys.Group}/{Uri.EscapeDataString(sys.DatName)}.dat";

        var fetch = await FetchWithRetryAsync(url, sys.DatName, ct);
        if (!fetch.IsOk) return Result<int>.Fail(fetch.Error);
        var content = fetch.Value;

        try
        {
            var parser = new ClrMameProParser();
            var games = parser.Parse(new StringReader(content)).ToList();
            var systemId = await store.UpsertSystemAsync(sys.DatName, sys.Console, sys.Group, ct);
            await store.ReplaceSystemGamesAsync(systemId, games, parser.Header?.Version, ct);
            return Result<int>.Ok(games.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Fail($"persist failed: {ex.Message}");
        }
    }

    /// <summary>
    /// GET the DAT body, retrying rate-limited (429 / 403 with no remaining quota) and transient
    /// failures with exponential backoff that prefers the server's <c>Retry-After</c>. A non-retryable
    /// HTTP error, or exhausting the attempt budget, returns a failure. Cancellation always propagates.
    /// </summary>
    private async Task<Result<string>> FetchWithRetryAsync(string url, string name, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var resp = await http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode)
                    return Result<string>.Ok(await resp.Content.ReadAsStringAsync(ct));

                if (IsRateLimited(resp) && attempt < MaxFetchAttempts)
                {
                    var wait = RetryAfter(resp) ?? Backoff(attempt);
                    log.LogWarning("Catalog: {System} rate-limited (HTTP {Code}); waiting {Wait:0.#}s before retry {Next}/{Max}",
                        name, (int)resp.StatusCode, wait.TotalSeconds, attempt + 1, MaxFetchAttempts);
                    await Delay(wait, ct);
                    continue;
                }
                return Result<string>.Fail($"HTTP {(int)resp.StatusCode} for {name}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;  // genuine cancellation — never swallow
            }
            catch (Exception ex)  // transient (network / HttpClient timeout): retry, then give up
            {
                if (attempt >= MaxFetchAttempts) return Result<string>.Fail($"fetch failed: {ex.Message}");
                var wait = Backoff(attempt);
                log.LogWarning("Catalog: {System} fetch error ({Error}); waiting {Wait:0.#}s before retry {Next}/{Max}",
                    name, ex.Message, wait.TotalSeconds, attempt + 1, MaxFetchAttempts);
                await Delay(wait, ct);
            }
        }
    }

    /// <summary>True for HTTP 429, or a 403 that GitHub returns with the rate quota exhausted.</summary>
    private static bool IsRateLimited(HttpResponseMessage resp)
        => resp.StatusCode == HttpStatusCode.TooManyRequests
           || (resp.StatusCode == HttpStatusCode.Forbidden
               && resp.Headers.TryGetValues("X-RateLimit-Remaining", out var rem)
               && rem.FirstOrDefault() == "0");

    /// <summary>The server's requested wait, clamped to <see cref="MaxBackoff"/>, or null if unset.</summary>
    private TimeSpan? RetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra is null) return null;
        var wait = ra.Delta ?? (ra.Date is { } when ? when - DateTimeOffset.UtcNow : (TimeSpan?)null);
        if (wait is not { } w) return null;
        return w < TimeSpan.Zero ? TimeSpan.Zero : (w > MaxBackoff ? MaxBackoff : w);
    }

    /// <summary>Exponential backoff for the given attempt (1-based), capped at <see cref="MaxBackoff"/>.</summary>
    private TimeSpan Backoff(int attempt)
    {
        var ticks = RetryBackoff.Ticks * (1L << (attempt - 1));
        return ticks > MaxBackoff.Ticks || ticks < 0 ? MaxBackoff : TimeSpan.FromTicks(ticks);
    }
}

/// <summary>Outcome of a catalog sync run.</summary>
public sealed record CatalogSyncSummary(int SystemsSynced, int SystemsFailed, int TotalGames);
