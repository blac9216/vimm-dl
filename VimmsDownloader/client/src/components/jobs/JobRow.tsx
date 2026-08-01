import { Badge } from '../shared/Badge'
import { fmtDuration } from '../../lib/format'

// The Jobs tab row (design handoff "3. Jobs (new)" → Job row): kind-colored left border, fixed
// 104px kind badge, name over message, 140px progress bar, status badge, elapsed, stop button.
// One component for both feeds (live conversions + GET /api/jobs), so they stay visually identical.

type BadgeVariant = 'converting' | 'extracting' | 'waiting' | 'done' | 'error' | 'skipped'

interface JobRowProps {
  /** Kind badge label, e.g. "PS3 Convert" (rendered uppercase). */
  kind: string
  /** Kind color — drives the left border, the badge and the progress fill. */
  color: string
  name: string
  message?: string | null
  /** 0-100, or null when the job reports no total (indeterminate). */
  percent: number | null
  /** Working right now: full-strength accents + an animated bar. Queued/finished rows dim down. */
  active: boolean
  status: { variant: BadgeVariant; text: string }
  /**
   * Milliseconds. While the job runs, pass the live elapsed time; for a finished run (#292) pass the
   * backend's frozen `lastDurationMs` instead — never the still-growing `elapsedMs` from GET /api/jobs.
   */
  elapsedMs?: number | null
  onStop?: () => void
  stopTitle?: string
  stopDisabled?: boolean
  /** Rendered inside a per-game group: the group supplies the left accent. */
  grouped?: boolean
  /**
   * Small text shown in the progress-bar slot instead of a bar (#292) — e.g. "Finished 3m ago" on a
   * completed background-job row. Mutually exclusive with an active/percent progress bar.
   */
  meta?: string | null
}

export function JobRow({
  kind, color, name, message, percent, active, status, elapsedMs,
  onStop, stopTitle = 'Stop', stopDisabled, grouped, meta,
}: JobRowProps) {
  const pct = percent == null ? null : Math.max(0, Math.min(100, Math.round(percent)))

  return (
    <div
      className={`flex items-center gap-2 sm:gap-3.5 px-3 sm:px-[22px] py-2.5 sm:py-3 border-b border-border-row
        ${grouped ? 'bg-surface-2/20' : 'border-l-[2.5px]'}`}
      // ~60% alpha of the kind color while working; queued/finished rows fall back to the card border.
      style={grouped ? undefined : { borderLeftColor: active ? `${color}99` : '#211f3a' }}
    >
      <span
        className="hidden sm:block shrink-0 w-[104px] text-center font-mono text-[9px] font-bold uppercase
          tracking-[0.06em] rounded-[7px] border px-[9px] py-[5px] truncate"
        style={{ color, backgroundColor: `${color}1f`, borderColor: `${color}4d` }}
      >
        {kind}
      </span>

      <div className="flex-1 min-w-0">
        <div className="text-[13px] text-text-body truncate">{name}</div>
        {message && <div className="text-[10px] text-text-faint truncate">{message}</div>}
      </div>

      <div className="hidden sm:block shrink-0 w-[140px]">
        {meta ? (
          <div className="text-[10px] text-text-4 truncate">{meta}</div>
        ) : (
          <div className="h-[5px] rounded-[3px] bg-surface-3/70 overflow-hidden">
            {pct != null ? (
              <div className="h-full rounded-[3px] transition-all duration-500"
                style={{ width: `${pct}%`, backgroundColor: active ? color : 'rgba(36,34,64,.9)' }} />
            ) : active ? (
              // No total reported — a sliding sliver stands in for a percentage.
              <div className="job-indeterminate h-full rounded-[3px]" style={{ backgroundColor: color }} />
            ) : null}
          </div>
        )}
      </div>

      <Badge variant={status.variant}>{status.text}</Badge>

      {elapsedMs != null && elapsedMs > 1000 ? (
        <span className="hidden sm:inline shrink-0 w-[54px] text-right font-mono text-[10px] text-text-4 tabular-nums">
          {fmtDuration(elapsedMs)}
        </span>
      ) : (
        <span className="hidden sm:inline shrink-0 w-[54px]" />
      )}

      {onStop ? (
        <button
          onClick={onStop}
          disabled={stopDisabled}
          title={stopTitle}
          aria-label={stopTitle}
          className="shrink-0 w-[25px] h-[25px] flex items-center justify-center rounded-[7px] text-[10px]
            border border-error/25 text-error hover:bg-error/10 disabled:opacity-30 disabled:hover:bg-transparent"
        >&#9632;</button>
      ) : (
        <span className="shrink-0 w-[25px]" />
      )}
    </div>
  )
}
