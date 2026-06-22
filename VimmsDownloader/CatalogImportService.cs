/// <summary>
/// The local-import background job (epic #118 / L2): walks the configured <b>import</b> drop folder and
/// hands each file to the L1 <see cref="ImportService"/> (hash-match → place into <c>completed/{console}/</c>
/// or set aside in <c>rejected/</c>), logging a per-file event so the UI can show what happened. Raw files
/// only — archives (.zip/.7z) are L3 (#126); they currently hash as opaque bytes and fall to rejected.
/// Mirrors the other catalog jobs (scan/verify): single-flight via <c>CatalogImportState</c>, the UI polls
/// <c>/api/catalog/status</c> while it runs.
/// </summary>
class CatalogImportService(ImportService import, QueueRepository queue, ILogger<CatalogImportService> log)
{
    /// <summary>Default drop folder when <c>import_path</c> is unset: <c>downloads/import</c>.</summary>
    public string DefaultImportDir => Path.Combine(queue.GetDownloadPath(), "import");

    /// <summary>Default reject folder when <c>rejected_path</c> is unset: <c>downloads/rejected</c>.</summary>
    public string DefaultRejectedDir => Path.Combine(queue.GetDownloadPath(), "rejected");

    /// <summary>Ingest every top-level file in the import folder; returns a matched/rejected summary.</summary>
    public async Task<ImportSummary> ImportAsync(CancellationToken ct)
    {
        var importDir = await ResolveAsync(SettingsKeys.ImportPath, DefaultImportDir);
        var rejectedDir = await ResolveAsync(SettingsKeys.RejectedPath, DefaultRejectedDir);
        Directory.CreateDirectory(importDir);

        var matched = 0;
        var rejected = 0;
        // Top-level files only: subfolders / multi-file disc sets are handled later (epic open question).
        foreach (var file in Directory.EnumerateFiles(importDir))
        {
            ct.ThrowIfCancellationRequested();
            var result = await import.ImportFileAsync(file, rejectedDir, ct);
            await RecordAsync(result);
            if (result.Outcome == ImportOutcome.Matched) matched++; else rejected++;
        }

        log.LogInformation("Import: {Matched} matched, {Rejected} rejected from {Dir}", matched, rejected, importDir);
        return new ImportSummary(matched + rejected, matched, rejected);
    }

    /// <summary>The configured path (trimmed), or the default when the setting is unset/blank.</summary>
    private async Task<string> ResolveAsync(string key, string fallback)
    {
        var configured = (await queue.GetSettingAsync(key))?.Trim();
        return string.IsNullOrEmpty(configured) ? fallback : configured;
    }

    /// <summary>Append one per-file event (type <c>import</c>, phase matched/rejected) for the events log.</summary>
    private async Task RecordAsync(ImportResult r)
    {
        if (r.Outcome == ImportOutcome.Matched)
        {
            var data = $"{{\"gameId\":{r.GameId},\"console\":\"{Escape(r.Console)}\",\"matchKind\":\"{Escape(r.MatchKind)}\",\"dest\":\"{Escape(r.DestPath)}\"}}";
            await queue.AppendEventAsync(r.FileName, "import", "matched",
                $"Matched game {r.GameId} → completed/{r.Console}/", data, gameId: r.GameId);
        }
        else
        {
            var data = $"{{\"reason\":\"{Escape(r.Reason)}\"}}";
            await queue.AppendEventAsync(r.FileName, "import", "rejected", $"Rejected: {r.Reason}", data);
        }
    }

    private static string Escape(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>Per-run import tally (total files processed, and the matched/rejected split).</summary>
sealed record ImportSummary(int Total, int Matched, int Rejected);
