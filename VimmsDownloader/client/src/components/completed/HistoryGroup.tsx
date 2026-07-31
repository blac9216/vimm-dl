import { useState } from 'react'
import { ConsoleTile } from '../shared/ConsoleTile'
import { HistoryItem } from './HistoryItem'
import { fmtBytes } from '../../lib/format'
import type { HistoryItem as HistoryItemType } from '../../types/api'

interface HistoryGroupProps {
  items: HistoryItemType[]
  showEventsLink?: boolean
  onViewEvents?: (itemName: string) => void
}

/**
 * One game with several completed formats/sources (#151/B), collapsed into a single expandable row that
 * mirrors the Library chips. Expanding reveals the per-format/source rows (full HistoryItems), so every
 * underlying action — convert, delete, view events — keeps working on the individual copy.
 */
export function HistoryGroup({ items, showEventsLink, onViewEvents }: HistoryGroupProps) {
  const [open, setOpen] = useState(false)
  const first = items[0]
  const title = first.title || first.filename
  const totalSize = items.reduce((s, i) => s + (i.fileSize ?? 0), 0)
  const formats = [...new Set(items.map(i => i.format).filter((f): f is number => f != null))].sort((a, b) => a - b)

  return (
    <div className="border-b border-border-row">
      <div className="flex items-center gap-[13px] px-3 sm:px-[22px] py-3 cursor-pointer hover:bg-accent/[0.06] transition-colors"
        onClick={() => setOpen(v => !v)}>
        <ConsoleTile platform={first.platform} size={34} />
        <div className="flex-1 min-w-0">
          <div className="text-[13px] text-text-body truncate">{title}</div>
          {formats.length > 0 && (
            <div className="flex items-center gap-1 text-[10px] text-text-faint">
              {formats.map(f => (
                <span key={f} className="font-mono px-1.5 py-px rounded-md border border-border-light text-text-2">f{f}</span>
              ))}
            </div>
          )}
        </div>
        <span className="shrink-0 w-[66px] text-right font-mono text-[11px] text-text-2 tabular-nums">
          {totalSize > 0 ? fmtBytes(totalSize) : '--'}
        </span>
        <span className="shrink-0 text-[10px] font-mono px-2 py-[3px] rounded-[7px] bg-info-bg text-info border border-info-border"
          title={`${items.length} copies of this game`}>
          {items.length}&times;
        </span>
        <span className="shrink-0 text-[10px] text-text-faint">{open ? '▼' : '▶'}</span>
      </div>
      {open && (
        <div className="border-l-2 border-l-accent/20 ml-3 sm:ml-[22px]">
          {items.map(item => (
            <HistoryItem key={item.id} item={item}
              showEventsLink={showEventsLink} onViewEvents={onViewEvents} />
          ))}
        </div>
      )}
    </div>
  )
}
