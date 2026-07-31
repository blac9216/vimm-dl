import { getPlatformColor } from '../../lib/platformConsole'

interface ConsoleTileProps {
  /** Platform display name as it comes from the source ("PlayStation 3"). */
  platform: string | null
  /** Tile edge length in px — 32 for queue rows, 34 for completed rows (design handoff §2). */
  size?: number
  className?: string
}

/**
 * The console colour tile used by the Downloads rows (design handoff §2): a rounded square filled
 * with the console's palette colour, carrying its short mono code in the window background colour.
 */
export function ConsoleTile({ platform, size = 32, className = '' }: ConsoleTileProps) {
  const { code, color } = getPlatformColor(platform)

  return (
    <span
      title={platform ?? undefined}
      className={`shrink-0 inline-flex items-center justify-center rounded-lg font-mono font-bold
        text-[9px] leading-none text-bg ${className}`}
      style={{ width: size, height: size, background: color }}
    >
      {code}
    </span>
  )
}
