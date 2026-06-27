using Module.WiiUTools;

/// <summary>
/// Host implementation of the Wii U key seam. Holds the user-configured 16-byte common key, parsed from
/// the <c>wiiu_common_key</c> setting (32 hex chars). Loaded at startup and refreshed when the setting is
/// saved, so a key change takes effect without a restart (mirrors <see cref="ArchiveAuth"/>). NO key
/// material ships with the app — when unconfigured, both accessors return null and the pipeline stops at a
/// clear "keys required" state. Registered as a singleton; the volatile reference swap makes reads
/// lock-free.
/// </summary>
sealed class WiiUTitleKeyProvider : ITitleKeyProvider
{
    private volatile byte[]? _commonKey;

    public byte[]? GetCommonKey() => _commonKey;

    /// <summary>
    /// Per-title key override is not configured here yet — return null so the pipeline derives the title
    /// key from the ticket via the common key. A title-key database is a possible follow-up.
    /// </summary>
    public byte[]? GetTitleKey(string titleId) => null;

    /// <summary>Set the common key from a 32-hex-char string; invalid/blank input clears it.</summary>
    public void SetCommonKey(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) { _commonKey = null; return; }
        var trimmed = hex.Trim();
        if (trimmed.Length != 32) { _commonKey = null; return; }
        try { _commonKey = Convert.FromHexString(trimmed); }
        catch (FormatException) { _commonKey = null; }
    }
}
