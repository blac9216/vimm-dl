import { useEffect, useMemo, useState } from 'react'
import {
  useJobs, useCancelJob, usePs3Action, useCatalogConsoles,
  useSyncCatalog, useScanCatalog, useVerifyCatalog, useSyncCompat, useSyncVimm,
  useSyncIgdb, useSyncIgdbRank, useSyncRa,
} from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { JobRow } from './JobRow'
import { convKindMeta, countActiveJobs, isActiveConvPhase, jobKindMeta } from '../../lib/jobs'
import type { JobStatus } from '../../types/api'
import type { Ps3IsoStatusEvent } from '../../types/signalr'

// The Jobs tab (issue #259, design handoff "3. Jobs (new)"): everything that is *not* a download.
// Two feeds are merged client-side, rendered through the same JobRow:
//   1. live conversions — SignalR ConvertStatus events kept in DownloadContext (`convStatuses`),
//      stopped with POST /api/ps3/action {action:'abort'};
//   2. background jobs — GET /api/jobs (L1 #255), stopped with POST /api/jobs/{kind}/cancel.
// The catalog job triggers moved here from the Library's "⋯" overflow menu (epic #254 decision 4);
// each is disabled while its own gate reports running, so the 409 single-flight path stays unreachable
// from the UI.

/** One catalog trigger in the header's "Run job" group, keyed by its backend job kind. */
interface Trigger {
  kind: string
  label: string
  title: string
  /** Fires the mutation; `opts` carries the error callback so a rejected POST surfaces in the header. */
  run: (opts: { onError: (e: Error) => void }) => void
}

const BTN = 'px-3 py-[5px] text-[11px] font-semibold rounded-lg border shrink-0 whitespace-nowrap transition-colors'
const BTN_IDLE = 'border-border-light bg-surface/50 text-text-2 hover:bg-surface-3/60 hover:text-text-body'
const BTN_BUSY = 'border-accent/45 bg-accent/[0.18] text-link-3 cursor-default'

export function JobsPanel() {
  const { state } = useDownload()
  const { data: jobs } = useJobs()
  const { data: consoles } = useCatalogConsoles()
  const cancelJob = useCancelJob()
  const ps3Action = usePs3Action()

  const syncMutation = useSyncCatalog()
  const scanMutation = useScanCatalog()
  const verifyMutation = useVerifyCatalog()
  const compatMutation = useSyncCompat()
  const vimmMutation = useSyncVimm()
  const igdbMutation = useSyncIgdb()
  const igdbRankMutation = useSyncIgdbRank()
  const raMutation = useSyncRa()

  const [vimmConsole, setVimmConsole] = useState('')
  const [dismissed, setDismissed] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)

  // Tick once a second so elapsed advances between polls without calling Date.now() during render.
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(id)
  }, [])

  const runningJobs = useMemo(() => (jobs ?? []).filter(j => j.running), [jobs])
  const isRunning = (kind: string) => runningJobs.some(j => j.kind === kind)

  // Conversion rows: everything the pipeline has told us about this session, minus the finished rows
  // the user dismissed with "Clear finished". Terminal rows stay until dismissed so a finished/failed
  // conversion doesn't vanish silently; a dismissed item that starts converting again comes back
  // (the dismissal only ever hides a terminal row).
  const conversions = useMemo(
    () => Object.values(state.convStatuses)
      .filter(s => isActiveConvPhase(s.phase) || !dismissed.has(s.itemName)),
    [state.convStatuses, dismissed])
  const finishedCount = conversions.filter(c => !isActiveConvPhase(c.phase)).length

  // Group live conversions by game (#151/A): several formats of one game render together, each row
  // keeping its own filename for display + abort. Rows without a gameId group by their filename.
  const convGroups = useMemo(() => {
    const map = new Map<string, Ps3IsoStatusEvent[]>()
    for (const c of conversions) {
      const key = c.gameId != null ? `g${c.gameId}` : `f:${c.itemName}`
      const arr = map.get(key)
      if (arr) arr.push(c)
      else map.set(key, [c])
    }
    return [...map.values()]
  }, [conversions])

  // Same helper the TabBar pill uses, so the header count and the pill can never disagree.
  const jobCount = countActiveJobs(jobs, state.convStatuses)

  function clearFinished() {
    setDismissed(prev => {
      const next = new Set(prev)
      for (const c of conversions) if (!isActiveConvPhase(c.phase)) next.add(c.itemName)
      return next
    })
  }

  function runTrigger(t: Trigger) {
    setError(null)
    t.run({ onError: e => setError(e.message || `Failed to start ${t.label}`) })
  }

  const triggers: Trigger[] = [
    { kind: 'sync', label: 'Sync catalog', title: 'Re-sync the No-Intro / Redump DATs',
      run: o => syncMutation.mutate(undefined, o) },
    { kind: 'scan', label: 'Scan owned', title: 'Scan completed/ for owned games',
      run: o => scanMutation.mutate(undefined, o) },
    { kind: 'verify', label: 'Verify hashes', title: 'Verify owned files against the catalog hashes (CRC32)',
      run: o => verifyMutation.mutate(undefined, o) },
    { kind: 'compat', label: 'Emulator compat', title: 'Sync every registered emulator’s compatibility list',
      run: o => compatMutation.mutate(undefined, o) },
    { kind: 'vimm', label: 'Vimm match',
      title: vimmConsole
        ? `Match ${vimmConsole} against Vimm's Lair by hash, binding a vault URL + formats`
        : "Match the catalog against Vimm's Lair by hash (pick a console to scope the run)",
      run: o => vimmMutation.mutate(vimmConsole || undefined, o) },
    { kind: 'igdb-description', label: 'IGDB descriptions',
      title: 'Sync game descriptions from IGDB (needs Twitch credentials in Settings → IGDB)',
      run: o => igdbMutation.mutate(undefined, o) },
    { kind: 'igdb-rank', label: 'IGDB rank ★',
      title: 'Rank games from IGDB ratings (needs Twitch credentials in Settings → IGDB)',
      run: o => igdbRankMutation.mutate(undefined, o) },
    { kind: 'ra', label: 'RetroAchievements',
      title: 'Blend RetroAchievements popularity into the rank (needs an API key in Settings)',
      run: o => raMutation.mutate(undefined, o) },
  ]

  return (
    <div className="flex flex-col h-full">
      <div className="shrink-0 border-b border-border-section bg-surface-2/40">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 px-3 sm:px-[22px] py-3">
          <span className="text-[10px] uppercase tracking-[0.12em] text-text-3 whitespace-nowrap">
            {jobCount} {jobCount === 1 ? 'job' : 'jobs'}
          </span>
          <span className="text-[11px] text-text-4">
            Extraction, conversion, verification and catalog work &mdash; everything that isn&rsquo;t a download.
          </span>
          <div className="flex-1" />
          <button
            onClick={clearFinished}
            disabled={finishedCount === 0}
            title="Dismiss finished conversion rows"
            className={`px-3.5 py-2 text-xs font-semibold rounded-lg border whitespace-nowrap ${
              finishedCount === 0
                ? 'border-border-section bg-surface/40 text-text-disabled cursor-default'
                : 'border-border-light bg-surface/50 text-text-2 hover:bg-surface-3/60 hover:text-text-body'}`}
          >
            Clear finished
          </button>
        </div>

        {/* Run-job group — the catalog triggers that used to live in the Library "⋯" menu. */}
        <div className="flex flex-wrap items-center gap-2 px-3 sm:px-[22px] pb-2.5">
          <span className="text-[10px] uppercase tracking-[0.12em] text-text-4 whitespace-nowrap">Run job</span>
          {triggers.map(t => {
            const busy = isRunning(t.kind)
            return (
              <button key={t.kind} onClick={() => runTrigger(t)} disabled={busy} title={t.title}
                className={`${BTN} ${busy ? BTN_BUSY : BTN_IDLE}`}>
                {busy ? `${t.label}…` : t.label}
              </button>
            )
          })}
          {/* Vimm scoping — the Library used to pass its selected console; keep the capability here. */}
          <select value={vimmConsole} onChange={e => setVimmConsole(e.target.value)}
            title="Console scope for the Vimm match job"
            className="bg-input border border-border-light rounded-lg px-2 py-1 text-[11px] text-text-2
              focus:outline-none focus:border-accent/40">
            <option value="">Vimm: all consoles</option>
            {consoles?.map(c => <option key={c.console} value={c.console}>Vimm: {c.console}</option>)}
          </select>
        </div>

        {error && (
          <div className="mx-3 sm:mx-[22px] mb-2.5 p-2 bg-error/8 border border-error/20 rounded text-xs text-error
            flex items-center gap-2">
            <span className="flex-1">{error}</span>
            <button onClick={() => setError(null)} className="text-text-4 hover:text-text shrink-0">×</button>
          </div>
        )}
      </div>

      <div className="flex-1 overflow-y-auto">
        {convGroups.map(group => group.length === 1 ? (
          <ConversionRow key={group[0].itemName} status={group[0]} now={now}
            startedAt={state.convStartTimes[group[0].itemName]}
            onStop={() => ps3Action.mutate({ filename: group[0].itemName, action: 'abort' })} />
        ) : (
          <div key={`grp-${group[0].gameId}`} className="border-l-[2.5px] border-l-[#3f7cf099]">
            <div className="px-3 sm:px-[22px] pt-1.5 text-[10px] uppercase tracking-[0.06em] text-text-4">
              Same game &middot; {group.length} formats
            </div>
            {group.map(s => (
              <ConversionRow key={s.itemName} status={s} now={now} grouped
                startedAt={state.convStartTimes[s.itemName]}
                onStop={() => ps3Action.mutate({ filename: s.itemName, action: 'abort' })} />
            ))}
          </div>
        ))}

        {runningJobs.map(job => (
          <BackgroundJobRow key={job.kind} job={job} now={now}
            onStop={() => {
              setError(null)
              cancelJob.mutate(job.kind, {
                onError: e => setError(e instanceof Error ? e.message : `Failed to stop ${job.kind}`),
              })
            }} />
        ))}

        {convGroups.length === 0 && runningJobs.length === 0 && (
          <div className="flex items-center justify-center h-[200px] px-6 text-center text-text-4 text-[13px]">
            Nothing running &mdash; conversions and catalog jobs show up here while they work.
          </div>
        )}
      </div>
    </div>
  )
}

/** A live conversion/extraction row (SignalR `ConvertStatus`, PS3 + Wii U pipelines). */
function ConversionRow({ status, startedAt, now, onStop, grouped }: {
  status: Ps3IsoStatusEvent
  startedAt: number | undefined
  now: number
  onStop: () => void
  grouped?: boolean
}) {
  const meta = convKindMeta(status.phase)
  const live = isActiveConvPhase(status.phase)      // still in the pipeline: stoppable, elapsed ticks
  const working = live && status.phase !== 'queued' // actually doing work: full-strength accents
  // The pipeline reports extraction progress inside its message ("… 42%"); conversion has no total.
  const pctMatch = status.message.match(/(\d+)%/)
  const pct = pctMatch ? parseInt(pctMatch[1], 10) : null

  let badge: { variant: 'converting' | 'extracting' | 'waiting' | 'done' | 'error' | 'skipped'; text: string }
  switch (status.phase) {
    case 'extracting': badge = { variant: 'extracting', text: pct != null ? `Extract ${pct}%` : 'Extracting' }; break
    case 'converting': badge = { variant: 'converting', text: pct != null ? `Running ${pct}%` : 'Running' }; break
    case 'extracted': badge = { variant: 'waiting', text: 'Waiting' }; break
    case 'queued': badge = { variant: 'waiting', text: 'Queued' }; break
    case 'done': badge = { variant: 'done', text: 'Done' }; break
    case 'error': badge = { variant: 'error', text: 'Failed' }; break
    case 'skipped': badge = { variant: 'skipped', text: 'Skipped' }; break
    default: badge = { variant: 'waiting', text: status.phase }
  }

  const name = status.format != null ? `${status.itemName} · f${status.format}` : status.itemName

  return (
    <JobRow
      kind={meta.label} color={meta.color} name={name} message={status.message}
      percent={status.phase === 'queued' ? 0 : pct} active={working} status={badge}
      elapsedMs={live && startedAt ? now - startedAt : null}
      onStop={live ? onStop : undefined} stopTitle="Abort conversion" grouped={grouped}
    />
  )
}

/** A running background job from GET /api/jobs. */
function BackgroundJobRow({ job, now, onStop }: { job: JobStatus; now: number; onStop: () => void }) {
  const meta = jobKindMeta(job.kind)
  // Prefer startedAt so elapsed ticks smoothly between polls; elapsedMs is the fallback. Neither is
  // rendered unless the job is running — the backend keeps both growing after a run ends (L1 review).
  const started = job.startedAt ? Date.parse(job.startedAt) : NaN
  const elapsed = Number.isNaN(started) ? job.elapsedMs : Math.max(0, now - started)
  const detail = job.current != null && job.total != null
    ? `${job.current.toLocaleString()} of ${job.total.toLocaleString()}`
    : null

  return (
    <JobRow
      kind={meta.label} color={meta.color} name={meta.name}
      message={[job.message, detail].filter(Boolean).join(' · ') || 'Working…'}
      percent={job.percent} active
      status={{ variant: 'converting', text: job.percent != null ? `Running ${Math.round(job.percent)}%` : 'Running' }}
      elapsedMs={elapsed} onStop={onStop} stopTitle={`Stop ${meta.label}`}
    />
  )
}
