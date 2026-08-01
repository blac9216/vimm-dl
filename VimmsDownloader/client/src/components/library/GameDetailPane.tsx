import { useGameDescription } from '../../api/queries'
import type { CatalogGame } from '../../types/api'
import { compatClass } from '../../lib/compat'
import { fmtBytes } from '../../lib/format'
import { getConsoleColor } from '../../lib/consoleColors'
import { CatalogThumb } from '../shared/CatalogThumb'
import { fmtLabel, originLabel, isVimmSourced, datOrigins } from './filters'

// Column C of the Library (issue #257, design handoff "1. Library (home)" -> Column C): the detail
// pane for the selected game. Supersedes the old inline GameDetail expansion — cover + identity +
// chips, the 16:9 title-screen box, the IGDB description, the game's download formats and the
// Download / ✓ Queued / owned-status action row. Art and description each degrade gracefully.

const CHIP = 'text-[10px] px-2 py-0.5 rounded-[7px] border font-semibold whitespace-nowrap'

/** The title-screen box: the cached screenshot renders at its own natural aspect ratio (un-cropped,
 *  capped at the pane width / ~250px tall, never upscaled -- `fit="contain-auto"`, issue #293), since
 *  libretro-thumbnails title screens come in the console's native ratio (4:3 for most retro consoles,
 *  10:9 for Game Boy, ~1.5:1 for Virtual Boy, ...) and cropping destroys the logos/menus at the
 *  edges. The no-cached-screenshot fallback has no intrinsic ratio, so it keeps the 16:9 gradient box
 *  with the design's scrim + "no screenshot cached" placeholder. */
function TitleScreen({ id, title }: { id: number; title: string }) {
  return (
    <div>
      <div className="text-[10px] uppercase tracking-[0.6px] text-text-4 mb-2">Title screen</div>
      <CatalogThumb id={id} title={title} type="title" fit="contain-auto"
        className="w-full aspect-video max-h-[250px] rounded-xl"
        fallback={
          <>
            <span className="absolute inset-0"
              style={{ background: 'radial-gradient(120% 110% at 50% 0%,rgba(0,0,0,.05),rgba(0,0,0,.6))' }} />
            <span className="relative text-[11px] tracking-[0.1em] font-mono text-text-heading/50">
              no screenshot cached
            </span>
          </>
        } />
    </div>
  )
}

export function GameDetailPane({ game, emuName, queued, queuePending, onQueue, onBack, consoleDisplayName }: {
  game: CatalogGame
  emuName: (id: string) => string
  queued: boolean
  queuePending: boolean
  onQueue: (game: CatalogGame) => void
  onBack: () => void
  // Friendly console name for the chip tooltip (from GET /api/catalog/consoles, issue #286). The game
  // payload only carries the slug, so the caller looks it up from the consoles list it already has;
  // falls back to the slug when the console isn't in that list (e.g. still loading).
  consoleDisplayName?: string
}) {
  const { data: desc, isLoading } = useGameDescription(game.id)
  const { code, color } = getConsoleColor(game.console)

  const metaLine = [game.region, game.languages].filter(Boolean).join(' · ')
  const ownedFormats = new Set(game.ownedFormats)
  const alts = [...new Set([...game.availableFormats, ...game.ownedFormats])].sort((a, b) => a - b)

  return (
    <div className="flex flex-col gap-[19px] px-5 sm:px-[26px] pt-4 sm:pt-[26px] pb-8">
      {/* Mobile drill-in: back to the list */}
      <button onClick={onBack}
        className="sm:hidden self-start text-[11px] text-link hover:text-link-3">‹ Back to list</button>

      {/* Header: cover + identity + chips */}
      <div className="flex flex-col sm:flex-row gap-5">
        <CatalogThumb id={game.id} title={game.name} initialsClassName="text-[32px]"
          className="w-32 h-[172px] rounded-xl shadow-[0_14px_40px_rgba(0,0,0,0.45)]" />
        <div className="flex-1 min-w-0 flex flex-col gap-[9px]">
          <div className="text-[19px] font-extrabold leading-[1.2] text-text break-words">{game.name}</div>
          <div className="flex items-center gap-[9px] flex-wrap">
            <span className="px-[9px] py-[3px] rounded-md text-[9px] font-bold font-mono text-bg"
              style={{ background: color }} title={consoleDisplayName ?? game.console}>{code}</span>
            {metaLine && <span className="text-[11px] font-mono text-text-3">{metaLine}</span>}
          </div>
          {game.serial && <span className="text-[11px] font-mono text-text-4">{game.serial}</span>}
          <div className="flex gap-1.5 flex-wrap mt-[3px]">
            {game.compat.map(c => (
              <span key={c.emulator} className={`${CHIP} ${compatClass(c.status)}`}
                title={`${emuName(c.emulator)}: ${c.status}`}>{emuName(c.emulator)}: {c.status}</span>
            ))}
            {game.vimmMatch && game.vimmMatch !== 'none' && (
              <span className={`${CHIP} bg-info/10 text-info border-info/25`}
                title={`Matched to a Vimm vault entry by ${game.vimmMatch.toUpperCase()}`}>Vimm</span>
            )}
            {game.vimmMatch === 'none' && (
              <span className={`${CHIP} bg-surface-3/40 text-text-4 border-border`}
                title="No Vimm match found — rectify manually">no Vimm</span>
            )}
            {game.rankScore != null && (
              <span className={`${CHIP} bg-warning/[0.13] text-warning border-warning/30 font-mono`}
                title={`IGDB rank score ${game.rankScore.toFixed(2)} (quality, vote-weighted)`}>
                ★ {Math.round(game.rankScore)}
              </span>
            )}
            {isVimmSourced(game.origins) && (
              <span className={`${CHIP} bg-warning/[0.13] text-warning border-warning/30`}
                title="Catalogued from Vimm, not from a No-Intro/Redump DAT — no public datfile exists for this console. The hashes are Redump's own, but nothing independent confirms this entry; it will be superseded if a DAT becomes available.">
                Vimm-sourced
              </span>
            )}
            {datOrigins(game.origins).length > 0 && (
              <span className={`${CHIP} bg-surface-3/30 text-text-4 border-border`}
                title={`Catalog DAT source: ${datOrigins(game.origins).map(originLabel).join(', ')}`}>
                {datOrigins(game.origins).map(originLabel).join('+')}
              </span>
            )}
          </div>
        </div>
      </div>

      <TitleScreen id={game.id} title={game.name} />

      {/* IGDB description */}
      <div className="text-[13px] leading-[1.7] text-text-2">
        {isLoading
          ? <span className="text-text-4">Loading description…</span>
          : desc
            ? <p className="whitespace-pre-line">{desc.description}</p>
            : <span className="text-text-4">No description — run an IGDB sync to fetch one.</span>}
      </div>

      {/* Download formats — owned ones green with a ✓ */}
      {alts.length > 0 && (
        <div>
          <div className="text-[10px] uppercase tracking-[0.6px] text-text-4 mb-2">Download format</div>
          <div className="flex gap-[7px] flex-wrap">
            {alts.map(alt => (
              <span key={alt}
                className={`text-xs px-[13px] py-1.5 rounded-lg border ${
                  ownedFormats.has(alt)
                    ? 'bg-success/[0.12] text-success border-success/[0.26]'
                    : 'bg-surface-3/[0.55] text-text-2 border-border-light'}`}
                title={ownedFormats.has(alt) ? `Owned: ${fmtLabel(alt)}` : `Available: ${fmtLabel(alt)}`}>
                {ownedFormats.has(alt) ? '✓ ' : ''}{fmtLabel(alt)}
              </span>
            ))}
          </div>
        </div>
      )}

      {/* Action row */}
      <div className="flex items-center gap-[13px] flex-wrap mt-[5px]">
        {!game.owned && (queued
          ? (
            <span className="px-[22px] py-3 text-[13px] font-bold rounded-[10px] border border-border-light
              bg-surface/60 text-link-3">✓ Queued</span>
          ) : (
            <button onClick={() => onQueue(game)} disabled={queuePending}
              className="px-6 py-3 text-[13px] font-bold rounded-[10px] text-white
                disabled:opacity-50 shadow-[0_6px_22px_rgba(111,141,255,0.4)]"
              style={{ background: 'linear-gradient(135deg,#6f8dff,#9d6dff)' }}>
              ⤓ Download
            </button>
          ))}
        {game.owned && (
          game.verified === true ? (
            <span className={`${CHIP} bg-success/15 text-success border-success/25`}
              title="File CRC32 matches the catalog">✓ Verified</span>
          ) : game.verified === false ? (
            <span className={`${CHIP} bg-error/10 text-error border-error/20`}
              title="File CRC32 does not match any catalog hash">✗ Mismatch</span>
          ) : (
            <span className={`${CHIP} bg-success/15 text-success border-success/25`}
              title="Owned — run Verify to check the file hash">Owned</span>
          )
        )}
        {game.size > 0 && (
          <span className="text-[11px] font-mono text-text-4 tabular-nums">{fmtBytes(game.size)}</span>
        )}
        {game.ownedSources.length > 0 && (
          <span className="text-[11px] font-mono text-text-4"
            title="Where the on-disk copies came from">{game.ownedSources.join(', ')}</span>
        )}
      </div>
    </div>
  )
}
