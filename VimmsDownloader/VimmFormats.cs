using Module.Catalog;

/// <summary>
/// Builds the <c>catalog_vimm_format</c> rows for a vault page. Shared by the two Vimm paths —
/// <see cref="VimmSyncService"/> (bind Vimm onto a DAT-sourced game) and
/// <see cref="VimmCatalogSeedService"/> (Vimm as the authoritative source) — so both record a title's
/// download formats identically.
/// </summary>
static class VimmFormats
{
    /// <summary>
    /// The game's downloadable formats: the dl_format options (multi-format titles) paired with their
    /// sizes from the media JSON, or a single implicit format 0 (single-file titles) labelled by the
    /// ROM extension.
    /// </summary>
    public static IReadOnlyList<CatalogRepository.VimmFormatRow> Build(string pageHtml, IReadOnlyList<VimmMedia> media)
    {
        var sizes = media.Count > 0 ? media[0].Sizes : [];
        var sizeByAlt = sizes.ToDictionary(s => s.Alt);
        var labels = VimmVaultParser.ParseFormats(pageHtml);
        var rows = new List<CatalogRepository.VimmFormatRow>();
        if (labels.Count > 0)
        {
            foreach (var f in labels)
            {
                sizeByAlt.TryGetValue(f.Alt, out var sz);
                rows.Add(new(f.Alt, f.Label, sz?.Bytes ?? 0, sz?.Text));
            }
        }
        else
        {
            var sz = sizes.Count > 0 ? sizes[0] : null;
            var label = ExtensionOf(media.Count > 0 ? media[0].Name : null) ?? "Download";
            rows.Add(new(0, label, sz?.Bytes ?? 0, sz?.Text));
        }
        return rows;
    }

    private static string? ExtensionOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var ext = Path.GetExtension(name);
        return string.IsNullOrEmpty(ext) ? null : ext;
    }
}
