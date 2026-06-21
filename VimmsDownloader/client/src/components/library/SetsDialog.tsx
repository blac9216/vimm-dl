import { useState } from 'react'
import { useCatalogSets, useAddSet, useUpdateSet, useDeleteSet, useCatalogConsoles } from '../../api/queries'
import type { CatalogSet } from '../../types/api'

/// Manage download "sets" — a named, per-console list of archive.org links (one set can span
/// several archive.org items). Games are resolved by matching their name against the files behind
/// every link in the console's sets.
export function SetsDialog({ onClose }: { onClose: () => void }) {
  const { data: sets } = useCatalogSets()
  const { data: consoles } = useCatalogConsoles()
  const addSet = useAddSet()
  const updateSet = useUpdateSet()
  const deleteSet = useDeleteSet()

  const [editingId, setEditingId] = useState<number | null>(null)
  const [name, setName] = useState('')
  const [selectedConsole, setSelectedConsole] = useState('')
  const [linksText, setLinksText] = useState('')

  const links = linksText.split('\n').map(l => l.trim()).filter(Boolean)
  const saving = addSet.isPending || updateSet.isPending
  const canSave = name.trim().length > 0 && !!selectedConsole && links.length > 0 && !saving

  function reset() { setEditingId(null); setName(''); setSelectedConsole(''); setLinksText('') }

  function save() {
    if (!canSave) return
    const payload = { name: name.trim(), console: selectedConsole, links }
    if (editingId != null) updateSet.mutate({ id: editingId, ...payload }, { onSuccess: reset })
    else addSet.mutate(payload, { onSuccess: reset })
  }

  function edit(s: CatalogSet) {
    setEditingId(s.id)
    setName(s.name)
    setSelectedConsole(s.console)
    setLinksText(s.links.join('\n'))
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div onClick={e => e.stopPropagation()}
        className="w-full max-w-lg bg-surface border border-border/40 rounded-lg shadow-xl flex flex-col max-h-[85vh]">
        <div className="flex items-center justify-between px-4 py-3 border-b border-border/30">
          <span className="text-sm font-medium text-text">Download sources</span>
          <button onClick={onClose} className="text-text-4 hover:text-text text-lg leading-none px-1">×</button>
        </div>

        <div className="px-4 py-3 text-[11px] text-text-4 border-b border-border/20 leading-relaxed">
          A set is a named, per-console list of archive.org links (e.g.
          <span className="font-mono text-text-3"> https://archive.org/download/sony_playstation_part1</span>).
          Games are matched by name across every link in the set.
        </div>

        <div className="flex-1 overflow-y-auto">
          {sets && sets.length > 0 ? sets.map(s => (
            <div key={s.id} className="flex items-center gap-2 px-4 py-2 border-b border-border/10 text-sm">
              <span className="text-[10px] uppercase text-accent/70 w-14 shrink-0">{s.console}</span>
              <div className="flex-1 min-w-0">
                <div className="text-text-2 truncate" title={s.name}>{s.name}</div>
                <div className="text-[10px] text-text-4">{s.links.length} link{s.links.length === 1 ? '' : 's'}</div>
              </div>
              <button onClick={() => edit(s)}
                className="text-[11px] text-text-4 hover:text-accent shrink-0">Edit</button>
              <button onClick={() => deleteSet.mutate(s.id)}
                className="text-[11px] text-text-4 hover:text-[#e06070] shrink-0">Remove</button>
            </div>
          )) : (
            <div className="px-4 py-6 text-center text-text-4 text-sm">No sources yet</div>
          )}
        </div>

        <div className="px-4 py-3 border-t border-border/30 flex flex-col gap-2">
          <div className="flex gap-2">
            <input value={name} onChange={e => setName(e.target.value)}
              placeholder="Set name (e.g. PS1 Archive)"
              className="flex-1 min-w-0 bg-surface-2/60 border border-border/40 rounded px-2 py-1 text-sm text-text
                placeholder:text-text-4 focus:outline-none focus:border-accent/40" />
            <select value={selectedConsole} onChange={e => setSelectedConsole(e.target.value)}
              className="bg-surface-2/60 border border-border/40 rounded px-2 py-1 text-sm text-text shrink-0
                focus:outline-none focus:border-accent/40">
              <option value="">Console…</option>
              {consoles?.map(c => <option key={c.console} value={c.console}>{c.console}</option>)}
            </select>
          </div>
          <textarea value={linksText} onChange={e => setLinksText(e.target.value)}
            placeholder="One archive.org link per line"
            rows={3}
            className="bg-surface-2/60 border border-border/40 rounded px-2 py-1 text-xs font-mono text-text
              placeholder:text-text-4 focus:outline-none focus:border-accent/40 resize-y" />
          <div className="flex items-center gap-2">
            <span className="text-[10px] text-text-4 flex-1">{links.length} link{links.length === 1 ? '' : 's'}{editingId != null ? ' · editing' : ''}</span>
            {editingId != null && (
              <button onClick={reset} className="px-3 py-1 text-sm rounded text-text-4 hover:text-text shrink-0">Cancel</button>
            )}
            <button onClick={save} disabled={!canSave}
              className="px-4 py-1 text-sm rounded bg-ps-cross/20 text-[#7eb3e0] border border-ps-cross/30
                hover:bg-ps-cross/30 disabled:opacity-40 shrink-0">
              {editingId != null ? 'Save' : 'Add'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
