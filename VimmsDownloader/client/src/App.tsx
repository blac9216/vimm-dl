import { useState, useMemo, useReducer, useEffect, useCallback } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Header } from './components/layout/Header'
import { Toolbar } from './components/layout/Toolbar'
import { ControlBar } from './components/layout/ControlBar'
import { TabBar, type Tab } from './components/layout/TabBar'
import { StatusBar } from './components/layout/StatusBar'
import { ErrorBanner } from './components/shared/ErrorBanner'
import { UpdateBanner } from './components/shared/UpdateBanner'
import { ActivePanel } from './components/active/ActivePanel'
import { CompletedPanel } from './components/completed/CompletedPanel'
import { SyncPanel } from './components/sync/SyncPanel'
import { LibraryPanel } from './components/library/LibraryPanel'
import { SettingsPanel } from './components/layout/SettingsPanel'
import { EventsPanel } from './components/events/EventsPanel'
import { MetricsPanel } from './components/metrics/MetricsPanel'
import { DownloadContext, downloadReducer, initialState } from './hooks/useDownloadState'
import { useSignalR } from './hooks/useSignalR'
import { useData, useSettings } from './api/queries'
import { parseProgress } from './lib/format'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

function AppContent() {
  const [activeTab, setActiveTab] = useState<Tab>('active')
  const [eventsItemFilter, setEventsItemFilter] = useState<string | null>(null)
  const [state, dispatch] = useReducer(downloadReducer, initialState)
  const connection = useSignalR(dispatch)
  const { data } = useData()
  const { data: settings } = useSettings()

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
  }, [data?.isRunning, data?.isPaused, data?.currentUrl]) // eslint-disable-line react-hooks/exhaustive-deps


  // Update document title with progress
  useEffect(() => {
    if (state.activeDlInfo && state.running) {
      document.title = `${state.activeDlInfo.pct} — ${state.activeDlInfo.filename}`
    } else {
      document.title = 'VIMM//DL'
    }
  }, [state.activeDlInfo, state.running])

  const queued = data?.queued ?? []
  const history = data?.history ?? []
  const convertingCount = Object.values(state.convStatuses).filter(
    s => ['queued', 'extracting', 'extracted', 'converting'].includes(s.phase)
  ).length

  const counts = {
    active: queued.length + convertingCount,
    completed: history.length,
    events: 0,
    sync: 0,
  }

  const hiddenTabs = useMemo(() => {
    const hidden = new Set<Tab>()
    if (!settings?.featureSync) hidden.add('sync')
    if (!settings?.featureEvents) hidden.add('events')
    if (!settings?.featureLibrary) hidden.add('library')
    return hidden
  }, [settings?.featureSync, settings?.featureEvents, settings?.featureLibrary])

  // If the active tab is hidden (its feature flag is off), fall back to 'active'. Derived during
  // render instead of synced through an effect, so the visible tab is always consistent in one pass.
  const effectiveTab = hiddenTabs.has(activeTab) ? 'active' : activeTab

  const handleViewEvents = useCallback((itemName: string) => {
    setEventsItemFilter(itemName)
    setActiveTab('events')
  }, [])

  const eventsEnabled = !!settings?.featureEvents

  return (
    <DownloadContext.Provider value={{ state, dispatch, connection }}>
      <div className="flex flex-col h-screen bg-bg">
        <Header />
        <UpdateBanner />
        <Toolbar />
        <ControlBar />
        <ErrorBanner />
        <TabBar activeTab={effectiveTab} onTabChange={setActiveTab} counts={counts} hiddenTabs={hiddenTabs} />
        <main className="flex-1 overflow-hidden">
          {effectiveTab === 'active' && <ActivePanel />}
          {effectiveTab === 'completed' && (
            <CompletedPanel
              showEventsLink={eventsEnabled}
              onViewEvents={handleViewEvents}
            />
          )}
          {effectiveTab === 'library' && <LibraryPanel />}
          {effectiveTab === 'metrics' && <MetricsPanel />}
          {effectiveTab === 'events' && (
            <EventsPanel
              itemFilter={eventsItemFilter}
              onClearItemFilter={() => setEventsItemFilter(null)}
            />
          )}
          {effectiveTab === 'sync' && <SyncPanel />}
          {effectiveTab === 'settings' && <SettingsPanel />}
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
