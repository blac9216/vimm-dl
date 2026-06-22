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
}

export interface MetaResponse {
  title: string
  platform: string
  size: string
  formats: string | null
  serial: string | null
}

export interface SourceInfo {
  id: string
  displayName: string
  catalog: boolean
}

// Canonical catalog (No-Intro / Redump)
export interface CatalogConsole {
  console: string
  gameCount: number
  ownedCount: number
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
  compat: string | null
  verified: boolean | null
  vimmMatch: string | null   // 'sha1' | 'md5' | 'crc' (matched) | 'none' (no match) | null (unscraped)
  // Phase C (C5): the game's formats/sources consolidated into one row.
  availableFormats: number[] // Vimm download format alts offered for this game
  ownedFormats: number[]     // download formats already on disk for this game
  ownedSources: string[]     // sources the on-disk copies came from (e.g. 'vimm', 'archive')
}

export interface CatalogGamesResponse {
  total: number
  page: number
  pageSize: number
  games: CatalogGame[]
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

export interface CatalogSystemStatus {
  datName: string
  console: string
  source: string
  datVersion: string | null
  gameCount: number
  syncedAt: string | null
}

export interface CatalogStatus {
  syncing: boolean
  scanning: boolean
  compatSyncing: boolean
  verifying: boolean
  vimmSyncing: boolean
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

export interface AddResponse {
  queued: { id: number; url: string; format: number }[] | null
  duplicates: DuplicateInfo[] | null
}

export interface DuplicateInfo {
  url: string
  source: 'queued' | 'completed'
  reason: string
  title: string | null
  filename: string | null
  isoFilename: string | null
  archiveExists: boolean
  isoExists: boolean
  /** Phase C (C4): this is the same catalog game in a different format/source, not an exact duplicate. */
  crossFormat: boolean
  existingFormat: number | null
}

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
  featureLibrary: boolean
  archiveParallelism: number
  archiveRetries: number
  archiveIdle: number
  archiveS3Access: string
  archiveS3Secret: string
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
