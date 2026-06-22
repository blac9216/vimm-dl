export interface Ps3IsoStatusEvent {
  itemName: string
  phase: string
  message: string
  outputFilename?: string
  correlationId?: string
  /** Catalog identity stamped by the bridge (#151/A) — group live conversions by game across formats;
   *  null/absent for legacy/unmatched items (which group by itemName). itemName stays the abort key. */
  gameId?: number | null
  format?: number | null
}

export interface SyncProgressEvent {
  filename: string
  percent: number
  copied: number
  total: number
}

export interface SyncCompletedEvent {
  filename: string
  success: boolean
  error?: string
}

export interface CompletedEvent {
  url: string
  filename: string
  filepath: string
}
