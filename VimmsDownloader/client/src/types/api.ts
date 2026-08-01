export interface DataResponse {
  queued: QueuedItem[]
  history: HistoryItem[]
  isRunning: boolean
  isPaused: boolean
  currentFile: string | null
  currentUrl: string | null
  progress: string | null
  totalBytes: number
  downloadedBytes: number
  /** All in-flight downloads, each with independent progress (EPIC #113). currentUrl/progress above are aliases of the first. */
  activeDownloads: ActiveDownload[]
}

/** One in-flight download (EPIC #113 / A1). */
export interface ActiveDownload {
  key: string
  url: string
  source: string
  filename: string | null
  state: string // starting | downloading | done | error
  progress: string | null
  pct: number
  speedMBps: number
  downloaded: number
  total: number
}

export interface QueuedItem {
  id: number
  url: string
  format: number
  title: string | null
  platform: string | null
  size: string | null
  formats: string | null
}

export interface FormatOption {
  value: number
  label: string
  title: string
  size: string
}

export interface TraceStep {
  name: string
  status: 'pending' | 'active' | 'done' | 'error' | 'skipped'
  message: string | null
  durationMs: number | null
}

export interface PipelineTrace {
  pipelineType: string
  steps: TraceStep[]
  isoFilename: string | null
  isoSize: number | null
  actions: string[]
}

export interface HistoryItem {
  id: number
  url: string
  filename: string
  filepath: string | null
  title: string | null
  platform: string | null
  size: string | null
  fileExists: boolean
  fileSize: number | null
  trace: PipelineTrace | null
  completedAt: string | null
  format: number | null
  /** Catalog game identity (#151/B) — group completed copies of one game; null for legacy/unmatched. */
  gameId: number | null
}

export interface MetaResponse {
  title: string
  platform: string
  size: string
  formats: string | null
  serial: string | null
}

// Canonical catalog (No-Intro / Redump)
export interface CatalogConsole {
  console: string
  gameCount: number
  ownedCount: number
  displayName: string
}

// One emulator's playability verdict for a game (e.g. { emulator: 'rpcs3', status: 'Playable' }).
export interface CompatStatus {
  emulator: string
  status: string
}

// An emulator whose compatibility is ingested — for the Library emulator/status filter.
export interface Emulator {
  id: string
  name: string
  console: string
  matchKind: string   // how it joins to a game: 'serial' | 'title_id' | 'name'
}

export interface CatalogGame {
  id: number
  name: string
  console: string
  region: string | null
  serial: string | null
  languages: string | null
  size: number
  owned: boolean
  compat: CompatStatus[]     // per-emulator playability (may be empty)
  verified: boolean | null
  vimmMatch: string | null   // 'sha1' | 'md5' | 'crc' (matched) | 'none' (no match) | null (unscraped)
  // #289: archive.org set-index availability, same shape as vimmMatch (hash kinds, plus 'name' for a
  // filename-stem fallback match) | 'none' (indexed, no match) | null (not yet indexed).
  archiveMatch: string | null
  // Phase C (C5): the game's formats/sources consolidated into one row.
  availableFormats: number[] // Vimm download format alts offered for this game
  ownedFormats: number[]     // download formats already on disk for this game
  ownedSources: string[]     // sources the on-disk copies came from (e.g. 'vimm', 'archive')
  // D2b-2: origin(s) backing this catalog entry — 'libretro' | 'daily-bundle' (DAT-sourced), or
  // 'vimm' where no public DAT exists and Vimm is authoritative (Wii U discs). See filters.ts.
  origins: string[]
  rankScore: number | null   // R1 (#140): IGDB-derived "best games" score; null = unranked
}

export interface CatalogGamesResponse {
  total: number
  page: number
  pageSize: number
  games: CatalogGame[]
}

// Library name-search mode (E3b): substring (default), glob (*,?), or regex.
export type SearchMode = 'substring' | 'glob' | 'regex'

// Library sort order (R1 #140): alphabetical (default) or by "best games" rank score (unranked last).
export type SortMode = 'name' | 'rank'

// Result of a batch queue (E3b "queue selected"): one entry per requested game id.
export interface CatalogQueueResult {
  id: number
  status: string         // 'queued' | 'duplicate' | 'unavailable' | 'unknown'
  source: string | null
}
export interface CatalogQueueBatchResponse {
  queued: number
  skipped: number
  failed: number
  results: CatalogQueueResult[]
}

// Curation (R3): the best non-owned games (by rank) that fit a cumulative byte budget, from
// GET /api/catalog/curate. ids are pre-selected in the Library; the user confirms → batch queue.
export interface CatalogCurateResponse {
  ids: number[]
  count: number
  totalBytes: number
  budgetBytes: number
}

// A game's Vimm download options (for the format picker), from GET /api/catalog/games/{id}/vimm.
export interface CatalogVimmFormat {
  alt: number
  label: string
  sizeBytes: number
  sizeText: string | null
}

export interface CatalogVimm {
  vaultId: number
  formats: CatalogVimmFormat[]
}

// A game's IGDB description (M3 detail panel), from GET /api/catalog/games/{id}/description (404 → none).
export interface CatalogGameDescription {
  description: string
}

export interface CatalogSystemStatus {
  datName: string
  console: string
  source: string
  datVersion: string | null
  gameCount: number
  syncedAt: string | null
}

/**
 * One background job as reported by `GET /api/jobs` (L1 #255) — one row per registered
 * `BackgroundJobGate`. `percent` is null when the job doesn't know a total up front (indeterminate
 * progress). NOTE: `startedAt`/`elapsedMs` describe the *last* run and keep growing after it ends,
 * so elapsed may only be rendered while `running` is true.
 *
 * #292: additive last-run facts (in-memory only, reset on server restart) so the Jobs tab's Completed
 * sub-view can show a finished run's outcome/duration without the still-growing `elapsedMs` above. All
 * three are null until the gate has completed (via `Run`) at least once.
 */
export interface JobStatus {
  kind: string
  running: boolean
  message: string | null
  current: number | null
  total: number | null
  percent: number | null
  startedAt: string | null
  elapsedMs: number | null
  lastCompletedAt: string | null
  lastDurationMs: number | null
  /** 'completed' | 'failed' | 'cancelled' */
  lastOutcome: string | null
}

export interface CatalogStatus {
  syncing: boolean
  scanning: boolean
  compatSyncing: boolean
  verifying: boolean
  vimmSyncing: boolean
  importing: boolean
  igdbSyncing: boolean
  raSyncing: boolean
  totalGames: number
  systems: CatalogSystemStatus[]
}

export interface CatalogSet {
  id: number
  name: string
  console: string
  links: string[]
}

export interface VersionResponse {
  current: string
  latest: string | null
  hasUpdate: boolean
  url: string | null
  changelog: string | null
}

// NOTE (#258): the URL paste bar and its force-add duplicate dialog were removed — all downloads are
// queued from the Library, whose batch-queue response reports duplicates per game (CatalogQueueResult
// above). Queue JSON import goes through `POST /api/queue/import` (`POST /api/queue` itself was
// removed in #299).

export interface QueueExportItem {
  url: string
  format: number
}

export interface QueueImportResponse {
  added: number
  skipped: number
}

// Merged: config + settings
export interface SettingsResponse {
  platform: string
  osDescription: string
  hostname: string
  user: string
  defaultPath: string
  activePath: string
  fixThe: boolean
  addSerial: boolean
  stripRegion: boolean
  ipv4: string
  ps3Parallelism: number
  ps3DefaultFormat: number
  ps3PreserveArchive: boolean
  featureSync: boolean
  featureEvents: boolean
  featureImport: boolean
  catalogDatSource: string
  archiveParallelism: number
  archiveRetries: number
  archiveIdle: number
  archiveS3Access: string
  archiveS3Secret: string
  importPath: string
  rejectedPath: string
  wiiUCommonKey: string
  igdbClientId: string
  igdbClientSecret: string
  raApiKey: string
}

export interface CheckPathResponse {
  path: string | null
  exists: boolean
  writable: boolean
  freeSpace: number | null
  error: string | null
}

export interface Ps3ConvertResponse {
  queued: number
  skipped: number
  files: string[]
}

export interface SyncCompareResponse {
  syncPath: string
  pathExists: boolean
  new: SyncFileInfo[]
  synced: SyncFileInfo[]
  targetOnly: SyncFileInfo[]
  source: SyncDiskInfo | null
  target: SyncDiskInfo | null
  error: string | null
}

export interface SyncDiskInfo {
  label: string
  isoCount: number
  isoTotalSize: number
  freeSpace: number
  totalSpace: number
}

export interface SyncFileInfo {
  name: string
  size: number
}

export interface MetricsResponse {
  diskFreeBytes: number
  diskTotalBytes: number
  queuedTotalBytes: number
  queuedCount: number
  completedTotalBytes: number
  completedCount: number
  orphanedTotalBytes: number
  orphanedCount: number
  downloadingTotalBytes: number
  downloadingCount: number
}

export interface EventRow {
  id: number
  itemName: string
  eventType: string
  phase: string | null
  message: string | null
  data: string | null
  timestamp: string
  correlationId: string | null
  /** Catalog game identity (Phase C) — present once an event resolves to a catalog game; null for legacy/unmatched. */
  gameId: number | null
  format: number | null
}

export interface EventsResponse {
  events: EventRow[]
  total: number
}
