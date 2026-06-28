import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type {
  DataResponse, VersionResponse, SettingsResponse, MetaResponse,
  QueueImportResponse, Ps3ConvertResponse, SyncCompareResponse, QueueExportItem,
  EventsResponse, MetricsResponse, AddResponse, SourceInfo,
  CatalogConsole, CatalogGamesResponse, CatalogStatus, CatalogSet, CatalogVimm,
  CatalogQueueBatchResponse, Emulator, CatalogGameDescription, CatalogCurateResponse,
} from '../types/api'

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.json()
}

async function postJson<T>(url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method: 'POST',
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : {},
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) throw new Error(await res.text())
  const text = await res.text()
  return text ? JSON.parse(text) : (undefined as T)
}

async function del(url: string): Promise<void> {
  const res = await fetch(url, { method: 'DELETE' })
  if (!res.ok) throw new Error(await res.text())
}

async function patch(url: string, body: unknown): Promise<void> {
  const res = await fetch(url, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await res.text())
}

async function putJson<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await res.text())
  const text = await res.text()
  return text ? JSON.parse(text) : (undefined as T)
}

// --- Data (merged: queue + history + status) ---

export function useData() {
  return useQuery({
    queryKey: ['data'],
    queryFn: () => fetchJson<DataResponse>('/api/data'),
    refetchInterval: 10000,
  })
}

// --- Settings (merged: config + settings) ---

export function useSettings() {
  return useQuery({
    queryKey: ['settings'],
    queryFn: () => fetchJson<SettingsResponse>('/api/settings'),
  })
}

export function useSaveSetting() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { key: string; value: string }) =>
      postJson('/api/settings', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

// --- Metadata ---

const ONE_HOUR = 60 * 60 * 1000

export function useVersion() {
  return useQuery({
    queryKey: ['version'],
    queryFn: () => fetchJson<VersionResponse>('/api/version'),
    staleTime: ONE_HOUR,
    refetchInterval: ONE_HOUR,
  })
}

export function useMeta(url: string | null) {
  return useQuery({
    queryKey: ['meta', url],
    queryFn: () => fetchJson<MetaResponse>(`/api/meta?url=${encodeURIComponent(url!)}`),
    enabled: !!url,
    staleTime: Infinity,
  })
}

// --- Queue ---

export function useSources() {
  return useQuery({
    queryKey: ['sources'],
    queryFn: () => fetchJson<SourceInfo[]>('/api/sources'),
    staleTime: Infinity,
  })
}

// --- Catalog (canonical No-Intro / Redump library) ---

export function useCatalogConsoles() {
  return useQuery({
    queryKey: ['catalog-consoles'],
    queryFn: () => fetchJson<CatalogConsole[]>('/api/catalog/consoles'),
    staleTime: 60 * 1000,
  })
}

// Emulators with ingested compatibility — populates the Library emulator/status filter.
export function useEmulators() {
  return useQuery({
    queryKey: ['catalog-emulators'],
    queryFn: () => fetchJson<Emulator[]>('/api/catalog/emulators'),
    staleTime: 5 * 60 * 1000,
  })
}

export function useCatalogGames(console: string | null, q: string, local: string, dedupe: boolean, english: boolean, excludeCategories: boolean, searchMode: string, page: number, pageSize = 100, emulator = '', compat = '', sort = 'name') {
  const params = new URLSearchParams()
  if (console) params.set('console', console)
  if (q) params.set('q', q)
  if (local && local !== 'all') params.set('local', local)
  if (dedupe) params.set('dedupe', 'true')
  if (english) params.set('english', 'true')
  if (excludeCategories) params.set('excludeCategories', 'true')
  if (searchMode && searchMode !== 'substring') params.set('mode', searchMode)
  if (emulator) params.set('emulator', emulator)
  if (emulator && compat) params.set('compat', compat)
  if (sort && sort !== 'name') params.set('sort', sort)
  params.set('page', page.toString())
  params.set('pageSize', pageSize.toString())
  return useQuery({
    queryKey: ['catalog-games', console, q, local, dedupe, english, excludeCategories, searchMode, page, pageSize, emulator, compat, sort],
    queryFn: () => fetchJson<CatalogGamesResponse>(`/api/catalog/games?${params}`),
    staleTime: 60 * 1000,
  })
}

export function useCatalogStatus() {
  return useQuery({
    queryKey: ['catalog-status'],
    queryFn: () => fetchJson<CatalogStatus>('/api/catalog/status'),
    // Poll while any catalog job is running so the UI advances; idle otherwise.
    refetchInterval: q => {
      const s = q.state.data
      return s?.syncing || s?.scanning || s?.compatSyncing || s?.verifying || s?.vimmSyncing || s?.importing || s?.igdbSyncing ? 2000 : false
    },
  })
}

// Ingest the import drop folder (hash-match → place/reject). Background, single-flight.
export function useImportCatalog() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => postJson('/api/catalog/import'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

export function useSyncCatalog() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => postJson('/api/catalog/sync'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

export function useScanCatalog() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => postJson('/api/catalog/scan'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

export function useSyncCompat() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => postJson('/api/catalog/compat/sync'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

export function useVerifyCatalog() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => postJson('/api/catalog/verify'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

// Scrape Vimm and hash-bind the catalog (optionally one console). Surfaces the "Vimm match" badges.
export function useSyncVimm() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (console?: string) =>
      postJson('/api/catalog/vimm-sync' + (console ? `?console=${encodeURIComponent(console)}` : '')),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

// Sync game descriptions from IGDB (Twitch OAuth). No-ops server-side without configured creds.
export function useSyncIgdb() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => postJson('/api/catalog/igdb-sync'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

// Sync game rankings from IGDB (total_rating → "best games" score). Shares the IGDB job gate with the
// description sync server-side, so it surfaces via the same igdbSyncing status. No-ops without creds.
export function useSyncIgdbRank() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => postJson('/api/catalog/igdb-rank-sync'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  })
}

export function useCatalogSets() {
  return useQuery({
    queryKey: ['catalog-sets'],
    queryFn: () => fetchJson<CatalogSet[]>('/api/catalog/sets'),
    staleTime: 60 * 1000,
  })
}

export function useAddSet() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { name: string; console: string; links: string[] }) =>
      postJson<CatalogSet>('/api/catalog/sets', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-sets'] }),
  })
}

export function useUpdateSet() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...data }: { id: number; name: string; console: string; links: string[] }) =>
      putJson<CatalogSet>(`/api/catalog/sets/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-sets'] }),
  })
}

export function useDeleteSet() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => del(`/api/catalog/sets/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-sets'] }),
  })
}

// A game's Vimm download options, or null when it has no Vimm binding (404).
export async function fetchGameVimm(id: number): Promise<CatalogVimm | null> {
  const res = await fetch(`/api/catalog/games/${id}/vimm`)
  if (res.status === 404) return null
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.json()
}

// Curation (R3): ask the backend for the best non-owned games (by rank) that fit a byte budget, given
// the current Library filters. Returns the ids to pre-select + their cumulative size.
export async function fetchCurate(opts: {
  console: string | null; q: string; dedupe: boolean; english: boolean; excludeCategories: boolean
  searchMode: string; emulator: string; compat: string; budgetBytes: number; maxCount?: number
}): Promise<CatalogCurateResponse> {
  const params = new URLSearchParams()
  if (opts.console) params.set('console', opts.console)
  if (opts.q) params.set('q', opts.q)
  if (opts.dedupe) params.set('dedupe', 'true')
  if (opts.english) params.set('english', 'true')
  if (opts.excludeCategories) params.set('excludeCategories', 'true')
  if (opts.searchMode && opts.searchMode !== 'substring') params.set('mode', opts.searchMode)
  if (opts.emulator) params.set('emulator', opts.emulator)
  if (opts.emulator && opts.compat) params.set('compat', opts.compat)
  params.set('budgetBytes', Math.floor(opts.budgetBytes).toString())
  if (opts.maxCount && opts.maxCount > 0) params.set('maxCount', Math.floor(opts.maxCount).toString())
  return fetchJson<CatalogCurateResponse>(`/api/catalog/curate?${params}`)
}

// A game's IGDB description, or null when none is stored (404). Lazily fetched when a row is expanded
// (enabled only once an id is set); descriptions are effectively immutable, so cache them indefinitely.
export function useGameDescription(id: number | null) {
  return useQuery({
    queryKey: ['catalog-description', id],
    queryFn: async (): Promise<CatalogGameDescription | null> => {
      const res = await fetch(`/api/catalog/games/${id}/description`)
      if (res.status === 404) return null
      if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
      return res.json()
    },
    enabled: id != null,
    staleTime: Infinity,
  })
}

export function useQueueCatalogGame() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, format }: { id: number; format?: number }) =>
      postJson<{ url: string; source: string }>(
        `/api/catalog/games/${id}/queue${format != null ? `?format=${format}` : ''}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

// E3b: batch-queue the bulk-selected games in one request (default format resolution).
export function useQueueCatalogGamesBatch() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ ids, format }: { ids: number[]; format?: number }) =>
      postJson<CatalogQueueBatchResponse>('/api/catalog/games/queue', { ids, format }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useAddToQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { urls: string[]; format?: number; force?: boolean; source?: string }) =>
      postJson<AddResponse>('/api/queue', data),
    onSuccess: (data) => {
      if (data?.queued) qc.invalidateQueries({ queryKey: ['data'] })
    },
  })
}

export function usePatchQueueItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { id: number; direction?: string; format?: number }) =>
      patch(`/api/queue/${data.id}`, { direction: data.direction, format: data.format }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useReorderQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (ids: number[]) =>
      postJson('/api/queue/reorder', { ids }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useDeleteFromQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => del(`/api/queue/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useClearQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => del('/api/queue'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useDeleteCompleted() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, deleteFiles }: { id: number; deleteFiles?: boolean }) =>
      del(`/api/completed/${id}${deleteFiles ? '?deleteFiles=true' : ''}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useImportQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (items: QueueExportItem[]) =>
      postJson<QueueImportResponse>('/api/queue/import', items),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export async function exportQueue(): Promise<QueueExportItem[]> {
  return fetchJson('/api/queue/export')
}

// --- PS3 (merged endpoints) ---

export function useConvertPs3() {
  return useMutation({
    mutationFn: (filename?: string) =>
      postJson<Ps3ConvertResponse>('/api/ps3/convert', { filename: filename ?? null }),
  })
}

export function usePs3Action() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { filename: string; action: 'mark-done' | 'abort' }) =>
      postJson('/api/ps3/action', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

// --- Sync (merged endpoints) ---

export function useSyncCompare() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (path: string) =>
      postJson<SyncCompareResponse>('/api/sync/compare', { path }),
    onSuccess: (data) => qc.setQueryData(['sync'], data),
  })
}

export function useSyncCopy() {
  return useMutation({
    mutationFn: (filename?: string) =>
      postJson('/api/sync/copy', { filename: filename ?? null }),
  })
}

export function useSyncCancel() {
  return useMutation({
    mutationFn: () => postJson('/api/sync/cancel'),
  })
}

// --- Events ---

export function useEvents(type?: string, item?: string, limit = 100) {
  const params = new URLSearchParams()
  params.set('limit', limit.toString())
  if (type) params.set('type', type)
  if (item) params.set('item', item)
  return useQuery({
    queryKey: ['events', type, item, limit],
    queryFn: () => fetchJson<EventsResponse>(`/api/events?${params}`),
    refetchInterval: 5000,
  })
}

// --- Metrics ---

export function useMetrics() {
  return useQuery({
    queryKey: ['metrics'],
    queryFn: () => fetchJson<MetricsResponse>('/api/metrics'),
    refetchInterval: 10000,
  })
}
