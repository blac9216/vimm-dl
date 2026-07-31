import { useEffect, useRef, useState } from 'react'
import type { CatalogGame, Emulator } from '../../types/api'
import { CatalogThumb } from '../shared/CatalogThumb'
import { getConsoleColor } from '../../lib/consoleColors'
import { fmtBytes } from '../../lib/format'
import { COMPAT_STATUSES } from '../../lib/compat'
import { hasAdvancedFilters, type LibraryFilters } from './filters'

// Column B of the Library (issue #257, design handoff "1. Library (home)" -> Column B): the game
// list. Search field, availability + sort segments and the result count on top; the design's compact
// rows (34px art thumb, title, "CODE · SIZE" meta, rank star, owned dot, selected-row indigo overlay)
// in the middle; paging as the footer.
//
// Everything the old flat toolbar carried is preserved here rather than dropped (epic #254 decision):
// the less-used filters live in a "Filters" popover and the batch-select + curation controls on a
// sub-bar. The catalog job triggers that used to sit in a "⋯" overflow menu moved to the Jobs tab
// (#259); only the sources dialog stayed behind.

/** Close a popover on outside click or Escape. */
function useDismiss(open: boolean, close: () => void) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    function onDown(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) close()
    }
    function onKey(e: KeyboardEvent) { if (e.key === 'Escape') close() }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open, close])
  return ref
}

const SEG_ACTIVE = 'border-accent/45 bg-accent/[0.18] text-link-3'
const SEG_IDLE = 'border-border bg-surface/50 text-text-3 hover:text-text-body-2'

function Seg({ active, onClick, title, children }: {
  active: boolean; onClick: () => void; title?: string; children: React.ReactNode
}) {
  return (
    <button onClick={onClick} title={title}
      className={`px-3 py-[5px] text-[11px] font-semibold rounded-lg border shrink-0 ${active ? SEG_ACTIVE : SEG_IDLE}`}>
      {children}
    </button>
  )
}

const SELECT_CLASS = 'bg-input border border-border-light rounded-lg px-2 py-1 text-[11px] text-text-2 focus:outline-none focus:border-accent/40'

export function GameList({
  filters, patch, games, total, pageSize, isFetching, emulators, detailOpen,
  selectedIds, onToggleSelect, onToggleSelectVisible, onQueueSelected, onClearSelection, batchPending,
  onPickBest, curating, onManageSources, catalogBusy,
}: {
  filters: LibraryFilters
  patch: (p: Partial<LibraryFilters>, resetView?: boolean) => void
  games: CatalogGame[]
  total: number
  pageSize: number
  isFetching: boolean
  emulators: Emulator[] | undefined
  /** Sub-`sm` the detail pane is a drill-in: the list hides while a game is open. */
  detailOpen: boolean
  selectedIds: Set<number>
  onToggleSelect: (id: number) => void
  onToggleSelectVisible: () => void
  onQueueSelected: () => void
  onClearSelection: () => void
  batchPending: boolean
  onPickBest: (budgetGb: string, maxCount: string) => void
  curating: boolean
  onManageSources: () => void
  /** A catalog job is running (Jobs tab) — curation would race it, so the button waits. */
  catalogBusy: boolean
}) {
  const [showFilters, setShowFilters] = useState(false)
  const [budgetGb, setBudgetGb] = useState('')
  const [maxCount, setMaxCount] = useState('')
  const filtersRef = useDismiss(showFilters, () => setShowFilters(false))

  const page = filters.page
  const pageCount = Math.ceil(total / pageSize)
  const start = total === 0 ? 0 : page * pageSize + 1
  const end = Math.min((page + 1) * pageSize, total)

  // Only non-owned rows are queueable; "select visible" toggles the current page.
  const queueableIds = games.filter(g => !g.owned).map(g => g.id)
  const allVisibleSelected = queueableIds.length > 0 && queueableIds.every(id => selectedIds.has(id))

  const searchPlaceholder = filters.searchMode === 'glob' ? 'Glob: Mario*, Final ?antasy…'
    : filters.searchMode === 'regex' ? 'Regex: ^Final Fantasy (VII|IX)…'
    : 'Search the catalog…'

  return (
    <div className={`flex-col flex-1 sm:flex-none w-full sm:w-[430px] min-h-0 border-r border-border-subtle
      ${detailOpen ? 'hidden sm:flex' : 'flex'}`}>
      {/* Search */}
      <div className="px-[18px] pt-4 pb-[11px] border-b border-border-section shrink-0">
        <div className="flex items-center gap-[9px] bg-input border border-border-input rounded-[10px] px-[13px] py-[9px]">
          <span className="text-link text-sm font-bold leading-none">⌕</span>
          <input value={filters.search} onChange={e => patch({ search: e.target.value })}
            placeholder={searchPlaceholder}
            className="flex-1 min-w-0 bg-transparent border-0 outline-none text-[13px] font-mono
              text-text-heading placeholder:text-text-4" />
          {filters.search && (
            <button onClick={() => patch({ search: '' })} title="Clear search"
              className="text-text-4 hover:text-text shrink-0 leading-none">×</button>
          )}
        </div>
      </div>

      {/* Availability + sort segments, advanced filters, job menu, count */}
      <div className="flex items-center gap-2.5 px-[18px] py-2.5 border-b border-border-subtle
        bg-surface-2/40 shrink-0 flex-wrap">
        <div className="flex gap-[5px]">
          <Seg active={filters.local === 'all'} onClick={() => patch({ local: 'all' })}>All</Seg>
          <Seg active={filters.local === 'owned'} onClick={() => patch({ local: 'owned' })}>Owned</Seg>
          <Seg active={filters.local === 'remote'} onClick={() => patch({ local: 'remote' })}>Missing</Seg>
        </div>
        <div className="flex gap-[5px]">
          <Seg active={filters.sort === 'name'} onClick={() => patch({ sort: 'name' })} title="Sort alphabetically">Name</Seg>
          <Seg active={filters.sort === 'rank'} onClick={() => patch({ sort: 'rank' })}
            title="Sort by IGDB &quot;best games&quot; rank (run Rank ★ first)">Rank ★</Seg>
        </div>

        {/* Advanced filters popover — 1G1R / English / demos / search mode / emulator + status */}
        <div className="relative" ref={filtersRef}>
          <Seg active={showFilters || hasAdvancedFilters(filters)} onClick={() => setShowFilters(v => !v)}
            title="More filters: 1G1R, English only, hide demos, search mode, emulator compatibility">
            Filters{hasAdvancedFilters(filters) ? ' •' : ''}
          </Seg>
          {showFilters && (
            <div className="absolute z-30 mt-1.5 left-0 w-64 max-w-[80vw] p-3 flex flex-col gap-2
              bg-card border border-border rounded-xl shadow-xl">
              <label className="flex items-center gap-2 text-[11px] text-text-2 cursor-pointer"
                title="One game per title (1G1R) — hide regional/revision duplicates">
                <input type="checkbox" checked={filters.dedupe} onChange={e => patch({ dedupe: e.target.checked })}
                  className="accent-accent w-3.5 h-3.5" />
                1G1R — one game per title
              </label>
              <label className="flex items-center gap-2 text-[11px] text-text-2 cursor-pointer"
                title="English-only — hide titles with no English/Western release">
                <input type="checkbox" checked={filters.english} onChange={e => patch({ english: e.target.checked })}
                  className="accent-accent w-3.5 h-3.5" />
                English releases only
              </label>
              <label className="flex items-center gap-2 text-[11px] text-text-2 cursor-pointer"
                title="Hide demos, betas, prototypes, kiosk &amp; sample builds">
                <input type="checkbox" checked={filters.excludeCategories}
                  onChange={e => patch({ excludeCategories: e.target.checked })}
                  className="accent-accent w-3.5 h-3.5" />
                Hide demos &amp; prototypes
              </label>
              <div className="border-t border-border-inner pt-2 flex flex-col gap-1.5">
                <span className="text-[10px] uppercase tracking-[0.12em] text-text-4">Search mode</span>
                <select value={filters.searchMode} onChange={e => patch({ searchMode: e.target.value })}
                  title="Search mode: substring, glob (*,?) or regex" className={SELECT_CLASS}>
                  <option value="substring">Name (substring)</option>
                  <option value="glob">Glob (*, ?)</option>
                  <option value="regex">Regex</option>
                </select>
              </div>
              {emulators && emulators.length > 0 && (
                <div className="border-t border-border-inner pt-2 flex flex-col gap-1.5">
                  <span className="text-[10px] uppercase tracking-[0.12em] text-text-4">Emulator compatibility</span>
                  <select value={filters.emulator} title="Filter by emulator compatibility" className={SELECT_CLASS}
                    onChange={e => patch({ emulator: e.target.value, compatStatus: e.target.value ? filters.compatStatus : '' })}>
                    <option value="">Any emulator</option>
                    {emulators.map(e => <option key={e.id} value={e.id}>{e.name}</option>)}
                  </select>
                  {filters.emulator && (
                    <select value={filters.compatStatus} onChange={e => patch({ compatStatus: e.target.value })}
                      title="Filter by playability status" className={SELECT_CLASS}>
                      <option value="">Any status</option>
                      {COMPAT_STATUSES.map(s => <option key={s} value={s}>{s}</option>)}
                    </select>
                  )}
                </div>
              )}
            </div>
          )}
        </div>

        {/* Archive.org download sets. The catalog job triggers that shared this slot are on the Jobs tab (#259). */}
        <Seg active={false} onClick={onManageSources} title="Manage archive.org download sets">
          Sources
        </Seg>

        <div className="flex-1" />
        <span className="text-[10px] font-mono text-text-4 tabular-nums shrink-0">
          {total.toLocaleString()} GAMES
        </span>
      </div>

      {/* Batch selection + curation sub-bar */}
      <div className="flex items-center gap-2 flex-wrap px-[18px] py-1.5 border-b border-border-subtle
        text-[10px] text-text-4 shrink-0">
        <input type="checkbox" checked={allVisibleSelected} onChange={onToggleSelectVisible}
          disabled={queueableIds.length === 0} title="Select all queueable games on this page"
          className="accent-accent w-3 h-3 shrink-0 cursor-pointer disabled:opacity-30" />
        {selectedIds.size > 0 ? (
          <>
            <span className="text-accent">{selectedIds.size.toLocaleString()} selected</span>
            <button onClick={onQueueSelected} disabled={batchPending}
              className="px-2 py-0.5 text-[10px] font-medium rounded bg-info/20 text-info
                border border-info/30 hover:bg-info/30 disabled:opacity-40">
              {batchPending ? 'Queuing…' : 'Queue selected'}
            </button>
            <button onClick={onClearSelection}
              className="px-2 py-0.5 text-[10px] rounded bg-surface-3/40 text-text-3 border border-border hover:text-text">
              Clear
            </button>
          </>
        ) : (
          <span className="tabular-nums">
            {total > 0 ? `${start.toLocaleString()}–${end.toLocaleString()} of ${total.toLocaleString()}` : 'No matches'}
          </span>
        )}
        {/* Curation (R3): best missing games by rank within a GB budget, under the current filters. */}
        <span className="flex items-center gap-1 shrink-0 ml-auto">
          <span className="text-warning" title="Best N up to X GB — picks the top-ranked missing games that fit the budget">★</span>
          <input type="number" min="0" step="1" inputMode="decimal" value={budgetGb} onChange={e => setBudgetGb(e.target.value)}
            placeholder="GB" title="Size budget in GB — picks the best-ranked missing games that fit"
            className="w-11 bg-input border border-border-light rounded px-1 py-0.5 text-[10px] text-text
              focus:outline-none focus:border-accent/40" />
          <input type="number" min="0" step="1" inputMode="numeric" value={maxCount} onChange={e => setMaxCount(e.target.value)}
            placeholder="max#" title="Optional cap on the number of games"
            className="w-11 bg-input border border-border-light rounded px-1 py-0.5 text-[10px] text-text
              focus:outline-none focus:border-accent/40" />
          <button onClick={() => onPickBest(budgetGb, maxCount)} disabled={curating || catalogBusy}
            title="Select the best-ranked missing games that fit the GB budget (then Queue selected)"
            className="px-2 py-0.5 text-[10px] font-medium rounded bg-warning/15 text-warning
              border border-warning/30 hover:bg-warning/25 disabled:opacity-40">
            {curating ? 'Picking…' : 'Pick best'}
          </button>
        </span>
      </div>

      {/* Rows */}
      <div className="flex-1 min-h-0 overflow-y-auto">
        {games.map(g => {
          const active = filters.selectedId === g.id
          const { code } = getConsoleColor(g.console)
          const checked = selectedIds.has(g.id)
          return (
            <div key={g.id} className="relative border-b border-border-row">
              {active && (
                <span className="absolute inset-0 pointer-events-none border-l-[2.5px] border-link"
                  style={{ background: 'linear-gradient(90deg,rgba(111,141,255,.2),rgba(157,109,255,.05) 90%)' }} />
              )}
              <div className={`relative z-[1] flex items-center gap-[11px] px-[18px] py-2.5
                border-l-[2.5px] border-transparent ${active ? '' : 'hover:bg-accent/[0.06]'}`}>
                {g.owned
                  ? <span className="w-3 shrink-0" />
                  : <input type="checkbox" checked={checked} onChange={() => onToggleSelect(g.id)}
                      title="Select for batch queue"
                      className={`accent-accent w-3 h-3 shrink-0 cursor-pointer ${checked ? '' : 'opacity-40 hover:opacity-100'}`} />}
                <button type="button" onClick={() => patch({ selectedId: g.id }, false)}
                  title={g.name} aria-current={active}
                  className="flex items-center gap-[11px] flex-1 min-w-0 text-left">
                  <CatalogThumb id={g.id} title={g.name} className="w-[34px] h-[34px] rounded-lg" initialsClassName="text-[10px]" />
                  <span className="flex-1 min-w-0 block">
                    <span className={`block text-[13px] truncate ${active ? 'text-text' : 'text-text-body'}`}>{g.name}</span>
                    <span className="block text-[9.5px] font-mono text-text-faint truncate">
                      {code}{g.size > 0 ? ` · ${fmtBytes(g.size)}` : ''}
                    </span>
                  </span>
                  {g.rankScore != null && (
                    <span className="shrink-0 text-[10px] font-bold font-mono text-warning tabular-nums"
                      title={`IGDB rank score ${g.rankScore.toFixed(2)} (quality, vote-weighted)`}>
                      ★{Math.round(g.rankScore)}
                    </span>
                  )}
                  {g.owned && (
                    <span className="w-[7px] h-[7px] rounded-full bg-success shrink-0"
                      title={g.verified === true ? 'Owned — verified' : g.verified === false ? 'Owned — hash mismatch' : 'Owned'}
                      style={{ boxShadow: '0 0 6px rgba(62,224,160,.5)' }} />
                  )}
                </button>
              </div>
            </div>
          )
        })}
        {isFetching && games.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-[13px]">Loading…</div>
        )}
        {!isFetching && games.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-[13px]">No games match your filter</div>
        )}
      </div>

      {/* Paging footer */}
      {pageCount > 1 && (
        <div className="flex items-center justify-center gap-3 px-[18px] py-2 border-t border-border-section
          text-[11px] shrink-0">
          <button onClick={() => patch({ page: Math.max(0, page - 1) }, false)} disabled={page === 0}
            className="px-2.5 py-1 rounded bg-surface-3/40 text-text-3 border border-border hover:text-text disabled:opacity-30">
            ‹ Prev
          </button>
          <span className="text-text-4 tabular-nums font-mono">Page {page + 1} / {pageCount.toLocaleString()}</span>
          <button onClick={() => patch({ page: end < total ? page + 1 : page }, false)} disabled={end >= total}
            className="px-2.5 py-1 rounded bg-surface-3/40 text-text-3 border border-border hover:text-text disabled:opacity-30">
            Next ›
          </button>
        </div>
      )}
    </div>
  )
}
