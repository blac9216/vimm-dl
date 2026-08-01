// Library view state (issue #257) — the filter/sort/paging/selection set shared by the console rail,
// the game list and the detail pane, persisted so it survives tab navigation (the panel unmounts on
// tab change) and page reloads.

import type { SearchMode, SortMode } from '../../types/api'

export interface LibraryFilters {
  console: string            // '' = all consoles
  search: string
  local: string              // all | owned | remote
  dedupe: boolean            // 1G1R — one game per title
  english: boolean           // English/Western releases only
  excludeCategories: boolean // hide demos/betas/protos/kiosk/samples
  searchMode: SearchMode
  emulator: string           // '' = any emulator
  compatStatus: string       // '' = any status (only meaningful with an emulator)
  sort: SortMode
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

/** Human label for a Vimm/archive match kind (#289 shares this with vimmMatch's sha1/md5/crc). */
export function matchKindLabel(kind: string): string {
  return kind === 'name' ? 'file name' : kind.toUpperCase()
}

/**
 * Origin marking a game seeded FROM Vimm rather than from a No-Intro/Redump DAT — used where no
 * public DAT exists (Wii U discs). Vimm publishes the canonical Redump hashes, so the stored values
 * mean the same thing as DAT-sourced ones; what differs is that nothing independent ever confirmed
 * them, since Vimm is both the source and the only witness.
 */
export const VIMM_ORIGIN = 'vimm'

/** True when this game's rows came from a Vimm scrape instead of a DAT. */
export function isVimmSourced(origins: string[]): boolean {
  return origins.includes(VIMM_ORIGIN)
}

/** The DAT-source origins only — the Vimm origin gets its own badge, not a "DAT source" one. */
export function datOrigins(origins: string[]): string[] {
  return origins.filter(o => o !== VIMM_ORIGIN)
}
