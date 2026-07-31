// Library view state (issue #257) — the filter/sort/paging/selection set shared by the console rail,
// the game list and the detail pane, persisted so it survives tab navigation (the panel unmounts on
// tab change) and page reloads.

export interface LibraryFilters {
  console: string            // '' = all consoles
  search: string
  local: string              // all | owned | remote
  dedupe: boolean            // 1G1R — one game per title
  english: boolean           // English/Western releases only
  excludeCategories: boolean // hide demos/betas/protos/kiosk/samples
  searchMode: string         // substring | glob | regex
  emulator: string           // '' = any emulator
  compatStatus: string       // '' = any status (only meaningful with an emulator)
  sort: string               // name | rank
  page: number
  selectedId: number | null  // the game shown in the detail pane
}

export const DEFAULT_FILTERS: LibraryFilters = {
  console: '', search: '', local: 'all', dedupe: false, english: false, excludeCategories: false,
  searchMode: 'substring', emulator: '', compatStatus: '', sort: 'name', page: 0, selectedId: null,
}

export const FILTERS_KEY = 'vimm:library-filters'

export function loadFilters(): LibraryFilters {
  try {
    const raw = localStorage.getItem(FILTERS_KEY)
    return raw ? { ...DEFAULT_FILTERS, ...(JSON.parse(raw) as Partial<LibraryFilters>) } : DEFAULT_FILTERS
  } catch {
    return DEFAULT_FILTERS
  }
}

export function saveFilters(filters: LibraryFilters): void {
  try {
    localStorage.setItem(FILTERS_KEY, JSON.stringify(filters))
  } catch { /* ignore storage write errors (private mode, quota) */ }
}

/** Does the filter set differ from the defaults in anything hidden behind the advanced popover? */
export function hasAdvancedFilters(f: LibraryFilters): boolean {
  return f.dedupe || f.english || f.excludeCategories || f.searchMode !== 'substring' || !!f.emulator
}

/** Human label for a download format alt (PS3 conventions; mirrors the backend FormatLabel). */
export function fmtLabel(alt: number): string {
  switch (alt) {
    case 0: return 'JB Folder'
    case 1: return '.dec.iso'
    default: return `fmt ${alt}`
  }
}

/** Short label for a DAT-source origin (D2b-2): 'daily-bundle' → 'bundle'; anything else as-is. */
export function originLabel(origin: string): string {
  return origin === 'daily-bundle' ? 'bundle' : origin
}
