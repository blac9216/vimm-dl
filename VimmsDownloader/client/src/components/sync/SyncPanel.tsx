import { useState } from 'react'
import { useSyncCompare, useSyncCopy, useSyncCancel } from '../../api/queries'
import { SyncDiskCard } from './SyncDiskCard'
import { SyncFileRow } from './SyncFileRow'
import type { SyncCompareResponse } from '../../types/api'

// The Sync tab (issue #261, design handoff "5. Sync"): mono path input + gradient Compare, a green
// Copy All once a compare turns up new files, side-by-side disk cards, a counts line and the file
// list. Cancel and the error strip are ours — `SyncCompareResponse.error` carries the
// drive-disconnected states, so both stay even though the spec omits them.

export function SyncPanel() {
  const [path, setPath] = useState('')
  const [syncData, setSyncData] = useState<SyncCompareResponse | null>(null)
  const compareMutation = useSyncCompare()
  const copyAllMutation = useSyncCopy()
  const cancelMutation = useSyncCancel()

  function handleCompare() {
    if (!path.trim()) return
    compareMutation.mutate(path, { onSuccess: data => setSyncData(data) })
  }

  const newFiles = syncData?.new ?? []
  const syncedFiles = syncData?.synced ?? []
  const targetOnlyFiles = syncData?.targetOnly ?? []

  const btn = 'px-3 sm:px-[17px] py-[9px] text-xs font-semibold rounded-[9px] shrink-0 whitespace-nowrap'

  return (
    <div className="flex flex-col h-full">
      <div className="flex flex-wrap items-center gap-2 sm:gap-2.5 px-3 sm:px-[22px] py-2.5 sm:py-3
        bg-surface-2/40 border-b border-border-section">
        <input
          type="text"
          value={path}
          onChange={e => setPath(e.target.value)}
          placeholder="Target path (e.g. /mnt/usb/PS3ISO)"
          className="flex-1 min-w-[160px] bg-input border border-border-input rounded-[9px]
            px-[13px] py-[9px] font-mono text-[13px] text-text-heading placeholder:text-text-4
            focus:outline-none focus:border-accent/40"
        />
        <button
          onClick={handleCompare}
          disabled={compareMutation.isPending || !path.trim()}
          className={`${btn} border-0 text-white bg-gradient-to-br from-accent to-accent-2
            shadow-[0_6px_22px_rgba(111,141,255,0.28)]
            disabled:opacity-40 disabled:shadow-none`}
        >
          {compareMutation.isPending ? 'Comparing…' : 'Compare'}
        </button>
        {newFiles.length > 0 && (
          <button
            onClick={() => copyAllMutation.mutate(undefined)}
            className={`${btn} border border-success-border bg-success-bg text-success hover:bg-success/25`}
          >
            Copy All
          </button>
        )}
        <button
          onClick={() => cancelMutation.mutate()}
          title="Cancel the running copy"
          className={`${btn} border border-border-light bg-surface/50 text-text-2
            hover:bg-error/10 hover:text-error hover:border-error/25`}
        >
          Cancel
        </button>
      </div>

      {syncData?.error && (
        <div className="mx-3 sm:mx-[22px] mt-2.5 p-2 rounded-lg bg-error/8 border border-error/20
          text-xs text-error">
          {syncData.error}
        </div>
      )}

      {syncData ? (
        <div className="flex flex-col flex-1 min-h-0">
          {(syncData.source || syncData.target) && (
            <div className="flex flex-col sm:flex-row gap-3.5 px-3 sm:px-[22px] py-[18px]">
              <SyncDiskCard label="Source" sub="completed/" info={syncData.source} variant="source" />
              <SyncDiskCard label="Target" sub={path} info={syncData.target} variant="target" />
            </div>
          )}

          <div className="px-3 sm:px-[22px] pb-2 pt-1 text-[10px]">
            <span className="text-success">{newFiles.length} new</span>
            <span className="text-text-4"> · </span>
            <span className="text-info">{syncedFiles.length} synced</span>
            <span className="text-text-4"> · </span>
            <span className="text-text-3">{targetOnlyFiles.length} target only</span>
          </div>

          <div className="flex-1 overflow-y-auto">
            {newFiles.map(f => <SyncFileRow key={`new:${f.name}`} file={f} status="new" />)}
            {syncedFiles.map(f => <SyncFileRow key={`sync:${f.name}`} file={f} status="synced" />)}
            {targetOnlyFiles.map(f => <SyncFileRow key={`tgt:${f.name}`} file={f} status="target-only" />)}
          </div>
        </div>
      ) : (
        <div className="flex-1 flex items-center justify-center px-6 text-center text-[13px] text-text-4">
          Enter a target path and Compare
        </div>
      )}
    </div>
  )
}
