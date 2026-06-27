/// <summary>
/// Centralized settings key constants. Every settings read/write uses these.
/// </summary>
static class SettingsKeys
{
    public const string FixThe = "rename_fix_the";
    public const string AddSerial = "rename_add_serial";
    public const string StripRegion = "rename_strip_region";
    public const string Ps3Parallelism = "ps3_parallelism";
    public const string SyncPath = "sync_path";
    public const string Ps3DefaultFormat = "ps3_default_format";
    public const string Ps3PreserveArchive = "ps3_preserve_archive";
    public const string FeatureSync = "feature_sync";
    public const string FeatureEvents = "feature_events";
    public const string FeatureLibrary = "feature_library";
    public const string FeatureImport = "feature_import";
    public const string DefaultSetsSeeded = "default_sets_seeded";

    // Catalog DAT source selector: "libretro" (default, raw per-system mirror) or "daily-bundle"
    // (fresher hugo auto-datfile-generator release zips). See DailyBundleDatSource.
    public const string CatalogDatSource = "catalog_dat_source";

    // Local import (epic #118): the drop folder ingested by POST /api/catalog/import, and where
    // non-matching files are set aside. Empty → defaults to downloads/import and downloads/rejected.
    public const string ImportPath = "import_path";
    public const string RejectedPath = "rejected_path";

    // archive.org download tuning (RomGoGetter parity). Parallelism/retries/idle are stored now and
    // wired into the download engine in a follow-up; the S3 keys are active immediately (see ArchiveAuth).
    public const string ArchiveParallelism = "archive_parallelism";
    public const string ArchiveRetries = "archive_retries";
    public const string ArchiveIdle = "archive_idle";
    public const string ArchiveS3Access = "archive_s3_access";
    public const string ArchiveS3Secret = "archive_s3_secret";

    // Wii U decryption (epic #104). The 16-byte common key as 32 hex chars; supplied by the user, never
    // bundled. Empty → the Wii U pipeline stops at a "keys required" state. Active immediately on save.
    public const string WiiUCommonKey = "wiiu_common_key";

    // IGDB metadata (epic #122 / M2). User-provided Twitch app credentials for the IGDB API (client
    // credentials OAuth). Both empty → the IGDB description sync no-ops. Read fresh at each sync.
    public const string IgdbClientId = "igdb_client_id";
    public const string IgdbClientSecret = "igdb_client_secret";
}
