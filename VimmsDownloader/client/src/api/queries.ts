import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type {
  DataResponse, VersionResponse, SettingsResponse, MetaResponse,
  QueueImportResponse, Ps3ConvertResponse, SyncCompareResponse, QueueExportItem,
  EventsResponse, MetricsResponse, AddResponse, SourceInfo,
  CatalogConsole, CatalogGamesResponse, CatalogStatus, CatalogSet,
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

export function useCatalogGames(console: string | null, q: string, local: string, page: number, pageSize = 100) {
  const params = new URLSearchParams()
  if (console) params.set('console', console)
  if (q) params.set('q', q)
  if (local && local !== 'all') params.set('local', local)
  params.set('page', page.toString())
  params.set('pageSize', pageSize.toString())
  return useQuery({
    queryKey: ['catalog-games', console, q, local, page, pageSize],
    queryFn: () => fetchJson<CatalogGamesResponse>(`/api/catalog/games?${params}`),
    staleTime: 60 * 1000,
  })
}

export function useCatalogStatus() {
  return useQuery({
    queryKey: ['catalog-status'],
    queryFn: () => fetchJson<CatalogStatus>('/api/catalog/status'),
    // Poll while a sync or scan is running so the UI advances; idle otherwise.
    refetchInterval: q => (q.state.data?.syncing || q.state.data?.scanning ? 2000 : false),
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
    mutationFn: (data: { console: string; identifier: string; label?: string }) =>
      postJson<CatalogSet>('/api/catalog/sets', data),
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

export function useQueueCatalogGame() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => postJson<{ url: string }>(`/api/catalog/games/${id}/queue`),
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
