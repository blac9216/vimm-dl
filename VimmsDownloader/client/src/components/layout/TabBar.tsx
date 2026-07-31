// Tab order per the design handoff: Library · Downloads · Jobs · Metrics · Sync · Events · Import ·
// Settings. #258 merged the old `active`/`completed` tabs into a single `downloads` tab (its
// Active/Completed segmented sub-view lives inside `DownloadsPanel`); #259 added `jobs`, the home of
// the conversion rows plus the catalog background work.
export type Tab = 'downloads' | 'library' | 'jobs' | 'import' | 'metrics' | 'events' | 'sync' | 'settings'

interface TabBarProps {
  activeTab: Tab
  onTabChange: (tab: Tab) => void
  counts: { downloads: number; jobs: number; events: number; sync: number }
  hiddenTabs?: Set<Tab>
}

export function TabBar({ activeTab, onTabChange, counts, hiddenTabs }: TabBarProps) {
  const allTabs: { id: Tab; label: string; count: number }[] = [
    { id: 'library', label: 'Library', count: 0 },
    { id: 'downloads', label: 'Downloads', count: counts.downloads },
    { id: 'jobs', label: 'Jobs', count: counts.jobs },
    { id: 'metrics', label: 'Metrics', count: 0 },
    { id: 'sync', label: 'Sync', count: counts.sync },
    { id: 'events', label: 'Events', count: counts.events },
    { id: 'import', label: 'Import', count: 0 },
    { id: 'settings', label: 'Settings', count: 0 },
  ]

  const tabs = hiddenTabs ? allTabs.filter(t => !hiddenTabs.has(t.id)) : allTabs

  return (
    <div className="flex items-center gap-0.5 px-3 sm:px-4 pt-2 bg-surface-2/50 border-b border-border-section overflow-x-auto scrollbar-none">
      {tabs.map(tab => {
        const active = activeTab === tab.id
        return (
          <button
            key={tab.id}
            onClick={() => onTabChange(tab.id)}
            className={`relative pt-[11px] px-3 sm:px-[17px] pb-3 sm:pb-[13px] text-xs font-semibold tracking-[0.04em] uppercase transition-colors whitespace-nowrap ${
              active ? 'text-text-heading' : 'text-text-3 hover:text-text-2'
            }`}
          >
            {tab.label}
            {tab.count > 0 && (
              <span className="ml-1.5 font-mono text-[10px] normal-case tracking-normal px-1.5 py-px rounded-full bg-info-bg text-link-3">
                {tab.count}
              </span>
            )}
            {/* Gradient underline on the active tab */}
            {active && (
              <div className="absolute -bottom-px left-3 right-3 h-[2.5px] rounded-[3px] bg-gradient-to-r from-accent to-accent-2
                shadow-[0_0_10px_rgba(111,141,255,0.6)]" />
            )}
          </button>
        )
      })}
    </div>
  )
}
