using System.Text.Json;

namespace Module.Catalog;

/// <summary>
/// Azahar (Nintendo 3DS) compatibility source. Parses <c>compatibility_list.json</c> (raw GitHub): an
/// array of <c>{ "compatibility": 0–5/99, "title": "…", "releases": [{ "id": "…" }] }</c>. The No-Intro
/// 3DS DAT keys games by product code (<c>CTR-P-xxxx</c>), not the 16-hex Title ID Azahar's releases
/// carry, so we join by **normalized name** (<see cref="Dedup.TitleKey"/> on the entry <c>title</c>)
/// instead — console-gated to <c>n3ds</c>. Pure/AOT-safe (JsonDocument).
///
/// Azahar (Citra-derived) rating → normalized status: 0 Perfect / 1 Great → Playable · 2 Okay / 3 Bad →
/// Ingame · 4 Intro/Menu → Intro · 5 Won't Boot → Nothing · 99 Not Tested (or unknown) → no badge.
/// </summary>
public sealed class AzaharCompatSource : SingleUrlCompatSource
{
    public override string EmulatorId => "azahar";
    public override string Url => "https://raw.githubusercontent.com/azahar-emu/compatibility-list/master/compatibility_list.json";

    public override IEnumerable<CompatEntry> Parse(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) yield break;

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("title", out var t) || t.ValueKind != JsonValueKind.String) continue;
            if (!entry.TryGetProperty("compatibility", out var c) || c.ValueKind != JsonValueKind.Number) continue;
            var status = MapStatus(c.GetInt32());
            if (status is null) continue;
            var key = Dedup.TitleKey(t.GetString()!);
            if (key.Length > 0) yield return new CompatEntry(key, status);
        }
    }

    /// <summary>Map an Azahar/Citra compatibility level to the canonical status; null → no badge.</summary>
    public static string? MapStatus(int compatibility) => compatibility switch
    {
        0 or 1 => CompatStatuses.Playable, // Perfect / Great
        2 or 3 => CompatStatuses.Ingame,   // Okay / Bad (reaches gameplay)
        4 => CompatStatuses.Intro,         // Intro/Menu
        5 => CompatStatuses.Nothing,       // Won't Boot
        _ => null,                         // 99 Not Tested or anything unexpected → skip
    };
}
