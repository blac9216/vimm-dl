import { useMemo } from 'react'
import { useData, useConvertPs3 } from '../../api/queries'
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

export function CompletedPanel({ showEventsLink, onViewEvents }: CompletedPanelProps) {
  const { data } = useData()
  const convertAllMutation = useConvertPs3()

  const history = useMemo(() => data?.history ?? [], [data?.history])
  const groups = useMemo(() => groupByGame(history), [history])

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between px-3 sm:px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase">
          {history.length} completed
        </span>
        <button
          onClick={() => convertAllMutation.mutate(undefined)}
          disabled={convertAllMutation.isPending}
          className="text-[10px] text-ps-cross/50 hover:text-[#7eb3e0] tracking-wide uppercase
            transition-colors disabled:opacity-40"
        >
          Convert All PS3
        </button>
      </div>

      <div className="flex-1 overflow-y-auto">
        {groups.map(g => g.items.length === 1 ? (
          <HistoryItem key={g.items[0].id} item={g.items[0]}
            showEventsLink={showEventsLink} onViewEvents={onViewEvents} />
        ) : (
          <HistoryGroup key={g.key} items={g.items}
            showEventsLink={showEventsLink} onViewEvents={onViewEvents} />
        ))}
        {history.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
            No completed downloads
          </div>
        )}
      </div>
    </div>
  )
}
