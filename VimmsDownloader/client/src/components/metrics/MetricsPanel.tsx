import { useState } from 'react'
import { Chart as ChartJS, CategoryScale, LinearScale, PointElement, LineElement, Filler, Tooltip } from 'chart.js'
import type { ScriptableContext } from 'chart.js'
import { Line } from 'react-chartjs-2'
import { useSettings, useMetrics } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { fmtBytes } from '../../lib/format'
import { Badge } from '../shared/Badge'

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Filler, Tooltip)

const MAX_POINTS = 60 // ~2 min at 2s intervals

// Design handoff "4. Metrics" (issue #260): three labeled card columns — System, Download Speed,
// Disk Usage. The section label + card recipe is shared by all three columns, so it lives here once.
const SECTION_LABEL = 'text-[10px] font-semibold uppercase tracking-[0.12em] text-link mb-2.5'
const CARD = 'border border-border rounded-[13px] p-4 bg-card/45'

function parseSpeed(speed: string | null | undefined): number {
  if (!speed) return 0
  const m = speed.match(/([\d.]+)\s*MB\/s/)
  return m ? parseFloat(m[1]) : 0
}

/**
 * Sparkline fill — the spec's `linearGradient` from `rgba(111,141,255,.35)` to transparent, built
 * against the live chart area (chart.js hands us the canvas context on every layout pass).
 */
function areaFill(ctx: ScriptableContext<'line'>): CanvasGradient | string {
  const { chartArea, ctx: canvas } = ctx.chart
  if (!chartArea) return 'rgba(111, 141, 255, 0.18)' // first paint, before the area is measured
  const gradient = canvas.createLinearGradient(0, chartArea.top, 0, chartArea.bottom)
  gradient.addColorStop(0, 'rgba(111, 141, 255, 0.35)')
  gradient.addColorStop(1, 'rgba(111, 141, 255, 0)')
  return gradient
}

export function MetricsPanel() {
  const { data: settings } = useSettings()
  const { data: metrics } = useMetrics()
  const { state } = useDownload()

  const [speedHistory, setSpeedHistory] = useState<number[]>([])
  const [prevSpeed, setPrevSpeed] = useState<string | null>(null)
  const [prevRunning, setPrevRunning] = useState(state.running)

  // Accumulate the SignalR speed series, and reset it when a download stops. Both are adjusted
  // during render (React's "store info from previous renders" pattern) rather than via effects.
  const rawSpeed = state.activeDlInfo?.speed ?? null
  if (rawSpeed !== prevSpeed) {
    setPrevSpeed(rawSpeed)
    setSpeedHistory(h => {
      const next = [...h, parseSpeed(rawSpeed)]
      return next.length > MAX_POINTS ? next.slice(-MAX_POINTS) : next
    })
  }
  if (state.running !== prevRunning) {
    setPrevRunning(state.running)
    if (!state.running) setSpeedHistory([])
  }

  const currentSpeed = speedHistory.at(-1) ?? 0
  const avgSpeed = speedHistory.length > 0
    ? speedHistory.reduce((a, b) => a + b, 0) / speedHistory.length : 0

  const diskUsed = metrics ? metrics.diskTotalBytes - metrics.diskFreeBytes : 0
  const diskPct = metrics && metrics.diskTotalBytes > 0
    ? ((diskUsed / metrics.diskTotalBytes) * 100) : 0
  const queuedPct = metrics && metrics.diskFreeBytes > 0
    ? ((metrics.queuedTotalBytes / metrics.diskFreeBytes) * 100) : 0

  const diskBadge = diskPct > 90 ? 'error' as const
    : diskPct > 75 ? 'extracting' as const
    : 'done' as const

  const queueBadge = queuedPct > 80 ? 'error' as const
    : queuedPct > 50 ? 'extracting' as const
    : 'done' as const

  return (
    <div className="h-full overflow-y-auto p-3 sm:p-[22px]">
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-[18px] items-start">

        {/* System */}
        <div>
          <div className={SECTION_LABEL}>System</div>
          <div className={`${CARD} flex flex-col gap-2.5`}>
            <InfoRow label="Hostname" value={settings?.hostname} />
            <InfoRow label="IPv4" value={settings?.ipv4} />
            <InfoRow label="Platform" value={settings?.platform} />
            <InfoRow label="OS" value={settings?.osDescription} />
            <InfoRow label="Download path" value={settings?.activePath} />
          </div>
        </div>

        {/* Download Speed */}
        <div>
          <div className={SECTION_LABEL}>Download Speed</div>
          <div className={CARD}>
            <div className="flex items-baseline gap-4 mb-3">
              <span>
                <span className="text-[24px] font-bold font-mono text-link-2 tabular-nums">
                  {currentSpeed.toFixed(2)}
                </span>
                <span className="text-[10px] text-text-3 ml-1">MB/s</span>
              </span>
              <span>
                <span className="text-[13px] font-mono text-text-2 tabular-nums">{avgSpeed.toFixed(2)}</span>
                <span className="text-[10px] text-text-3 ml-1">avg</span>
              </span>
            </div>
            <div className="h-[120px]">
              <Line
                data={{
                  labels: speedHistory.map(() => ''),
                  datasets: [{
                    data: speedHistory,
                    borderColor: '#7d83ff',
                    backgroundColor: areaFill,
                    borderWidth: 1.3,
                    pointRadius: 0,
                    fill: 'origin',
                    tension: 0,
                  }],
                }}
                options={{
                  responsive: true,
                  maintainAspectRatio: false,
                  animation: false,
                  // The spec's sparkline is bare — no axes, ticks or grid lines; the readout above
                  // the chart carries the numbers.
                  scales: {
                    x: { display: false },
                    y: { display: false, beginAtZero: true },
                  },
                  plugins: { tooltip: { enabled: false } },
                }}
              />
            </div>
            {!state.running && speedHistory.length === 0 && (
              <div className="text-[10px] text-text-4 text-center mt-2">No active download</div>
            )}
          </div>
        </div>

        {/* Disk Usage */}
        <div>
          <div className={SECTION_LABEL}>Disk Usage</div>
          <div className={`${CARD} flex flex-col gap-[13px]`}>
            {/* Volume */}
            <div>
              <div className="flex items-center justify-between gap-2 mb-1.5">
                <span className="text-[10px] text-text-3">Volume</span>
                <Badge variant={diskBadge}>{diskPct.toFixed(1)}% used</Badge>
              </div>
              <div className="h-[9px] rounded-md bg-surface-3/60 overflow-hidden">
                <div className="h-full rounded-md bg-gradient-to-r from-accent to-accent-2 transition-all"
                  style={{ width: `${Math.min(diskPct, 100)}%` }} />
              </div>
              <div className="flex justify-between gap-2 mt-[5px] text-[10px] text-text-3">
                <span>{fmtBytes(diskUsed)} used</span>
                <span>{metrics ? fmtBytes(metrics.diskFreeBytes) : '--'} free</span>
              </div>
            </div>

            {/* Queued */}
            <DiskRow
              label="Queued"
              detail={`${metrics?.queuedCount ?? 0} games · ${metrics ? fmtBytes(metrics.queuedTotalBytes) : '--'}`}
              note="Estimated from metadata — actual size may differ"
              badge={<Badge variant={queueBadge}>{queuedPct > 0 ? `${queuedPct.toFixed(0)}% of free` : 'OK'}</Badge>}
            />

            {/* Downloading */}
            {metrics && metrics.downloadingCount > 0 && (
              <DiskRow
                label="Downloading"
                detail={`${metrics.downloadingCount} files · ${fmtBytes(metrics.downloadingTotalBytes)}`}
                note="Partial files — resumes automatically on restart"
                badge={<Badge variant="downloading">In Progress</Badge>}
              />
            )}

            {/* Completed */}
            <DiskRow
              label="Completed"
              detail={`${metrics?.completedCount ?? 0} files · ${metrics ? fmtBytes(metrics.completedTotalBytes) : '--'}`}
              note="Archives and converted ISOs tracked in the database"
            />

            {/* Orphaned */}
            {metrics && metrics.orphanedCount > 0 && (
              <DiskRow
                label="Orphaned"
                detail={`${metrics.orphanedCount} files · ${fmtBytes(metrics.orphanedTotalBytes)}`}
                note="Not in database — leftover from a previous install or DB reset"
                badge={<Badge variant="extracting">Untracked</Badge>}
              />
            )}
          </div>
        </div>

      </div>
    </div>
  )
}

function InfoRow({ label, value }: { label: string; value?: string | null }) {
  return (
    <span className="flex items-center justify-between gap-3">
      <span className="text-[10px] text-text-3 whitespace-nowrap">{label}</span>
      <span className="text-[11px] font-mono text-text-2 text-right truncate max-w-[170px]">
        {value ?? '--'}
      </span>
    </span>
  )
}

/** A disk breakdown row: 12px label over a 10px detail line, with an optional status badge. */
function DiskRow({ label, detail, note, badge }: {
  label: string
  detail: string
  note: string
  badge?: React.ReactNode
}) {
  return (
    <div className="flex items-center justify-between gap-2.5 pt-[11px] border-t border-border/70">
      <span className="min-w-0">
        <span className="block text-xs text-text-body-2">{label}</span>
        <span className="block text-[10px] text-text-3">{detail}</span>
        <span className="block text-[9px] text-text-4 mt-0.5">{note}</span>
      </span>
      {badge && <span className="shrink-0">{badge}</span>}
    </div>
  )
}
