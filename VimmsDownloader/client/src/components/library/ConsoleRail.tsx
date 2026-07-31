import type { CatalogConsole } from '../../types/api'
import { getConsoleColor, ALL_CONSOLE_COLOR } from '../../lib/consoleColors'

// Column A of the Library (issue #257, design handoff "1. Library (home)" -> Column A): the console
// rail. "All consoles" first, then only the consoles actually present in the catalog, each as a
// color code chip + full name + game count. Backend-is-king: the list is exactly what
// GET /api/catalog/consoles returns.
//
// `variant='strip'` is the sub-`sm` rendering — the same data as a horizontally scrolling chip strip
// above the game list, since a 212px rail does not fit a phone.

interface RailEntry {
  value: string       // '' = all consoles
  label: string
  code: string
  color: string
  count: number
  title: string
}

function buildEntries(consoles: CatalogConsole[] | undefined, totalInCatalog: number): RailEntry[] {
  const entries: RailEntry[] = [{
    value: '', label: 'All consoles', code: ALL_CONSOLE_COLOR.code, color: ALL_CONSOLE_COLOR.color,
    count: totalInCatalog, title: `All consoles — ${totalInCatalog.toLocaleString()} games`,
  }]
  for (const c of consoles ?? []) {
    const { code, color } = getConsoleColor(c.console)
    entries.push({
      value: c.console, label: c.console, code, color, count: c.gameCount,
      title: `${c.console} — ${c.ownedCount.toLocaleString()} owned of ${c.gameCount.toLocaleString()}`,
    })
  }
  return entries
}

export function ConsoleRail({ consoles, totalInCatalog, selected, onSelect, variant = 'rail' }: {
  consoles: CatalogConsole[] | undefined
  totalInCatalog: number
  selected: string
  onSelect: (console: string) => void
  variant?: 'rail' | 'strip'
}) {
  const entries = buildEntries(consoles, totalInCatalog)

  if (variant === 'strip') {
    return (
      <div className="flex sm:hidden gap-1.5 overflow-x-auto scrollbar-none px-3 py-2
        border-b border-border-subtle bg-surface-2/50 shrink-0">
        {entries.map(e => {
          const active = e.value === selected
          return (
            <button key={e.value || '__all'} onClick={() => onSelect(e.value)} title={e.title}
              className={`flex items-center gap-1.5 shrink-0 px-2 py-1 rounded-lg border ${
                active ? 'border-accent/40 bg-accent/20' : 'border-border bg-surface/50'}`}>
              <span className="text-[8.5px] font-bold font-mono tracking-[0.02em] px-1.5 py-[2px] rounded-[5px] text-bg"
                style={{ background: e.color, opacity: active ? 1 : 0.62 }}>{e.code}</span>
              <span className={`text-[9.5px] font-mono ${active ? 'text-link-2' : 'text-text-4'}`}>
                {e.count.toLocaleString()}
              </span>
            </button>
          )
        })}
      </div>
    )
  }

  return (
    <div className="hidden sm:flex w-[212px] shrink-0 flex-col gap-0.5 px-2.5 py-3 overflow-y-auto
      border-r border-border-subtle bg-surface-2/50">
      <div className="px-2 pt-0.5 pb-[9px] text-[10px] font-semibold uppercase tracking-[0.14em] text-text-4">
        Consoles
      </div>
      {entries.map(e => {
        const active = e.value === selected
        return (
          <button key={e.value || '__all'} onClick={() => onSelect(e.value)} title={e.title}
            style={active
              ? { background: 'linear-gradient(135deg,rgba(111,141,255,.24),rgba(157,109,255,.14))' }
              : undefined}
            className={`flex items-center gap-[9px] w-full text-left shrink-0 px-[9px] py-[7px] rounded-[9px] border ${
              active ? 'border-accent/40' : 'border-transparent hover:bg-accent/[0.06]'}`}>
            <span className="w-[42px] shrink-0 text-center text-[8.5px] font-bold font-mono tracking-[0.02em]
              py-[3px] rounded-[5px] text-bg"
              style={{ background: e.color, opacity: active ? 1 : 0.62 }}>{e.code}</span>
            <span className={`flex-1 min-w-0 truncate text-xs ${
              active ? 'text-text-heading font-semibold' : 'text-[#8b91b3] font-medium'}`}>{e.label}</span>
            <span className={`shrink-0 text-[9.5px] font-mono tabular-nums ${
              active ? 'text-link-2' : 'text-text-4'}`}>{e.count.toLocaleString()}</span>
          </button>
        )
      })}
    </div>
  )
}
