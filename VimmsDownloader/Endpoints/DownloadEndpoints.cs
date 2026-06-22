static class DownloadEndpoints
{
    /// <summary>Directory portion of a stored filepath, or null if unavailable.</summary>
    private static string? DirOf(string? filepath)
        => !string.IsNullOrEmpty(filepath) && Path.GetDirectoryName(filepath) is { Length: > 0 } d ? d : null;

    /// <summary>Human label for a download format in a cross-format duplicate warning (PS3 conventions).</summary>
    private static string FormatLabel(int? format) => format switch
    {
        0 => "JB Folder (.7z)",
        1 => ".dec.iso",
        null => "another format",
        _ => $"format {format}",
    };

    public static void MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/queue", async (AddRequest req, QueueRepository repo,
            DownloadQueue queue, IServiceProvider services, ILogger<QueueRepository> logger) =>
        {
            var urls = req.Urls;

            if (!req.Force && urls.Count > 0)
            {
                var incomingFormat = req.Format ?? 0;
                var dbMatches = await repo.CheckDuplicatesAsync(urls);
                // Cross-format / cross-source matches (same catalog game, different format/source).
                var gameMatches = await repo.CheckGameDuplicatesAsync(urls, incomingFormat);
                if (dbMatches.Count > 0 || gameMatches.Count > 0)
                {
                    var completedDir = Path.Combine(queue.GetBasePath(), "completed");
                    var duplicates = new List<DuplicateInfo>();
                    // Dedup cross-format entries by (url, existing format) across both passes.
                    var crossSeen = new HashSet<(string, int?)>();

                    void AddCrossFormat(QueueRepository.DuplicateDbMatch m)
                    {
                        if (!crossSeen.Add((m.Url.ToLowerInvariant(), m.Format))) return;
                        duplicates.Add(new DuplicateInfo(m.Url, m.Source,
                            $"Already have this game as {FormatLabel(m.Format)}",
                            m.Title, m.Filename, m.IsoFilename, false, false,
                            CrossFormat: true, ExistingFormat: m.Format));
                    }

                    foreach (var m in dbMatches)
                    {
                        // Same game, different format (e.g. queued/own format 0, adding format 1):
                        // a soft cross-format warning, not the exact-dup disk check.
                        if (m.Format is int f && f != incomingFormat)
                        {
                            AddCrossFormat(m);
                            continue;
                        }

                        if (m.Source == "queued")
                        {
                            duplicates.Add(new DuplicateInfo(m.Url, "queued", "Already in download queue",
                                m.Title, null, null, false, false));
                            continue;
                        }

                        // Files live in a per-console subfolder (completed/ps3/, …).
                        // Use the stored filepath's directory so disk checks look in the
                        // right place; fall back to the completed root for legacy items.
                        var itemDir = DirOf(m.Filepath) ?? completedDir;

                        // Delegate to pipeline — each console defines its own duplicate rules
                        var pipeline = queue.GetPipeline(m.Platform);
                        if (pipeline != null)
                        {
                            var result = pipeline.CheckDuplicate(itemDir, m.Filename, m.IsoFilename, m.ConvPhase);
                            if (result == null) continue;
                            duplicates.Add(new DuplicateInfo(m.Url, "completed", result.Reason,
                                m.Title, m.Filename, m.IsoFilename, result.ArchiveExists, result.IsoExists));
                        }
                        else
                        {
                            // Generic fallback for non-pipeline platforms
                            var archiveExists = m.Filename != null && File.Exists(Path.Combine(itemDir, m.Filename));
                            if (!archiveExists) continue;
                            duplicates.Add(new DuplicateInfo(m.Url, "completed", "Already downloaded",
                                m.Title, m.Filename, null, archiveExists, false));
                        }
                    }

                    // Same-game-different-source matches (different URL) the URL pass can't see.
                    foreach (var gm in gameMatches)
                        AddCrossFormat(gm);

                    if (duplicates.Count > 0)
                        return Results.Ok(new AddResponse(null, duplicates));
                }
            }

            var source = string.IsNullOrWhiteSpace(req.Source) ? "vimm" : req.Source;
            foreach (var url in urls)
                await repo.AddToQueueAsync(url, req.Format ?? 0, source);

            // Background metadata fetch is Vimm page-scraping; other sources derive
            // their metadata differently (see later phases), so gate it on Vimm.
            if (urls.Count > 0 && source == "vimm")
                MetadataFetcher.FetchInBackground(urls, services, logger);

            return Results.Ok(new AddResponse(await repo.GetQueueIdsAsync(), null));
        });

        app.MapDelete("/api/queue/{id:int}", async (int id, QueueRepository repo) =>
        {
            await repo.DeleteFromQueueAsync(id);
            return Results.Ok();
        });

        // Merged: move + format into PATCH
        app.MapPatch("/api/queue/{id:int}", async (int id, QueuePatchRequest req, QueueRepository repo) =>
        {
            if (req.Direction != null)
                return await repo.MoveInQueueAsync(id, req.Direction) ? Results.Ok() : Results.NotFound();

            if (req.Format.HasValue)
            {
                await repo.SetFormatAsync(id, req.Format.Value);
                return Results.Ok();
            }
            return Results.BadRequest();
        });

        app.MapPost("/api/queue/reorder", async (QueueReorderRequest req, QueueRepository repo) =>
        {
            await repo.ReorderQueueAsync(req.Ids);
            return Results.Ok();
        });

        app.MapDelete("/api/queue", async (QueueRepository repo) =>
        {
            await repo.ClearQueueAsync();
            return Results.Ok();
        });

        app.MapDelete("/api/completed/{id:int}", async (int id, bool? deleteFiles, QueueRepository repo, DownloadQueue queue) =>
        {
            if (deleteFiles == true)
            {
                var item = await repo.GetCompletedByIdAsync(id);
                if (item != null)
                {
                    var (filepath, filename, isoFilename) = item.Value;
                    var completedDir = Path.Combine(queue.GetBasePath(), "completed");
                    // The ISO sits in the same per-console folder as the archive.
                    var itemDir = DirOf(filepath) ?? completedDir;
                    // Delete archive
                    if (filepath != null) try { File.Delete(filepath); } catch { }
                    else if (filename != null) try { File.Delete(Path.Combine(itemDir, filename)); } catch { }
                    // Delete ISO
                    if (isoFilename != null) try { File.Delete(Path.Combine(itemDir, isoFilename)); } catch { }
                }
            }
            await repo.DeleteCompletedAsync(id);
            return Results.Ok();
        });

        app.MapGet("/api/queue/export", async (QueueRepository repo) =>
        {
            var items = (await repo.GetQueueIdsAsync())
                .Select(q => new QueueExportItem(q.Url, q.Format))
                .ToList();
            return Results.Ok(items);
        });

        app.MapPost("/api/queue/import", async (List<QueueExportItem> items, QueueRepository repo,
            IServiceProvider services, ILogger<QueueRepository> logger) =>
        {
            var existing = new HashSet<string>(
                (await repo.GetQueueIdsAsync()).Select(q => q.Url), StringComparer.OrdinalIgnoreCase);

            int added = 0, skipped = 0;
            var newUrls = new List<string>();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Url) || existing.Contains(item.Url))
                { skipped++; continue; }

                await repo.AddToQueueAsync(item.Url, item.Format);
                existing.Add(item.Url);
                newUrls.Add(item.Url);
                added++;
            }

            if (newUrls.Count > 0)
                MetadataFetcher.FetchInBackground(newUrls, services, logger);

            return Results.Ok(new QueueImportResponse(added, skipped));
        });
    }

}
