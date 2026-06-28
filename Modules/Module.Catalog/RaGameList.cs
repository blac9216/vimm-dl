using System.Text.Json;

namespace Module.Catalog;

/// <summary>One RetroAchievements game from the system game list: its RA id and the ROM MD5 hashes RA
/// recognises for it (lowercased).</summary>
public sealed record RaGame(int Id, IReadOnlyList<string> Hashes);

/// <summary>
/// Pure, web-free parsing for the RetroAchievements popularity ingest (epic #123 / R2). The host
/// <c>RetroAchievementsSyncService</c> fetches the JSON over HTTP and stores the result; here we just
/// parse <c>API_GetGameList</c> (id + recognised ROM MD5 hashes) and <c>API_GetGameExtended</c>
/// (<c>NumDistinctPlayers</c>). The hash-join itself is an exact lowercased-MD5 equality against
/// <c>catalog_rom</c>, so RA games whose hashing differs from No-Intro simply don't match. AOT-safe
/// (JsonDocument DOM, no reflection).
/// </summary>
public static class RaGameList
{
    /// <summary>Parse an <c>API_GetGameList</c> JSON array into games carrying their MD5 hash list (h=1).</summary>
    public static IReadOnlyList<RaGame> ParseGameList(string json)
    {
        var list = new List<RaGame>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            if (!TryId(el, "ID", out var id)) continue;
            var hashes = new List<string>();
            if (el.TryGetProperty("Hashes", out var hs) && hs.ValueKind == JsonValueKind.Array)
                foreach (var h in hs.EnumerateArray())
                    if (h.ValueKind == JsonValueKind.String && h.GetString() is { Length: > 0 } s)
                        hashes.Add(s.Trim().ToLowerInvariant());
            list.Add(new RaGame(id, hashes));
        }
        return list;
    }

    /// <summary>
    /// Index RA games by recognised ROM MD5 → RA id (lowercased). On a hash shared by multiple RA games
    /// the first wins (hashes are effectively unique). This is the lookup the catalog MD5s join against.
    /// </summary>
    public static Dictionary<string, int> BuildHashIndex(IEnumerable<RaGame> games)
    {
        var index = new Dictionary<string, int>();
        foreach (var g in games)
            foreach (var h in g.Hashes)
                if (h.Length > 0) index.TryAdd(h, g.Id);
        return index;
    }

    /// <summary>
    /// Parse the <c>NumDistinctPlayers</c> popularity count from an <c>API_GetGameExtended</c> response
    /// (a single game object). Returns null when the field is absent or non-numeric.
    /// </summary>
    public static int? ParsePlayers(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        return doc.RootElement.TryGetProperty("NumDistinctPlayers", out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : null;
    }

    // RA ids come through as JSON numbers; tolerate a string id too (some RA responses stringify ids).
    private static bool TryId(JsonElement el, string prop, out int id)
    {
        id = 0;
        if (!el.TryGetProperty(prop, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out id);
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out id);
    }
}
