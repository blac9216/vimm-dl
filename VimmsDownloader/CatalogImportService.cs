/// <summary>
/// The local-import background job (epic #118 / L2): walks the configured <b>import</b> drop folder and
/// hands each file to the L1 <see cref="ImportService"/> (hash-match → place into <c>completed/{console}/</c>
/// or set aside in <c>rejected/</c>), logging a per-file event so the UI can show what happened. Archives
/// (.zip/.7z) are extracted and matched by their inner ROMs (L3 / #126), so one archive can yield several
/// per-file results. Mirrors the other catalog jobs (scan/verify): single-flight via
/// <c>CatalogImportState</c>, the UI polls <c>/api/catalog/status</c> while it runs.
/// </summary>
class CatalogImportService(ImportService import, QueueRepository queue, ILogger<CatalogImportService> log)
{
    /// <summary>Default drop folder when <c>import_path</c> is unset: <c>downloads/import</c>.</summary>
    public string DefaultImportDir => Path.Combine(queue.GetDownloadPath(), "import");

    /// <summary>Default reject folder when <c>rejected_path</c> is unset: <c>downloads/rejected</c>.</summary>
    public string DefaultRejectedDir => Path.Combine(queue.GetDownloadPath(), "rejected");

    /// <summary>
    /// Ingest every top-level file in the import folder; returns a matched/rejected summary.
    /// <paramref name="report"/>, when given, is called once per file (message = file name, current =
    /// 1-based file index, total = file count) plus a terminal call carrying the run's
    /// <see cref="ImportSummary"/> counts — the L1 Jobs API progress checkpoint.
    /// </summary>
    public async Task<ImportSummary> ImportAsync(Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var importDir = await ResolveAsync(SettingsKeys.ImportPath, DefaultImportDir);
        var rejectedDir = await ResolveAsync(SettingsKeys.RejectedPath, DefaultRejectedDir);
        Directory.CreateDirectory(importDir);

        var matched = 0;
        var rejected = 0;
        // Top-level files only: subfolders / multi-file disc sets are handled later (epic open question).
        // A raw file yields one result; an archive yields one per inner file (L3) — record + count each.
        var files = Directory.GetFiles(importDir);
        for (var i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            report?.Invoke(Path.GetFileName(file), i + 1, files.Length);
            foreach (var result in await import.ImportFileAsync(file, rejectedDir, ct))
            {
                await RecordAsync(result);
                if (result.Outcome == ImportOutcome.Matched) matched++; else rejected++;
            }
        }

        var summary = new ImportSummary(matched + rejected, matched, rejected);
        log.LogInformation("Import: {Matched} matched, {Rejected} rejected from {Dir}", matched, rejected, importDir);
        report?.Invoke($"Done: {summary.Matched} matched, {summary.Rejected} rejected of {summary.Total}",
            summary.Total, summary.Total);
        return summary;
    }

    public Task<ImportSummary> ImportAsync(CancellationToken ct) => ImportAsync(null, ct);

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
