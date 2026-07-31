import type { JobStatus } from '../types/api'
import type { Ps3IsoStatusEvent } from '../types/signalr'

// Job identity for the Jobs tab (#259, design handoff "3. Jobs (new)"). Two feeds share this file:
// the background jobs of GET /api/jobs (keyed by the backend's `kind`) and the live conversion
// events off SignalR (keyed by pipeline phase).
//
// Kind colors — the handoff pins five (PS3 Convert #3f7cf0 · Extract #f79d3f · Hash Verify #3ee0a0 ·
// Catalog Sync #9575ff · Import Scan #8f9cd8) and asks for the ramp to be extended "sensibly" for the
// remaining kinds. The extension keeps one hue per *family* and varies lightness within it, matching
// how the console palette is built:
//   pipeline        blue  #3f7cf0 (convert)      + orange #f79d3f (extract)      — the pinned pair
//   local hashing   green #3ee0a0 (hash verify)  + #26b07c (compat sync — the other emulator-facing
//                                                  ingest, one generation darker)
//   folder scans    periwinkle #8f9cd8 (import scan, pinned) + #aab4e6 (owned scan, one step lighter)
//   catalog data    violet #9575ff (catalog sync, pinned)    + #b79bff (Vimm match, one step lighter)
//   external meta   amber #ffd08a (IGDB descriptions) · #f4b44e (IGDB rank — the same gold as the
//                                                  Library's ★ rank) · #d9791f (RetroAchievements)
export interface JobKindMeta {
  /** 9px mono uppercase badge label — keep it inside the 104px badge (≈14 characters). */
  label: string
  /** What the job is working on — the row's name line. */
  name: string
  color: string
}

export const JOB_KIND_META: Record<string, JobKindMeta> = {
  sync: { label: 'Catalog Sync', name: 'No-Intro / Redump DATs', color: '#9575ff' },
  vimm: { label: 'Vimm Match', name: "Vimm's Lair hash binding", color: '#b79bff' },
  scan: { label: 'Owned Scan', name: 'completed/', color: '#aab4e6' },
  import: { label: 'Import Scan', name: 'Import drop folder', color: '#8f9cd8' },
  verify: { label: 'Hash Verify', name: 'Owned files (CRC32)', color: '#3ee0a0' },
  compat: { label: 'Compat Sync', name: 'Emulator compatibility', color: '#26b07c' },
  'igdb-description': { label: 'IGDB Desc', name: 'IGDB descriptions', color: '#ffd08a' },
  'igdb-rank': { label: 'IGDB Rank', name: 'IGDB ratings → rank', color: '#f4b44e' },
  ra: { label: 'RA Popularity', name: 'RetroAchievements players', color: '#d9791f' },
}

export function jobKindMeta(kind: string): JobKindMeta {
  return JOB_KIND_META[kind] ?? { label: kind, name: kind, color: '#8f9cd8' }
}

/**
 * Conversion rows borrow the pipeline pair — extraction is orange, conversion blue — keyed by the
 * pipeline phase, which is all a `ConvertStatus` event carries. The Wii U pipeline shares that
 * channel (fetch → decrypt → extract → package), so its phases sit one lightness step up the same
 * blue ramp (#6b9dff) to stay distinguishable from a PS3 conversion.
 */
const CONV_KIND: Record<string, JobKindMeta> = {
  extracting: { label: 'Extract', name: '', color: '#f79d3f' },
  extracted: { label: 'Extract', name: '', color: '#f79d3f' },
  converting: { label: 'PS3 Convert', name: '', color: '#3f7cf0' },
  fetching: { label: 'Wii U Fetch', name: '', color: '#6b9dff' },
  decrypting: { label: 'Wii U Decrypt', name: '', color: '#6b9dff' },
  packaging: { label: 'Wii U Pack', name: '', color: '#6b9dff' },
}

export function convKindMeta(phase: string): JobKindMeta {
  return CONV_KIND[phase] ?? { label: 'Convert', name: '', color: '#3f7cf0' }
}

/** Pipeline phases that still have work to do (PipelinePhase.IsActive on the backend). */
export function isActiveConvPhase(phase: string): boolean {
  return phase !== 'done' && phase !== 'error' && phase !== 'skipped'
}

/**
 * The Jobs tab count pill: running background jobs + conversions that haven't reached a terminal
 * phase. Finished conversion rows stay on screen until "Clear finished", but they are not "active".
 */
export function countActiveJobs(
  jobs: JobStatus[] | undefined,
  convStatuses: Record<string, Ps3IsoStatusEvent>,
): number {
  const running = jobs?.filter(j => j.running).length ?? 0
  const converting = Object.values(convStatuses).filter(s => isActiveConvPhase(s.phase)).length
  return running + converting
}
