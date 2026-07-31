import { useState } from 'react'
import { getCoverGradient, getCoverInitials } from '../../lib/consoleColors'

// A small box-art thumbnail for a catalog row (epic #122 / M3). Points an <img> at the image endpoint
// and, when the game has no cached art (the endpoint 404s -> onError), falls back to a generated
// gradient + initials (design handoff "Assets" -- issue #256), keyed by the game title so the same
// game always gets the same placeholder. Backend-is-king: no logic here beyond the graceful fallback.
export function CatalogThumb({ id, title, className = '' }: { id: number; title: string; className?: string }) {
  const [failed, setFailed] = useState(false)
  const box = `w-8 h-10 shrink-0 rounded-sm overflow-hidden border border-white/[0.08] ${className}`
  if (failed) {
    return (
      <span className={`${box} flex items-center justify-center`} style={{ background: getCoverGradient(title) }}>
        <span className="text-[9px] font-bold font-mono text-white/90 leading-none">{getCoverInitials(title)}</span>
      </span>
    )
  }
  return (
    <img src={`/api/catalog/games/${id}/image?type=boxart`} alt="" loading="lazy"
      onError={() => setFailed(true)}
      className={`${box} object-cover`} />
  )
}
