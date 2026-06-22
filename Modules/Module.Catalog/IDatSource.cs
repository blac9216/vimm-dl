using Module.Core;

namespace Module.Catalog;

/// <summary>
/// Where a catalog sync gets each system's clrmamepro DAT text. The default source is the
/// libretro-database mirror (<see cref="LibretroDatSource"/>, one raw fetch per system); the
/// fresher option pulls daily bundle zips (<see cref="DailyBundleDatSource"/>). A source owns its
/// own rate-safety; <see cref="CatalogSyncService"/> only paces between systems by
/// <see cref="InterSystemDelay"/> and a missing/failed system is skipped, never aborting the run.
/// </summary>
public interface IDatSource
{
    /// <summary>
    /// Stable identifier for this data-source origin, persisted per game in <c>catalog_game_source</c>
    /// so origins can coexist and a sync accumulates rather than replaces (D2b / #162).
    /// Currently <c>"libretro"</c> or <c>"daily-bundle"</c>.
    /// </summary>
    string Origin { get; }

    /// <summary>How long the orchestrator should pause between systems for this source (0 = none).</summary>
    TimeSpan InterSystemDelay { get; }

    /// <summary>Fetch one system's clrmamepro DAT text, or fail (the system is skipped).</summary>
    Task<Result<string>> GetDatAsync(CatalogSystemInfo sys, CancellationToken ct);
}
