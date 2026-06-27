using System.Text.Json;

namespace Module.Catalog;

/// <summary>
/// RPCS3 (PS3) compatibility source. Parses the export at
/// <c>rpcs3.net/compatibility?api=v1&amp;export</c>:
/// <c>{ "results": { "&lt;SERIAL&gt;": { "status": "Playable", … } } }</c>. Serial-keyed; serials are
/// normalized (alphanumeric, upper) so the dash-less RPCS3 keys (<c>BLES00932</c>) match the Redump
/// serials (<c>BLES-00932</c>) stored in the catalog. Pure/AOT-safe (JsonDocument). Data is GPLv2.
/// </summary>
public sealed class Rpcs3CompatSource : SingleUrlCompatSource
{
    public override string EmulatorId => "rpcs3";
    public override string Url => "https://rpcs3.net/compatibility?api=v1&export";

    public override IEnumerable<CompatEntry> Parse(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var entry in results.EnumerateObject())
        {
            var serial = CompatKeys.NormalizeSerial(entry.Name);
            if (serial.Length == 0) continue;
            if (entry.Value.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
            {
                var status = st.GetString();
                if (!string.IsNullOrEmpty(status)) yield return new CompatEntry(serial, status);
            }
        }
    }
}
