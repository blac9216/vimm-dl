using System.Net;
using Microsoft.Extensions.Logging;
using Module.Core;

namespace Module.Catalog;

/// <summary>
/// The default catalog DAT source: the No-Intro/Redump mirror in libretro-database, one raw HTTPS
/// GET per system (<c>metadat/{group}/{DatName}.dat</c>).
///
/// <para>A full multi-console sync is ~one request per system against GitHub, whose anonymous cap is
/// low, so the fetch is <b>rate-limit-safe</b> (D3a / #130): a rate-limited (HTTP 429, or 403 with
/// <c>X-RateLimit-Remaining: 0</c>) or transient fetch is retried with exponential backoff that honors
/// a <c>Retry-After</c> header when present, and <see cref="InterSystemDelay"/> paces the run so it
/// stays under the cap. The fresher daily-bundle alternative is <see cref="DailyBundleDatSource"/>.</para>
/// </summary>
public sealed class LibretroDatSource(HttpClient http, ILogger<LibretroDatSource> log) : IDatSource
{
    private const string BaseUrl = "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/";

    /// <summary>Total attempts (1 try + N retries) for a rate-limited/transient fetch before a system fails.</summary>
    internal int MaxFetchAttempts { get; set; } = 4;
    /// <summary>First-retry wait; doubles each retry, capped at <see cref="MaxBackoff"/>.</summary>
    internal TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Upper bound on a single backoff wait (also caps an over-long <c>Retry-After</c>).</summary>
    internal TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
    /// <inheritdoc/>
    public string Origin => "libretro";
    /// <summary>Polite pause between systems so a full multi-console run stays under the anonymous cap.</summary>
    public TimeSpan InterSystemDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Delay seam — defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests substitute a no-op recorder.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public Task<Result<string>> GetDatAsync(CatalogSystemInfo sys, CancellationToken ct)
    {
        var url = $"{BaseUrl}{sys.Group}/{Uri.EscapeDataString(sys.DatName)}.dat";
        return FetchWithRetryAsync(url, sys.DatName, ct);
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
