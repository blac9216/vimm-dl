using System.Globalization;
using System.Xml;

namespace Module.Catalog;

/// <summary>
/// Streaming parser for the XML DAT formats, covering both dialects the daily bundle ships (#265):
/// <list type="bullet">
/// <item><b>Logiqx</b> (redump.zip) — <c>&lt;datafile&gt;&lt;header&gt;&lt;name/&gt;&lt;version/&gt;&lt;/header&gt;</c>
/// then <c>&lt;game name=".."&gt;</c> with <c>&lt;rom name size crc md5 sha1/&gt;</c>.</item>
/// <item><b>DAT-o-MATIC v3</b> (no-intro.zip) — the same shape plus <c>&lt;game_id&gt;</c>, and roms
/// carrying <c>serial</c>, <c>sha256</c>, <c>status</c>, <c>mia</c>.</item>
/// </list>
/// Forward-only over <see cref="XmlReader"/>: yields one <see cref="DatGame"/> at a time so multi-MB
/// DATs parse in constant memory, matching <see cref="ClrMameProParser"/>. No reflection and no XML
/// serialization — AOT-safe.
/// </summary>
/// <remarks>
/// Unknown elements and attributes are skipped, so new DAT fields never break parsing.
///
/// <para><b>Field fidelity vs clrmamepro.</b> Neither XML dialect carries a region, and the Logiqx
/// (redump) dialect carries no serial at all, whereas the libretro clrmamepro mirror supplies both.
/// Region is left null deliberately: <see cref="Dedup"/> (1G1R parent selection, the English filter)
/// and the SQL mirror in <c>CatalogRepository.GetGamesAsync</c> already fall back to boundary-matched
/// region tags in the game name when the column is empty, so synthesizing one here would duplicate
/// that logic and risk drifting from it. The missing Logiqx serial is a genuine fidelity loss for
/// disc systems synced from the bundle — it is not recoverable from the file.</para>
/// </remarks>
public sealed class XmlDatParser : IDatParser
{
    /// <inheritdoc/>
    public DatHeader? Header { get; private set; }

    private static readonly XmlReaderSettings Settings = new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Ignore,   // the Logiqx DOCTYPE is declarative only — never fetched
        CloseInput = false,
    };

    /// <inheritdoc/>
    public IEnumerable<DatGame> Parse(TextReader reader)
    {
        using var xml = XmlReader.Create(reader, Settings);
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element) continue;
            if (xml.Name == "header") Header = ReadHeader(xml);
            else if (xml.Name is "game" or "machine") yield return ReadGame(xml);
        }
    }

    /// <summary>Read <c>&lt;name&gt;</c> / <c>&lt;version&gt;</c> out of the header element.</summary>
    private static DatHeader ReadHeader(XmlReader xml)
    {
        if (xml.IsEmptyElement) return new DatHeader("", null);

        string? name = null, version = null;
        using var sub = xml.ReadSubtree();
        sub.Read();                                  // position on <header> itself
        while (!sub.EOF)
        {
            if (sub.NodeType == XmlNodeType.Element)
            {
                // ReadElementContentAsString consumes the element and leaves the reader on the NEXT
                // node, so `continue` past the loop's own advance or a sibling would be skipped.
                switch (sub.Name)
                {
                    case "name": name = sub.ReadElementContentAsString(); continue;
                    case "version": version = sub.ReadElementContentAsString(); continue;
                }
            }
            sub.Read();
        }
        return new DatHeader(name ?? "", version);
    }

    /// <summary>
    /// Read one <c>&lt;game&gt;</c>. Serial precedence is game attribute → first rom that carries one →
    /// <c>&lt;game_id&gt;</c>. The last step surfaces a Wii U 16-hex title id into the
    /// structured-identifier column for the digital/CDN DAT (#266); it is consulted only when the
    /// dialect supplies no real serial, so a cartridge serial such as "BJBE" always wins.
    /// </summary>
    private static DatGame ReadGame(XmlReader xml)
    {
        var name = xml.GetAttribute("name") ?? "";
        var serial = NullIfBlank(xml.GetAttribute("serial"));
        var region = NullIfBlank(xml.GetAttribute("region"));
        string? gameId = null;
        List<DatRom>? roms = null;

        if (!xml.IsEmptyElement)
        {
            using var sub = xml.ReadSubtree();
            sub.Read();                              // position on <game> itself
            while (!sub.EOF)
            {
                if (sub.NodeType == XmlNodeType.Element)
                {
                    switch (sub.Name)
                    {
                        case "rom":
                            (roms ??= []).Add(ReadRom(sub));
                            break;                   // empty element: fall through to the advance below
                        case "game_id":
                            gameId ??= NullIfBlank(sub.ReadElementContentAsString());
                            continue;
                        case "region":
                            region ??= NullIfBlank(sub.ReadElementContentAsString());
                            continue;
                        case "serial":
                            serial ??= NullIfBlank(sub.ReadElementContentAsString());
                            continue;
                    }
                }
                sub.Read();
            }
        }

        roms ??= [];
        serial ??= FirstRomSerial(roms) ?? gameId;

        return new DatGame(name, region, serial, roms, ClrMameProParser.ParseLanguages(name));
    }

    private static string? FirstRomSerial(List<DatRom> roms)
    {
        foreach (var r in roms)
            if (!string.IsNullOrEmpty(r.Serial)) return r.Serial;
        return null;
    }

    private static DatRom ReadRom(XmlReader xml)
    {
        var name = xml.GetAttribute("name") ?? "";
        long.TryParse(xml.GetAttribute("size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
        return new DatRom(
            name,
            size,
            NullIfBlank(xml.GetAttribute("crc")),
            NullIfBlank(xml.GetAttribute("md5")),
            NullIfBlank(xml.GetAttribute("sha1")),
            NullIfBlank(xml.GetAttribute("serial")));
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
