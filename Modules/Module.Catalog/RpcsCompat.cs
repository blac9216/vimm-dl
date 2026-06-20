using System.Text;
using System.Text.Json;

namespace Module.Catalog;

/// <summary>
/// Parses the RPCS3 compatibility export (<c>rpcs3.net/compatibility?api=v1&amp;export</c>):
/// <c>{ "results": { "&lt;SERIAL&gt;": { "status": "Playable", … } } }</c>. Pure/AOT-safe (JsonDocument).
/// Serials are normalized (alphanumeric, upper) so the dash-less RPCS3 keys (<c>BLES00932</c>) match
/// the Redump serials (<c>BLES-00932</c>) stored in the catalog.
/// </summary>
public static class RpcsCompat
{
    public static IEnumerable<(string Serial, string Status)> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var entry in results.EnumerateObject())
        {
            var serial = NormalizeSerial(entry.Name);
            if (serial.Length == 0) continue;
            if (entry.Value.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
            {
                var status = st.GetString();
                if (!string.IsNullOrEmpty(status)) yield return (serial, status);
            }
        }
    }

    /// <summary>Strip everything but letters/digits and uppercase, so "BLES-00932" == "BLES00932".</summary>
    public static string NormalizeSerial(string serial)
    {
        var sb = new StringBuilder(serial.Length);
        foreach (var c in serial)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }
}
