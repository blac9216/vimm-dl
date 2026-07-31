import { useState } from 'react'
import { useData, useReorderQueue } from '../../api/queries'
import { QueueItem } from './QueueItem'

/**
 * The Downloads → Active sub-view (#258): the download queue only. Conversion/extraction rows moved
 * to the Jobs tab (#259) — this list is downloads, nothing else. The toolbar (sub-view switcher,
 * engine transport, queue JSON export/import) lives in the parent `DownloadsPanel`.
 */
export function ActivePanel() {
  const { data } = useData()
  const reorderMutation = useReorderQueue()

  const queued = data?.queued ?? []

  // --- Drag and drop state ---
  const [dragId, setDragId] = useState<number | null>(null)
  const [dragOverId, setDragOverId] = useState<number | null>(null)

  function handleDragStart(id: number) {
    setDragId(id)
  }

  function handleDragOver(e: React.DragEvent, id: number) {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'move'
    if (id !== dragId) setDragOverId(id)
  }

  function handleDragLeave() {
    setDragOverId(null)
  }

  function handleDrop(targetId: number) {
    if (dragId == null || dragId === targetId) {
      setDragId(null)
      setDragOverId(null)
      return
    }

    // Build new order: remove dragged item, insert before target
    const ids = queued.map(q => q.id)
    const fromIdx = ids.indexOf(dragId)
    const toIdx = ids.indexOf(targetId)
    if (fromIdx < 0 || toIdx < 0) return

    ids.splice(fromIdx, 1)
    ids.splice(toIdx, 0, dragId)

    reorderMutation.mutate(ids)
    setDragId(null)
    setDragOverId(null)
  }

  function handleDragEnd() {
    setDragId(null)
    setDragOverId(null)
  }

  return (
    <div className="h-full overflow-y-auto">
      {queued.map(item => (
        <QueueItem
          key={item.id}
          item={item}
          isDragging={dragId === item.id}
          isDragOver={dragOverId === item.id}
          onDragStart={() => handleDragStart(item.id)}
          onDragOver={(e) => handleDragOver(e, item.id)}
          onDragLeave={handleDragLeave}
          onDrop={() => handleDrop(item.id)}
          onDragEnd={handleDragEnd}
        />
      ))}
      {queued.length === 0 && (
        <div className="flex items-center justify-center h-[200px] px-6 text-center text-text-4 text-[13px]">
          Queue is empty &mdash; add games from the Library
        </div>
      )}
    </div>
  )
}
