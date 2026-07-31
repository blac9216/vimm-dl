import { useState } from 'react'
import { useEvents } from '../../api/queries'
import { useSettings } from '../../api/queries'
import { EventLogRow } from './EventLogRow'

// Events tab (issue #262, design handoff "6. Events"): "N EVENTS" plus wrapping filter chips in the
// header, fixed-column rows below, and one expanded row at a time. Everything the tab already did is
// kept — the cross-tab item filter chip, 100-per-page "Load more", the 5s poll (useEvents), and the
// mobile card variant of the row.

interface FilterDef {
  label: string
  value: string
  flag?: 'featureSync'
}

const FILTERS: FilterDef[] = [
  { label: 'All', value: '' },
  { label: 'Status', value: 'download_status' },
  { label: 'Progress', value: 'download_progress' },
  { label: 'Downloads', value: 'download_completed' },
  { label: 'Errors', value: 'download_error' },
  { label: 'Pipeline', value: 'pipeline_status' },
  { label: 'Sync', value: 'sync', flag: 'featureSync' },
]

const CHIP = 'px-[11px] py-[5px] text-[10px] uppercase tracking-[0.05em] rounded-[7px] border whitespace-nowrap transition-colors'
const CHIP_ACTIVE = 'border-accent/35 bg-accent/[0.16] text-link-3'
const CHIP_IDLE = 'border-transparent bg-transparent text-text-faint hover:text-text-2'

const PAGE_SIZE = 100

interface EventsPanelProps {
  itemFilter?: string | null
  onClearItemFilter?: () => void
}

export function EventsPanel({ itemFilter, onClearItemFilter }: EventsPanelProps) {
  const [filter, setFilter] = useState('')
  const [itemSearch, setItemSearch] = useState(itemFilter ?? '')
  const [limit, setLimit] = useState(PAGE_SIZE)
  const [prevItemFilter, setPrevItemFilter] = useState(itemFilter)
  const [expandedId, setExpandedId] = useState<number | null>(null)
  const { data: settings } = useSettings()

  // Sync the external item filter into local state when it changes — adjusted during render
  // (React's "store info from previous renders" pattern) rather than synced through an effect.
  if (itemFilter !== prevItemFilter) {
    setPrevItemFilter(itemFilter)
    if (itemFilter) {
      setItemSearch(itemFilter)
      setLimit(PAGE_SIZE)
    }
  }

  const activeItem = itemSearch || undefined
  const { data } = useEvents(filter || undefined, activeItem, limit)

  const events = data?.events ?? []
  const total = data?.total ?? 0
  const hasMore = events.length < total

  const visibleFilters = FILTERS.filter(f => {
    if (!f.flag) return true
    return settings?.[f.flag] ?? false
  })

  function handleFilterChange(value: string) {
    setFilter(value)
    setLimit(PAGE_SIZE)
    setExpandedId(null)
  }

  function handleClearItem() {
    setItemSearch('')
    setExpandedId(null)
    onClearItemFilter?.()
  }

  return (
    <div className="flex flex-col h-full">
      <div className="shrink-0 flex flex-col gap-2 px-3 sm:px-[22px] py-[11px]
        border-b border-border-section bg-surface-2/40">
        <div className="flex items-center justify-between gap-3">
          <span className="shrink-0 text-[10px] uppercase tracking-[0.12em] text-text-3 whitespace-nowrap">
            {hasMore ? `${events.length} of ${total}` : total} {total === 1 ? 'event' : 'events'}
          </span>
          <div className="flex flex-wrap justify-end gap-1">
            {visibleFilters.map(f => (
              <button
                key={f.value}
                onClick={() => handleFilterChange(f.value)}
                className={`${CHIP} ${filter === f.value ? CHIP_ACTIVE : CHIP_IDLE}`}
              >
                {f.label}
              </button>
            ))}
          </div>
        </div>

        {itemSearch && (
          <div className="flex items-center gap-2">
            <span className="text-[10px] text-text-4">Filtered by:</span>
            <span className="min-w-0 truncate font-mono text-[10px] text-link-3 bg-accent/[0.12]
              px-2 py-0.5 rounded-md border border-accent/25">
              {itemSearch}
            </span>
            <button onClick={handleClearItem}
              className="shrink-0 text-[10px] text-text-4 hover:text-text-2 transition-colors">
              &times; Clear
            </button>
          </div>
        )}
      </div>

      <div className="flex-1 overflow-y-auto">
        {events.map(e => (
          <EventLogRow
            key={e.id}
            event={e}
            expanded={expandedId === e.id}
            onToggle={() => setExpandedId(id => (id === e.id ? null : e.id))}
          />
        ))}
        {hasMore && (
          <button
            onClick={() => setLimit(l => l + PAGE_SIZE)}
            className="w-full py-3 text-[10px] uppercase tracking-[0.1em] text-link/70 hover:text-link
              border-t border-border-row transition-colors"
          >
            Load more ({total - events.length} remaining)
          </button>
        )}
        {events.length === 0 && (
          <div className="flex items-center justify-center h-40 text-[13px] text-text-4 tracking-wide">
            {itemSearch ? `No events for "${itemSearch}"` : 'No events recorded'}
          </div>
        )}
      </div>
    </div>
  )
}
