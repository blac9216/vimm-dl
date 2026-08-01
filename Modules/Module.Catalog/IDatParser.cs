namespace Module.Catalog;

/// <summary>
/// Streams games out of one system's DAT text. Two wire formats are in the wild and both are used by
/// shipped sources, so the parser is a seam rather than a hardcoded choice (#265):
/// <list type="bullet">
/// <item>the clrmamepro "parenthesis" text format — <see cref="ClrMameProParser"/>, served by the
/// libretro-database mirror;</item>
/// <item>XML — <see cref="XmlDatParser"/>, served by the daily bundle zips in both the Logiqx
/// (redump) and DAT-o-MATIC v3 (No-Intro) dialects.</item>
/// </list>
/// </summary>
public interface IDatParser
{
    /// <summary>The DAT's header, available once enumeration has begun (it is the first block/element).</summary>
    DatHeader? Header { get; }

    /// <summary>Stream games from the DAT. Enumerate to drive parsing.</summary>
    IEnumerable<DatGame> Parse(TextReader reader);
}

/// <summary>Picks the parser that matches a DAT's actual wire format.</summary>
public static class DatParsers
{
    /// <summary>
    /// Choose a parser by sniffing the leading non-whitespace bytes. Detection is on the format's own
    /// markers (an XML prolog, doctype, or a <c>&lt;datafile&gt;</c> root) rather than on which source
    /// supplied the text, so a source that changes format upstream keeps working instead of silently
    /// parsing to nothing — which is exactly how #265 escaped notice.
    /// </summary>
    public static IDatParser For(string content) => LooksLikeXml(content)
        ? new XmlDatParser()
        : new ClrMameProParser();

    /// <summary>True when the text opens as XML (prolog, doctype, comment, or a bare element).</summary>
    internal static bool LooksLikeXml(string content)
    {
        foreach (var c in content)
        {
            if (char.IsWhiteSpace(c)) continue;
            return c == '<';
        }
        return false;   // empty / all-whitespace — treat as the legacy format and let it parse to nothing
    }
}
