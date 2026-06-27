using System.Buffers.Binary;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Module.Core;
using Module.Core.Pipeline;
using Module.WiiUPipeline.Bridge;
using Module.WiiUTools;

namespace Module.WiiUPipeline;

/// <summary>
/// Wii U post-download pipeline: turns the encrypted WUP set downloaded by the NUS source (W5) into a
/// decrypted, Cemu-ready title folder. Implements <see cref="IPipeline"/> so the host treats it just like
/// the PS3 pipeline. Per title: Fetching (read TMD/ticket) → Decrypting (AES-128-CBC per content, key from
/// the ticket + <see cref="ITitleKeyProvider"/>) → Extracting (U8 archives in decrypted content) →
/// Packaging (assemble the decrypted folder) → Done.
///
/// Clean-room: composes the W1–W3 tools (all from public specs). No keys are embedded — when the user has
/// not configured a key the pipeline stops at a clear "keys required" state instead of crashing.
///
/// Scope note: non-hashed content is fully decrypted; hashed content (a <c>.h3</c> hash tree) is decrypted
/// as raw CBC blocks and flagged — full de-hashing and FST-driven file layout are follow-ups (they need
/// real titles to validate). The packaged output is the decrypted content folder.
/// </summary>
public class WiiUConversionPipeline : IPipeline
{
    private readonly PipelineState _state;
    private readonly ITitleKeyProvider _keys;
    private readonly Channel<WiiUJob> _queue = Channel.CreateUnbounded<WiiUJob>();
    private int _started;
    private int _maxParallelism = 2;

    /// <summary>Name of the decrypted-output subfolder created inside a title's folder.</summary>
    public const string DecryptedDirName = "decrypted";

    private record WiiUJob(string TitleFolder);

    public WiiUConversionPipeline(IWiiUPipelineBridge bridge, ITitleKeyProvider keys,
        ILogger<WiiUConversionPipeline> log)
    {
        _state = new PipelineState(bridge, log);
        _keys = keys;
    }

    public void Configure(int maxParallelism) => _maxParallelism = Math.Max(1, maxParallelism);
    public void SeedConverted(IEnumerable<string> names) => _state.SeedConverted(names);

    /// <summary>Key for an item is the title-id folder name (the last path segment).</summary>
    internal static string KeyFor(string titleFolder) =>
        Path.GetFileName(titleFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Queue a downloaded WUP set (the <c>completed/wiiu/{TitleID}/</c> folder) for decryption.</summary>
    public bool Enqueue(string titleFolder, bool force = false)
    {
        var key = KeyFor(titleFolder);
        if (!force && _state.IsConverted(key)) return false;

        var queued = new PipelineStatusEvent(key, WiiUPhase.Queued, "Waiting...");
        var result = _state.Statuses.AddOrUpdate(key, queued, (_, existing) =>
            PipelinePhase.IsActive(existing.Phase) ? existing : queued);
        if (result != queued) return false;

        var cts = new CancellationTokenSource();
        _state.Cancellations[key] = cts;
        _queue.Writer.TryWrite(new WiiUJob(titleFolder));
        EnsureStarted();
        return true;
    }

    private void EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
        {
            _state.Log.LogInformation("Starting Wii U pipeline with {N} workers", _maxParallelism);
            for (var i = 0; i < _maxParallelism; i++)
                _ = Task.Run(Worker);
        }
    }

    private async Task Worker()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            var key = KeyFor(job.TitleFolder);
            _state.Cancellations.TryGetValue(key, out var cts);
            var ct = cts?.Token ?? CancellationToken.None;
            if (ct.IsCancellationRequested) { _state.Cancellations.TryRemove(key, out _); continue; }

            try { await ProcessTitleAsync(job.TitleFolder, key, ct); }
            catch (OperationCanceledException) { await _state.EmitStatus(key, WiiUPhase.Error, "Aborted by user"); }
            catch (Exception ex)
            {
                _state.Log.LogError(ex, "Wii U pipeline failed for {Title}", key);
                await _state.EmitStatus(key, WiiUPhase.Error, $"Pipeline error: {ex.Message}");
            }
            finally { _state.Cancellations.TryRemove(key, out _); }
        }
    }

    private async Task ProcessTitleAsync(string folder, string key, CancellationToken ct)
    {
        // --- Fetching: read the downloaded set's metadata ---
        await _state.EmitStatus(key, WiiUPhase.Fetching, "Reading title metadata…");
        var tmdPath = Path.Combine(folder, "title.tmd");
        var tikPath = Path.Combine(folder, "title.tik");
        if (!File.Exists(tmdPath) || !File.Exists(tikPath))
        {
            await _state.EmitStatus(key, WiiUPhase.Error, "Missing title.tmd or title.tik in the downloaded set.");
            return;
        }

        var tmdResult = Tmd.Parse(await File.ReadAllBytesAsync(tmdPath, ct));
        if (!tmdResult.IsOk) { await _state.EmitStatus(key, WiiUPhase.Error, $"TMD: {tmdResult.Error}"); return; }
        var ticketResult = Ticket.Parse(await File.ReadAllBytesAsync(tikPath, ct));
        if (!ticketResult.IsOk) { await _state.EmitStatus(key, WiiUPhase.Error, $"Ticket: {ticketResult.Error}"); return; }
        var tmd = tmdResult.Value!;
        var ticket = ticketResult.Value!;

        // --- Resolve the title key (no keys → clear "keys required" stop, not a crash) ---
        var titleKey = _keys.GetTitleKey(tmd.TitleIdHex);
        if (titleKey is null)
        {
            var commonKey = _keys.GetCommonKey();
            if (commonKey is null)
            {
                await _state.EmitStatus(key, WiiUPhase.Error,
                    "Wii U keys required — configure the common key in Settings to decrypt this title.");
                return;
            }
            var derived = WiiUCrypto.DecryptTitleKey(ticket.EncryptedTitleKey, ticket.TitleId, commonKey);
            if (!derived.IsOk) { await _state.EmitStatus(key, WiiUPhase.Error, $"Title key: {derived.Error}"); return; }
            titleKey = derived.Value!;
        }

        // --- Decrypting: AES-128-CBC each content into decrypted/ ---
        var decryptedDir = Path.Combine(folder, DecryptedDirName);
        Directory.CreateDirectory(decryptedDir);
        for (var i = 0; i < tmd.Contents.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var content = tmd.Contents[i];
            var appPath = Path.Combine(folder, $"{content.ContentIdHex}.app");
            if (!File.Exists(appPath))
            {
                await _state.EmitStatus(key, WiiUPhase.Error, $"Missing content {content.ContentIdHex}.app.");
                return;
            }

            var note = content.IsHashed ? " (hashed — raw blocks, de-hashing pending)" : "";
            await _state.EmitStatus(key, WiiUPhase.Decrypting,
                $"Decrypting content {i + 1}/{tmd.Contents.Count} ({content.ContentIdHex}){note}");

            var decPath = Path.Combine(decryptedDir, $"{content.ContentIdHex}.app");
            await using (var input = File.OpenRead(appPath))
            await using (var output = File.Create(decPath))
            {
                var dec = await WiiUCrypto.DecryptContentAsync(input, output, titleKey, content.Index, ct);
                if (!dec.IsOk) { await _state.EmitStatus(key, WiiUPhase.Error, $"Decrypt {content.ContentIdHex}: {dec.Error}"); return; }
            }

            // Encrypted .app is 16-byte aligned; trim the decrypted file to the content's true size.
            if (content.Size > 0 && new FileInfo(decPath).Length > (long)content.Size)
            {
                await using var fs = new FileStream(decPath, FileMode.Open, FileAccess.Write);
                fs.SetLength((long)content.Size);
            }
        }

        // --- Extracting: assemble the real file layout (FST), and pull files out of any U8 archives ---
        await _state.EmitStatus(key, WiiUPhase.Extracting, "Extracting files…");

        // FST-driven layout: if a content is an FST, lay the title's files out at their real paths (#223).
        var assembled = AssembleFromFst(tmd, decryptedDir, ct);

        var extracted = 0;
        foreach (var content in tmd.Contents)
        {
            ct.ThrowIfCancellationRequested();
            if (content.IsHashed) continue; // raw hashed blocks aren't a parseable archive yet
            var decPath = Path.Combine(decryptedDir, $"{content.ContentIdHex}.app");
            if (!IsU8(decPath)) continue;
            var outDir = Path.Combine(decryptedDir, content.ContentIdHex);
            var result = ExtractU8(decPath, outDir);
            if (result.IsOk) extracted += result.Value;
            else _state.Log.LogWarning("U8 extract for {Id} failed: {Error}", content.ContentIdHex, result.Error);
        }

        // --- Packaging: the decrypted folder is the deliverable ---
        await _state.EmitStatus(key, WiiUPhase.Packaging,
            $"Assembling decrypted title ({tmd.Contents.Count} content(s), {assembled} FST file(s), {extracted} U8 file(s))…");

        await _state.EmitStatus(key, WiiUPhase.Done, $"Decrypted: {key}", DecryptedDirName);
        _state.AddToConvertedList(key);
        _state.Log.LogInformation("Wii U title decrypted: {Title} -> {Dir}", key, decryptedDir);
    }

    /// <summary>
    /// If a decrypted content is an FST (filesystem table — magic <c>FST\0</c>), parse it (W3) and assemble
    /// the title's real files into the decrypted folder at their FST paths, reading each entry from the
    /// decrypted content its secondary index points at (offset × offset-factor). Returns the file count.
    ///
    /// Additive + best-effort: returns 0 (leaving the raw .app files in place) when there's no FST or it
    /// can't be resolved, so nothing regresses. Clean-room from the FST structure (W3); the offset/section
    /// semantics are synthetic-validated — real-title confirmation (and hashed-content support) is tracked
    /// in #231, so hashed source content is skipped here.
    /// </summary>
    private int AssembleFromFst(Tmd tmd, string decryptedDir, CancellationToken ct)
    {
        string? fstPath = null;
        foreach (var c in tmd.Contents)
        {
            if (c.IsHashed) continue;
            var p = Path.Combine(decryptedDir, $"{c.ContentIdHex}.app");
            if (IsFst(p)) { fstPath = p; break; }
        }
        if (fstPath == null) return 0;

        var parsed = Fst.Parse(File.ReadAllBytes(fstPath));
        if (!parsed.IsOk) { _state.Log.LogWarning("FST parse failed: {Error}", parsed.Error); return 0; }
        var fst = parsed.Value!;

        var count = 0;
        foreach (var entry in fst.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsDirectory) continue;
            if (entry.SecondaryIndex >= tmd.Contents.Count) continue; // unknown section — skip
            var source = tmd.Contents[entry.SecondaryIndex];
            if (source.IsHashed) continue; // raw hashed source not yet de-hashed (#231)
            var sourcePath = Path.Combine(decryptedDir, $"{source.ContentIdHex}.app");
            if (!File.Exists(sourcePath)) continue;

            var byteOffset = (long)entry.Offset * fst.OffsetFactor;
            var dest = Path.Combine(decryptedDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                using var src = File.OpenRead(sourcePath);
                if (byteOffset + entry.Size > src.Length) continue; // out of range — skip defensively
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                src.Seek(byteOffset, SeekOrigin.Begin);
                using var dst = File.Create(dest);
                CopyExactly(src, dst, entry.Size);
                count++;
            }
            catch (Exception ex) { _state.Log.LogWarning("FST extract {Path} failed: {Error}", entry.Path, ex.Message); }
        }
        return count;
    }

    private static void CopyExactly(Stream src, Stream dst, uint count)
    {
        var buffer = new byte[81920];
        var remaining = (long)count;
        while (remaining > 0)
        {
            var read = src.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) break;
            dst.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    /// <summary>True if the file begins with the FST magic (0x46535400, "FST\0").</summary>
    private static bool IsFst(string path) => HasMagic(path, 0x46535400);

    /// <summary>True if the file begins with the U8 magic (0x55AA382D).</summary>
    private static bool IsU8(string path) => HasMagic(path, 0x55AA382D);

    private static bool HasMagic(string path, uint magic)
    {
        try
        {
            Span<byte> head = stackalloc byte[4];
            using var fs = File.OpenRead(path);
            return fs.Read(head) == 4 && BinaryPrimitives.ReadUInt32BigEndian(head) == magic;
        }
        catch { return false; }
    }

    /// <summary>Extract a decrypted U8 archive's files into <paramref name="destDir"/>; returns the file count.</summary>
    private static Result<int> ExtractU8(string u8Path, string destDir)
    {
        byte[] data;
        try { data = File.ReadAllBytes(u8Path); }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }

        var parsed = U8Archive.Parse(data);
        if (!parsed.IsOk) return Result<int>.Fail(parsed.Error!);

        Directory.CreateDirectory(destDir);
        var count = 0;
        foreach (var entry in parsed.Value!.Entries)
        {
            var rel = entry.Path.Replace('/', Path.DirectorySeparatorChar);
            var dest = Path.Combine(destDir, rel);
            if (entry.IsDirectory) { Directory.CreateDirectory(dest); continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var end = (long)entry.Offset + entry.Size;
            if (entry.Offset <= (uint)data.Length && end <= data.Length)
            {
                File.WriteAllBytes(dest, data[(int)entry.Offset..(int)end]);
                count++;
            }
        }
        return Result<int>.Ok(count);
    }

    // --- IPipeline ---

    public List<PipelineStatusEvent> GetStatuses() => _state.GetStatuses();
    public bool Abort(string itemName) => _state.Abort(itemName);
    public bool IsConverted(string itemName) => _state.IsConverted(itemName);
    public void MarkConverted(string itemName) => _state.MarkConverted(itemName);
    public Dictionary<string, long> GetStepDurations(string itemName) => _state.GetStepDurations(itemName);

    public PipelineFlowInfo? BuildFlow(string? phase, string? message, bool fileExists)
    {
        if (phase == null)
        {
            if (!fileExists) return null;
            return new PipelineFlowInfo("wiiu", [], ["convert", "mark-done"]);
        }

        var steps = new List<PipelineFlowStep>
        {
            new("Fetch", StepStatus(phase, WiiUPhase.Fetching), Msg(phase, WiiUPhase.Fetching, message)),
            new("Decrypt", StepStatus(phase, WiiUPhase.Decrypting), Msg(phase, WiiUPhase.Decrypting, message)),
            new("Extract", StepStatus(phase, WiiUPhase.Extracting), Msg(phase, WiiUPhase.Extracting, message)),
            new("Package", StepStatus(phase, WiiUPhase.Packaging), Msg(phase, WiiUPhase.Packaging, message)),
        };
        return new PipelineFlowInfo("wiiu", steps, BuildActions(phase, fileExists));
    }

    // Order of the Wii U phases, used to map "is this step done/active/pending" for the trace.
    private static readonly string[] PhaseOrder =
        [WiiUPhase.Queued, WiiUPhase.Fetching, WiiUPhase.Decrypting, WiiUPhase.Extracting, WiiUPhase.Packaging, WiiUPhase.Done];

    private static string StepStatus(string current, string step)
    {
        if (current == PipelinePhase.Error)
            return step == WiiUPhase.Packaging ? "error" : "done"; // error surfaces on the last step
        if (current == PipelinePhase.Skipped) return "skipped";
        if (current == PipelinePhase.Done) return "done";

        var ci = Array.IndexOf(PhaseOrder, current);
        var si = Array.IndexOf(PhaseOrder, step);
        if (ci < 0 || si < 0) return "pending";
        if (si < ci) return "done";
        if (si == ci) return "active";
        return "pending";
    }

    private static string? Msg(string current, string step, string? message) =>
        StepStatus(current, step) is "active" or "error" ? message : null;

    private static List<string> BuildActions(string? phase, bool fileExists)
    {
        if (phase == null && fileExists) return ["convert", "mark-done"];
        if (PipelinePhase.IsActive(phase)) return ["abort"];
        if (phase == PipelinePhase.Error) return ["retry"];
        return [];
    }

    public DuplicateCheckResult? CheckDuplicate(string completedDir, string? filename, string? isoFilename, string? convPhase)
    {
        // Active decryption — block (the pipeline is mid-run).
        if (PipelinePhase.IsActive(convPhase))
            return new DuplicateCheckResult("Already downloaded (decryption in progress)", true, false);

        // The downloaded set lands in completedDir/{titleId}/; "decrypted/" appears once processed.
        var titleDir = filename != null ? Path.Combine(completedDir, filename) : completedDir;
        var setExists = File.Exists(Path.Combine(titleDir, "title.tmd"));
        var decryptedExists = Directory.Exists(Path.Combine(titleDir, DecryptedDirName));

        if (!setExists && !decryptedExists) return null; // nothing on disk → not a duplicate

        var reason = decryptedExists
            ? "Already decrypted"
            : "Already downloaded (not yet decrypted)";
        return new DuplicateCheckResult(reason, setExists, decryptedExists);
    }
}
