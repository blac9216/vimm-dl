import { useState } from 'react'
import { getCoverGradient, getCoverInitials } from '../../lib/consoleColors'

// Cached catalog artwork with a graceful fallback (epic #122 / M3, restyled for #257). Points an
// <img> at the image endpoint and, when the game has no cached art (the endpoint 404s -> onError),
// falls back to a generated gradient + initials (design handoff "Assets" -- issue #256), keyed by the
// game title so the same game always gets the same placeholder. Backend-is-king: no logic here beyond
// the fallback. Sizing is caller-supplied (`className`) so the same component serves the 34px list
// thumb and the 128x172 detail cover.
export function CatalogThumb({ id, title, type = 'boxart', className = 'w-8 h-10 rounded-sm', initialsClassName = 'text-[9px]' }: {
  id: number
  title: string
  type?: 'boxart' | 'title'
  className?: string
  initialsClassName?: string
}) {
  const [failed, setFailed] = useState(false)
  const box = `shrink-0 overflow-hidden border border-white/[0.08] ${className}`
  if (failed) {
    return (
      <span className={`${box} flex items-center justify-center`} style={{ background: getCoverGradient(title) }}>
        <span className={`${initialsClassName} font-bold font-mono text-white/90 leading-none`}>{getCoverInitials(title)}</span>
      </span>
    )
  }
  return (
    <img src={`/api/catalog/games/${id}/image?type=${type}`} alt="" loading="lazy"
      onError={() => setFailed(true)}
      className={`${box} object-cover`} />
  )
}
