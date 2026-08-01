import { useState, type ReactNode } from 'react'
import { getCoverGradient, getCoverInitials } from '../../lib/consoleColors'

// Cached catalog artwork with a graceful fallback (epic #122 / M3, restyled for #257). Points an
// <img> at the image endpoint and, when the game has no cached art (the endpoint 404s -> onError),
// falls back to a generated gradient (design handoff "Assets" -- issue #256) keyed by the game title,
// carrying either the derived initials or a caller-supplied placeholder. Backend-is-king: no logic
// here beyond the fallback. Sizing is caller-supplied (`className`), so one component serves the 34px
// list thumb, the 128x172 detail cover and the 16:9 title-screen box.
export function CatalogThumb({
  id, title, type = 'boxart', className = 'w-8 h-10 rounded-sm', initialsClassName = 'text-[9px]',
  fallback, fit = 'cover', maxHeightPx = 250,
}: {
  id: number
  title: string
  /** Which cached image to request: box art (default) or the title screen. */
  type?: 'boxart' | 'title'
  className?: string
  initialsClassName?: string
  /** Rendered over the gradient instead of the initials when there is no cached image. */
  fallback?: ReactNode
  /** 'cover' (default): fills `className`'s box, cropping to fill -- the list thumb and the 128x172
   *  detail cover want this. 'contain-auto': opt out of a forced box for the loaded image (issue
   *  #293) -- cropping destroys title-screen content (logos/menus sit at the edges), so once the
   *  image loads it renders un-cropped at its own natural aspect ratio, capped by the pane's width
   *  and `maxHeightPx`, and never upscaled past its natural size. Before load (and for the
   *  no-cached-art fallback, which has no intrinsic ratio) `className`'s box still applies as-is, so
   *  the pane doesn't jump around while the image streams in. */
  fit?: 'cover' | 'contain-auto'
  /** Height cap in px for the loaded image when `fit` is 'contain-auto' (default 250). Ignored otherwise. */
  maxHeightPx?: number
}) {
  // The 404 is remembered per image, not per component instance. This component is rendered in reused
  // positions — the Library detail pane keeps one instance while the selected game changes — where a
  // plain boolean would latch the first miss and stop every game selected afterwards from ever
  // requesting its own artwork. `loadedFor` mirrors that per-image tracking for the contain-auto
  // load transition below.
  const [failedFor, setFailedFor] = useState<string | null>(null)
  const [loadedFor, setLoadedFor] = useState<string | null>(null)
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

  if (fit === 'contain-auto') {
    // Until the image reports loaded, keep `className`'s forced box (un-cropped via object-contain,
    // matching the fallback's footprint) so the pane doesn't reflow mid-fetch (issue #293's
    // "reserving height until load" option). Once loaded, drop the forced box: natural size, capped
    // by the pane width and maxHeightPx, no upscaling.
    const isLoaded = loadedFor === imageKey
    return (
      <img src={`/api/catalog/games/${id}/image?type=${type}`} alt="" loading="lazy"
        onError={() => setFailedFor(imageKey)}
        onLoad={() => setLoadedFor(imageKey)}
        style={isLoaded ? { maxHeight: maxHeightPx } : undefined}
        className={isLoaded
          ? 'block max-w-full w-auto h-auto rounded-xl border border-white/[0.08]'
          : `${box} object-contain`} />
    )
  }

  return (
    <img src={`/api/catalog/games/${id}/image?type=${type}`} alt="" loading="lazy"
      onError={() => setFailedFor(imageKey)}
      className={`${box} object-cover`} />
  )
}
