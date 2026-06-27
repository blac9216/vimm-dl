namespace Module.Catalog;

/// <summary>
/// PCSX2 (PS2) compatibility source. Parses PCSX2's <c>GameIndex.yaml</c> (raw GitHub): top-level disc
/// serials (e.g. <c>SLUS-20062:</c>) each with a 2-space <c>compat:</c> field rated 0–6. Serial-keyed;
/// serials normalized (alphanumeric, upper) to match the Redump serials in the catalog. Pure/AOT-safe
/// (via <see cref="YamlScanner"/>). Data is GPLv3.
///
/// PCSX2 rating → normalized status (order-preserving over the 6→5 buckets):
/// 6 Perfect / 5 Playable → Playable · 4 Ingame → Ingame · 3 Menu → Intro · 2 Intro → Loadable ·
/// 1 Nothing → Nothing · 0 Unknown (or unexpected) → no badge.
/// </summary>
public sealed class Pcsx2CompatSource : ICompatSource
{
    public string EmulatorId => "pcsx2";
    public string Url => "https://raw.githubusercontent.com/PCSX2/pcsx2/master/bin/resources/GameIndex.yaml";

    public IEnumerable<CompatEntry> Parse(string payload)
    {
        string? serial = null;
        foreach (var line in YamlScanner.Scan(payload))
        {
            // A top-level key with no value is a disc serial header; it scopes the fields below it.
            if (line.Indent == 0) { serial = line.Value is null ? line.Key : null; continue; }
            if (serial is null) continue;

            if (line.Indent == 2 && line.Key == "compat" && line.Value is not null)
            {
                var status = MapStatus(line.Value);
                if (status is not null)
                {
                    var key = CompatKeys.NormalizeSerial(serial);
                    if (key.Length > 0) yield return new CompatEntry(key, status);
                }
                serial = null; // one rating per game; ignore the rest of this entry
            }
        }
    }

    private static string? MapStatus(string compat) => compat switch
    {
        "6" or "5" => CompatStatuses.Playable,
        "4" => CompatStatuses.Ingame,
        "3" => CompatStatuses.Intro,
        "2" => CompatStatuses.Loadable,
        "1" => CompatStatuses.Nothing,
        _ => null, // 0 Unknown or anything unexpected → no badge
    };
}
