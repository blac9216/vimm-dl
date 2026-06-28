# CLAUDE.md

## Project Overview

ROM management system built around the **No-Intro / Redump catalog** as the authoritative "every game
that exists" set. Browse the catalog by console / name / owned-vs-remote, see real metadata + emulator
compatibility, and queue downloads from the best available **source** — **archive.org** (parallel, via
configured "sets") with an automatic fallback to **Vimm's Lair** (the original single source), bound to
each catalog game **by hash**. Includes a live **PS3 ISO conversion** pipeline and external-drive
**sync**. Modular architecture under `Modules/`; React + Vite + Tailwind frontend in
`VimmsDownloader/client/`; host project `VimmsDownloader/`; mock server in `MockServer/` for testing.
Upstream GitHub user: eduvhc; work repo: blac9216/vimm-dl. Target platform: Linux (Docker + bare
metal), also runs on Windows for dev.

## Development Workflow

**Follow this every session, including immediately after a context compaction.**

- **GitHub workflow** — load the `github-workflow` skill at the start of every session, including immediately after a context compaction, and follow it for all GitHub work: issues, epics, branches, commits, PRs, and the contextless PR review (which it hands off to the `github-pr-review` skill). It is the durable home for these conventions — do not rely on chat memory or summaries to carry them.
- **Toolchain (remote)** — .NET SDK at `/tmp/dotnet`, bun at `/root/.bun/bin`, clang at `/usr/bin/clang`. MSTest 4 emits no results without a TRX logger: `dotnet test <proj> -c Debug --logger "trx;LogFileName=x.trx" --results-directory /tmp/trx`. **Coverage:** every `*.Tests` project references `coverlet.collector`, so append `--collect:"XPlat Code Coverage"` to emit a `coverage.cobertura.xml` under the results dir (e.g. `dotnet test VimmsDownloader.slnx -c Debug --collect:"XPlat Code Coverage" --results-directory /tmp/cov`). Frontend build: `bun run build` (runs `tsc -b && vite build`). The build is analyzer-warning-clean; a root `Directory.Build.props` opts every `*.Tests` project out of MSTest parallelization (`[assembly: DoNotParallelize]`) — the suite runs sequentially because many tests use real file I/O, temp SQLite, and Testcontainers.

## Required Tools

Everything needed to build, test, and lint this repo. The **Toolchain (remote)** bullet above is the
quick invocation cheat-sheet; this is the install-and-locate reference for a fresh environment.

- **.NET 10 SDK** — the solution targets `net10.0`. **It is *not* pre-installed in a fresh remote
  sandbox** — install it to `/tmp/dotnet` (it won't be on `PATH`, so invoke it explicitly):

  ```bash
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /tmp/dotnet
  /tmp/dotnet/dotnet --version   # 10.0.x
  ```

  Then `export PATH="/tmp/dotnet:$PATH"` and use `dotnet build VimmsDownloader.slnx -c Debug` /
  `dotnet test <proj> -c Debug --logger "trx;LogFileName=x.trx" --results-directory /tmp/trx`.
- **bun** — the frontend package manager + build runner, at `/root/.bun/bin/bun` (pre-installed). In
  `VimmsDownloader/client/`: `bun install`, then `bun run build` (`tsc -b && vite build`) and
  `bun run lint` (`eslint .`). The built bundle under `VimmsDownloader/wwwroot/` is **tracked** — commit
  the rebuild alongside any frontend source change.
- **clang** — at `/usr/bin/clang` (pre-installed). Needed only for the AOT native publish
  (`PublishAot=true`, i.e. `dotnet publish` / the Docker image); plain Debug builds + tests don't use it.
- **7-Zip (`7z`) and Docker** — optional. `Module.Extractor` tests and the Testcontainers-backed tests
  (`ghcr.io/eduvhc/vimm-dl-tools`) **skip gracefully** when these are absent, so the suite stays green
  without them. Install only when working on the extractor itself.
- **Outbound HTTPS** — required for catalog / IGDB / libretro-thumbnails fetches and NuGet + bun
  restores; it routes through the sandbox proxy.

## Architecture

### Modules (under `Modules/`)

All modules follow the convention in `Modules/MODULE_GUIDE.md`. Each module is a standalone class library with zero web dependencies, communicating with the host via a typed bridge (`IModuleBridge<TEvent>`).

- **Module.Core** — `IModuleBridge<TEvent>` interface, `Result<T>` (generic result type + `FileOps` safe I/O helpers), `SharedConstants` (`FileExtensions`, `Platforms`), `ConsoleDirectories` (platform name → EmuDeck folder, e.g. "PlayStation 3" → `ps3`, "GameCube" → `gc`; null for unknown), and `Pipeline/` infrastructure (`IPipeline`, `PipelineState`, `PipelinePhase`, `PipelineStatusEvent`, `PipelineFlowInfo`, `DuplicateCheckResult`). Every module references this.
- **Module.Core.Testing** — Shared test infrastructure: `FakeBridge<T>`, `TempDirectory`, `ToolsContainer` (Testcontainers with `ghcr.io/eduvhc/vimm-dl-tools`).
- **Module.Download** — Download service + the **source seam**: `DownloadService` (download loop, resume, progress, Result-based error handling, `StreamDownload` with per-source headers), `VaultPageParser` (Vimm HTML parsing + format resolution with fallback). `Sources/` holds `IDownloadSource` (`ResolveAsync` → `ResolvedDownload`), `ISourceRegistry`, `ICatalogSource` (browse/search), and the concrete `VimmSource` (wraps `VaultPageParser`) + `ArchiveSource` (archive.org direct + listing). `IDownloadItemProvider` (async, host provides queue items keyed by `(source, source_id, format)`). Bridge: `IDownloadBridge` emitting `DownloadStatusEvent`, `DownloadProgressEvent`, `DownloadCompletedEvent`, `DownloadErrorEvent`, `DownloadDoneEvent`.
- **Module.Catalog** — The canonical catalog + Vimm binding (web-free; persists via `ICatalogStore`). `ClrMameProParser` + `CatalogSyncService` (fetch No-Intro/Redump DATs from the libretro-database mirror), `CatalogSystems` (the consoles synced — DAT name + group + EmuDeck folder), `CatalogMatcher` (name normalization / file match), `Dedup` (1G1R title-key + parent selection), the **compat seam** (`ICompatSource.LoadAsync(fetch)` adapters — `Rpcs3CompatSource` JSON + `Pcsx2CompatSource`/`DuckStationCompatSource` YAML via the AOT `YamlScanner` (single-GET via `SingleUrlCompatSource`) + `DolphinCompatSource` (paginated MediaWiki, name-keyed) + `AzaharCompatSource` (3DS JSON, name-keyed) — feeding the `CompatSources`/`Emulators` registries with a per-emulator `CompatMatchKind` (serial/name; name joins by `title_key`, per-row console-gated to the emulator's `Consoles` so colliding titles can't cross consoles), normalized to `CompatStatuses` via `CompatKeys`/`Dedup.TitleKey`), `Crc32`, and the Vimm layer: `VimmSystems` (catalog console → Vimm vault code) + `VimmVaultParser` (list rows, inline `media` JSON hashes, `hashes2.php`, `dl_format`).
- **Module.Extractor** — 7z wrapper (`ZipExtract.QuickCheckAsync`, `ExtractAsync` returning `Result<bool>`). Auto-detects `7z` on PATH or `C:\Program Files\7-Zip\7z.exe` on Windows.
- **Module.Ps3IsoTools** — Pure PS3 tools, no pipeline: `ParamSfo` (PARAM.SFO binary parser), `Ps3IsoConverter` (makeps3iso + patchps3iso + `FindJbFolder`, returns `Result<string>`), `IsoFilenameFormatter` (serial/The-fix/region rename with `IsoRenameOptions`).
- **Module.Ps3Pipeline** — PS3 pipeline orchestration, implements `IPipeline`: `Ps3ConversionPipeline` (facade with `BuildFlow`, `CheckDuplicate`, `GetStepDurations`), `Ps3JbFolderPipeline` (extract→convert workers), `Ps3DecIsoPipeline` (rename/extract .dec.iso, optional archive preservation). Uses `PipelineState` from Module.Core. Bridge: `IPs3PipelineBridge : IModuleBridge<PipelineStatusEvent>`.
- **Module.Sync** — Compares ISOs between `completed/` and an external drive. Copy with progress, disk info, space checks via `ISyncBridge`.

### Host (VimmsDownloader/)

- **SRP file structure** — `Program.cs` (startup/DI), `Models.cs` (records + PathHelpers), `AppJsonContext.cs` (JSON source gen), `QueueRepository.cs`, `SettingsKeys.cs`, `DatabaseMigrator.cs` (embedded SQL migrations), `DownloadHub.cs`, `DownloadQueue.cs`, `QueueItemProvider.cs`, `MetadataFetcher.cs`.
- **Catalog/source host services** — `CatalogRepository.cs` (implements `ICatalogStore`, all catalog SQL incl. the Vimm binding), `CatalogSyncService` (wired in `Program.cs` over the `libretro` client), `CatalogScanService` (owned scan of `completed/`), `CatalogVerifyService` (CRC32 verify), `CompatSyncService` (per-emulator compat via `CompatSources`), `CatalogResolveService` (archive→Vimm download resolution), `VimmSyncService` (per-console Vimm hash scrape/binding), `DefaultSets.cs` (seeded RomGoGetter archive sets), `ArchiveAuth.cs` (Internet Archive S3 "LOW" auth via a `DelegatingHandler`). The concrete `SourceRegistry` lives in Module.Download/Sources, built from DI.
- **Endpoints/** — `FileEndpoints` (merged `/api/data` with pipeline trace), `DownloadEndpoints`, `MetadataEndpoints`, `SourceEndpoints`, `CatalogEndpoints` (+ the `BackgroundJobGate` single-flight base & `Catalog*State` markers), `Ps3Endpoints`, `SyncEndpoints`, `SettingsEndpoints`, `EventEndpoints`, `MetricsEndpoints`. **47 endpoints total** (enumerated in the API Endpoints table below — that table is the source of truth for the count).
- **SignalR bridges** — `SignalRPs3PipelineBridge.cs`, `SignalRSyncNotifier.cs`, `SignalRDownloadBridge.cs` route module events to SignalR + append to the events table. Pipeline bridge also updates the `completed_urls` projection for terminal states.
- **AOT-ready** — `PublishAot=true`, raw ADO.NET (Microsoft.Data.Sqlite), JSON source generator (`AppJsonContext`), all modules `IsAotCompatible`. JSON in the catalog parsers uses `JsonDocument` (DOM, no reflection).
- **QueueRepository / CatalogRepository** — singletons, all async SQLite operations. Database initialized via `DatabaseMigrator` with embedded SQL files; both repositories share `queue.db`.
- **Settings stored in DB** — `settings` table (key-value). Keys in `SettingsKeys.cs`.

### Result Pattern

- `Result<T>` struct in `Module.Core/Result.cs` — zero-allocation, `IsOk`/`Error`/`Value`
- `FileOps` utility — `TryMove`, `TryDelete`, `TryDeleteDirectory`, `TryWriteAllText`
- Modules return `Result<T>` instead of throwing for expected failures
- Exceptions reserved for critical failures + `OperationCanceledException` (cancellation mechanism)

### Event Sourcing

- `events` table — append-only log of ALL module events (download, pipeline, sync) including progress
- `correlation_id` column links all events for a single pipeline run (12-char hex UUID)
- Bridges write to events table before SignalR dispatch
- `completed_urls` is a projection — `conv_phase`/`conv_message`/`iso_filename` updated by pipeline bridge on terminal events
- Events pruned on startup: 7-day retention + 50k max rows

### Database Migrator

- `DatabaseMigrator.cs` — runs embedded SQL files from `Migrations/*.sql` in order (auto-discovered by manifest-resource name; currently through **031**)
- Tracks executed migrations in `schema_migrations` table
- Each migration is idempotent (catches "duplicate column" / "already exists" errors)
- Migrations split into individual statements for SQLite compatibility

## Database

SQLite, file `queue.db` in `data/` subdirectory (derived from connection string). Tables:

**Queue / downloads**
- `queued_urls` (id, url, format, **source**, **source_id**) — download queue ordered by id
- `completed_urls` (id, url, filename, filepath, completed_at, conv_phase, conv_message, iso_filename, format, **source**, **source_id**) — finished downloads + conversion-state projection
- `source_meta` (source, source_id, title, platform, size, formats, serial, md5, sha1; PK (source, source_id)) — per-source metadata cache (replaced `url_meta`, which remains as a one-release read fallback)
- `settings` (key PK, value) — user settings
- `events` (id, item_name, event_type, phase, message, data, timestamp, correlation_id) — append-only event log
- `schema_migrations` (name PK, executed_at) — migration tracking

**Catalog (No-Intro / Redump + bindings)**
- `catalog_system` (id, dat_name UNIQUE, console, source, dat_version, game_count, synced_at) — one row per console DAT
- `catalog_game` (id, system_id, name, region, serial, serial_key, languages, title_key, is_parent, **vault_id**, **vimm_match**, description, **igdb_rating**, **igdb_rating_count**, **ra_players**, **rank_score**) — one entry per title/region/revision
- `catalog_rom` (id, game_id, name, size, crc, md5, sha1) — file(s) per game (disc titles carry several; indexed on sha1)
- `catalog_owned` (game_id, filepath, verified) — which catalog games are present on disk
- `catalog_set` (id, name, console, …) + `catalog_set_link` (id, set_id, url, position) — per-console archive.org download sets
- `catalog_compat` (emulator, match_kind, match_key, status, consoles; PK (emulator, match_kind, match_key)) — per-emulator compatibility, joined to a game by serial | title_id | name (the emulator's `match_kind`); `consoles` (CSV) gates name-keyed rows to the consoles the emulator targets
- `catalog_vimm_format` (id, game_id, alt, label, size_bytes, size_text) — Vimm download formats for a bound game

### Settings Keys
- `rename_fix_the` = "true" — fix "Godfather, The" → "The Godfather"
- `rename_add_serial` = "true" — append BLES-00043 to filename
- `rename_strip_region` = "true" — remove (Europe) etc.
- `ps3_parallelism` = "3" — workers per pipeline phase
- `ps3_default_format` = "1" — default download format (0=JB Folder, 1=.dec.iso)
- `ps3_preserve_archive` = "true" — keep .7z after conversion
- `sync_path` = "" — external drive path
- `archive_parallelism` = "4" — concurrent archive.org downloads (RomGoGetter parity; stored, engine wiring is a follow-up)
- `archive_retries` = "3" — retry attempts on a failed archive download
- `archive_idle` = "60" — stall-watchdog seconds (no byte progress)
- `archive_s3_access` / `archive_s3_secret` = "" — Internet Archive S3 keys; sent as `Authorization: LOW access:secret` on archive.org requests when **both** are set (active immediately via `ArchiveAuth` + a `DelegatingHandler`)
- `default_sets_seeded` = "true" — one-time guard for seeding the default archive sets
- `feature_library` = "false" — beta: Library tab
- `feature_sync` = "false" — beta: Sync tab
- `feature_events` = "false" — developer: Events tab

## Catalog, Sources & Vimm Binding

The catalog is the identity layer; sources bind onto it. (See ROADMAP.md.)

- **Catalog sync** (`POST /api/catalog/sync`) — `CatalogSyncService` fetches the No-Intro/Redump DATs for every console in `CatalogSystems.All` from the libretro-database mirror (`metadat/{no-intro|redump}/{DatName}.dat`), parses via `ClrMameProParser`, and replaces each system's games/roms. Console tags = EmuDeck folders. 1G1R parent selection (`Dedup`) marks one variant per title as `is_parent`.
- **Owned + verify + compat** — `CatalogScanService` records which games exist under `completed/` (`catalog_owned`); `CatalogVerifyService` checks file CRC32 against `catalog_rom`; `CompatSyncService` imports each registered emulator's compatibility (`CompatSources`), joined to a game by serial/title_id/name per the emulator's `match_kind`.
- **Download sets** — a "set" is a named, per-console list of archive.org links (`catalog_set` + `catalog_set_link`). Defaults are seeded once from `DefaultSets` (ported from RomGoGetter). Managed via `/api/catalog/sets` CRUD (and the Library "Sources" dialog / Settings → Archive).
- **Vimm hash binding** (`POST /api/catalog/vimm-sync?console=`) — `VimmSyncService` scrapes Vimm per console (list sections A–Z+number → each vault page), reads the Redump/No-Intro hash triple (inline `GoodHash`/`GoodMd5`/`GoodSha1`, or `vault/ajax/hashes2.php` for multi-disc), and **matches by hash SHA1→MD5→CRC** against `catalog_rom`. On a match it binds `catalog_game.vault_id` + the available `catalog_vimm_format` rows; unmatched games on a scraped console are flagged `vimm_match='none'` (the "no Vimm match" badge). Throttled, per-console, cancellable (`CatalogVimmState : BackgroundJobGate`).
- **Download resolution** (`CatalogResolveService.ResolveForQueueAsync`, used by `POST /api/catalog/games/{id}/queue?format=`) — prefer archive.org sets (match a game's file across the console's set links); if none, fall back to the bound Vimm vault URL (`https://vimm.net/vault/{vault_id}`) with the requested format if Vimm offers it, else the first available. Queues `(url, format, source)`; archive uses format 0. Returns 404 when neither source has it.
- **Background jobs** are single-flight via `BackgroundJobGate` (202 Accepted / 409 Conflict); `GET /api/catalog/status` reports `syncing`/`scanning`/`compatSyncing`/`verifying`/`vimmSyncing` so the UI polls while any runs.

## Download Flow (Module.Download)

1. `DownloadService.Run()` loops `while (!cancelled)`, gets next item via `IDownloadItemProvider` (async). Each item is keyed by `(source, source_id, format)`.
2. `registry.Get(item.Source).ResolveAsync(...)` resolves the concrete download. **VimmSource** wraps `VaultPageParser.Parse` (extracts mediaId/title/server, resolves format with fallback) and supplies Vimm's anti-bot headers; **ArchiveSource** resolves an archive.org file URL directly.
3. `StreamDownload` returns `Result<(string, string)>` (no exceptions for HTTP errors), with per-source `extraHeaders`.
4. Streams with an 80KB buffer, reports progress every 2s with speed (MB/s).
5. Crash recovery: detects partial files, resumes via Range headers.
6. On completion: file is moved into an EmuDeck-style per-console folder — `completed/{ConsoleDirectories.Resolve(platform)}/` (e.g. `completed/ps3/`); unknown/empty platforms stay in `completed/` root. Then `provider.CompleteAsync()` (host handles DB, stores full `filepath`), emits `DownloadCompletedEvent`.
7. `OnPostDownload` callback for PS3 pipeline routing.
8. Auto-resumes on backend startup if the queue has items.
9. Background metadata fetch sends `MetaReady` SignalR event for instant UI refresh.

## PS3 Pipelines (Module.Ps3Pipeline)

Two scoped pipelines sharing `PipelineState` from Module.Core:

**Ps3JbFolderPipeline** (format=0):
1. Extract archive → find `PS3_GAME/PARAM.SFO` via `FindJbFolder`
2. If no JB folder → delegates to `Ps3DecIsoPipeline.HandleExtractedArchive()` (checks for .dec.iso/.iso)
3. If JB folder found → write crash recovery marker → enqueue to convert workers
4. Convert: parse PARAM.SFO → `makeps3iso` → `patchps3iso` → rename ISO
5. N workers per phase (configurable via `ps3_parallelism` setting)

**Ps3DecIsoPipeline** (format>0):
- Raw `.dec.iso` → `RenameDecIsoAsync()` with `IsoFilenameFormatter`
- Archive with `.dec.iso` → `ExtractAndRenameDecIsoAsync()` → extract → find → rename (optional `deleteArchive` param controlled by `ps3_preserve_archive` setting)
- `HandleExtractedArchive()` returns `Result<string?>` — shared method called by both pipelines

**ISO Filename Formatting** (`IsoFilenameFormatter`):
- Strips all trailing `(Region) (Languages)` groups
- Fixes "The" placement: "Godfather, The" → "The Godfather"
- Appends serial: "- BLES-00043"
- All rules configurable via `IsoRenameOptions` (settings UI toggles, .dec.iso only)

**Pipeline Trace** (pipeline-owned flow):
- Each pipeline implements `IPipeline.BuildFlow()` — defines its own step order, status mapping, and available actions
- `Ps3ConversionPipeline` infers variant (JB folder / dec.iso / dec.iso archive) and builds `PipelineFlowInfo`
- Host's `FileEndpoints.BuildTrace()` delegates to pipeline generically, adds ISO size
- Steps with statuses (pending/active/done/error/skipped) + step durations from in-memory timing

## Pipeline Infrastructure (Module.Core/Pipeline)

- `IPipeline` — generic contract: `GetStatuses()`, `Abort()`, `IsConverted()`, `MarkConverted()`, `BuildFlow()`, `CheckDuplicate()`, `GetStepDurations()`
- `PipelinePhase` — universal lifecycle: Queued, Done, Error, Skipped. `IsActive()`/`IsTerminal()` helpers
- `PipelineState` — shared state class: statuses dict, converted set (seeded from DB on startup), cancellation tokens, correlation IDs, phase timings, bridge
- `PipelineStatusEvent` — generic event: `ItemName`, `Phase`, `Message`, `OutputFilename`, `CorrelationId`
- `PipelineFlowInfo` / `PipelineFlowStep` — pipeline's self-description of its flow for a given item
- `DuplicateCheckResult` — pipeline's answer for duplicate detection
- Console-specific phases (e.g. `Ps3Phase.Extracting`) extend the universal set
- `DownloadQueue.GetPipeline(platform)` — registry for pipeline lookup by platform

## Duplicate Detection

- `POST /api/queue` checks for duplicates before adding URLs
- `QueueRepository.CheckDuplicatesAsync()` — DB query across `queued_urls` and `completed_urls` (case-insensitive URL match, includes platform from metadata)
- Pipeline-owned: `IPipeline.CheckDuplicate()` — each console defines its own filesystem/phase rules
- PS3 rules: active conversions always block, terminal states check disk (archive + ISO existence)
- No files on disk → not a duplicate (user can re-download freely)
- `AddRequest.Force` flag to override duplicate check
- Frontend `DuplicateDialog` shows per-file status with force-add option

## Frontend (React + Vite + Tailwind)

- `VimmsDownloader/client/` — React 19 + TypeScript + Vite + Tailwind CSS v4
- **PWA** — installable via vite-plugin-pwa, auto-update service worker, workbox caching
- PS3 XMB-inspired dark theme with blue glow accents, PS3 controller button colors (X=blue, O=red, △=green, □=purple)
- **Responsive** — mobile-friendly with `sm:` breakpoint, touch actions always visible, horizontal scroll tabs
- qBittorrent-style layout: Header → Toolbar → ControlBar → TabBar → Content → StatusBar
- Tabs: Active, Completed, Metrics (always visible), Library (beta flag), Sync (beta flag), Events (developer flag), Settings
- **Library tab** (`components/library/`) — browse the catalog with console / availability / 1G1R / name filters (persisted to `localStorage` across navigation); per-row badges for emulator compat, Owned/Verified, and **Vimm match / no Vimm**; toolbar Sync/Scan/Verify/Compat/**Vimm** buttons; `SetsDialog` (manage archive.org sets, also reachable from Settings → Archive); `FormatPickerDialog` (choose the Vimm download format when a bound game offers more than one).
- State: React Query for REST data, `DownloadContext` (useReducer) for SignalR live state
- `useSignalR` hook with auto-reconnect, invalidates React Query on data events + `MetaReady` for instant metadata refresh
- Drag-and-drop queue reordering (HTML5 native, bulk reorder via `POST /api/queue/reorder`)
- Auto-restores download state from `/api/data` on page load (isRunning, currentUrl, progress)
- Chart.js + react-chartjs-2 for real-time download speed graph in Metrics tab
- Step durations in pipeline trace (done steps show time, active steps show elapsed)
- `bun run build` outputs to `../wwwroot/`, served by .NET static files
- Dev: Vite proxy on :5173 → .NET on :5031

### Feature Flags
- Stored in `settings` table, exposed in `GET /api/settings` response
- Frontend `TabBar` accepts `hiddenTabs` set, `App.tsx` gates tabs on flags
- Settings UI has a Feature Flags section with toggles
- Two tiers: Beta (Library, Sync) and Developer (Events)
- Metrics tab is always visible — not behind a flag

## API Endpoints (47 total)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/data` | Queue + history with pipeline trace + download status |
| GET | `/api/settings` | System info + user settings (incl. archive settings) |
| POST | `/api/settings` | Save setting by key (refreshes S3 auth on `archive_s3_*`) |
| POST | `/api/settings/check-path` | Validate directory path |
| GET | `/api/version` | Update check (hourly polling) |
| GET | `/api/meta` | Source metadata cache (`?source=&sourceId=`) |
| GET | `/api/metrics` | Disk usage, queue/completed/orphaned/downloading sizes |
| GET | `/api/events` | Paginated event log with type/item filters |
| POST | `/api/queue` | Add URLs (duplicate check, force flag, default format, source) |
| PATCH | `/api/queue/{id}` | Move or set format |
| DELETE | `/api/queue/{id}` | Remove from queue |
| DELETE | `/api/queue` | Clear queue |
| POST | `/api/queue/reorder` | Bulk drag-and-drop reorder |
| GET | `/api/queue/export` | Export queue JSON |
| POST | `/api/queue/import` | Import queue JSON (triggers background metadata fetch) |
| DELETE | `/api/completed/{id}` | Remove history entry (`?deleteFiles=true` deletes archive + ISO) |
| GET | `/api/sources` | Registered download sources (for the picker) |
| GET | `/api/sources/{source}/sets` | Browse a source's sets/collections |
| GET | `/api/sources/{source}/files` | List files in a source set |
| GET | `/api/catalog/status` | Per-console counts/versions + running background jobs |
| GET | `/api/catalog/consoles` | Consoles with counts (Library filter) |
| GET | `/api/catalog/games` | Paged/filtered game browse (carries `vimmMatch` + per-emulator `compat`; `?emulator=&compat=` filter) |
| GET | `/api/catalog/emulators` | Emulators with ingested compat (Library filter) |
| GET | `/api/catalog/games/{id}/vimm` | A game's Vimm vault id + formats (picker) |
| GET | `/api/catalog/games/{id}/image` | A game's cached box art / title screen (`?type=`); 404 = no art |
| GET | `/api/catalog/games/{id}/description` | A game's IGDB description (Library detail panel); 404 = none |
| POST | `/api/catalog/games/{id}/queue` | Resolve + queue (archive → Vimm fallback, `?format=`) |
| POST | `/api/catalog/games/queue` | Batch resolve + queue selected games (E3b) |
| GET | `/api/catalog/curate` | Best missing games (by rank) within a `budgetBytes` budget (`?maxCount=`); pre-select → batch queue |
| POST | `/api/catalog/sync` | Sync No-Intro/Redump DATs (background) |
| POST | `/api/catalog/scan` | Scan `completed/` for owned games (background) |
| POST | `/api/catalog/import` | Ingest the import drop folder — hash-match → place/reject (background) |
| POST | `/api/catalog/verify` | Verify owned files' CRC32 (background) |
| POST | `/api/catalog/compat/sync` | Sync every registered emulator's compatibility (background) |
| POST | `/api/catalog/vimm-sync` | Hash-bind catalog ↔ Vimm (`?console=`, background) |
| POST | `/api/catalog/igdb-sync` | Sync IGDB descriptions (Twitch OAuth; `?force=`, background) |
| POST | `/api/catalog/igdb-rank-sync` | Sync IGDB rankings → per-game quality score (Twitch OAuth; `?force=`, background) |
| POST | `/api/catalog/ra-sync` | Sync RetroAchievements popularity → blended into rank (hash-join carts; `?force=`, background) |
| GET | `/api/catalog/sets` | List download sets |
| POST | `/api/catalog/sets` | Add a set (name + console + links) |
| PUT | `/api/catalog/sets/{id}` | Update a set |
| DELETE | `/api/catalog/sets/{id}` | Delete a set |
| POST | `/api/ps3/convert` | Convert all or single |
| POST | `/api/ps3/action` | Mark-done or abort |
| POST | `/api/sync/compare` | Set path + compare |
| POST | `/api/sync/copy` | Copy all or single |
| POST | `/api/sync/cancel` | Cancel sync |

## Volume Layout (Docker)

Single bind mount: `-v ~/vimm:/vimms`

```
/vimms/
├── data/
│   └── queue.db          ← SQLite database (queue + catalog)
└── downloads/
    ├── downloading/      ← Partial files (auto-resume)
    ├── completed/        ← Archives + ISOs, sorted into EmuDeck per-console folders
    │   ├── ps3/          ← e.g. PlayStation 3 archives + converted ISOs
    │   ├── gc/           ← e.g. GameCube
    │   └── snes/         ← e.g. Super Nintendo
    └── ps3_temp/         ← Conversion temp (auto-cleaned)
```

- Download path derived from DB connection string — `data/` and `downloads/` are siblings
- `Dockerfile` sets `ConnectionStrings__Default="Data Source=/vimms/data/queue.db"`
- `InitAsync` creates `data/` dir, startup creates `downloading/` + `completed/` subdirs
- Bare metal: no connection string override → DB in working dir, downloads in `~/Downloads`

### Completed-file organization (EmuDeck layout)

- Completed downloads are sorted into `completed/{console}/` using `ConsoleDirectories.Resolve(platform)` — folder names match EmuDeck / EmulationStation (`ps3`, `gc`, `snes`, `psx`, …). Platform comes from the source (e.g. `VaultPageParser.ExtractPlatform`).
- Unknown/empty platforms stay in `completed/` root (no wrong-folder guessing). Only **new** downloads are sorted — pre-existing flat files are left where they are.
- PS3 conversion output (ISO / extracted files) lands in the **same** console folder as its archive.
- All readers recurse into the subfolders: `/api/data`, `/api/metrics`, catalog scan, PS3 convert-all, orphan cleanup, and Sync (which also mirrors the per-console folder onto the sync target). Duplicate detection / delete resolve the item's folder from the stored `filepath`, so both new (nested) and legacy (flat) layouts work.

## Testing

416 tests across 7 projects:
- 128 Download (state management, file recovery, vault parser, source seam, platform extraction, EmuDeck console-folder mapping, format resolution, duplicate detection, edge cases)
- 87 Sync (real file I/O, disk simulation, edge cases)
- 87 Host `VimmsDownloader.Tests` (real `DatabaseMigrator`/repositories, catalog query, sets, resolve + archive→Vimm fallback, `ArchiveAuth`, Vimm binding/scrape, source identity)
- 62 Catalog (ClrMamePro parser, sync service, matcher, dedup, compat sources + registry, Crc32, `CatalogSystems`, `VimmSystems`, `VimmVaultParser`)
- 25 Extractor (7z via Testcontainers — container tests skip when Docker is unavailable)
- 17 Ps3Pipeline (pipeline state, rename, extract, abort, IPipeline contract)
- 10 Ps3IsoTools (ParamSfo, FindJbFolder, IsoFilenameFormatter)

Most integration tests use real file I/O via `TempDirectory` or a temp SQLite file. Container tests use `ghcr.io/eduvhc/vimm-dl-tools`.

## Docker

- Main app: `Dockerfile` at repo root. Multi-stage (Bun frontend → ps3tools → .NET AOT → runtime).
- Tools image: `Modules/Module.Core.Testing/Dockerfile.tools`. Published to `ghcr.io/eduvhc/vimm-dl-tools:latest`.
- Single volume: `/vimms` (data/ + downloads/)
- Port 5000
- Assembly version set from git tag via `ARG VERSION` build arg
- **Sync to external drive** — the target drive must be bind-mounted into the container:
  ```bash
  docker run -p 5000:5000 \
    -v ~/vimm:/vimms \
    -v /mnt/usb/PS3ISO:/sync-target \
    --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
  ```

## Roadmap

See [ROADMAP.md](ROADMAP.md). **Shipped:** the No-Intro/Redump catalog (all consoles with a libretro DAT), archive.org sets + settings, the **Catalog ↔ Vimm hash binding** (scrape → hash-match → bind vault URL + formats → archive-preferred download with Vimm fallback + format picker), and **Phase C — identity/dedup deepening** (epic #96): catalog-game identity (`game_id` + `format`) on the event log + `completed_urls`, events stamped with it at the SignalR bridge, **hash-based owned detection** (CRC32/MD5/SHA1, name-independent), **cross-format/source duplicate detection**, and one Library row per game with its consolidated formats/sources. **Local catalog import** (epic #118): a `feature_import`-gated drop folder that hash-matches user-supplied ROMs and `.zip`/`.7z` archives into the catalog — placing matches into `completed/{console}/` (owned) and non-matches into `rejected/` — header-aware for iNES/FDS/Lynx/7800. **Next:** future console pipelines via `IPipeline`; per-game grouping for queue/history + live conversions (#151).

## User Preferences

- Keep it simple. Repository abstraction for DB. No EF Core, no Dapper.
- Result pattern for expected errors, exceptions only for critical failures.
- Backend is the king — frontend renders what backend says, no business logic in UI.
- Modern dark UI — PS3 XMB aesthetic, blue glow, PS3 button colors.
- 2 decimal precision on all percentages and file sizes.
- No redundant status info. Errors only when they happen.
- Linux is the target platform. Windows dev supported (7z auto-detection).
- Bun for frontend builds (not npm).
- Per-platform settings convention: `ps3_*` prefix; per-source convention: `archive_*` prefix.
- Identity by hash: Vimm bound to the catalog by CRC32/MD5/SHA1, not by name.
- MockServer on 5111, main app on 5031 (dev) / 5000 (Docker).
- Future console support: add Module.{Console}Tools + Module.{Console}Pipeline, implement IPipeline.
