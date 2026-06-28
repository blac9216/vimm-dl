using System.Text.Json;

namespace Module.Catalog;

/// <summary>One IGDB game's ranking signals: its name, the blended <c>total_rating</c> (0-100, IGDB user
/// + external critic scores), and <c>total_rating_count</c> (how many ratings back it — vote volume /
/// confidence).</summary>
public sealed record IgdbRating(string Name, double TotalRating, int TotalRatingCount);

/// <summary>
/// Pure, web-free parsing + name-matching for the IGDB ranking ingest (epic #123 / R1). The host
/// <c>IgdbRankSyncService</c> fetches the JSON (Apicalypse over HTTP) and stores the result; here we just
/// parse it and join it to the catalog by the same normalized name key the description sync uses
/// (<see cref="IgdbDescriptions.Key"/>), so a catalog "Chrono Trigger (USA)" lines up with IGDB's bare
/// "Chrono Trigger". AOT-safe (JsonDocument DOM, no reflection).
/// </summary>
public static class IgdbRatings
{
    /// <summary>
    /// Parse an IGDB <c>/v4/games</c> JSON array into rated games. Entries without a name or without a
    /// <c>total_rating</c> are skipped (they can't be matched or ranked).
    /// </summary>
    public static IReadOnlyList<IgdbRating> ParseGames(string json)
    {
        var list = new List<IgdbRating>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var name = Str(el, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (Num(el, "total_rating") is not { } rating) continue; // no rating → can't rank
            var count = Num(el, "total_rating_count") is { } c ? (int)c : 0;
            list.Add(new IgdbRating(name!, rating, count));
        }
        return list;
    }

    /// <summary>
    /// Index IGDB games by normalized name → rating. On a normalized-name collision the first wins
    /// (stable — a later duplicate never overwrites), matching the description index's behaviour.
    /// </summary>
    public static Dictionary<string, IgdbRating> BuildIndex(IEnumerable<IgdbRating> games)
    {
        var index = new Dictionary<string, IgdbRating>();
        foreach (var g in games)
        {
            var key = IgdbDescriptions.Key(g.Name);
            if (key.Length > 0) index.TryAdd(key, g);
        }
        return index;
    }

    /// <summary>
    /// Join catalog games to ratings via normalized name. Returns the (gameId, rating, count) triples
    /// that matched the index.
    /// </summary>
    public static IReadOnlyList<(long Id, double Rating, int Count)> Match(
        IReadOnlyDictionary<string, IgdbRating> index, IEnumerable<(long Id, string Name)> catalogGames)
    {
        var matched = new List<(long, double, int)>();
        foreach (var (id, name) in catalogGames)
            if (index.TryGetValue(IgdbDescriptions.Key(name), out var r))
                matched.Add((id, r.TotalRating, r.TotalRatingCount));
        return matched;
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
