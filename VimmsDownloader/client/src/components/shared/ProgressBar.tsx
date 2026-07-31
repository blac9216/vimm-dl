type ProgressVariant = 'download' | 'paused' | 'extract' | 'convert' | 'sync' | 'partial'

interface ProgressBarProps {
  width: string
  variant?: ProgressVariant
}

// Fills per the design handoff's progress-bar recipe (issue #256): the primary indigo->violet
// gradient for active downloads, solid status colors elsewhere (paused = violet, extract = amber,
// convert = indigo/info, partial = dim amber). Retires the old PS3-button palette.
const gradients: Record<ProgressVariant, string> = {
  download: 'bg-gradient-to-r from-accent to-accent-2',
  paused: 'bg-paused/60',
  extract: 'bg-warning/60',
  convert: 'bg-info/60',
  sync: 'bg-accent/60',
  partial: 'bg-warning/40',
}

const glows: Record<ProgressVariant, string> = {
  download: 'shadow-[0_0_8px_rgba(111,141,255,0.4)]',
  paused: '',
  extract: 'shadow-[0_0_6px_rgba(244,180,78,0.2)]',
  convert: 'shadow-[0_0_6px_rgba(111,141,255,0.2)]',
  sync: 'shadow-[0_0_6px_rgba(111,141,255,0.2)]',
  partial: '',
}

export function ProgressBar({ width, variant = 'download' }: ProgressBarProps) {
  return (
    <div className="w-full h-[5px] bg-surface-3/70 rounded-full overflow-hidden">
      <div
        className={`h-full rounded-full transition-all duration-500 ${gradients[variant]} ${glows[variant]}`}
        style={{ width }}
      />
    </div>
  )
}
