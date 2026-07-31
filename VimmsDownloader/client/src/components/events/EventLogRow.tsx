import { useState } from 'react'
import { Badge } from '../shared/Badge'
import { eventBadge, formatEventTimestamp } from '../../lib/events'
import type { EventRow } from '../../types/api'

// One row of the Events tab (issue #262, design handoff "6. Events"): fixed columns —
// timestamp 110px / badge slot 80px / item 180px / message flexes / disclosure caret — over an
// expandable detail panel indented to the badge column. Expansion is *controlled* by EventsPanel so
// only one row is open at a time (the spec's "rows expand one at a time").
//
// This is Events-specific on purpose: `EventItem.tsx` stays as-is for the Import tab, which is being
// restyled in parallel (#263) and owns its own row presentation.

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false)

  function handleCopy(ev: React.MouseEvent) {
    ev.stopPropagation()
    navigator.clipboard.writeText(text)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <button onClick={handleCopy}
      className="text-[10px] px-1.5 py-0.5 rounded text-text-4 hover:text-text-2
        hover:bg-surface-3/40 transition-colors shrink-0">
      {copied ? 'Copied' : 'Copy'}
    </button>
  )
}

function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-start gap-2.5">
      <span className="text-[10px] text-text-faint w-[60px] shrink-0">{label}</span>
      {children}
    </div>
  )
}

interface EventLogRowProps {
  event: EventRow
  expanded: boolean
  onToggle: () => void
}

export function EventLogRow({ event: e, expanded, onToggle }: EventLogRowProps) {
  const badge = eventBadge(e)
  const timestamp = formatEventTimestamp(e.timestamp)

  return (
    <div className="border-b border-[#141326]">
      {/* Desktop: single fixed-column row */}
      <div
        className="hidden sm:flex items-center gap-[13px] px-[22px] py-[9px] cursor-pointer
          hover:bg-accent/[0.06] transition-colors"
        onClick={onToggle}
      >
        <span className="w-[110px] shrink-0 font-mono text-[10px] text-text-4 tabular-nums">
          {timestamp}
        </span>
        <span className="w-20 shrink-0">
          <Badge variant={badge.variant}>{badge.label}</Badge>
        </span>
        <span className="w-[180px] shrink-0 text-[11px] text-text-2 truncate" title={e.itemName}>
          {e.itemName}
        </span>
        <span className="flex-1 min-w-0 text-[11px] text-text-3 truncate" title={e.message ?? undefined}>
          {e.message}
        </span>
        <span className={`shrink-0 text-[10px] ${expanded ? 'text-text-faint' : 'text-text-4'}`}>
          {expanded ? '▼' : '▶'}
        </span>
      </div>

      {/* Mobile: two-line card */}
      <div
        className="sm:hidden px-3 py-2 cursor-pointer space-y-1 hover:bg-accent/[0.06] transition-colors"
        onClick={onToggle}
      >
        <div className="flex items-center gap-2">
          <Badge variant={badge.variant}>{badge.label}</Badge>
          <span className="flex-1 min-w-0 text-[11px] text-text-2 truncate">{e.itemName}</span>
          <span className={`shrink-0 text-[10px] ${expanded ? 'text-text-faint' : 'text-text-4'}`}>
            {expanded ? '▼' : '▶'}
          </span>
        </div>
        <div className="flex items-center gap-2 text-[10px] text-text-3">
          <span className="font-mono tabular-nums shrink-0 text-text-4">{timestamp}</span>
          {e.message && (
            <>
              <span className="text-border-light">&middot;</span>
              <span className="truncate">{e.message}</span>
            </>
          )}
        </div>
      </div>

      {expanded && (
        <div className="flex flex-col gap-1 bg-surface-2/50 px-3 pb-3 pt-1 sm:pl-[145px] sm:pr-[22px] sm:pb-3 sm:pt-1">
          <DetailRow label="Item">
            <span className="flex-1 min-w-0 font-mono text-[11px] text-text-2 break-all">{e.itemName}</span>
            <CopyButton text={e.itemName} />
          </DetailRow>
          <DetailRow label="Message">
            <span className="flex-1 min-w-0 text-[11px] text-text-2 break-all">{e.message ?? '-'}</span>
            {e.message && <CopyButton text={e.message} />}
          </DetailRow>
          {e.phase && (
            <DetailRow label="Phase">
              <span className="font-mono text-[11px] text-text-2">{e.phase}</span>
            </DetailRow>
          )}
          {e.data && (
            <DetailRow label="Data">
              <pre className="flex-1 min-w-0 font-mono text-[10px] text-text-3 break-all whitespace-pre-wrap">{e.data}</pre>
              <CopyButton text={e.data} />
            </DetailRow>
          )}
          <DetailRow label="Type">
            <span className="font-mono text-[11px] text-text-3">{e.eventType}</span>
          </DetailRow>
          {e.correlationId && (
            <DetailRow label="Run ID">
              <span className="font-mono text-[11px] text-text-3">{e.correlationId}</span>
              <CopyButton text={e.correlationId} />
            </DetailRow>
          )}
        </div>
      )}
    </div>
  )
}
