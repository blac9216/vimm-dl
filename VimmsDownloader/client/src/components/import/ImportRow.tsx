import { Badge } from '../shared/Badge'
import type { EventRow } from '../../types/api'

/**
 * One "Recent imports" row (design handoff "7. Import", issue #263): a Matched/Rejected badge, the
 * file name in mono, and the destination (matched) or the rejection reason (rejected).
 *
 * Deliberately local to the Import tab instead of reusing `events/EventItem` — this row renders a
 * single event shape (`type=import`) in the spec's list layout, while the Events tab's row is a
 * generic, expandable audit line. Keeping them apart lets each follow its own spec.
 */

/** The backend's per-file import event payloads (`CatalogImportService.RecordAsync`). */
interface MatchedData { console?: string; dest?: string }
interface RejectedData { reason?: string }

function parseData<T>(data: string | null): T | null {
  if (!data) return null
  try {
    return JSON.parse(data) as T
  } catch {
    return null
  }
}

/**
 * The right-hand detail: where a matched file landed, or why a file was rejected. Prefers the
 * event's structured `data` (backend is the king) and falls back to its prose message.
 */
function importDetail(e: EventRow): string {
  if (e.phase === 'matched') {
    const d = parseData<MatchedData>(e.data)
    if (d?.console) return `→ completed/${d.console}/`
    const arrow = e.message?.indexOf('→') ?? -1
    if (arrow >= 0) return `→ ${e.message!.slice(arrow + 1).trim()}`
    return e.message ?? ''
  }
  const d = parseData<RejectedData>(e.data)
  if (d?.reason) return d.reason
  return e.message?.replace(/^Rejected:\s*/, '') ?? ''
}

function formatTimestamp(ts: string): string {
  const d = new Date(ts + 'Z')
  return Number.isNaN(d.getTime()) ? ts : d.toLocaleString()
}

export function ImportRow({ event: e }: { event: EventRow }) {
  const matched = e.phase === 'matched'
  const detail = importDetail(e)

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 sm:px-[17px] py-[11px]
      border-b border-border-row last:border-b-0 hover:bg-card-hover/30 transition-colors">
      <Badge variant={matched ? 'done' : 'error'}>{matched ? 'Matched' : 'Rejected'}</Badge>
      <span
        className="flex-1 min-w-0 truncate font-mono text-xs text-text-body-2"
        title={`${e.itemName}\n${formatTimestamp(e.timestamp)}`}
      >
        {e.itemName}
      </span>
      <span
        className={`w-full sm:w-auto sm:max-w-[45%] shrink-0 truncate text-[11px] sm:text-right ${
          matched ? 'text-text-3' : 'text-error/80'}`}
        title={detail}
      >
        {detail}
      </span>
    </div>
  )
}
