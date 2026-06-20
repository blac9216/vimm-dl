import { useState, useEffect, useMemo } from 'react'
import {
  useSources, useBrowseSets, useBrowseFiles, useAddToQueue, useSettings,
} from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { DuplicateDialog } from '../shared/DuplicateDialog'
import { PlatformIcon } from '../shared/PlatformIcon'
import { fmtBytes } from '../../lib/format'
import type { CatalogSet, DuplicateInfo } from '../../types/api'

// Debounce a fast-changing value so the file filter doesn't hit the API per keystroke.
function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return debounced
}

export function BrowsePanel() {
  const { data: sources } = useSources()
  const catalogSources = useMemo(() => sources?.filter(s => s.catalog) ?? [], [sources])

  // Derive the active source at render (default to the first catalog source) rather
  // than syncing it through an effect — avoids a cascading-render setState-in-effect.
  const [sourceOverride, setSourceOverride] = useState<string | null>(null)
  const source = sourceOverride ?? catalogSources[0]?.id ?? ''
  const [searchInput, setSearchInput] = useState('')
  const [query, setQuery] = useState('')
  const [searched, setSearched] = useState(false)
  const [selectedSet, setSelectedSet] = useState<CatalogSet | null>(null)
  const [filterInput, setFilterInput] = useState('')
  const filter = useDebounced(filterInput, 350)

  const [duplicates, setDuplicates] = useState<DuplicateInfo[] | null>(null)
  const [pendingUrl, setPendingUrl] = useState<string | null>(null)
  const [addedUrls, setAddedUrls] = useState<Set<string>>(new Set())

  const { data: config } = useSettings()
  const { state, connection } = useDownload()
  const addMutation = useAddToQueue()

  const setsQuery = useBrowseSets(source, query, searched && !!source)
  const filesQuery = useBrowseFiles(source, selectedSet?.id ?? null, filter)

  function handleSearch() {
    setSelectedSet(null)
    setQuery(searchInput)
    setSearched(true)
  }

  function startIfIdle() {
    if (!state.running && connection) {
      connection.invoke('StartDownload', config?.activePath ?? null)
    }
  }

  async function handleAdd(downloadUrl: string) {
    // archive.org has no PS3-style format; resolve to 0.
    const result = await addMutation.mutateAsync({ urls: [downloadUrl], format: 0, source })
    if (result?.duplicates && result.duplicates.length > 0) {
      setDuplicates(result.duplicates)
      setPendingUrl(downloadUrl)
      return
    }
    setAddedUrls(prev => new Set(prev).add(downloadUrl))
    startIfIdle()
  }

  async function handleForceAdd() {
    if (!pendingUrl) return
    await addMutation.mutateAsync({ urls: [pendingUrl], format: 0, force: true, source })
    setAddedUrls(prev => new Set(prev).add(pendingUrl))
    setDuplicates(null)
    setPendingUrl(null)
    startIfIdle()
  }

  if (catalogSources.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-text-4 text-sm tracking-wide">
        No browsable sources available.
      </div>
    )
  }

  const sets = setsQuery.data ?? []
  const files = filesQuery.data ?? []

  return (
    <div className="flex flex-col h-full">
      {!selectedSet ? (
        // --- Set search ---
        <div className="flex items-center gap-2 px-3 sm:px-6 py-2.5 bg-surface/30 border-b border-border/20 flex-wrap">
          {catalogSources.length > 1 && (
            <select
              value={source}
              onChange={e => { setSourceOverride(e.target.value); setSearched(false) }}
              title="Catalog source"
              className="bg-surface/80 border border-border/60 rounded px-2 py-1 text-sm text-text
                focus:outline-none focus:border-accent/40 shrink-0"
            >
              {catalogSources.map(s => <option key={s.id} value={s.id}>{s.displayName}</option>)}
            </select>
          )}
          <input
            type="text"
            value={searchInput}
            onChange={e => setSearchInput(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') handleSearch() }}
            placeholder="Search ROM sets (e.g. game boy advance)"
            className="flex-1 bg-surface/60 border border-border/40 rounded px-3 py-1 text-sm text-text
              placeholder:text-text-4 focus:outline-none focus:border-accent/30
              focus:shadow-[0_0_10px_rgba(91,155,213,0.08)]"
          />
          <button onClick={handleSearch} disabled={setsQuery.isFetching}
            className="px-3.5 py-1 text-xs font-medium rounded bg-ps-cross/15 text-[#7eb3e0]
              border border-ps-cross/25 hover:bg-ps-cross/25
              hover:shadow-[0_0_12px_rgba(46,109,180,0.15)] disabled:opacity-40">
            Search
          </button>
        </div>
      ) : (
        // --- Selected set: files ---
        <div className="flex items-center gap-2 px-3 sm:px-6 py-2.5 bg-surface/30 border-b border-border/20 flex-wrap">
          <button onClick={() => setSelectedSet(null)}
            className="px-2.5 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
              border border-border/30 hover:bg-surface-2/70 hover:text-text shrink-0">
            ← Back
          </button>
          <PlatformIcon platform={selectedSet.platform} />
          <span className="flex-1 text-sm text-text truncate min-w-0" title={selectedSet.title}>
            {selectedSet.title}
          </span>
          <input
            type="text"
            value={filterInput}
            onChange={e => setFilterInput(e.target.value)}
            placeholder="Filter files..."
            className="w-48 bg-surface/60 border border-border/40 rounded px-3 py-1 text-sm text-text
              placeholder:text-text-4 focus:outline-none focus:border-accent/30"
          />
        </div>
      )}

      {(setsQuery.error || filesQuery.error) && (
        <div className="mx-3 sm:mx-6 mt-2 p-2 bg-ps-circle/8 border border-ps-circle/20 rounded text-xs text-[#e06070]">
          {(selectedSet ? filesQuery.error : setsQuery.error)?.message ?? 'Request failed'}
        </div>
      )}

      <div className="flex-1 overflow-y-auto">
        {!selectedSet ? (
          <>
            {sets.map(set => (
              <button
                key={set.id}
                onClick={() => { setSelectedSet(set); setFilterInput('') }}
                className="w-full flex items-center gap-3 px-3 sm:px-6 py-2.5 border-b border-border/10
                  hover:bg-surface/40 text-left transition-colors"
              >
                <PlatformIcon platform={set.platform} className="shrink-0" />
                <span className="flex-1 text-sm text-text-2 truncate min-w-0" title={set.title}>{set.title}</span>
                <span className="text-text-4 text-sm shrink-0">›</span>
              </button>
            ))}
            {!setsQuery.isFetching && searched && sets.length === 0 && (
              <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
                No sets found
              </div>
            )}
            {setsQuery.isFetching && (
              <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
                Searching…
              </div>
            )}
            {!searched && (
              <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
                Search a catalog to browse ROM sets
              </div>
            )}
          </>
        ) : (
          <>
            {files.map(file => {
              const added = addedUrls.has(file.downloadUrl)
              return (
                <div key={file.downloadUrl}
                  className="flex items-center gap-3 px-3 sm:px-6 py-2 border-b border-border/10 hover:bg-surface/30">
                  <span className="flex-1 text-sm text-text-3 truncate min-w-0" title={file.name}>{file.name}</span>
                  {file.size > 0 && (
                    <span className="text-[10px] text-text-4 font-mono tabular-nums shrink-0">{fmtBytes(file.size)}</span>
                  )}
                  <button onClick={() => handleAdd(file.downloadUrl)}
                    disabled={added || addMutation.isPending}
                    className="px-3 py-1 text-xs font-medium rounded bg-ps-cross/20 text-[#7eb3e0]
                      border border-ps-cross/30 hover:bg-ps-cross/30 disabled:opacity-40 shrink-0">
                    {added ? 'Added' : 'Add'}
                  </button>
                </div>
              )
            })}
            {!filesQuery.isFetching && files.length === 0 && (
              <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
                {filter ? 'No files match the filter' : 'No downloadable files'}
              </div>
            )}
            {filesQuery.isFetching && (
              <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
                Loading files…
              </div>
            )}
          </>
        )}
      </div>

      {duplicates && (
        <DuplicateDialog
          duplicates={duplicates}
          onConfirm={handleForceAdd}
          onCancel={() => { setDuplicates(null); setPendingUrl(null) }}
          isPending={addMutation.isPending}
        />
      )}
    </div>
  )
}
