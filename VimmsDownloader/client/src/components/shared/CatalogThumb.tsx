import { useState } from 'react'
import { PlatformIcon } from './PlatformIcon'

// A small box-art thumbnail for a catalog row (epic #122 / M3). Points an <img> at the image endpoint
// and, when the game has no cached art (the endpoint 404s → onError), falls back to the platform glyph.
// Backend-is-king: no logic here beyond the graceful fallback.
export function CatalogThumb({ id, platform, className = '' }: { id: number; platform: string; className?: string }) {
  const [failed, setFailed] = useState(false)
  const box = `w-8 h-10 shrink-0 rounded-sm bg-surface-3/40 ${className}`
  if (failed) {
    return (
      <span className={`${box} flex items-center justify-center border border-border/20`}>
        <PlatformIcon platform={platform} />
      </span>
    )
  }
  return (
    <img src={`/api/catalog/games/${id}/image?type=boxart`} alt="" loading="lazy"
      onError={() => setFailed(true)}
      className={`${box} object-cover border border-border/20`} />
  )
}
