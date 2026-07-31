import { useSyncCopy } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { fmtBytes } from '../../lib/format'
import { ProgressBar } from '../shared/ProgressBar'
import type { SyncFileInfo } from '../../types/api'

// Design handoff "5. Sync" → file row: a 52px status word, the mono filename, the size, and a Copy
// button on new files. The live per-row copy progress bar (SignalR `SyncProgress`) is ours — the
// spec omits it, but it is the only feedback a running copy gives, so it stays.

interface SyncFileRowProps {
  file: SyncFileInfo
  status: 'new' | 'synced' | 'target-only'
}

const statusStyles: Record<SyncFileRowProps['status'], { label: string; color: string }> = {
  'new': { label: 'NEW', color: 'text-success' },
  'synced': { label: 'SYNCED', color: 'text-info' },
  'target-only': { label: 'TARGET', color: 'text-text-3' },
}

export function SyncFileRow({ file, status }: SyncFileRowProps) {
  const { state } = useDownload()
  const copyMutation = useSyncCopy()

  const copyProgress = state.syncCopying[file.name]
  const isCopying = !!copyProgress
  const { label, color } = statusStyles[status]

  return (
    <div className="flex items-center gap-2 sm:gap-[13px] px-3 sm:px-[22px] py-[9px]
      border-b border-border-row hover:bg-accent/[0.06] transition-colors">
      <span className={`w-[52px] shrink-0 text-[9px] font-bold tracking-[0.08em] ${color}`}>
        {label}
      </span>
      <span className="flex-1 min-w-0 truncate font-mono text-[11.5px] text-text-body-2">
        {file.name}
      </span>
      <span className="w-[66px] shrink-0 text-right font-mono text-[10px] text-text-3 tabular-nums">
        {fmtBytes(file.size)}
      </span>

      {isCopying && (
        <div className="w-20 sm:w-24 shrink-0">
          <ProgressBar width={`${copyProgress.percent}%`} variant="sync" />
        </div>
      )}

      {/* Fixed slot so the size column lines up whether or not a row can be copied. */}
      <span className="w-[52px] shrink-0 flex justify-end">
        {status === 'new' && !isCopying && (
          <button
            onClick={() => copyMutation.mutate(file.name)}
            disabled={copyMutation.isPending}
            title={`Copy ${file.name} to the target drive`}
            className="px-[11px] py-[4px] text-[10px] rounded-[7px] border border-accent/30 text-info
              hover:bg-accent/10 disabled:opacity-40 disabled:hover:bg-transparent"
          >
            Copy
          </button>
        )}
      </span>
    </div>
  )
}
