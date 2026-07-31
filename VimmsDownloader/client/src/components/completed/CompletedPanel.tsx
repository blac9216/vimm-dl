import { useMemo } from 'react'
import { useData } from '../../api/queries'
import { HistoryItem } from './HistoryItem'
import { HistoryGroup } from './HistoryGroup'
import type { HistoryItem as HistoryItemType } from '../../types/api'

interface CompletedPanelProps {
  showEventsLink?: boolean
  onViewEvents?: (itemName: string) => void
}

// Group completed items by catalog game (#151/B), preserving the newest-first order. Items with no
// gameId (legacy / unmatched) stay standalone (keyed by their own id), so nothing is mis-grouped.
function groupByGame(history: HistoryItemType[]): { key: string; items: HistoryItemType[] }[] {
  const order: string[] = []
  const map = new Map<string, HistoryItemType[]>()
  for (const item of history) {
    const key = item.gameId != null ? `g${item.gameId}` : `i${item.id}`
    const arr = map.get(key)
    if (arr) arr.push(item)
    else { map.set(key, [item]); order.push(key) }
  }
  return order.map(key => ({ key, items: map.get(key)! }))
}

/**
 * The Downloads → Completed sub-view (#258). The toolbar (sub-view switcher, engine transport,
 * "Convert all PS3") lives in the parent `DownloadsPanel`.
 */
export function CompletedPanel({ showEventsLink, onViewEvents }: CompletedPanelProps) {
  const { data } = useData()

  const history = useMemo(() => data?.history ?? [], [data?.history])
  const groups = useMemo(() => groupByGame(history), [history])

  return (
    <div className="h-full overflow-y-auto">
      {groups.map(g => g.items.length === 1 ? (
        <HistoryItem key={g.items[0].id} item={g.items[0]}
          showEventsLink={showEventsLink} onViewEvents={onViewEvents} />
      ) : (
        <HistoryGroup key={g.key} items={g.items}
          showEventsLink={showEventsLink} onViewEvents={onViewEvents} />
      ))}
      {history.length === 0 && (
        <div className="flex items-center justify-center h-[200px] px-6 text-center text-text-4 text-[13px]">
          Nothing downloaded yet
        </div>
      )}
    </div>
  )
}
