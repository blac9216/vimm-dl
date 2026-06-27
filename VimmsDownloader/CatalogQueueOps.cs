/// <summary>
/// The catalog batch-queue loop, extracted from the <c>POST /api/catalog/games/queue</c> endpoint so its
/// per-id classification (queued / duplicate / unavailable / unknown) and the queued/skipped/failed
/// tallying are unit-testable without an HTTP host — mirroring how the resolve path itself is covered at
/// the service level (see VimmFallbackTests). The endpoint keeps the surrounding concerns: request
/// validation and starting the download queue.
/// </summary>
static class CatalogQueueOps
{
    /// <summary>
    /// Resolve and enqueue each game id through the same archive-preferred / Vimm-fallback path as the
    /// single-queue endpoint, classifying each: <c>unknown</c> (no such catalog game), <c>unavailable</c>
    /// (no archive set and no Vimm binding), <c>duplicate</c> (already queued/completed — skipped), or
    /// <c>queued</c>. Ids are processed in order, so a second id resolving to an already-enqueued URL is
    /// reported as a duplicate rather than enqueued twice.
    /// </summary>
    public static async Task<CatalogQueueBatchResponse> ResolveAndQueueBatchAsync(
        IReadOnlyList<int> ids, int? format, CatalogRepository repo, CatalogResolveService resolver,
        QueueRepository queue, CancellationToken ct)
    {
        int queued = 0, skipped = 0, failed = 0;
        var results = new List<CatalogQueueResultDto>(ids.Count);
        foreach (var id in ids)
        {
            var game = await repo.GetGameByIdAsync(id);
            if (game is null) { failed++; results.Add(new(id, "unknown", null)); continue; }

            var resolved = await resolver.ResolveForQueueAsync(id, game.Value.Console, game.Value.Name, format, ct);
            if (resolved is null) { failed++; results.Add(new(id, "unavailable", null)); continue; }
            var (url, source, fmt) = resolved.Value;

            if ((await queue.CheckDuplicatesAsync([url])).Count > 0) { skipped++; results.Add(new(id, "duplicate", source)); continue; }

            await queue.AddToQueueAsync(url, fmt, source);
            queued++; results.Add(new(id, "queued", source));
        }
        return new CatalogQueueBatchResponse(queued, skipped, failed, results);
    }
}
