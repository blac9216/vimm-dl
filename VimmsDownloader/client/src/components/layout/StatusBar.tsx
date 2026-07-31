import { useDownload } from '../../hooks/useDownloadState'
import { useData } from '../../api/queries'

export function StatusBar() {
  const { state } = useDownload()
  const { data } = useData()

  const queued = data?.queued.length ?? 0
  const completed = data?.history.length ?? 0
  // Real number of concurrent downloads (EPIC #113 / A2), not a hardcoded 1.
  const downloading = state.activeDownloads.length > 0
    ? state.activeDownloads.length
    : (state.running && !state.paused ? 1 : 0)

  return (
    <footer className="flex items-center justify-between px-3 sm:px-[22px] py-2 bg-surface-2/60 border-t border-border-section
      text-[10px] tracking-[0.06em] text-text-4">
      <div className="flex items-center gap-2">
        <span>{queued} queued</span>
        <span className="text-[#2a2848]">&middot;</span>
        <span>{downloading} downloading</span>
        <span className="text-[#2a2848]">&middot;</span>
        <span>{completed} completed</span>
      </div>
      <div className="uppercase tracking-[0.14em]">
        VIMM // DL
      </div>
    </footer>
  )
}
