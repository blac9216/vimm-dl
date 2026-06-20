import { useState } from 'react'
import { useAddToQueue, useSettings, useSources } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { parseUrls } from '../../lib/format'
import { DuplicateDialog } from '../shared/DuplicateDialog'
import type { DuplicateInfo } from '../../types/api'

export function Toolbar() {
  const [text, setText] = useState('')
  const [source, setSource] = useState('vimm')
  const [duplicates, setDuplicates] = useState<DuplicateInfo[] | null>(null)
  const [pendingUrls, setPendingUrls] = useState<string[]>([])
  const addMutation = useAddToQueue()
  const { state, connection } = useDownload()
  const { data: config } = useSettings()
  const { data: sources } = useSources()

  // Vimm uses the configured PS3 format; other sources ignore it (resolve to 0).
  const addFormat = source === 'vimm' ? config?.ps3DefaultFormat ?? 1 : 0
  const sourceName = sources?.find(s => s.id === source)?.displayName ?? "Vimm's Lair"

  async function handleAdd() {
    const urls = parseUrls(text)
    if (urls.length === 0) return

    const result = await addMutation.mutateAsync({ urls, format: addFormat, source })

    if (result?.duplicates && result.duplicates.length > 0) {
      setDuplicates(result.duplicates)
      setPendingUrls(urls)
      return
    }

    setText('')
    if (!state.running && connection) {
      connection.invoke('StartDownload', config?.activePath ?? null)
    }
  }

  async function handleForceAdd() {
    if (pendingUrls.length === 0) return

    await addMutation.mutateAsync({ urls: pendingUrls, format: addFormat, force: true, source })
    setDuplicates(null)
    setPendingUrls([])
    setText('')

    if (!state.running && connection) {
      connection.invoke('StartDownload', config?.activePath ?? null)
    }
  }

  function handleCancelDuplicates() {
    setDuplicates(null)
    setPendingUrls([])
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.ctrlKey && e.key === 'Enter') {
      e.preventDefault()
      handleAdd()
    }
  }

  return (
    <>
      <div className="flex gap-2 sm:gap-3 px-3 sm:px-6 py-2 sm:py-2.5 border-b border-border/30">
        {sources && sources.length > 1 && (
          <select
            value={source}
            onChange={e => setSource(e.target.value)}
            title="Download source"
            className="bg-surface/80 border border-border/60 rounded px-2 py-1.5 text-sm text-text
              focus:outline-none focus:border-accent/40 transition-all min-h-[32px] shrink-0"
          >
            {sources.map(s => (
              <option key={s.id} value={s.id}>{s.displayName}</option>
            ))}
          </select>
        )}
        <textarea
          value={text}
          onChange={e => setText(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={source === 'vimm' ? 'Paste vault URLs here...' : `Paste ${sourceName} file URLs here...`}
          rows={1}
          className="flex-1 bg-surface/80 border border-border/60 rounded px-3 py-1.5 text-sm text-text
            placeholder:text-text-4 resize-none focus:outline-none focus:border-accent/40 focus:shadow-[0_0_12px_rgba(91,155,213,0.1)]
            transition-all min-h-[32px]"
        />
        {/* PS3 Cross (X) button = confirm/action = blue */}
        <button
          onClick={handleAdd}
          disabled={addMutation.isPending}
          className="px-5 py-1.5 bg-ps-cross/20 text-[#7eb3e0] border border-ps-cross/30 rounded text-sm
            font-medium hover:bg-ps-cross/30 hover:shadow-[0_0_16px_rgba(46,109,180,0.2)]
            transition-all disabled:opacity-40"
        >
          Add
        </button>
      </div>

      {duplicates && (
        <DuplicateDialog
          duplicates={duplicates}
          onConfirm={handleForceAdd}
          onCancel={handleCancelDuplicates}
          isPending={addMutation.isPending}
        />
      )}
    </>
  )
}
