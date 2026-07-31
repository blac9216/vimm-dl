export function fmtBytes(bytes: number): string {
  if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + ' GB'
  if (bytes >= 1048576) return (bytes / 1048576).toFixed(2) + ' MB'
  if (bytes >= 1024) return (bytes / 1024).toFixed(2) + ' KB'
  return bytes + ' B'
}

export interface ParsedProgress {
  filename: string
  pct: string
  size: string
  width: string
  speed: string | null
}

export function parseProgress(msg: string): ParsedProgress | null {
  // Format: "filename: 123.45 / 456.78 MB (45.67%) [2.50 MB/s]"
  const m1 = msg.match(/^(.+?):\s+([\d.]+)\s*\/\s*([\d.]+)\s*MB\s*\(([\d.]+)%\)(?:\s*\[([\d.]+)\s*MB\/s\])?/)
  if (m1) {
    return {
      filename: m1[1],
      pct: parseFloat(m1[4]).toFixed(2) + '%',
      size: `${m1[2]} / ${m1[3]} MB`,
      width: parseFloat(m1[4]).toFixed(2) + '%',
      speed: m1[5] ? `${parseFloat(m1[5]).toFixed(2)} MB/s` : null,
    }
  }

  // Format: "filename: 123.45 MB downloaded [2.50 MB/s]"
  const m2 = msg.match(/^(.+?):\s+([\d.]+)\s*MB(?:.*?\[([\d.]+)\s*MB\/s\])?/)
  if (m2) {
    return {
      filename: m2[1],
      pct: '--',
      size: `${m2[2]} MB`,
      width: '0%',
      speed: m2[3] ? `${parseFloat(m2[3]).toFixed(2)} MB/s` : null,
    }
  }

  return null
}

/**
 * Relative "3h ago" label for a completed timestamp (design handoff §2 — Completed row).
 * The backend stores `datetime('now')`, i.e. UTC "YYYY-MM-DD HH:MM:SS" with no zone marker, so the
 * plain form is normalised to an ISO instant before parsing. Unparsable input is returned as-is.
 */
export function fmtRelative(ts: string | null | undefined): string | null {
  if (!ts) return null
  const normalized = /^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}(:\d{2})?$/.test(ts)
    ? ts.replace(' ', 'T') + 'Z'
    : ts
  const parsed = Date.parse(normalized)
  if (Number.isNaN(parsed)) return ts

  const seconds = Math.max(0, (Date.now() - parsed) / 1000)
  if (seconds < 60) return 'just now'
  const minutes = seconds / 60
  if (minutes < 60) return `${Math.floor(minutes)}m ago`
  const hours = minutes / 60
  if (hours < 24) return `${Math.floor(hours)}h ago`
  const days = hours / 24
  if (days < 30) return `${Math.floor(days)}d ago`
  return new Date(parsed).toISOString().slice(0, 10)
}

export function fmtDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)}s`
  const m = Math.floor(s / 60)
  const rem = s % 60
  return `${m}m ${rem.toFixed(0)}s`
}

export function slugFromUrl(url: string): string {
  const parts = url.replace(/\/$/, '').split('/')
  return decodeURIComponent(parts[parts.length - 1] || url)
}
