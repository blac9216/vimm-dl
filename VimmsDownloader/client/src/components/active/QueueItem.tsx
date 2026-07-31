import { useDownload } from '../../hooks/useDownloadState'
import { useDeleteFromQueue, usePatchQueueItem, useSettings } from '../../api/queries'
import { ConsoleTile } from '../shared/ConsoleTile'
import { slugFromUrl } from '../../lib/format'
import type { QueuedItem, FormatOption } from '../../types/api'

interface QueueItemProps {
  item: QueuedItem
  isDragging: boolean
  isDragOver: boolean
  onDragStart: () => void
  onDragOver: (e: React.DragEvent) => void
  onDragLeave: () => void
  onDrop: () => void
  onDragEnd: () => void
}

const pillBase = 'shrink-0 px-[9px] py-[3px] text-[10px] font-semibold rounded-[7px] border'

export function QueueItem({
  item, isDragging, isDragOver,
  onDragStart, onDragOver, onDragLeave, onDrop, onDragEnd,
}: QueueItemProps) {
  const { state, connection } = useDownload()
  const { data: config } = useSettings()
  const deleteMutation = useDeleteFromQueue()
  const patchMutation = usePatchQueueItem()

  const title = item.title || slugFromUrl(item.url)
  // Per-download state (EPIC #113 / A2): each item finds its own in-flight record, so N concurrent
  // downloads each render independent progress. Falls back to the single live parse for legacy items.
  const active = state.activeDownloads.find(a => a.url === item.url)
  const isActive = !!active || state.activeUrl === item.url
  const isDownloading = isActive && state.running && !state.paused
  const isPaused = isActive && state.paused

  const legacyInfo = !active && state.activeUrl === item.url ? state.activeDlInfo : null
  const dlPct = active ? (active.pct >= 0 ? `${active.pct.toFixed(2)}%` : 'Starting') : legacyInfo?.pct ?? null
  const dlWidth = active ? `${Math.max(0, active.pct).toFixed(2)}%` : legacyInfo?.width ?? null
  const dlSpeed = active ? (active.speedMBps > 0 ? `${active.speedMBps.toFixed(2)} MB/s` : null) : legacyInfo?.speed ?? null
  const hasProgress = active ? active.pct >= 0 : !!legacyInfo

  const formats: FormatOption[] = item.formats ? JSON.parse(item.formats) : []

  function handlePlay() {
    if (!connection) return
    connection.invoke('StartSpecific', config?.activePath ?? null, item.id)
  }

  function handlePause() {
    if (!connection) return
    connection.invoke('PauseDownload')
  }

  function handleDelete() {
    deleteMutation.mutate(item.id)
  }

  function handleFormatChange(e: React.ChangeEvent<HTMLSelectElement>) {
    patchMutation.mutate({ id: item.id, format: parseInt(e.target.value) })
  }

  // State pill (design handoff §2): live percentage (indigo) / Paused (violet) / Queued (neutral).
  let pillClass = 'border-border-light bg-neutral-bg text-text-2'
  let pillText = 'Queued'
  if (isDownloading && hasProgress && dlPct) {
    pillClass = 'border-info-border bg-info-bg text-info'
    pillText = dlPct
  } else if (isDownloading) {
    pillClass = 'border-info-border bg-info-bg text-info'
    pillText = 'Starting'
  } else if (isPaused) {
    pillClass = 'border-paused-border bg-paused-bg text-paused'
    pillText = 'Paused'
  }

  return (
    <div
      draggable={!isActive}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      onDragEnd={onDragEnd}
      className={`group relative border-b border-border-row select-none transition-colors
        ${isActive ? '' : 'hover:bg-accent/[0.06]'}
        ${isDragging ? 'opacity-30' : ''}
        ${isDragOver ? 'border-t-2 border-t-accent/50' : ''}
        ${!isActive ? 'sm:cursor-grab sm:active:cursor-grabbing' : ''}
      `}
    >
      {/* Indigo overlay on the row that is currently downloading */}
      {isActive && (
        <span className="absolute inset-0 pointer-events-none
          bg-[linear-gradient(90deg,rgba(111,141,255,0.12),transparent_80%)]" />
      )}

      <div className="relative flex items-center gap-2 sm:gap-3 px-3 sm:px-[22px] py-2.5 sm:py-[11px]">
        {/* Drag handle — 6 dots (desktop only) */}
        <span className={`hidden sm:flex flex-col gap-[2px] shrink-0 opacity-30
          ${isActive ? 'invisible' : ''}`}>
          {[0, 1, 2].map(row => (
            <span key={row} className="flex gap-[2px]">
              <i className="w-[3px] h-[3px] rounded-full bg-text-3" />
              <i className="w-[3px] h-[3px] rounded-full bg-text-3" />
            </span>
          ))}
        </span>

        <ConsoleTile platform={item.platform} size={32} />

        <div className="flex-1 min-w-0">
          <div className="text-[13px] text-text-body truncate">{title}</div>
          {item.platform && (
            <div className="text-[10px] text-text-faint truncate">{item.platform}</div>
          )}
        </div>

        {formats.length > 1 && (
          <select
            value={item.format}
            onChange={handleFormatChange}
            title="Download format"
            className="hidden sm:block shrink-0 bg-transparent border border-border-light rounded-md
              px-2 py-[3px] font-mono text-[10px] text-text-2 focus:outline-none focus:border-accent/40"
          >
            {formats.map(f => (
              <option key={f.value} value={f.value}>{f.label}</option>
            ))}
          </select>
        )}

        <span className="hidden sm:inline shrink-0 w-[66px] text-right font-mono text-[11px] text-text-2 tabular-nums">
          {item.size || '--'}
        </span>

        <span className="hidden md:inline shrink-0 w-[74px] text-right font-mono text-[10px] text-link tabular-nums">
          {isDownloading && dlSpeed ? dlSpeed : ''}
        </span>

        <span className="hidden sm:block shrink-0 w-[120px]">
          {(isDownloading || isPaused) && dlWidth && (
            <span className="block h-[5px] rounded-[3px] bg-surface-3/70 overflow-hidden">
              <span
                className={`block h-full rounded-[3px] transition-all duration-500 ${
                  isPaused ? 'bg-paused/60' : 'bg-gradient-to-r from-accent to-accent-2'}`}
                style={{ width: dlWidth }}
              />
            </span>
          )}
        </span>

        <span className={`${pillBase} ${pillClass} tabular-nums`}>{pillText}</span>

        {/* Actions — always visible on touch, hover-reveal on desktop */}
        <div className="flex items-center gap-1 shrink-0 sm:opacity-0 sm:group-hover:opacity-100 transition-opacity duration-150">
          {isDownloading ? (
            <button
              onClick={handlePause}
              className="w-[25px] h-[25px] flex items-center justify-center rounded-[7px] text-[10px]
                border border-success/25 text-success hover:bg-success/10"
              title="Pause"
            >&#9208;</button>
          ) : (
            <button
              onClick={handlePlay}
              className="w-[25px] h-[25px] flex items-center justify-center rounded-[7px] text-[10px]
                border border-success/25 text-success hover:bg-success/10"
              title={isPaused ? 'Resume' : 'Start'}
            >&#9654;</button>
          )}
          <button
            onClick={handleDelete}
            className="w-[25px] h-[25px] flex items-center justify-center rounded-[7px] text-xs
              border border-error/25 text-error hover:bg-error/10"
            title="Remove"
          >&times;</button>
        </div>
      </div>
    </div>
  )
}
