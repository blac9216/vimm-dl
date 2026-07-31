type BadgeVariant = 'downloading' | 'paused' | 'queued' | 'starting' |
  'extracting' | 'converting' | 'done' | 'error' | 'skipped' | 'waiting'

interface BadgeProps {
  variant: BadgeVariant
  children: React.ReactNode
}

// Status colors per the design handoff's status triplets (issue #256) — retires the old
// PS3-button (ps-cross/ps-circle/ps-triangle/ps-square) palette. success = green, info = indigo,
// paused = violet, warning = amber, error = red, neutral = gray.
const variantStyles: Record<BadgeVariant, string> = {
  downloading: 'bg-success-bg text-success border-success-border shadow-[0_0_8px_rgba(62,224,160,0.12)]',
  paused: 'bg-paused-bg text-paused border-paused-border',
  queued: 'bg-neutral-bg text-text-4 border-neutral-border',
  starting: 'bg-info-bg text-info border-info-border',
  extracting: 'bg-warning-bg text-warning border-warning-border',
  converting: 'bg-info-bg text-info border-info-border',
  done: 'bg-success-bg text-success border-success-border',
  error: 'bg-error-bg text-error border-error-border',
  skipped: 'bg-neutral-bg/60 text-text-4 border-neutral-border/60',
  waiting: 'bg-neutral-bg/80 text-text-3 border-neutral-border',
}

export function Badge({ variant, children }: BadgeProps) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 text-[10px] font-medium
      rounded border tracking-wide ${variantStyles[variant]}`}>
      {children}
    </span>
  )
}
