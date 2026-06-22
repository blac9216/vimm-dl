using System.Text;
using Module.Core;

namespace VimmsDownloader.Tests;

/// <summary>
/// Deterministic <see cref="IArchiveExtractor"/> for import tests (epic #118 / L3): each archive is
/// configured with the inner files it "contains" (written into the output dir on extract) or marked to
/// fail. No real 7z — the real extraction is covered by Module.Extractor's own container tests; here we
/// exercise the import orchestration (extract → hash inner → place/reject → discard archive → temp cleanup).
/// </summary>
sealed class FakeArchiveExtractor : IArchiveExtractor
{
    private readonly Dictionary<string, List<(string Name, byte[] Content)>> _contents = new();
    private readonly HashSet<string> _failing = new();

    /// <summary>Configure what archive <paramref name="archiveName"/> extracts to (relative inner paths).</summary>
    public FakeArchiveExtractor Contains(string archiveName, params (string Name, string Content)[] inner)
    {
        _contents[archiveName] = inner.Select(i => (i.Name, Encoding.UTF8.GetBytes(i.Content))).ToList();
        return this;
    }

    /// <summary>Mark an archive as failing to extract (corrupt / unsupported / no 7z).</summary>
    public FakeArchiveExtractor Fails(string archiveName)
    {
        _failing.Add(archiveName);
        return this;
    }

    public Task<Result<bool>> ExtractAsync(string archivePath, string outputDir, CancellationToken ct)
    {
        var key = Path.GetFileName(archivePath);
        if (_failing.Contains(key)) return Task.FromResult(Result.Fail("simulated extract failure"));
        if (_contents.TryGetValue(key, out var inner))
            foreach (var (name, content) in inner)
            {
                var dest = Path.Combine(outputDir, name);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, content);
            }
        return Task.FromResult(Result.Ok());
    }
}
