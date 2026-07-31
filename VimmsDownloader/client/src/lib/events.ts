import type { EventRow } from '../types/api'

// Event-log presentation helpers (issue #262). The badge mapping is the design handoff's
// "6. Events" table: download_completed → Downloaded (green), download_error → Error (red),
// download_progress → Progress / download_status → Status (indigo), sync_completed → Synced (green),
// import matched → Matched (green) / rejected → Rejected (red), and pipeline_status by phase
// (done → Converted green, error → Error red, converting → Converting indigo,
// extracting → Extracting amber, everything else → Queued neutral).
//
// Lives in lib/ rather than in the row component because the Events tab and the Import tab both
// render event rows; `components/events/EventItem.tsx` (still used by Import, restyled separately
// in #263) keeps its own inline copy for now and can adopt these helpers when that lands.

export type EventBadgeVariant = 'downloading' | 'paused' | 'queued' | 'starting' |
  'extracting' | 'converting' | 'done' | 'error' | 'skipped' | 'waiting'

export interface EventBadge {
  variant: EventBadgeVariant
  label: string
}

export function eventBadge(e: EventRow): EventBadge {
  if (e.eventType === 'download_completed') return { variant: 'done', label: 'Downloaded' }
  if (e.eventType === 'download_error') return { variant: 'error', label: 'Error' }
  if (e.eventType === 'download_progress') return { variant: 'converting', label: 'Progress' }
  if (e.eventType === 'download_status') return { variant: 'starting', label: 'Status' }
  if (e.eventType === 'download_done') return { variant: 'done', label: 'Queue Done' }
  if (e.eventType === 'import') {
    return e.phase === 'matched'
      ? { variant: 'done', label: 'Matched' }
      : { variant: 'error', label: 'Rejected' }
  }
  if (e.eventType === 'sync_progress') return { variant: 'converting', label: 'Sync' }
  if (e.eventType === 'sync_completed') {
    const failed = e.message?.startsWith('Failed')
    return failed ? { variant: 'error', label: 'Sync Fail' } : { variant: 'done', label: 'Synced' }
  }
  if (e.eventType === 'pipeline_status') {
    if (e.phase === 'done') return { variant: 'done', label: 'Converted' }
    if (e.phase === 'error') return { variant: 'error', label: 'Error' }
    if (e.phase === 'extracting') return { variant: 'extracting', label: 'Extracting' }
    if (e.phase === 'converting') return { variant: 'converting', label: 'Converting' }
    if (e.phase === 'skipped') return { variant: 'skipped', label: 'Skipped' }
    if (e.phase === 'extracted') return { variant: 'waiting', label: 'Extracted' }
    return { variant: 'queued', label: 'Queued' }
  }
  return { variant: 'queued', label: e.eventType }
}

/** Backend timestamps are UTC without a zone suffix — append Z before parsing. */
export function formatEventTimestamp(ts: string): string {
  try {
    const d = new Date(ts + 'Z')
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
      + ' ' + d.toLocaleDateString([], { month: 'short', day: 'numeric' })
  } catch {
    return ts
  }
}
