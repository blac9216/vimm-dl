namespace Module.Catalog;

/// <summary>
/// PlayStation (PS1) compatibility via DuckStation's <c>gamedb.yaml</c> (raw GitHub): top-level disc
/// serials (e.g. <c>SLUS-00067:</c>) each with a nested <c>compatibility:</c> → <c>rating: &lt;Text&gt;</c>.
/// Serial-keyed; serials normalized (alphanumeric, upper) to match the Redump serials in the catalog.
/// Pure/AOT-safe (via <see cref="YamlScanner"/>).
///
/// LICENSE: DuckStation's data is CC BY-NC-ND. It is fetched at runtime and cached for the user's own
/// display only — never bundled or redistributed — the same posture as the other license-restricted
/// compat sources. SwanStation (the GPLv3 fork) ships no compatibility-rating database, so DuckStation
/// is the only viable machine-readable PS1 source. Coverage is thin (~1.2k rated games, mostly
/// NoIssues): the absence of a badge means "untested", not "incompatible".
///
/// rating → normalized status: NoIssues → Playable · GraphicalAudioIssues / CrashesInGame → Ingame ·
/// CrashesInIntro → Intro · DoesntBoot → Nothing · Unknown (or unexpected) → no badge.
/// </summary>
public sealed class DuckStationCompatSource : SingleUrlCompatSource
{
    public override string EmulatorId => "duckstation";
    public override string Url => "https://raw.githubusercontent.com/stenzek/duckstation/master/data/resources/gamedb.yaml";

    public override IEnumerable<CompatEntry> Parse(string payload)
    {
        string? serial = null;
        foreach (var line in YamlScanner.Scan(payload))
        {
            if (line.Indent == 0) { serial = line.Value is null ? line.Key : null; continue; }
            if (serial is null) continue;

            // `rating:` appears only nested under `compatibility:`, so take it as the game's rating.
            if (line.Key == "rating" && line.Value is not null)
            {
                var status = MapStatus(line.Value);
                if (status is not null)
                {
                    var key = CompatKeys.NormalizeSerial(serial);
                    if (key.Length > 0) yield return new CompatEntry(key, status);
                }
                serial = null; // one rating per game
            }
        }
    }

    private static string? MapStatus(string rating) => rating switch
    {
        "NoIssues" => CompatStatuses.Playable,
        "GraphicalAudioIssues" or "CrashesInGame" => CompatStatuses.Ingame,
        "CrashesInIntro" => CompatStatuses.Intro,
        "DoesntBoot" => CompatStatuses.Nothing,
        _ => null, // Unknown or anything unexpected → no badge
    };
}
