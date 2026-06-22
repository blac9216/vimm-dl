import { useState } from 'react'
import { useSettings, useCatalogStatus, useImportCatalog, useEvents } from '../../api/queries'
import { EventItem } from '../events/EventItem'

type Filter = 'all' | 'matched' | 'rejected'

const FILTERS: { label: string; value: Filter }[] = [
  { label: 'All', value: 'all' },
  { label: 'Matched', value: 'matched' },
  { label: 'Rejected', value: 'rejected' },
]

/**
 * Import view (epic #118 / L4): drop ROMs into the import folder, hit Ingest, and watch each file get
 * hash-matched into the catalog (→ completed/{console}/) or set aside in rejected/. Results come from the
 * per-file `import` events the L2 job logs; the run state comes from /api/catalog/status (importing).
 */
export function ImportPanel() {
  const { data: settings } = useSettings()
  const { data: status } = useCatalogStatus()
  const importMutation = useImportCatalog()
  const { data: eventsData } = useEvents('import', undefined, 200)
  const [filter, setFilter] = useState<Filter>('all')

  const importing = status?.importing ?? false
  const events = eventsData?.events ?? []
  const matchedCount = events.filter(e => e.phase === 'matched').length
  const rejectedCount = events.filter(e => e.phase === 'rejected').length
  const visible = events.filter(e =>
    filter === 'all' ? true : e.phase === filter)

  return (
    <div className="flex flex-col h-full">
      <div className="flex flex-col gap-3 px-3 sm:px-6 py-3 bg-surface/30 border-b border-border/20">
        <div className="flex items-start justify-between gap-3 flex-wrap">
          <div className="space-y-1 min-w-0">
            <div className="text-[10px] text-text-4 tracking-wide uppercase">Drop ROMs into</div>
            <div className="text-[11px] font-mono text-text-2 break-all" title={settings?.importPath}>
              {settings?.importPath ?? '…'}
            </div>
            <div className="text-[10px] text-text-4">
              Non-matches are kept in <span className="font-mono text-text-3 break-all">{settings?.rejectedPath ?? 'rejected/'}</span>
            </div>
          </div>
          <button
            onClick={() => importMutation.mutate()}
            disabled={importing}
            className="shrink-0 px-4 py-2 text-xs font-medium tracking-wide uppercase rounded transition-all
              bg-accent/15 text-accent border border-accent/30 hover:bg-accent/25
              disabled:opacity-50 disabled:cursor-not-allowed"
            title="Hash every file in the import folder and match it to the catalog"
          >
            {importing ? 'Ingesting…' : 'Ingest'}
          </button>
        </div>

        <div className="flex items-center justify-between gap-2">
          <span className="text-[10px] tracking-wide uppercase shrink-0">
            <span className="text-ps-triangle">{matchedCount} matched</span>
            <span className="mx-1.5 text-border-light/40">·</span>
            <span className="text-[#e06070]">{rejectedCount} rejected</span>
          </span>
          <div className="flex items-center gap-1">
            {FILTERS.map(f => (
              <button
                key={f.value}
                onClick={() => setFilter(f.value)}
                className={`px-2.5 py-1 text-[10px] tracking-wide uppercase rounded transition-colors whitespace-nowrap ${
                  filter === f.value
                    ? 'bg-accent/15 text-accent border border-accent/30'
                    : 'text-text-4 hover:text-text-3 border border-transparent'
                }`}
              >
                {f.label}
              </button>
            ))}
          </div>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto">
        {visible.map(e => (
          <EventItem key={e.id} event={e} />
        ))}
        {visible.length === 0 && (
          <div className="flex flex-col items-center justify-center h-40 text-text-4 text-sm tracking-wide gap-1">
            <span>{importing ? 'Ingesting…' : 'No import results yet'}</span>
            {!importing && filter === 'all' && (
              <span className="text-[11px] text-text-4/70">Drop files into the import folder and press Ingest</span>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
