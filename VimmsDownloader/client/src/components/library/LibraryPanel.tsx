import { useState, useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useCatalogConsoles, useCatalogGames, useCatalogStatus, useSyncCatalog, useScanCatalog, useSyncCompat, useQueueCatalogGame } from '../../api/queries'
import { PlatformIcon } from '../shared/PlatformIcon'
import { SetsDialog } from './SetsDialog'
import { fmtBytes } from '../../lib/format'

// Debounce the search box so we don't query the catalog on every keystroke.
function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return debounced
}

// Color an emulator compatibility status (RPCS3-style: Playable/Ingame/Intro/Loadable/Nothing).
function compatClass(status: string): string {
  switch (status) {
    case 'Playable': return 'bg-ps-triangle/15 text-ps-triangle border-ps-triangle/25'
    case 'Ingame': return 'bg-accent/15 text-accent border-accent/30'
    case 'Intro':
    case 'Loadable': return 'bg-[#e0a060]/15 text-[#e0a060] border-[#e0a060]/30'
    default: return 'bg-ps-circle/10 text-[#e06070] border-ps-circle/20' // Nothing / unknown
  }
}

const PAGE_SIZE = 100

export function LibraryPanel() {
  const [selectedConsole, setSelectedConsole] = useState('') // '' = all consoles
  const [searchInput, setSearchInput] = useState('')
  const [local, setLocal] = useState('all') // all | owned | remote
  const [dedupe, setDedupe] = useState(false) // 1G1R
  const [page, setPage] = useState(0)
  const query = useDebounced(searchInput, 350)

  const [showSets, setShowSets] = useState(false)
  const [queuedIds, setQueuedIds] = useState<Set<number>>(new Set())
  const [queueError, setQueueError] = useState<string | null>(null)

  const qc = useQueryClient()
  const { data: consoles } = useCatalogConsoles()
  const { data: status } = useCatalogStatus()
  const syncMutation = useSyncCatalog()
  const scanMutation = useScanCatalog()
  const compatMutation = useSyncCompat()
  const queueGame = useQueueCatalogGame()
  const { data: gamesResp, isFetching } = useCatalogGames(selectedConsole || null, query, local, dedupe, page, PAGE_SIZE)

  const syncing = status?.syncing ?? false
  const scanning = status?.scanning ?? false
  const compatSyncing = status?.compatSyncing ?? false
  const busy = syncing || scanning || compatSyncing
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

  function pickConsole(c: string) { setSelectedConsole(c); setPage(0) }
  function onSearch(v: string) { setSearchInput(v); setPage(0) }
  function pickLocal(v: string) { setLocal(v); setPage(0) }

  async function handleQueue(id: number) {
    setQueueError(null)
    try {
      await queueGame.mutateAsync(id)
      setQueuedIds(prev => new Set(prev).add(id))
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
        <button onClick={() => { setDedupe(d => !d); setPage(0) }} title="One game per title (1G1R) — hide regional/revision duplicates"
          className={`px-3 py-1 text-xs font-medium rounded border shrink-0 transition-colors ${
            dedupe ? 'bg-accent/20 text-accent border-accent/40'
                   : 'bg-surface/80 text-text-3 border-border/60 hover:text-text'}`}>
          1G1R
        </button>
        <input type="text" value={searchInput} onChange={e => onSearch(e.target.value)}
          placeholder="Search games by name…"
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
        <button onClick={() => compatMutation.mutate()} disabled={busy} title="Sync emulator compatibility (RPCS3)"
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text disabled:opacity-40 shrink-0">
          {compatSyncing ? 'Compat…' : 'Compat'}
        </button>
        <button onClick={() => syncMutation.mutate()} disabled={busy} title="Re-sync from No-Intro / Redump"
          className="px-3 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-surface-2/70 hover:text-text disabled:opacity-40 shrink-0">
          {syncing ? 'Syncing…' : 'Sync'}
        </button>
      </div>

      <div className="px-3 sm:px-6 py-1.5 text-[10px] text-text-4 tracking-wide border-b border-border/10">
        {total > 0 ? `Showing ${start.toLocaleString()}–${end.toLocaleString()} of ${total.toLocaleString()}` : 'No games match'}
        {' · '}<span className="text-text-3">{totalInCatalog.toLocaleString()} in catalog</span>
      </div>

      {queueError && (
        <div className="mx-3 sm:mx-6 mt-2 p-2 bg-ps-circle/8 border border-ps-circle/20 rounded text-xs text-[#e06070] flex items-center gap-2">
          <span className="flex-1">{queueError}</span>
          <button onClick={() => setShowSets(true)} className="underline hover:text-text-2 shrink-0">Manage sources</button>
          <button onClick={() => setQueueError(null)} className="text-text-4 hover:text-text shrink-0">×</button>
        </div>
      )}

      <div className="flex-1 overflow-y-auto">
        {games.map(g => (
          <div key={g.id} className="flex items-center gap-3 px-3 sm:px-6 py-2 border-b border-border/10 hover:bg-surface/30">
            <PlatformIcon platform={g.console} className="shrink-0" />
            <div className="flex-1 min-w-0">
              <div className="text-sm text-text-2 truncate" title={g.name}>{g.name}</div>
              <div className="text-[10px] text-text-4 flex gap-2 flex-wrap">
                {g.region && <span>{g.region}</span>}
                {g.languages && <span>{g.languages}</span>}
                {g.serial && <span className="font-mono">{g.serial}</span>}
              </div>
            </div>
            {g.compat && (
              <span className={`text-[9px] px-1.5 py-0.5 rounded border shrink-0 ${compatClass(g.compat)}`}
                title={`RPCS3: ${g.compat}`}>{g.compat}</span>
            )}
            {g.size > 0 && (
              <span className="text-[10px] text-text-4 font-mono tabular-nums shrink-0">{fmtBytes(g.size)}</span>
            )}
            {g.owned ? (
              <span className="text-[9px] px-1.5 py-0.5 rounded bg-ps-triangle/15 text-ps-triangle
                border border-ps-triangle/25 shrink-0">✓ Owned</span>
            ) : (
              <button onClick={() => handleQueue(g.id)} disabled={queuedIds.has(g.id) || queueGame.isPending}
                className="px-2.5 py-1 text-xs font-medium rounded bg-ps-cross/20 text-[#7eb3e0]
                  border border-ps-cross/30 hover:bg-ps-cross/30 disabled:opacity-40 shrink-0">
                {queuedIds.has(g.id) ? 'Queued' : 'Download'}
              </button>
            )}
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
    </div>
  )
}
