import { fmtBytes } from '../../lib/format'
import type { SyncDiskInfo } from '../../types/api'

// Design handoff "5. Sync" → disk card: 13px-radius card, uppercase label, ISOs/Free/Total rows,
// a 5px usage bar (source green / target amber) and a right-aligned percentage.

interface SyncDiskCardProps {
  /** "Source" or "Target" — rendered uppercase in front of `sub`. */
  label: string
  /** Path shown after the label (completed/ for the source, the compared path for the target). */
  sub?: string | null
  info: SyncDiskInfo | null
  /** The spec fixes the fill per card: source `#3ee0a0`, target `#f4b44e`. */
  variant: 'source' | 'target'
}

export function SyncDiskCard({ label, sub, info, variant }: SyncDiskCardProps) {
  if (!info) return null

  const usedPct = info.totalSpace > 0
    ? ((info.totalSpace - info.freeSpace) / info.totalSpace) * 100
    : 0

  return (
    <div className="flex-1 min-w-0 rounded-[13px] border border-border bg-card/45 p-[15px]">
      {/* The drive's volume label (backend `info.label`) rides along as the tooltip. */}
      <div
        className="mb-2.5 text-[10px] uppercase tracking-[0.1em] text-text-body-2 truncate"
        title={info.label}
      >
        {label}{sub ? ` · ${sub}` : ''}
      </div>

      <div className="flex flex-col gap-[5px] text-[10.5px] text-text-2">
        <span className="flex justify-between gap-3">
          <span className="text-text-3">ISOs</span>
          <span className="font-mono tabular-nums">{info.isoCount} ({fmtBytes(info.isoTotalSize)})</span>
        </span>
        <span className="flex justify-between gap-3">
          <span className="text-text-3">Free</span>
          <span className="font-mono tabular-nums">{fmtBytes(info.freeSpace)}</span>
        </span>
        <span className="flex justify-between gap-3">
          <span className="text-text-3">Total</span>
          <span className="font-mono tabular-nums">{fmtBytes(info.totalSpace)}</span>
        </span>
      </div>

      <div className="mt-[11px] h-[5px] rounded-[3px] bg-surface-3/60 overflow-hidden">
        <div
          className={`h-full rounded-[3px] ${variant === 'source' ? 'bg-success' : 'bg-warning'}`}
          style={{ width: `${usedPct.toFixed(1)}%` }}
        />
      </div>
      <div className="mt-1 text-right font-mono text-[10px] text-text-3 tabular-nums">
        {usedPct.toFixed(1)}%
      </div>
    </div>
  )
}
