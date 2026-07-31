import { useRef, useState } from 'react'
import { useDownload } from '../../hooks/useDownloadState'
import { useSettings, useClearQueue, useImportQueue, useConvertPs3, exportQueue } from '../../api/queries'
import { ActivePanel } from '../active/ActivePanel'
import { CompletedPanel } from '../completed/CompletedPanel'

type SubView = 'active' | 'completed'

interface DownloadsPanelProps {
  showEventsLink?: boolean
  onViewEvents?: (itemName: string) => void
}

const transportBase = 'px-3.5 py-2 text-xs font-semibold rounded-lg border transition-colors whitespace-nowrap'
const transportDisabled = 'border-border-section bg-surface/40 text-text-disabled cursor-default'

/**
 * The merged Downloads tab (#258): an Active/Completed segmented sub-view plus the engine transport
 * that used to live in the global ControlBar. There is no URL paste bar any more — downloads are
 * queued from the Library; queue JSON export/import (in the overflow menu) is the only other path.
 */
export function DownloadsPanel({ showEventsLink, onViewEvents }: DownloadsPanelProps) {
  const [sub, setSub] = useState<SubView>('active')
  const [menuOpen, setMenuOpen] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const { state, dispatch, connection } = useDownload()
  const { data: config } = useSettings()
  const clearMutation = useClearQueue()
  const importMutation = useImportQueue()
  const convertAllMutation = useConvertPs3()

  // Enablement matrix (design handoff §2): Start when stopped or paused, Pause when running and not
  // paused, Stop when running or paused. Same SignalR invokes as the old ControlBar.
  const canStart = !state.running || state.paused
  const canPause = state.running && !state.paused
  const canStop = state.running || state.paused

  function handleStart() {
    if (!connection) return
    dispatch({ type: 'SET_RUNNING', running: true, paused: false })
    connection.invoke('StartDownload', config?.activePath ?? null)
  }

  function handlePause() {
    if (!connection) return
    dispatch({ type: 'SET_RUNNING', running: false, paused: true })
    connection.invoke('PauseDownload')
  }

  function handleStop() {
    if (!connection) return
    dispatch({ type: 'DONE' })
    connection.invoke('StopDownload')
  }

  async function handleExport() {
    setMenuOpen(false)
    const items = await exportQueue()
    const blob = new Blob([JSON.stringify(items, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'queue-export.json'
    a.click()
    URL.revokeObjectURL(url)
  }

  async function handleImport(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    try {
      const text = await file.text()
      const items = JSON.parse(text)
      const result = await importMutation.mutateAsync(items)
      if (result.added > 0 && !state.running && connection) {
        connection.invoke('StartDownload', config?.activePath ?? null)
      }
    } catch {
      // surfaced by the mutation / ErrorBanner
    }
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  return (
    <div className="flex flex-col h-full">
      {/* flex-wrap (not overflow-x) so the narrow-screen toolbar reflows without clipping the menu */}
      <div className="flex flex-wrap items-center gap-2 sm:gap-3.5 px-3 sm:px-[22px] py-2.5 sm:py-3
        bg-surface-2/40 border-b border-border-section">
        {/* Active / Completed segmented switcher */}
        <div className="flex gap-[5px] shrink-0 p-1 rounded-[11px] bg-input border border-border">
          {(['active', 'completed'] as SubView[]).map(id => (
            <button
              key={id}
              onClick={() => setSub(id)}
              className={`px-3 sm:px-4 py-[7px] text-xs font-semibold rounded-lg transition-colors ${
                sub === id
                  ? 'bg-gradient-to-br from-accent to-accent-2 text-white'
                  : 'text-text-3 hover:text-text-2'
              }`}
            >
              {id === 'active' ? 'Active' : 'Completed'}
            </button>
          ))}
        </div>

        <div className="flex-1" />

        <span className="hidden md:inline text-[10px] uppercase tracking-[0.1em] text-text-4 shrink-0">
          Engine
        </span>

        {/* Start / Resume — green */}
        <button
          onClick={handleStart}
          disabled={!canStart}
          className={`${transportBase} shrink-0 ${canStart
            ? 'border-success-border bg-success-bg text-success hover:bg-success/25'
            : transportDisabled}`}
        >
          &#9654; {state.paused ? 'Resume' : 'Start'}
        </button>

        {/* Pause — violet */}
        <button
          onClick={handlePause}
          disabled={!canPause}
          className={`${transportBase} shrink-0 ${canPause
            ? 'border-paused-border bg-paused-bg text-paused hover:bg-paused/25'
            : transportDisabled}`}
        >
          &#9208; Pause
        </button>

        {/* Stop — red */}
        <button
          onClick={handleStop}
          disabled={!canStop}
          className={`${transportBase} shrink-0 ${canStop
            ? 'border-error-border bg-error-bg text-error hover:bg-error/25'
            : transportDisabled}`}
        >
          &#9209; Stop
        </button>

        {/* Clear — neutral */}
        <button
          onClick={() => clearMutation.mutate()}
          className={`${transportBase} shrink-0 border-border-light bg-surface/50 text-text-2 hover:bg-surface-3/60 hover:text-text-body`}
        >
          Clear
        </button>

        {/* Overflow: queue JSON export/import (the only non-Library restore path) + convert all */}
        <div className="relative shrink-0">
          <button
            onClick={() => setMenuOpen(v => !v)}
            title="More actions"
            aria-label="More actions"
            className={`${transportBase} border-border-light bg-surface/50 text-text-2 hover:bg-surface-3/60 hover:text-text-body`}
          >
            &#8943;
          </button>
          {menuOpen && (
            <>
              <div className="fixed inset-0 z-10" onClick={() => setMenuOpen(false)} />
              <div className="absolute right-0 top-full mt-1.5 z-20 w-52 py-1 rounded-xl
                bg-card border border-border shadow-[0_14px_40px_rgba(0,0,0,0.45)]">
                <MenuItem onClick={handleExport}>Export queue (JSON)</MenuItem>
                <MenuItem onClick={() => { setMenuOpen(false); fileInputRef.current?.click() }}>
                  Import queue (JSON)
                </MenuItem>
                <div className="my-1 border-t border-border-inner" />
                <MenuItem
                  disabled={convertAllMutation.isPending}
                  onClick={() => { setMenuOpen(false); convertAllMutation.mutate(undefined) }}
                >
                  Convert all PS3
                </MenuItem>
              </div>
            </>
          )}
          <input ref={fileInputRef} type="file" accept=".json" onChange={handleImport} className="hidden" />
        </div>
      </div>

      <div className="flex-1 overflow-hidden">
        {sub === 'active'
          ? <ActivePanel />
          : <CompletedPanel showEventsLink={showEventsLink} onViewEvents={onViewEvents} />}
      </div>
    </div>
  )
}

function MenuItem({ children, onClick, disabled }: {
  children: React.ReactNode
  onClick: () => void
  disabled?: boolean
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      className="w-full text-left px-3.5 py-2 text-xs text-text-2 hover:bg-card-hover hover:text-text-body
        disabled:text-text-disabled disabled:hover:bg-transparent transition-colors"
    >
      {children}
    </button>
  )
}
