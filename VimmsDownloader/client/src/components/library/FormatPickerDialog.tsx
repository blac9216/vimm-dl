import { useState } from 'react'
import type { CatalogVimmFormat } from '../../types/api'
import { fmtBytes } from '../../lib/format'

/// Pick a Vimm download format for a game that Vimm offers in more than one (e.g. PS3 JB Folder vs
/// .dec.iso). The chosen format is what gets downloaded when the game is fetched from Vimm (archive.org
/// is still preferred when a set provides it). Styling mirrors SetsDialog.
export function FormatPickerDialog({ gameName, formats, busy, onPick, onClose }: {
  gameName: string
  formats: CatalogVimmFormat[]
  busy: boolean
  onPick: (alt: number) => void
  onClose: () => void
}) {
  const [alt, setAlt] = useState(formats[0]?.alt ?? 0)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div onClick={e => e.stopPropagation()}
        className="w-full max-w-sm bg-surface border border-border/40 rounded-lg shadow-xl flex flex-col">
        <div className="flex items-center justify-between px-4 py-3 border-b border-border/30">
          <span className="text-sm font-medium text-text">Download format</span>
          <button onClick={onClose} className="text-text-4 hover:text-text text-lg leading-none px-1">×</button>
        </div>

        <div className="px-4 py-3 border-b border-border/20">
          <div className="text-xs text-text-2 truncate" title={gameName}>{gameName}</div>
          <div className="text-[10px] text-text-4 mt-0.5">Used when the download comes from Vimm.</div>
        </div>

        <div className="px-4 py-2 flex flex-col gap-1">
          {formats.map(f => (
            <label key={f.alt}
              className={`flex items-center gap-2 px-2 py-1.5 rounded cursor-pointer border text-sm ${
                alt === f.alt ? 'bg-ps-cross/15 border-ps-cross/30' : 'border-transparent hover:bg-surface-2/40'}`}>
              <input type="radio" name="vimm-format" checked={alt === f.alt} onChange={() => setAlt(f.alt)}
                className="accent-[#7eb3e0]" />
              <span className="flex-1 text-text-2">{f.label}</span>
              {f.sizeBytes > 0 && (
                <span className="text-[10px] text-text-4 font-mono tabular-nums">{f.sizeText || fmtBytes(f.sizeBytes)}</span>
              )}
            </label>
          ))}
        </div>

        <div className="px-4 py-3 border-t border-border/30 flex items-center justify-end gap-2">
          <button onClick={onClose} className="px-3 py-1 text-sm rounded text-text-4 hover:text-text">Cancel</button>
          <button onClick={() => onPick(alt)} disabled={busy}
            className="px-4 py-1 text-sm rounded bg-ps-cross/20 text-[#7eb3e0] border border-ps-cross/30
              hover:bg-ps-cross/30 disabled:opacity-40">
            {busy ? 'Queueing…' : 'Download'}
          </button>
        </div>
      </div>
    </div>
  )
}
