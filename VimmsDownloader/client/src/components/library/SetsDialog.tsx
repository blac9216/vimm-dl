import { useState } from 'react'
import { useCatalogSets, useAddSet, useDeleteSet, useCatalogConsoles } from '../../api/queries'

/// Manage download "sets" — per-console archive.org item ids whose files are that console's ROMs.
export function SetsDialog({ onClose }: { onClose: () => void }) {
  const { data: sets } = useCatalogSets()
  const { data: consoles } = useCatalogConsoles()
  const addSet = useAddSet()
  const deleteSet = useDeleteSet()

  const [selectedConsole, setSelectedConsole] = useState('')
  const [identifier, setIdentifier] = useState('')
  const [label, setLabel] = useState('')

  const trimmedId = identifier.trim()
  const isDuplicate = !!sets?.some(s => s.console === selectedConsole && s.identifier === trimmedId)
  const canAdd = !!selectedConsole && trimmedId.length > 0 && !isDuplicate && !addSet.isPending

  function add() {
    if (!canAdd) return
    addSet.mutate(
      { console: selectedConsole, identifier: trimmedId, label: label.trim() || undefined },
      { onSuccess: () => { setIdentifier(''); setLabel('') } },
    )
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div onClick={e => e.stopPropagation()}
        className="w-full max-w-lg bg-surface border border-border/40 rounded-lg shadow-xl flex flex-col max-h-[80vh]">
        <div className="flex items-center justify-between px-4 py-3 border-b border-border/30">
          <span className="text-sm font-medium text-text">Download sources</span>
          <button onClick={onClose} className="text-text-4 hover:text-text text-lg leading-none px-1">×</button>
        </div>

        <div className="px-4 py-3 text-[11px] text-text-4 border-b border-border/20 leading-relaxed">
          A source is an archive.org item id whose files are a console's ROMs (e.g.
          <span className="font-mono text-text-3"> ef_gba_no-intro_2024-02-21</span>). Games are matched by name.
        </div>

        <div className="flex-1 overflow-y-auto">
          {sets && sets.length > 0 ? sets.map(s => (
            <div key={s.id} className="flex items-center gap-2 px-4 py-2 border-b border-border/10 text-sm">
              <span className="text-[10px] uppercase text-accent/70 w-14 shrink-0">{s.console}</span>
              <div className="flex-1 min-w-0">
                <div className="text-text-2 truncate font-mono text-xs" title={s.identifier}>{s.identifier}</div>
                {s.label && <div className="text-[10px] text-text-4">{s.label}</div>}
              </div>
              <button onClick={() => deleteSet.mutate(s.id)}
                className="text-[11px] text-text-4 hover:text-[#e06070] shrink-0">Remove</button>
            </div>
          )) : (
            <div className="px-4 py-6 text-center text-text-4 text-sm">No sources yet</div>
          )}
        </div>

        <div className="px-4 py-3 border-t border-border/30 flex flex-col gap-2">
          <div className="flex gap-2">
            <select value={selectedConsole} onChange={e => setSelectedConsole(e.target.value)}
              className="bg-surface-2/60 border border-border/40 rounded px-2 py-1 text-sm text-text shrink-0
                focus:outline-none focus:border-accent/40">
              <option value="">Console…</option>
              {consoles?.map(c => <option key={c.console} value={c.console}>{c.console}</option>)}
            </select>
            <input value={identifier} onChange={e => setIdentifier(e.target.value)}
              placeholder="archive.org item id"
              className="flex-1 min-w-0 bg-surface-2/60 border border-border/40 rounded px-2 py-1 text-sm text-text
                placeholder:text-text-4 focus:outline-none focus:border-accent/40" />
          </div>
          <div className="flex gap-2">
            <input value={label} onChange={e => setLabel(e.target.value)}
              placeholder="Label (optional)"
              className="flex-1 min-w-0 bg-surface-2/60 border border-border/40 rounded px-2 py-1 text-sm text-text
                placeholder:text-text-4 focus:outline-none focus:border-accent/40" />
            <button onClick={add} disabled={!canAdd}
              className="px-4 py-1 text-sm rounded bg-ps-cross/20 text-[#7eb3e0] border border-ps-cross/30
                hover:bg-ps-cross/30 disabled:opacity-40 shrink-0">
              Add
            </button>
          </div>
          {isDuplicate && <div className="text-[10px] text-[#e0a060]">That source is already added for {selectedConsole}.</div>}
        </div>
      </div>
    </div>
  )
}
