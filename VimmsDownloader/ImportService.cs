using Module.Catalog;
using Module.Core;

/// <summary>
/// Local catalog import (epic #118): given a user-supplied file, hash it and match it to the catalog by
/// content (SHA1 → MD5 → CRC32), exactly the way the Vimm binding and the verify pass match. On a hit the
/// file is moved into <c>completed/{console}/</c> (the matched game's console) and the game is marked
/// owned; on a miss it's moved to a <c>rejected/</c> folder (kept, never deleted) with a reason.
///
/// <para>Header-aware (L1): for systems whose DAT hash is of the headerless ROM (iNES/FDS/Lynx/7800), a
/// locally-headered file is hashed both as-is and with its header stripped, and a match on either wins
/// (see <see cref="RomHeaders"/>). Archives (L3): a <c>.zip</c>/<c>.7z</c> is extracted to a temp dir and
/// each inner file imported by its own hash — so multi-ROM and <c>.bin</c>/<c>.cue</c> disc archives match
/// per file — then the archive wrapper is discarded.</para>
/// </summary>
class ImportService(CatalogRepository catalog, QueueRepository queue, IArchiveExtractor extractor, ILogger<ImportService> log)
{
    /// <summary>Import one path, placing the rejected folder beside <c>completed/</c> by default.</summary>
    public Task<IReadOnlyList<ImportResult>> ImportFileAsync(string filePath, CancellationToken ct)
        => ImportFileAsync(filePath, Path.Combine(queue.GetDownloadPath(), "rejected"), ct);

    /// <summary>
    /// Import one path, sending non-matches to <paramref name="rejectedRoot"/> (configurable in L2). A raw
    /// file yields one result; an archive yields one per inner file. The completed root is always
    /// <c>downloads/completed</c>.
    /// </summary>
    public async Task<IReadOnlyList<ImportResult>> ImportFileAsync(string filePath, string rejectedRoot, CancellationToken ct)
    {
        if (FileExtensions.IsArchive(filePath))
            return await ImportArchiveAsync(filePath, rejectedRoot, ct);
        return [await ImportRawFileAsync(filePath, rejectedRoot, ct)];
    }

    /// <summary>Extract an archive to a temp dir, import each inner file by hash, then discard the archive.</summary>
    private async Task<IReadOnlyList<ImportResult>> ImportArchiveAsync(string archivePath, string rejectedRoot, CancellationToken ct)
    {
        var name = Path.GetFileName(archivePath);
        var tempDir = Path.Combine(Path.GetTempPath(), $"vimm-import-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var extracted = await extractor.ExtractAsync(archivePath, tempDir, ct);
            if (!extracted.IsOk)
                return [Reject(archivePath, name, rejectedRoot, $"extract failed: {extracted.Error}")];

            var inner = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories).ToList();
            if (inner.Count == 0)
                return [Reject(archivePath, name, rejectedRoot, "archive contained no files")];

            var results = new List<ImportResult>(inner.Count);
            foreach (var f in inner)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await ImportRawFileAsync(f, rejectedRoot, ct));
            }

            // Contents are now placed (completed/) or set aside (rejected/); discard the archive wrapper.
            FileOps.TryDelete(archivePath);
            return results;
        }
        finally
        {
            FileOps.TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>Hash one raw file, place it into <c>completed/{console}/</c> on a match, else reject it.</summary>
    private async Task<ImportResult> ImportRawFileAsync(string filePath, string rejectedRoot, CancellationToken ct)
    {
        var name = Path.GetFileName(filePath);
        var completedRoot = Path.Combine(queue.GetDownloadPath(), "completed");

        // Hash the file as-is; if that misses and the file carries a known header, retry on the headerless
        // bytes (No-Intro hashes the headerless ROM for these systems).
        var asIs = TryHash(filePath, 0);
        if (asIs is not { } full)
            return Reject(filePath, name, rejectedRoot, "unreadable (could not hash)");

        var match = await catalog.FindGameByHashAsync(full, ct);
        if (match is null)
        {
            var headerLen = DetectHeader(filePath);
            if (headerLen > 0 && TryHash(filePath, headerLen) is { } headerless)
                match = await catalog.FindGameByHashAsync(headerless, ct);
        }

        if (match is null)
            return Reject(filePath, name, rejectedRoot, "no catalog hash match");

        // Place into the matched game's console folder and record ownership.
        var destDir = Path.Combine(completedRoot, match.Console);
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, name);
        var moved = FileOps.TryMove(filePath, dest);
        if (!moved.IsOk)
            return Reject(filePath, name, rejectedRoot, $"matched game {match.GameId} but move failed: {moved.Error}");

        await catalog.MarkOwnedAsync(match.GameId, dest, match.MatchKind, ct);
        log.LogInformation("Import: {File} matched game {GameId} ({Kind}) → {Dest}", name, match.GameId, match.MatchKind, dest);
        return ImportResult.Matched(name, dest, match.GameId, match.Console, match.MatchKind);
    }

    /// <summary>Move a non-match into the rejected folder (kept for review) and report why.</summary>
    private ImportResult Reject(string filePath, string name, string rejectedRoot, string reason)
    {
        Directory.CreateDirectory(rejectedRoot);
        var dest = Path.Combine(rejectedRoot, name);
        var moved = FileOps.TryMove(filePath, dest);
        log.LogInformation("Import: {File} rejected ({Reason})", name, reason);
        // If even the move failed the file stays put; surface that but still report the rejection reason.
        return ImportResult.Rejected(name, moved.IsOk ? dest : null, reason);
    }

    /// <summary>Compute CRC32/MD5/SHA1 over the file, skipping <paramref name="skip"/> leading bytes, or null if unreadable.</summary>
    private static FileHashes.Hashes? TryHash(string filePath, int skip)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            if (skip > 0) fs.Seek(skip, SeekOrigin.Begin);
            return FileHashes.ComputeAll(fs);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Peek the file's leading bytes and return the length of any recognised header (0 if none).</summary>
    private static int DetectHeader(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> head = stackalloc byte[RomHeaders.MaxHeaderLength];
            var got = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return RomHeaders.DetectHeaderLength(head[..got]);
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>Whether a file was placed into the catalog or set aside for review.</summary>
enum ImportOutcome { Matched, Rejected }

/// <summary>
/// The outcome of importing one file — enough for the host (L2) to log a per-file event: where a match
/// landed and which game/hash, or why a file was rejected.
/// </summary>
sealed record ImportResult(
    ImportOutcome Outcome,
    string FileName,
    string? DestPath,
    long? GameId,
    string? Console,
    string? MatchKind,
    string? Reason)
{
    public static ImportResult Matched(string fileName, string destPath, long gameId, string console, string matchKind)
        => new(ImportOutcome.Matched, fileName, destPath, gameId, console, matchKind, null);

    public static ImportResult Rejected(string fileName, string? destPath, string reason)
        => new(ImportOutcome.Rejected, fileName, destPath, null, null, null, reason);
}
