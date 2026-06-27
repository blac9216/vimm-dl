import { useState, useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useCatalogConsoles, useCatalogGames, useCatalogStatus, useEmulators, useSyncCatalog, useScanCatalog, useSyncCompat, useVerifyCatalog, useSyncVimm, useQueueCatalogGame, useQueueCatalogGamesBatch, fetchGameVimm } from '../../api/queries'
import type { CatalogGame, CatalogVimmFormat } from '../../types/api'
import { CatalogThumb } from '../shared/CatalogThumb'
import { GameDetail } from './GameDetail'
import { SetsDialog } from './SetsDialog'
import { FormatPickerDialog } from './FormatPickerDialog'
import { fmtBytes } from '../../lib/format'
import { COMPAT_STATUSES, compatClass } from '../../lib/compat'

// Debounce the search box so we don't query the catalog on every keystroke.
function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return debounced
}

// Short label for a DAT-source origin (D2b-2): the provenance of the catalog entry, distinct from the
// download source. 'daily-bundle' → 'bundle'; anything else shown as-is (e.g. 'libretro').
function originLabel(origin: string): string {
  return origin === 'daily-bundle' ? 'bundle' : origin
}

// Human label for a download format alt (PS3 conventions; mirrors the backend FormatLabel).
function fmtLabel(alt: number): string {
  switch (alt) {
    case 0: return 'JB Folder'
    case 1: return '.dec.iso'
    default: return `fmt ${alt}`
  }
}

// One row per game (Phase C / C5): consolidate the game's available + owned download formats into a
// compact chip group. Owned formats are highlighted; available-but-not-owned are downloadable.
function FormatChips({ game }: { game: CatalogGame }) {
  const owned = new Set(game.ownedFormats)
  const alts = [...new Set([...game.availableFormats, ...game.ownedFormats])].sort((a, b) => a - b)
  if (alts.length === 0) return null
  return (
    <div className="hidden sm:flex items-center gap-1 shrink-0">
      {alts.map(alt => (
        <span key={alt}
          className={`text-[9px] px-1.5 py-0.5 rounded border ${
            owned.has(alt)
              ? 'bg-ps-triangle/15 text-ps-triangle border-ps-triangle/25'
              : 'bg-surface-3/40 text-text-3 border-border/30'
          }`}
          title={owned.has(alt) ? `Owned: ${fmtLabel(alt)}` : `Available: ${fmtLabel(alt)}`}>
          {owned.has(alt) ? '✓ ' : ''}{fmtLabel(alt)}
        </span>
      ))}
    </div>
  )
}

const PAGE_SIZE = 100

// Persist Library filters so they survive tab navigation (the panel unmounts on tab change).
const FILTERS_KEY = 'vimm:library-filters'
interface LibraryFilters { console: string; search: string; local: string; dedupe: boolean; english: boolean; excludeCategories: boolean; searchMode: string; emulator: string; compatStatus: string; page: number }
const DEFAULT_FILTERS: LibraryFilters = { console: '', search: '', local: 'all', dedupe: false, english: false, excludeCategories: false, searchMode: 'substring', emulator: '', compatStatus: '', page: 0 }
function loadFilters(): LibraryFilters {
  try {
    const raw = localStorage.getItem(FILTERS_KEY)
    return raw ? { ...DEFAULT_FILTERS, ...(JSON.parse(raw) as Partial<LibraryFilters>) } : DEFAULT_FILTERS
  } catch {
    return DEFAULT_FILTERS
  }
}

export function LibraryPanel() {
  const [persisted] = useState(loadFilters) // read saved filters once on mount
  const [selectedConsole, setSelectedConsole] = useState(persisted.console) // '' = all consoles
  const [searchInput, setSearchInput] = useState(persisted.search)
  const [local, setLocal] = useState(persisted.local) // all | owned | remote
  const [dedupe, setDedupe] = useState(persisted.dedupe) // 1G1R
  const [english, setEnglish] = useState(persisted.english) // English/Western releases only
  const [excludeCategories, setExcludeCategories] = useState(persisted.excludeCategories) // hide demos/protos
  const [searchMode, setSearchMode] = useState(persisted.searchMode) // substring | glob | regex
  const [emulator, setEmulator] = useState(persisted.emulator) // '' = any emulator
  const [compatStatus, setCompatStatus] = useState(persisted.compatStatus) // '' = any status (needs an emulator)
  const [page, setPage] = useState(persisted.page)
  const query = useDebounced(searchInput, 350)

  const [showSets, setShowSets] = useState(false)
  const [queuedIds, setQueuedIds] = useState<Set<number>>(new Set())
  const [queueError, setQueueError] = useState<string | null>(null)
  const [batchMsg, setBatchMsg] = useState<string | null>(null)
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set()) // bulk-select for batch queue
  const [picker, setPicker] = useState<{ id: number; name: string; formats: CatalogVimmFormat[] } | null>(null)
  const [expandedId, setExpandedId] = useState<number | null>(null) // row click-to-expand detail (M3)

  const qc = useQueryClient()
  const { data: consoles } = useCatalogConsoles()
  const { data: emulators } = useEmulators()
  const { data: status } = useCatalogStatus()
  const syncMutation = useSyncCatalog()
  const scanMutation = useScanCatalog()
  const compatMutation = useSyncCompat()
  const verifyMutation = useVerifyCatalog()
  const vimmMutation = useSyncVimm()
  const queueGame = useQueueCatalogGame()
  const batchQueue = useQueueCatalogGamesBatch()
  const { data: gamesResp, isFetching } = useCatalogGames(selectedConsole || null, query, local, dedupe, english, excludeCategories, searchMode, page, PAGE_SIZE, emulator, compatStatus)

  // Map an emulator id to its display name (e.g. 'rpcs3' → 'RPCS3'); falls back to the id.
  const emuName = (id: string) => emulators?.find(e => e.id === id)?.name ?? id.toUpperCase()

  const syncing = status?.syncing ?? false
  const scanning = status?.scanning ?? false
  const compatSyncing = status?.compatSyncing ?? false
  const verifying = status?.verifying ?? false
  const vimmSyncing = status?.vimmSyncing ?? false
  const busy = syncing || scanning || compatSyncing || verifying || vimmSyncing
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

  // Remember the active filters across tab navigation (panel unmounts when you leave the tab).
  useEffect(() => {
    try {
      localStorage.setItem(FILTERS_KEY, JSON.stringify(
        { console: selectedConsole, search: searchInput, local, dedupe, english, excludeCategories, searchMode, emulator, compatStatus, page }))
    } catch { /* ignore storage write errors */ }
  }, [selectedConsole, searchInput, local, dedupe, english, excludeCategories, searchMode, emulator, compatStatus, page])

  // Changing any result-defining filter returns to page 0 and drops the now-stale bulk selection.
  function resetView() { setPage(0); setSelectedIds(new Set()) }
  function pickConsole(c: string) { setSelectedConsole(c); resetView() }
  function onSearch(v: string) { setSearchInput(v); resetView() }
  function pickLocal(v: string) { setLocal(v); resetView() }
  // Picking "any emulator" also clears the status (status only applies to a chosen emulator).
  function pickEmulator(v: string) { setEmulator(v); if (!v) setCompatStatus(''); resetView() }
  function pickCompatStatus(v: string) { setCompatStatus(v); resetView() }

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
          className="px-5 py-2 text-sm font-medium rounded bg-ps-cross/20 text-[#7eb3e0]
            border border-ps-cross/30 hover:bg-ps-cross/30 hover:shadow-[0_0_16px_rgba(46,109,180,0.2)]
            disabled:opacity-40">
          {syncing ? 'Syncing…' : 'Sync catalog'}
        </button>
        {syncing && <div className="text-[11px] text-text-4">Fetching DATs in the background…</div>}
      </div>
    )
  }

  const total = gamesResp?.total ?? 0
  const games = gamesResp?.games ?? []
  const start = total === 0 ? 0 : page * PAGE_SIZE + 1
  const end = Math.min((page + 1) * PAGE_SIZE, total)
  const pageCount = Math.ceil(total / PAGE_SIZE);

  // Bulk-select (E3b): only non-owned rows are queueable. "Select visible" toggles the current page.
  const queueableIds = games.filter(g => !g.owned).map(g => g.id)
  const allVisibleSelected = queueableIds.length > 0 && queueableIds.every(id => selectedIds.has(id))
  function toggleSelect(id: number) {
    setSelectedIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }
  function toggleSelectVisible() {
    setSelectedIds(prev => {
      const next = new Set(prev)
      if (allVisibleSelected) queueableIds.forEach(id => next.delete(id))
      else queueableIds.forEach(id => next.add(id))
      return next
    })
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

  const searchPlaceholder = searchMode === 'glob' ? 'Glob: Mario*, Final ?antasy…'
    : searchMode === 'regex' ? 'Regex: ^Final Fantasy (VII|IX)…'
    : 'Search games by name…'

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center gap-2 px-3 sm:px-6 py-2.5 bg-surface/30 border-b border-border/20 flex-wrap">
        <select value={selectedConsole} onChange={e => pickConsole(e.target.value)}
          title="Console"
          className="bg-surface/80 border border-border/60 rounded px-2 py-1 text-sm text-text
            focus:outline-none focus:border-accent/40 shrink-0">
          <option value="">All consoles</option>
          {consoles?.map(c => (
            <option key={c.console} value={c.console}>
              {c.console} ({c.ownedCount.toLocaleString()}/{c.gameCount.toLocaleString()})
            </option>
          ))}
        </select>
        <select value={local} onChange={e => pickLocal(e.target.value)} title="Availability"
          className="bg-surface/80 border border-border/60 rounded px-2 py-1 text-sm text-text
            focus:outline-none focus:border-accent/40 shrink-0">
          <option value="all">All</option>
          <option value="owned">Owned</option>
          <option value="remote">Missing</option>
        </select>
        <button onClick={() => { setDedupe(d => !d); resetView() }} title="One game per title (1G1R) — hide regional/revision duplicates"
          className={`px-3 py-1 text-xs font-medium rounded border shrink-0 transition-colors ${
            dedupe ? 'bg-accent/20 text-accent border-accent/40'
                   : 'bg-surface/80 text-text-3 border-border/60 hover:text-text'}`}>
          1G1R
        </button>
        <button onClick={() => { setEnglish(e => !e); resetView() }} title="English-only — hide titles with no English/Western release"
          className={`px-3 py-1 text-xs font-medium rounded border shrink-0 transition-colors ${
            english ? 'bg-accent/20 text-accent border-accent/40'
                    : 'bg-surface/80 text-text-3 border-border/60 hover:text-text'}`}>
          English
        </button>
        <button onClick={() => { setExcludeCategories(v => !v); resetView() }} title="Hide demos, betas, prototypes, kiosk & sample builds"
          className={`px-3 py-1 text-xs font-medium rounded border shrink-0 transition-colors ${
            excludeCategories ? 'bg-accent/20 text-accent border-accent/40'
                              : 'bg-surface/80 text-text-3 border-border/60 hover:text-text'}`}>
          Hide demos
        </button>
        <select value={searchMode} onChange={e => { setSearchMode(e.target.value); resetView() }} title="Search mode: substring, glob (*,?) or regex"
          className="bg-surface/80 border border-border/60 rounded px-2 py-1 text-xs text-text-3
            focus:outline-none focus:border-accent/40 shrink-0">
          <option value="substring">Name</option>
          <option value="glob">Glob</option>
          <option value="regex">Regex</option>
        </select>
        {emulators && emulators.length > 0 && (
          <select value={emulator} onChange={e => pickEmulator(e.target.value)} title="Filter by emulator compatibility"
            className="bg-surface/80 border border-border/60 rounded px-2 py-1 text-xs text-text-3
              focus:outline-none focus:border-accent/40 shrink-0">
            <option value="">Any emulator</option>
            {emulators.map(e => (
              <option key={e.id} value={e.id}>{e.name}</option>
            ))}
          </select>
        )}
        {emulator && (
          <select value={compatStatus} onChange={e => pickCompatStatus(e.target.value)} title="Filter by playability status"
            className="bg-surface/80 border border-border/60 rounded px-2 py-1 text-xs text-text-3
              focus:outline-none focus:border-accent/40 shrink-0">
            <option value="">Any status</option>
            {COMPAT_STATUSES.map(s => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        )}
        <input type="text" value={searchInput} onChange={e => onSearch(e.target.value)}
          placeholder={searchPlaceholder}
          className="flex-1 bg-surface/60 border border-border/40 rounded px-3 py-1 text-sm text-text
            placeholder:text-text-4 focus:outline-none focus:border-accent/30
            focus:shadow-[0_0_10px_rgba(91,155,213,0.08)]" />
        <button onClick={() => setShowSets(true)} title="Manage download sources"
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text shrink-0">
          Sources
        </button>
        <button onClick={() => scanMutation.mutate()} disabled={busy} title="Scan completed/ for owned games"
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text disabled:opacity-40 shrink-0">
          {scanning ? 'Scanning…' : 'Scan'}
        </button>
        <button onClick={() => verifyMutation.mutate()} disabled={busy} title="Verify owned files against catalog hashes (CRC32)"
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text disabled:opacity-40 shrink-0">
          {verifying ? 'Verifying…' : 'Verify'}
        </button>
        <button onClick={() => compatMutation.mutate()} disabled={busy} title="Sync emulator compatibility lists"
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text disabled:opacity-40 shrink-0">
          {compatSyncing ? 'Compat…' : 'Compat'}
        </button>
        <button onClick={() => vimmMutation.mutate(selectedConsole || undefined)} disabled={busy}
          title={selectedConsole
            ? `Match ${selectedConsole} against Vimm's Lair by hash, binding a vault URL + formats`
            : "Match the catalog against Vimm's Lair by hash (pick a console to scope; otherwise all Vimm consoles)"}
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text disabled:opacity-40 shrink-0">
          {vimmSyncing ? 'Vimm…' : 'Vimm'}
        </button>
        <button onClick={() => syncMutation.mutate()} disabled={busy} title="Re-sync from No-Intro / Redump"
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text disabled:opacity-40 shrink-0">
          {syncing ? 'Syncing…' : 'Sync'}
        </button>
      </div>

      <div className="px-3 sm:px-6 py-1.5 text-[10px] text-text-4 tracking-wide border-b border-border/10 flex items-center gap-2">
        <input type="checkbox" checked={allVisibleSelected} onChange={toggleSelectVisible}
          disabled={queueableIds.length === 0} title="Select all queueable games on this page"
          className="accent-accent w-3 h-3 shrink-0 cursor-pointer disabled:opacity-30" />
        <span>
          {total > 0 ? `Showing ${start.toLocaleString()}–${end.toLocaleString()} of ${total.toLocaleString()}` : 'No games match'}
          {' · '}<span className="text-text-3">{totalInCatalog.toLocaleString()} in catalog</span>
        </span>
        {selectedIds.size > 0 && (
          <span className="ml-auto flex items-center gap-2 shrink-0">
            <span className="text-accent">{selectedIds.size.toLocaleString()} selected</span>
            <button onClick={queueSelected} disabled={batchQueue.isPending}
              className="px-2 py-0.5 text-[10px] font-medium rounded bg-ps-cross/20 text-[#7eb3e0]
                border border-ps-cross/30 hover:bg-ps-cross/30 disabled:opacity-40">
              {batchQueue.isPending ? 'Queuing…' : 'Queue selected'}
            </button>
            <button onClick={() => setSelectedIds(new Set())}
              className="px-2 py-0.5 text-[10px] rounded bg-surface-2/40 text-text-3 border border-border/30 hover:text-text">
              Clear
            </button>
          </span>
        )}
      </div>

      {queueError && (
        <div className="mx-3 sm:mx-6 mt-2 p-2 bg-ps-circle/8 border border-ps-circle/20 rounded text-xs text-[#e06070] flex items-center gap-2">
          <span className="flex-1">{queueError}</span>
          <button onClick={() => setShowSets(true)} className="underline hover:text-text-2 shrink-0">Manage sources</button>
          <button onClick={() => setQueueError(null)} className="text-text-4 hover:text-text shrink-0">×</button>
        </div>
      )}

      {batchMsg && (
        <div className="mx-3 sm:mx-6 mt-2 p-2 bg-ps-cross/8 border border-ps-cross/20 rounded text-xs text-[#7eb3e0] flex items-center gap-2">
          <span className="flex-1">{batchMsg}</span>
          <button onClick={() => setBatchMsg(null)} className="text-text-4 hover:text-text shrink-0">×</button>
        </div>
      )}

      <div className="flex-1 overflow-y-auto">
        {games.map(g => (
          <div key={g.id} className="border-b border-border/10">
          <div className="flex items-center gap-3 px-3 sm:px-6 py-2 hover:bg-surface/30">
            {g.owned
              ? <span className="w-3.5 shrink-0" />
              : <input type="checkbox" checked={selectedIds.has(g.id)} onChange={() => toggleSelect(g.id)}
                  title="Select for batch queue"
                  className="accent-accent w-3.5 h-3.5 shrink-0 cursor-pointer" />}
            <button type="button" onClick={() => setExpandedId(prev => (prev === g.id ? null : g.id))}
              title="Show artwork & details" aria-expanded={expandedId === g.id}
              className="flex items-center gap-3 flex-1 min-w-0 text-left group">
              <CatalogThumb id={g.id} platform={g.console} />
              <span className="flex-1 min-w-0 block">
                <span className="block text-sm text-text-2 truncate group-hover:text-text" title={g.name}>{g.name}</span>
                <span className="flex gap-2 flex-wrap text-[10px] text-text-4">
                  {g.region && <span>{g.region}</span>}
                  {g.languages && <span>{g.languages}</span>}
                  {g.serial && <span className="font-mono">{g.serial}</span>}
                </span>
              </span>
            </button>
            {g.compat.map(c => (
              <span key={c.emulator} className={`text-[9px] px-1.5 py-0.5 rounded border shrink-0 ${compatClass(c.status)}`}
                title={`${emuName(c.emulator)}: ${c.status}`}>{c.status}</span>
            ))}
            {g.vimmMatch && g.vimmMatch !== 'none' && (
              <span className="text-[9px] px-1.5 py-0.5 rounded bg-ps-cross/10 text-[#7eb3e0]
                border border-ps-cross/25 shrink-0" title={`Matched to a Vimm vault entry by ${g.vimmMatch.toUpperCase()}`}>Vimm</span>
            )}
            {g.vimmMatch === 'none' && (
              <span className="text-[9px] px-1.5 py-0.5 rounded bg-surface-3/40 text-text-4
                border border-border/30 shrink-0" title="No Vimm match found — rectify manually">no Vimm</span>
            )}
            {g.origins.length > 0 && (
              <span className="text-[9px] px-1.5 py-0.5 rounded bg-surface-3/30 text-text-4
                border border-border/25 shrink-0 hidden sm:inline"
                title={`Catalog DAT source: ${g.origins.map(originLabel).join(', ')}`}>
                {g.origins.map(originLabel).join('+')}
              </span>
            )}
            {g.size > 0 && (
              <span className="text-[10px] text-text-4 font-mono tabular-nums shrink-0">{fmtBytes(g.size)}</span>
            )}
            <FormatChips game={g} />
            {g.owned ? (
              g.verified === true ? (
                <span className="text-[9px] px-1.5 py-0.5 rounded bg-ps-triangle/15 text-ps-triangle
                  border border-ps-triangle/25 shrink-0" title="File CRC32 matches the catalog">✓ Verified</span>
              ) : g.verified === false ? (
                <span className="text-[9px] px-1.5 py-0.5 rounded bg-ps-circle/10 text-[#e06070]
                  border border-ps-circle/20 shrink-0" title="File CRC32 does not match any catalog hash">✗ Mismatch</span>
              ) : (
                <span className="text-[9px] px-1.5 py-0.5 rounded bg-ps-triangle/15 text-ps-triangle
                  border border-ps-triangle/25 shrink-0" title="Owned — run Verify to check the file hash">Owned</span>
              )
            ) : (
              <button onClick={() => handleQueue(g)} disabled={queuedIds.has(g.id) || queueGame.isPending}
                className="px-2.5 py-1 text-xs font-medium rounded bg-ps-cross/20 text-[#7eb3e0]
                  border border-ps-cross/30 hover:bg-ps-cross/30 disabled:opacity-40 shrink-0">
                {queuedIds.has(g.id) ? 'Queued' : 'Download'}
              </button>
            )}
          </div>
          {expandedId === g.id && <GameDetail game={g} emuName={emuName} />}
          </div>
        ))}
        {isFetching && games.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-sm">Loading…</div>
        )}
        {!isFetching && games.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-sm">No games match your filter</div>
        )}
      </div>

      {pageCount > 1 && (
        <div className="flex items-center justify-center gap-3 px-3 sm:px-6 py-2 border-t border-border/20 text-xs">
          <button onClick={() => setPage(p => Math.max(0, p - 1))} disabled={page === 0}
            className="px-3 py-1 rounded bg-surface-2/40 text-text-3 border border-border/30 hover:text-text disabled:opacity-30">
            ‹ Prev
          </button>
          <span className="text-text-4 tabular-nums">Page {page + 1} of {pageCount.toLocaleString()}</span>
          <button onClick={() => setPage(p => (end < total ? p + 1 : p))} disabled={end >= total}
            className="px-3 py-1 rounded bg-surface-2/40 text-text-3 border border-border/30 hover:text-text disabled:opacity-30">
            Next ›
          </button>
        </div>
      )}

      {showSets && <SetsDialog onClose={() => setShowSets(false)} />}
      {picker && (
        <FormatPickerDialog gameName={picker.name} formats={picker.formats} busy={queueGame.isPending}
          onPick={alt => doQueue(picker.id, alt)} onClose={() => setPicker(null)} />
      )}
    </div>
  )
}
