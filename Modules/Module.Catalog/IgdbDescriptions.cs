using System.Text.Json;

namespace Module.Catalog;

/// <summary>One IGDB game: its name and the two free-text description fields IGDB exposes.</summary>
public sealed record IgdbGame(string Name, string? Summary, string? Storyline)
{
    /// <summary>The preferred description text — the short <c>summary</c>, else the longer <c>storyline</c>.</summary>
    public string? Description =>
        !string.IsNullOrWhiteSpace(Summary) ? Summary
        : !string.IsNullOrWhiteSpace(Storyline) ? Storyline
        : null;
}

/// <summary>
/// Pure, web-free parsing + name-matching for the IGDB description sync (epic #122 / M2). The host
/// <c>IgdbSyncService</c> fetches the JSON (Apicalypse over HTTP) and stores the result; here we just
/// parse it and join it to the catalog by a name key (see <see cref="Key"/>) that drops the No-Intro
/// parenthetical and applies <see cref="CatalogMatcher"/>'s normalization, so a catalog "Mega Man 2
/// (USA)" lines up with IGDB's bare "Mega Man 2". AOT-safe (JsonDocument DOM, no reflection).
/// </summary>
public static class IgdbDescriptions
{
    /// <summary>Parse an IGDB <c>/v4/games</c> JSON array into games (tolerant of missing fields).</summary>
    public static IReadOnlyList<IgdbGame> ParseGames(string json)
    {
        var list = new List<IgdbGame>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var name = Str(el, "name");
            if (string.IsNullOrWhiteSpace(name)) continue; // a game without a name can't be matched
            list.Add(new IgdbGame(name!, Str(el, "summary"), Str(el, "storyline")));
        }
        return list;
    }

    /// <summary>
    /// Index IGDB games by normalized name → description text. Only entries that actually carry a
    /// description are indexed; on a normalized-name collision the first one with a description wins
    /// (stable — a later duplicate never overwrites).
    /// </summary>
    public static Dictionary<string, string> BuildIndex(IEnumerable<IgdbGame> games)
    {
        var index = new Dictionary<string, string>();
        foreach (var g in games)
        {
            if (g.Description is not { } desc) continue;
            var key = Key(g.Name);
            if (key.Length > 0) index.TryAdd(key, desc);
        }
        return index;
    }

    /// <summary>
    /// Join catalog games to descriptions via normalized name. Returns the (gameId → description) pairs
    /// that matched the index.
    /// </summary>
    public static IReadOnlyList<(long Id, string Description)> Match(
        IReadOnlyDictionary<string, string> index, IEnumerable<(long Id, string Name)> catalogGames)
    {
        var matched = new List<(long, string)>();
        foreach (var (id, name) in catalogGames)
            if (index.TryGetValue(Key(name), out var desc))
                matched.Add((id, desc));
        return matched;
    }

    /// <summary>
    /// The join key for a game name: drop any No-Intro/Redump parenthetical (region / disc / revision —
    /// everything from the first '(') so a catalog "Mega Man 2 (USA)" lines up with IGDB's bare "Mega
    /// Man 2", then apply <see cref="CatalogMatcher.Normalize"/> (lowercase, punctuation → single spaces).
    /// </summary>
    internal static string Key(string name)
    {
        var paren = name.IndexOf('(');
        if (paren >= 0) name = name[..paren];
        return CatalogMatcher.Normalize(name);
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
