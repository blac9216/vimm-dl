import { useState } from 'react'
import { useDeleteCompleted, useConvertPs3, usePs3Action } from '../../api/queries'
import { Badge } from '../shared/Badge'
import { ConsoleTile } from '../shared/ConsoleTile'
import { fmtBytes, fmtDuration, fmtRelative } from '../../lib/format'
import type { HistoryItem as HistoryItemType, TraceStep } from '../../types/api'

type BadgeVariant = 'downloading' | 'paused' | 'queued' | 'starting' |
  'extracting' | 'converting' | 'done' | 'error' | 'skipped' | 'waiting'

const statusBadge: Record<string, BadgeVariant> = {
  pending: 'queued',
  active: 'converting',
  done: 'done',
  error: 'error',
  skipped: 'skipped',
}

function formatLabel(format: number | null): string {
  if (format === 0) return '0 — JB Folder (.7z)'
  if (format != null && format > 0) return `${format} — .dec.iso`
  return '—'
}

function StepIndicator({ step }: { step: TraceStep }) {
  const variant = statusBadge[step.status] ?? 'queued'
  const icon = step.status === 'done' ? '✓ ' :
    step.status === 'error' ? '✗ ' :
    step.status === 'active' ? '▶ ' : ''

  return (
    <div className="flex items-center gap-1.5">
      <Badge variant={variant}>{icon}{step.name}</Badge>
      {step.durationMs != null && step.status === 'done' && (
        <span className="text-[9px] font-mono text-text-4">{fmtDuration(step.durationMs)}</span>
      )}
      {step.durationMs != null && step.status === 'active' && (
        <span className="text-[9px] font-mono text-accent/50">{fmtDuration(step.durationMs)}</span>
      )}
      {step.status === 'active' && step.message && (
        <span className="text-[10px] text-text-4 truncate max-w-48">{step.message}</span>
      )}
    </div>
  )
}

function MetaRow({ label, value }: { label: string; value?: string | null }) {
  if (!value) return null
  return (
    <div className="flex items-start gap-2.5 py-0.5">
      <span className="text-[10px] text-text-faint w-[82px] shrink-0">{label}</span>
      <span className="text-[11px] text-text-2 font-mono break-all">{value}</span>
    </div>
  )
}

interface HistoryItemProps {
  item: HistoryItemType
  showEventsLink?: boolean
  onViewEvents?: (itemName: string) => void
}

export function HistoryItem({ item, showEventsLink, onViewEvents }: HistoryItemProps) {
  const deleteMutation = useDeleteCompleted()
  const convertMutation = useConvertPs3()
  const actionMutation = usePs3Action()
  const [expanded, setExpanded] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const title = item.title || item.filename
  const trace = item.trace

  const lastStep = trace?.steps.at(-1)
  const overallStatus = lastStep?.status ?? (item.fileExists ? 'none' : 'missing')
  const relative = fmtRelative(item.completedAt)

  const overallBadge: BadgeVariant =
    overallStatus === 'done' ? 'done' :
    overallStatus === 'error' ? 'error' :
    overallStatus === 'active' ? 'converting' :
    overallStatus === 'skipped' ? 'skipped' :
    'queued'

  const overallText =
    overallStatus === 'done' ? 'ISO Ready' :
    overallStatus === 'error' ? 'Failed' :
    overallStatus === 'active' ? 'Converting' :
    overallStatus === 'skipped' ? 'Skipped' :
    overallStatus === 'pending' ? 'Queued' :
    item.fileExists ? 'Downloaded' : 'Missing'

  const badge = trace
    ? <Badge variant={overallBadge}>{overallText}</Badge>
    : <Badge variant={item.fileExists ? 'queued' : 'error'}>{item.fileExists ? 'Downloaded' : 'Missing'}</Badge>

  function handleRowClick(e: React.MouseEvent) {
    // Don't toggle expand when clicking buttons
    if ((e.target as HTMLElement).closest('button')) return
    setExpanded(v => !v)
  }

  const traceRow = trace && trace.steps.length > 0 && (
    <div className="flex items-center gap-[7px] mt-0.5 flex-wrap">
      {trace.steps.map((step, i) => (
        <div key={step.name} className="flex items-center gap-[7px]">
          {i > 0 && <span className="text-text-disabled">&rarr;</span>}
          <StepIndicator step={step} />
        </div>
      ))}
    </div>
  )

  const isoLine = trace?.isoFilename && overallStatus === 'done' && (
    <div className="flex items-center gap-1.5 mt-1 text-[10px] text-success/85">
      <span>&#10003;</span>
      <span className="truncate font-mono">{trace.isoFilename}</span>
      {trace.isoSize != null && <span className="text-text-4">({fmtBytes(trace.isoSize)})</span>}
    </div>
  )

  const errorLine = overallStatus === 'error' && lastStep?.message && (
    <div className="text-[10px] text-error/90 mt-1 truncate">{lastStep.message}</div>
  )

  return (
    <div className="group border-b border-border-row hover:bg-accent/[0.06] transition-colors">
      {/* Desktop row */}
      <div className="hidden sm:flex items-center gap-[13px] px-[22px] py-3 cursor-pointer" onClick={handleRowClick}>
        <ConsoleTile platform={item.platform} size={34} />
        <div className="flex-1 min-w-0">
          <div className="text-[13px] text-text-body truncate">{title}</div>
          <div className="flex items-center gap-2 text-[10px] text-text-faint mt-px mb-1">
            <span className="truncate font-mono max-w-[280px]">{item.filename}</span>
            {relative && <span className="whitespace-nowrap">&middot; {relative}</span>}
          </div>
          {traceRow}
          {isoLine}
          {errorLine}
        </div>
        <span className="shrink-0 w-[66px] text-right font-mono text-[11px] text-text-2 tabular-nums">
          {item.fileSize ? fmtBytes(item.fileSize) : item.size || '--'}
        </span>
        {badge}
        <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
          <Actions trace={trace} item={item} convertMutation={convertMutation}
            actionMutation={actionMutation} onDelete={() => setConfirmDelete(v => !v)} />
        </div>
        <span className="shrink-0 text-[10px] text-text-faint">{expanded ? '▼' : '▶'}</span>
      </div>

      {/* Mobile stacked */}
      <div className="sm:hidden px-3 py-2 cursor-pointer space-y-1.5" onClick={handleRowClick}>
        <div className="flex items-start gap-2">
          <ConsoleTile platform={item.platform} size={30} />
          <div className="flex-1 min-w-0">
            <div className="text-[13px] text-text-body truncate">{title}</div>
            <div className="text-[10px] text-text-faint truncate font-mono">{item.filename}</div>
          </div>
          <span className="text-[10px] text-text-faint mt-1">{expanded ? '▼' : '▶'}</span>
        </div>
        {traceRow}
        {isoLine}
        {errorLine}
        <div className="flex items-center gap-2">
          {badge}
          {relative && <span className="text-[10px] text-text-4">{relative}</span>}
          <div className="flex items-center gap-1 ml-auto">
            <Actions trace={trace} item={item} convertMutation={convertMutation}
              actionMutation={actionMutation} onDelete={() => setConfirmDelete(v => !v)} />
          </div>
        </div>
      </div>

      {/* Expandable detail */}
      {expanded && (
        <div className="px-3 pb-4 pt-1.5 sm:pl-[69px] sm:pr-[22px] bg-surface-2/50 border-t border-border-row">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-0.5 max-w-[760px]">
            <MetaRow label="Platform" value={item.platform} />
            <MetaRow label="Format" value={item.format != null ? formatLabel(item.format) : null} />
            <MetaRow label="Archive" value={item.filename} />
            <MetaRow label="ISO" value={trace?.isoFilename} />
            <MetaRow label="Completed" value={item.completedAt} />
            <MetaRow label="Path" value={item.filepath} />
          </div>

          {showEventsLink && onViewEvents && (
            <button
              onClick={(e) => { e.stopPropagation(); onViewEvents(item.filename) }}
              className="mt-2.5 w-full sm:w-auto text-[10px] px-3 py-1.5 rounded-md bg-accent/10 text-accent/80
                border border-accent/20 hover:bg-accent/15 hover:text-accent transition-colors
                tracking-wide uppercase"
            >
              View Events
            </button>
          )}
        </div>
      )}

      {/* Delete confirmation */}
      {confirmDelete && (
        <div className="flex items-center gap-2 px-3 sm:px-[22px] py-2 bg-error/5 border-t border-error/10 flex-wrap">
          <span className="text-[10px] text-text-2">Remove?</span>
          <button
            onClick={() => { deleteMutation.mutate({ id: item.id }); setConfirmDelete(false) }}
            className="text-[10px] px-2 py-0.5 rounded bg-surface-3/50 text-text-2
              hover:bg-surface-3 hover:text-text transition-colors">
            Record only
          </button>
          {item.fileExists && (
            <button
              onClick={() => { deleteMutation.mutate({ id: item.id, deleteFiles: true }); setConfirmDelete(false) }}
              className="text-[10px] px-2 py-0.5 rounded bg-error/15 text-error
                hover:bg-error/25 transition-colors">
              Delete files too
            </button>
          )}
          <button
            onClick={() => setConfirmDelete(false)}
            className="text-[10px] text-text-4 hover:text-text-2 transition-colors ml-auto">
            Cancel
          </button>
        </div>
      )}
    </div>
  )
}

function Actions({ trace, item, convertMutation, actionMutation, onDelete }: {
  trace: HistoryItemType['trace']
  item: HistoryItemType
  convertMutation: ReturnType<typeof useConvertPs3>
  actionMutation: ReturnType<typeof usePs3Action>
  onDelete: () => void
}) {
  return (
    <>
      {trace?.actions.includes('convert') && (
        <button onClick={() => convertMutation.mutate(item.filename)}
          className="text-[10px] px-1.5 py-0.5 rounded text-info/60 hover:text-info
            hover:bg-info/10 transition-colors">Convert</button>
      )}
      {trace?.actions.includes('mark-done') && (
        <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'mark-done' })}
          className="text-[10px] px-1.5 py-0.5 rounded text-text-4 hover:text-text-2
            hover:bg-surface-3/40 transition-colors">Done</button>
      )}
      {trace?.actions.includes('retry') && (
        <button onClick={() => convertMutation.mutate(item.filename)}
          className="text-[10px] px-1.5 py-0.5 rounded text-warning/60 hover:text-warning
            hover:bg-warning/10 transition-colors">Retry</button>
      )}
      {trace?.actions.includes('abort') && (
        <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'abort' })}
          className="text-[10px] px-1.5 py-0.5 rounded text-error/60 hover:text-error
            hover:bg-error/10 transition-colors">Abort</button>
      )}
      <button onClick={onDelete}
        className="w-[25px] h-[25px] flex items-center justify-center rounded-[7px] text-xs
          border border-error/25 text-error/70 hover:text-error hover:bg-error/10"
        title="Remove">&times;</button>
    </>
  )
}
