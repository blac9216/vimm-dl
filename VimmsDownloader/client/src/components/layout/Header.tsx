import { useDownload } from '../../hooks/useDownloadState'
import { useVersion } from '../../api/queries'

const CONNECTION_LABEL: Record<string, string> = {
  connected: 'Connected',
  reconnecting: 'Reconnecting',
  disconnected: 'Disconnected',
}

// The global header per the design handoff (#256): gradient "V" logo mark, "VIMM/DL" wordmark with a
// violet slash, a mono tagline, and the app version + SignalR connection dot on the right.
export function Header() {
  const { state } = useDownload()
  const { data: version } = useVersion()

  const connected = state.connectionState === 'connected'
  const dotColor = connected ? 'bg-success' : state.connectionState === 'reconnecting' ? 'bg-warning' : 'bg-error'
  const dotGlow = connected ? 'shadow-[0_0_8px_#3ee0a0]' : ''

  return (
    <header className="xmb-glass flex items-center justify-between px-3 sm:px-[22px] py-2.5 sm:py-[13px] border-b border-border-section">
      <div className="flex items-center min-w-0">
        <div className="w-[27px] h-[27px] shrink-0 rounded-lg bg-gradient-to-br from-accent to-accent-2
          shadow-[0_4px_14px_rgba(111,141,255,0.4)] flex items-center justify-center">
          <span className="text-[13px] font-extrabold text-bg leading-none">V</span>
        </div>
        <h1 className="ml-2 font-sans text-sm font-bold tracking-[0.2em] uppercase text-text-heading whitespace-nowrap">
          VIMM<span className="text-link mx-px">/</span>DL
        </h1>
        <span className="hidden sm:inline ml-1.5 font-mono text-[10px] tracking-[0.05em] text-text-4 truncate">
          self-hosted ROM toolkit
        </span>
      </div>
      <div className="flex items-center gap-3 sm:gap-4 text-[10px] tracking-[0.16em] uppercase text-text-3 shrink-0">
        {version?.current && (
          <span className="hidden sm:inline font-mono normal-case tracking-normal">v{version.current}</span>
        )}
        <div className="flex items-center gap-1.5">
          <div className={`w-1.5 h-1.5 rounded-full ${dotColor} ${dotGlow}`} />
          <span>{CONNECTION_LABEL[state.connectionState] ?? state.connectionState}</span>
        </div>
      </div>
    </header>
  )
}
