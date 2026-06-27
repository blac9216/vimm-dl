using System.Text.Json;

namespace Module.Catalog;

/// <summary>
/// Dolphin (GameCube + Wii) compatibility via the wiki's MediaWiki API. Each game page is filed under a
/// "<c>Category:N stars (Rating)</c>" category (N = 0–5); this pages through the members of each rated
/// category and bridges the page title → catalog game by normalized name (<see cref="Dedup.TitleKey"/>),
/// since Dolphin exposes no serial / Title ID. Name-keyed, so the join is console-gated to GameCube/Wii
/// (the <see cref="Emulators"/> registry) — titles collide across consoles, unlike globally-unique
/// serials. Data is CC BY-SA 3.0 (attribution: wiki.dolphin-emu.org). AOT-safe (JsonDocument).
///
/// Star → normalized status: 5 Perfect / 4 Playable → Playable · 3 Starts → Ingame · 2 Intro/Menu →
/// Intro · 1 Broken → Nothing. 0 stars is skipped — it is the unrated/ambiguous default (the bulk of
/// pages), so a missing badge reads as "untested", consistent with the other adapters.
/// </summary>
public sealed class DolphinCompatSource : ICompatSource
{
    private const string ApiBase = "https://wiki.dolphin-emu.org/api.php";
    private const int PageLimit = 500;

    public string EmulatorId => "dolphin";

    public async Task<IReadOnlyList<CompatEntry>> LoadAsync(CompatFetch fetch, CancellationToken ct)
    {
        var entries = new List<CompatEntry>();
        for (int stars = 0; stars <= 5; stars++)
        {
            var status = MapStars(stars);
            if (status is null) continue; // 0 stars (unrated default) → never fetched

            string? cont = null;
            do
            {
                var json = await fetch(BuildUrl(stars, cont), ct);
                var (titles, next) = ParsePage(json);
                foreach (var title in titles)
                {
                    var key = Dedup.TitleKey(title);
                    if (key.Length > 0) entries.Add(new CompatEntry(key, status));
                }
                cont = next;
            } while (cont is not null);
        }
        return entries;
    }

    /// <summary>The categorymembers API URL for a star rating, optionally continuing a prior page.</summary>
    public static string BuildUrl(int stars, string? cmcontinue)
    {
        var category = Uri.EscapeDataString($"Category:{stars} stars (Rating)");
        var url = $"{ApiBase}?action=query&list=categorymembers&cmtitle={category}&cmlimit={PageLimit}&cmtype=page&format=json";
        return cmcontinue is null ? url : $"{url}&cmcontinue={Uri.EscapeDataString(cmcontinue)}";
    }

    /// <summary>Extract the page titles + continuation token from one categorymembers JSON response.</summary>
    public static (List<string> Titles, string? Continue) ParsePage(string json)
    {
        var titles = new List<string>();
        string? cont = null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("query", out var query)
            && query.TryGetProperty("categorymembers", out var members)
            && members.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in members.EnumerateArray())
                if (m.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var title = t.GetString();
                    if (!string.IsNullOrEmpty(title)) titles.Add(title);
                }
        }
        if (root.TryGetProperty("continue", out var c)
            && c.TryGetProperty("cmcontinue", out var cc) && cc.ValueKind == JsonValueKind.String)
            cont = cc.GetString();
        return (titles, cont);
    }

    /// <summary>Map a Dolphin star rating (1–5) to the canonical status; null → no badge.</summary>
    public static string? MapStars(int stars) => stars switch
    {
        5 or 4 => CompatStatuses.Playable,
        3 => CompatStatuses.Ingame,
        2 => CompatStatuses.Intro,
        1 => CompatStatuses.Nothing,
        _ => null, // 0 = unrated/ambiguous default → skip
    };
}
