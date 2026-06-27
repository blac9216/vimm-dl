import { useState } from 'react'
import { PlatformIcon } from '../shared/PlatformIcon'
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
    <div className="border-b border-border/20">
      <div className="flex items-center gap-3 px-3 sm:px-5 py-2.5 cursor-pointer hover:bg-card-hover/40 transition-colors"
        onClick={() => setOpen(v => !v)}>
        <PlatformIcon platform={first.platform} />
        <div className="flex-1 min-w-0">
          <div className="text-sm text-text truncate">{title}</div>
          {formats.length > 0 && (
            <div className="flex items-center gap-1 text-[10px] text-text-4">
              {formats.map(f => (
                <span key={f} className="font-mono px-1 rounded bg-surface-3/40 text-text-4">f{f}</span>
              ))}
            </div>
          )}
        </div>
        <span className="text-[11px] font-mono text-text-3 w-16 text-right tabular-nums">
          {totalSize > 0 ? fmtBytes(totalSize) : '--'}
        </span>
        <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-accent/10 text-accent/70 border border-accent/20"
          title={`${items.length} copies of this game`}>
          {items.length}&times;
        </span>
        <span className={`text-[10px] text-text-4/40 transition-transform ${open ? 'rotate-90' : ''}`}>&#9654;</span>
      </div>
      {open && (
        <div className="border-l-2 border-l-accent/20 ml-3 sm:ml-5">
          {items.map(item => (
            <HistoryItem key={item.id} item={item}
              showEventsLink={showEventsLink} onViewEvents={onViewEvents} />
          ))}
        </div>
      )}
    </div>
  )
}
