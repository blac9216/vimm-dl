import { useState, type ReactNode } from 'react'
import { getCoverGradient, getCoverInitials } from '../../lib/consoleColors'

// Cached catalog artwork with a graceful fallback (epic #122 / M3, restyled for #257). Points an
// <img> at the image endpoint and, when the game has no cached art (the endpoint 404s -> onError),
// falls back to a generated gradient (design handoff "Assets" -- issue #256) keyed by the game title,
// carrying either the derived initials or a caller-supplied placeholder. Backend-is-king: no logic
// here beyond the fallback. Sizing is caller-supplied (`className`), so one component serves the 34px
// list thumb, the 128x172 detail cover and the 16:9 title-screen box.
export function CatalogThumb({ id, title, type = 'boxart', className = 'w-8 h-10 rounded-sm', initialsClassName = 'text-[9px]', fallback }: {
  id: number
  title: string
  /** Which cached image to request: box art (default) or the title screen. */
  type?: 'boxart' | 'title'
  className?: string
  initialsClassName?: string
  /** Rendered over the gradient instead of the initials when there is no cached image. */
  fallback?: ReactNode
}) {
  // The 404 is remembered per image, not per component instance. This component is rendered in reused
  // positions — the Library detail pane keeps one instance while the selected game changes — where a
  // plain boolean would latch the first miss and stop every game selected afterwards from ever
  // requesting its own artwork.
  const [failedFor, setFailedFor] = useState<string | null>(null)
  const imageKey = `${id}:${type}`
  const box = `shrink-0 overflow-hidden border border-white/[0.08] ${className}`

  if (failedFor === imageKey) {
    return (
      <span className={`${box} relative flex items-center justify-center`} style={{ background: getCoverGradient(title) }}>
        {fallback ?? (
          <span className={`${initialsClassName} font-bold font-mono text-white/90 leading-none`}>
            {getCoverInitials(title)}
          </span>
        )}
      </span>
    )
  }
  return (
    <img src={`/api/catalog/games/${id}/image?type=${type}`} alt="" loading="lazy"
      onError={() => setFailedFor(imageKey)}
      className={`${box} object-cover`} />
  )
}
