import { useState, useEffect, useRef, useCallback } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useCatalogConsoles, useCatalogGames, useCatalogStatus, useEmulators, useSyncCatalog, useQueueCatalogGame, useQueueCatalogGamesBatch, fetchGameVimm, fetchCurate } from '../../api/queries'
import type { CatalogGame, CatalogVimmFormat } from '../../types/api'
import { ConsoleRail } from './ConsoleRail'
import { GameList } from './GameList'
import { GameDetailPane } from './GameDetailPane'
import { SetsDialog } from './SetsDialog'
import { FormatPickerDialog } from './FormatPickerDialog'
import { fmtBytes } from '../../lib/format'
import { loadFilters, saveFilters, type LibraryFilters } from './filters'

// The Library (issue #257, design handoff "1. Library (home)"): a three-column master/detail browse
// over the canonical No-Intro/Redump catalog — console rail (A) / game list (B) / detail pane (C).
// This file owns the state + data wiring only; each column lives in its own component.
// Backend-is-king: every filter/sort/page is a query parameter of GET /api/catalog/games, and the
// detail pane renders exactly what the catalog + IGDB endpoints return.

const PAGE_SIZE = 100

// Debounce the search box so we don't query the catalog on every keystroke.
function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return debounced
}

export function LibraryPanel() {
  const [filters, setFilters] = useState<LibraryFilters>(loadFilters) // read saved filters once on mount
  const query = useDebounced(filters.search, 350)

  const [showSets, setShowSets] = useState(false)
  const [queuedIds, setQueuedIds] = useState<Set<number>>(new Set())
  const [queueError, setQueueError] = useState<string | null>(null)
  const [batchMsg, setBatchMsg] = useState<string | null>(null)
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set()) // bulk-select for batch queue
  const [picker, setPicker] = useState<{ id: number; name: string; formats: CatalogVimmFormat[] } | null>(null)
  const [curating, setCurating] = useState(false)

  const qc = useQueryClient()
  const { data: consoles } = useCatalogConsoles()
  const { data: emulators } = useEmulators()
  const { data: status } = useCatalogStatus()
  // The only catalog job still triggered from the Library: the empty-catalog "Sync catalog" CTA
  // below. Every other trigger moved to the Jobs tab (#259).
  const syncMutation = useSyncCatalog()
  const queueGame = useQueueCatalogGame()
  const batchQueue = useQueueCatalogGamesBatch()
  const { data: gamesResp, isFetching } = useCatalogGames(
    filters.console || null, query, filters.local, filters.dedupe, filters.english, filters.excludeCategories,
    filters.searchMode, filters.page, PAGE_SIZE, filters.emulator, filters.compatStatus, filters.sort)

  // Map an emulator id to its display name (e.g. 'rpcs3' → 'RPCS3'); falls back to the id.
  const emuName = useCallback(
    (id: string) => emulators?.find(e => e.id === id)?.name ?? id.toUpperCase(), [emulators])

  const syncing = status?.syncing ?? false
  // Any catalog job running (started from the Jobs tab, or by another browser): the Library refreshes
  // its views when one finishes and holds off curation while one runs.
  const busy = !!status && (status.syncing || status.scanning || status.compatSyncing || status.verifying
    || status.vimmSyncing || status.importing || status.igdbSyncing || status.raSyncing)
  const totalInCatalog = status?.totalGames ?? 0

  // When a sync or scan finishes, refresh the catalog views so new games/counts/owned appear.
  const wasBusy = useRef(busy)
  useEffect(() => {
    if (wasBusy.current && !busy) {
      qc.invalidateQueries({ queryKey: ['catalog-consoles'] })
      qc.invalidateQueries({ queryKey: ['catalog-games'] })
    }
    wasBusy.current = busy
  }, [busy, qc])

  // Remember the whole view (filters, page, selected game) across tab navigation + reloads.
  useEffect(() => { saveFilters(filters) }, [filters])

  // Patch the view state. A result-defining change (`resetView`, the default) also returns to page 0
  // and drops the now-stale bulk selection + detail selection; navigation-only changes (page, picking
  // a game) pass resetView=false.
  const patch = useCallback((p: Partial<LibraryFilters>, resetView = true) => {
    setFilters(f => (resetView ? { ...f, selectedId: null, ...p, page: 0 } : { ...f, ...p }))
    if (resetView) setSelectedIds(new Set())
  }, [])

  const total = gamesResp?.total ?? 0
  const games = gamesResp?.games ?? []
  const selectedGame = games.find(g => g.id === filters.selectedId) ?? null

  function toggleSelect(id: number) {
    setSelectedIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }
  function toggleSelectVisible() {
    const queueableIds = games.filter(g => !g.owned).map(g => g.id)
    const allSelected = queueableIds.length > 0 && queueableIds.every(id => selectedIds.has(id))
    setSelectedIds(prev => {
      const next = new Set(prev)
      if (allSelected) queueableIds.forEach(id => next.delete(id))
      else queueableIds.forEach(id => next.add(id))
      return next
    })
  }

  // Download: if the game is hash-bound to Vimm with more than one format, let the user choose first;
  // otherwise queue straight away (archive-preferred, single-format, or no Vimm binding).
  async function handleQueue(g: CatalogGame) {
    setQueueError(null)
    if (g.vimmMatch && g.vimmMatch !== 'none') {
      try {
        const vimm = await fetchGameVimm(g.id)
        if (vimm && vimm.formats.length > 1) {
          setPicker({ id: g.id, name: g.name, formats: vimm.formats })
          return
        }
      } catch { /* fall through to a plain queue */ }
    }
    await doQueue(g.id)
  }

  async function doQueue(id: number, format?: number) {
    setQueueError(null)
    try {
      await queueGame.mutateAsync({ id, format })
      setQueuedIds(prev => new Set(prev).add(id))
      setPicker(null)
    } catch (e) {
      setQueueError(e instanceof Error ? e.message : 'Failed to queue download')
    }
  }

  async function queueSelected() {
    const ids = [...selectedIds]
    if (ids.length === 0) return
    setQueueError(null); setBatchMsg(null)
    try {
      const res = await batchQueue.mutateAsync({ ids })
      setQueuedIds(prev => {
        const next = new Set(prev)
        res.results.filter(r => r.status === 'queued').forEach(r => next.add(r.id))
        return next
      })
      setSelectedIds(new Set())
      const parts = [`Queued ${res.queued}`]
      if (res.skipped) parts.push(`${res.skipped} already queued`)
      if (res.failed) parts.push(`${res.failed} unavailable`)
      setBatchMsg(parts.join(' · '))
    } catch (e) {
      setQueueError(e instanceof Error ? e.message : 'Failed to queue selected')
    }
  }

  // Curation (R3): ask the backend for the best missing games (by rank) that fit a GB budget under the
  // current filters, and pre-select them — the user reviews, then uses the existing "Queue selected".
  const GB = 1024 ** 3
  async function pickBest(budgetGb: string, maxCount: string) {
    const gb = parseFloat(budgetGb)
    if (!Number.isFinite(gb) || gb <= 0) { setQueueError('Enter a positive GB budget for "best up to X GB"'); return }
    const n = parseInt(maxCount, 10)
    setQueueError(null); setBatchMsg(null); setCurating(true)
    try {
      const res = await fetchCurate({
        console: filters.console || null, q: query, dedupe: filters.dedupe, english: filters.english,
        excludeCategories: filters.excludeCategories, searchMode: filters.searchMode, emulator: filters.emulator,
        compat: filters.compatStatus, budgetBytes: Math.floor(gb * GB),
        maxCount: Number.isFinite(n) && n > 0 ? n : undefined,
      })
      setSelectedIds(new Set(res.ids))
      setBatchMsg(res.count === 0
        ? 'No missing games fit that budget — try a larger budget, fewer filters, or run Rank first.'
        : `Selected the best ${res.count.toLocaleString()} — ${fmtBytes(res.totalBytes)} of ${fmtBytes(res.budgetBytes)}. Review, then Queue selected.`)
    } catch (e) {
      setQueueError(e instanceof Error ? e.message : 'Failed to pick best games')
    } finally {
      setCurating(false)
    }
  }

  // Empty catalog → sync call-to-action.
  if (totalInCatalog === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-4 text-center px-6">
        <div className="text-text-3 text-sm max-w-md leading-relaxed">
          The game catalog is empty. Sync it from the <span className="text-text-2">No-Intro</span> &amp;
          <span className="text-text-2"> Redump</span> databases to browse every officially released game,
          organised by console.
        </div>
        <button onClick={() => syncMutation.mutate()} disabled={syncing}
          className="px-5 py-2 text-sm font-medium rounded bg-info/20 text-info
            border border-info/30 hover:bg-info/30 hover:shadow-[0_0_16px_rgba(111,141,255,0.2)]
            disabled:opacity-40">
          {syncing ? 'Syncing…' : 'Sync catalog'}
        </button>
        {syncing && <div className="text-[11px] text-text-4">Fetching DATs in the background…</div>}
      </div>
    )
  }

  return (
    <div className="flex flex-col h-full min-h-0">
      {(queueError || batchMsg) && (
        <div className="flex flex-col gap-1 px-3 sm:px-[18px] py-2 border-b border-border-subtle shrink-0">
          {queueError && (
            <div className="p-2 bg-error/8 border border-error/20 rounded text-xs text-error flex items-center gap-2">
              <span className="flex-1">{queueError}</span>
              <button onClick={() => setShowSets(true)} className="underline hover:text-text-2 shrink-0">Manage sources</button>
              <button onClick={() => setQueueError(null)} className="text-text-4 hover:text-text shrink-0">×</button>
            </div>
          )}
          {batchMsg && (
            <div className="p-2 bg-info/8 border border-info/20 rounded text-xs text-info flex items-center gap-2">
              <span className="flex-1">{batchMsg}</span>
              <button onClick={() => setBatchMsg(null)} className="text-text-4 hover:text-text shrink-0">×</button>
            </div>
          )}
        </div>
      )}

      <div className="flex-1 min-h-0 flex flex-col sm:flex-row">
        <ConsoleRail variant="strip" consoles={consoles} totalInCatalog={totalInCatalog}
          selected={filters.console} onSelect={c => patch({ console: c })} />
        <ConsoleRail consoles={consoles} totalInCatalog={totalInCatalog}
          selected={filters.console} onSelect={c => patch({ console: c })} />

        <GameList
          filters={filters} patch={patch} games={games} total={total} pageSize={PAGE_SIZE}
          isFetching={isFetching} emulators={emulators} detailOpen={selectedGame != null}
          selectedIds={selectedIds} onToggleSelect={toggleSelect} onToggleSelectVisible={toggleSelectVisible}
          onQueueSelected={queueSelected} onClearSelection={() => setSelectedIds(new Set())}
          batchPending={batchQueue.isPending} onPickBest={pickBest} curating={curating}
          catalogBusy={busy} onManageSources={() => setShowSets(true)} />

        <div className={`flex-1 min-w-0 min-h-0 overflow-y-auto ${selectedGame ? 'block' : 'hidden sm:block'}`}
          style={{ background: 'radial-gradient(130% 80% at 50% 0%,rgba(111,141,255,.09),transparent 60%)' }}>
          {selectedGame ? (
            // Keyed by game id: the pane sits in a reused tree position, so without a key React would
            // preserve its (and its children's) local state across selections.
            <GameDetailPane key={selectedGame.id} game={selectedGame} emuName={emuName}
              queued={queuedIds.has(selectedGame.id)} queuePending={queueGame.isPending}
              onQueue={handleQueue} onBack={() => patch({ selectedId: null }, false)}
              consoleDisplayName={consoles?.find(c => c.console === selectedGame.console)?.displayName} />
          ) : (
            <div className="flex items-center justify-center h-full text-[13px] text-text-4 px-6 text-center">
              Select a game to see its artwork, compatibility and download options.
            </div>
          )}
        </div>
      </div>

      {showSets && <SetsDialog onClose={() => setShowSets(false)} />}
      {picker && (
        <FormatPickerDialog gameName={picker.name} formats={picker.formats} busy={queueGame.isPending}
          onPick={alt => doQueue(picker.id, alt)} onClose={() => setPicker(null)} />
      )}
    </div>
  )
}
