using System.Text;

namespace Module.Catalog;

/// <summary>
/// Normalization helpers for compatibility match keys, shared by the emulator adapters and the
/// catalog's own <c>serial_key</c> column so both sides of the compat join are produced identically.
/// </summary>
public static class CompatKeys
{
    /// <summary>Strip everything but letters/digits and uppercase, so "BLES-00932" == "BLES00932".</summary>
    public static string NormalizeSerial(string serial)
    {
        var sb = new StringBuilder(serial.Length);
        foreach (var c in serial)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }
}
