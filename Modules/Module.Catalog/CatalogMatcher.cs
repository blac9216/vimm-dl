using System.Text;

namespace Module.Catalog;

/// <summary>
/// Matches local files to catalog games by console + normalised name. Pure and testable.
/// Console-scoped so identically-named games on different systems never cross-match; tolerant
/// of punctuation and archive/rom extension differences (it compares the filename stem). Best-effort
/// by name — hash-based verification is a separate concern.
/// </summary>
public static class CatalogMatcher
{
    /// <summary>Lowercase; every run of non-alphanumeric characters becomes one space; trimmed.</summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        bool pendingSpace = false;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else pendingSpace = true;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Map catalog game id → the local file that satisfies it. A file matches a game iff they share a
    /// console and their normalised names are equal, where the file's name is its filename minus extension.
    /// </summary>
    public static Dictionary<long, string> Match(
        IEnumerable<(long Id, string Console, string Name)> games,
        IEnumerable<(string Console, string Path)> localFiles)
    {
        var index = new Dictionary<string, long>();
        foreach (var (id, console, name) in games)
            index[Key(console, name)] = id; // on duplicate names a later entry wins; acceptable

        var owned = new Dictionary<long, string>();
        foreach (var (console, path) in localFiles)
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(path);
            if (index.TryGetValue(Key(console, stem), out var id))
                owned[id] = path;
        }
        return owned;
    }

    private static string Key(string console, string name) => console + "|" + Normalize(name);
}
