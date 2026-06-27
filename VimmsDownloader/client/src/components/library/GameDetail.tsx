import { useState } from 'react'
import { useGameDescription } from '../../api/queries'
import type { CatalogGame } from '../../types/api'
import { compatClass } from '../../lib/compat'
import { fmtBytes } from '../../lib/format'

// The click-to-expand detail for a Library row (epic #122 / M3): box art + title screen, per-emulator
// compat badges, owned/Vimm badges, and the IGDB description. Art and description each degrade
// gracefully — missing art shows a placeholder, a missing description shows a hint.
export function GameDetail({ game, emuName }: { game: CatalogGame; emuName: (id: string) => string }) {
  const { data: desc, isLoading } = useGameDescription(game.id)
  const [boxFailed, setBoxFailed] = useState(false)
  const [titleFailed, setTitleFailed] = useState(false)

  return (
    <div className="flex flex-col sm:flex-row gap-4 px-3 sm:px-6 py-3 bg-surface/20 border-b border-border/10">
      {/* Artwork */}
      <div className="flex gap-3 shrink-0">
        {!boxFailed && (
          <img src={`/api/catalog/games/${game.id}/image?type=boxart`} alt="Box art" loading="lazy"
            onError={() => setBoxFailed(true)}
            className="max-h-44 rounded border border-border/30 bg-surface-3/30 object-contain" />
        )}
        {!titleFailed && (
          <img src={`/api/catalog/games/${game.id}/image?type=title`} alt="Title screen" loading="lazy"
            onError={() => setTitleFailed(true)}
            className="max-h-44 rounded border border-border/30 bg-surface-3/30 object-contain" />
        )}
        {boxFailed && titleFailed && (
          <div className="w-32 h-44 rounded border border-border/20 bg-surface-3/20 flex items-center
            justify-center text-[10px] text-text-4 text-center px-2">
            No artwork
          </div>
        )}
      </div>

      {/* Info */}
      <div className="flex-1 min-w-0 flex flex-col gap-2">
        <div className="text-sm text-text-2 font-medium break-words">{game.name}</div>

        <div className="text-[11px] text-text-4 flex flex-wrap gap-x-3 gap-y-0.5">
          <span className="uppercase">{game.console}</span>
          {game.region && <span>{game.region}</span>}
          {game.languages && <span>{game.languages}</span>}
          {game.serial && <span className="font-mono">{game.serial}</span>}
          {game.size > 0 && <span className="font-mono tabular-nums">{fmtBytes(game.size)}</span>}
        </div>

        <div className="flex flex-wrap gap-1">
          {game.compat.map(c => (
            <span key={c.emulator} className={`text-[10px] px-1.5 py-0.5 rounded border ${compatClass(c.status)}`}>
              {emuName(c.emulator)}: {c.status}
            </span>
          ))}
          {game.owned && (
            <span className="text-[10px] px-1.5 py-0.5 rounded border bg-ps-triangle/15 text-ps-triangle border-ps-triangle/25">
              {game.verified === true ? '✓ Verified' : game.verified === false ? '✗ Mismatch' : 'Owned'}
            </span>
          )}
          {game.vimmMatch && game.vimmMatch !== 'none' && (
            <span className="text-[10px] px-1.5 py-0.5 rounded border bg-ps-cross/10 text-[#7eb3e0] border-ps-cross/25"
              title={`Matched to a Vimm vault entry by ${game.vimmMatch.toUpperCase()}`}>Vimm</span>
          )}
        </div>

        <div className="text-xs text-text-3 leading-relaxed">
          {isLoading
            ? <span className="text-text-4">Loading description…</span>
            : desc
              ? <p className="whitespace-pre-line">{desc.description}</p>
              : <span className="text-text-4">No description — run an IGDB sync to fetch one.</span>}
        </div>
      </div>
    </div>
  )
}
