import { useState, useMemo, useReducer, useEffect, useCallback } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Header } from './components/layout/Header'
import { TabBar, type Tab } from './components/layout/TabBar'
import { StatusBar } from './components/layout/StatusBar'
import { ErrorBanner } from './components/shared/ErrorBanner'
import { UpdateBanner } from './components/shared/UpdateBanner'
import { DownloadsPanel } from './components/downloads/DownloadsPanel'
import { JobsPanel } from './components/jobs/JobsPanel'
import { SyncPanel } from './components/sync/SyncPanel'
import { LibraryPanel } from './components/library/LibraryPanel'
import { ImportPanel } from './components/import/ImportPanel'
import { SettingsPanel } from './components/layout/SettingsPanel'
import { EventsPanel } from './components/events/EventsPanel'
import { MetricsPanel } from './components/metrics/MetricsPanel'
import { DownloadContext, downloadReducer, initialState } from './hooks/useDownloadState'
import { useSignalR } from './hooks/useSignalR'
import { useData, useJobs, useSettings } from './api/queries'
import { parseProgress } from './lib/format'
import { countActiveJobs } from './lib/jobs'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

function AppContent() {
  // null = the user hasn't picked a tab yet, so the settings-driven default (below) applies.
  const [userTab, setUserTab] = useState<Tab | null>(null)
  const [eventsItemFilter, setEventsItemFilter] = useState<string | null>(null)
  const [state, dispatch] = useReducer(downloadReducer, initialState)
  const connection = useSignalR(dispatch)
  const { data } = useData()
  const { data: jobs } = useJobs()
  const { data: settings, isLoading: settingsLoading } = useSettings()

  // Restore running state from /api/data response
  useEffect(() => {
    if (!data) return
    dispatch({
      type: 'SET_RUNNING',
      running: data.isRunning,
      paused: data.isPaused,
    })
    if (data.currentUrl) {
      dispatch({ type: 'STATUS', url: `Processing: ${data.currentUrl}` })
    }
    if (data.progress) {
      const info = parseProgress(data.progress)
      if (info) dispatch({ type: 'PROGRESS', info })
    }
    // Per-download state (EPIC #113 / A2) — the Active tab renders one progress row per item.
    dispatch({ type: 'SET_ACTIVE', active: data.activeDownloads ?? [] })
  }, [data?.isRunning, data?.isPaused, data?.currentUrl, data?.activeDownloads]) // eslint-disable-line react-hooks/exhaustive-deps


  // Update document title with progress
  useEffect(() => {
    if (state.activeDlInfo && state.running) {
      document.title = `${state.activeDlInfo.pct} — ${state.activeDlInfo.filename}`
    } else {
      document.title = 'VIMM//DL'
    }
  }, [state.activeDlInfo, state.running])

  const queued = data?.queued ?? []

  // Pills per the design handoff "Global Shell": Downloads = queue length, Jobs = active job count
  // (running background jobs + non-terminal conversions). The Jobs feed is polled app-wide so the
  // pill stays live while the user is on another tab.
  const counts = {
    downloads: queued.length,
    jobs: countActiveJobs(jobs, state.convStatuses),
    events: 0,
    sync: 0,
  }

  const hiddenTabs = useMemo(() => {
    const hidden = new Set<Tab>()
    if (!settings?.featureSync) hidden.add('sync')
    if (!settings?.featureEvents) hidden.add('events')
    if (!settings?.featureLibrary) hidden.add('library')
    if (!settings?.featureImport) hidden.add('import')
    return hidden
  }, [settings?.featureSync, settings?.featureEvents, settings?.featureLibrary, settings?.featureImport])

  // Default/fallback tab: Library when the beta flag is on, else Downloads (#256/#258). Used both before the
  // user has picked a tab (userTab === null, e.g. while settings are still loading) and whenever the
  // currently-picked tab is hidden (its feature flag turned off). Derived during render — no effect
  // needed, so there's no cascading-render setState-in-effect.
  const defaultTab: Tab = settings?.featureLibrary ? 'library' : 'downloads'
  const effectiveTab = userTab !== null && !hiddenTabs.has(userTab) ? userTab : defaultTab

  // Avoid a Downloads->Library flash on first load: until settings resolve, we don't know the real
  // default, so hold off mounting a content panel rather than briefly mounting the wrong one.
  const tabPending = userTab === null && settingsLoading

  const handleViewEvents = useCallback((itemName: string) => {
    setEventsItemFilter(itemName)
    setUserTab('events')
  }, [])

  const eventsEnabled = !!settings?.featureEvents

  return (
    <DownloadContext.Provider value={{ state, dispatch, connection }}>
      <div className="flex flex-col h-screen bg-bg">
        <Header />
        <UpdateBanner />
        <ErrorBanner />
        <TabBar activeTab={effectiveTab} onTabChange={setUserTab} counts={counts} hiddenTabs={hiddenTabs} />
        <main className="flex-1 overflow-hidden">
          {!tabPending && effectiveTab === 'downloads' && (
            <DownloadsPanel
              showEventsLink={eventsEnabled}
              onViewEvents={handleViewEvents}
            />
          )}
          {!tabPending && effectiveTab === 'jobs' && <JobsPanel />}
          {!tabPending && effectiveTab === 'library' && <LibraryPanel />}
          {!tabPending && effectiveTab === 'import' && <ImportPanel />}
          {!tabPending && effectiveTab === 'metrics' && <MetricsPanel />}
          {!tabPending && effectiveTab === 'events' && (
            <EventsPanel
              itemFilter={eventsItemFilter}
              onClearItemFilter={() => setEventsItemFilter(null)}
            />
          )}
          {!tabPending && effectiveTab === 'sync' && <SyncPanel />}
          {!tabPending && effectiveTab === 'settings' && <SettingsPanel />}
        </main>
        <StatusBar />
      </div>
    </DownloadContext.Provider>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppContent />
    </QueryClientProvider>
  )
}
