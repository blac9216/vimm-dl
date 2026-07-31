import { useState } from 'react'
import { useSettings, useImportCatalog, useEvents, useJobs } from '../../api/queries'
import { ImportRow } from './ImportRow'

type Filter = 'all' | 'matched' | 'rejected'

const FILTERS: { label: string; value: Filter }[] = [
  { label: 'All', value: 'all' },
  { label: 'Matched', value: 'matched' },
  { label: 'Rejected', value: 'rejected' },
]

const CHIP = 'px-[11px] py-[5px] text-[10px] uppercase tracking-[0.1em] rounded-[7px] border transition-colors whitespace-nowrap'
const CHIP_ON = 'border-accent/35 bg-accent/[0.16] text-link-3'
const CHIP_OFF = 'border-transparent text-text-faint hover:text-text-2'

/**
 * Import view (epic #118, restyled for the shell redesign in #263 — design handoff "7. Import").
 *
 * The dropzone panel is deliberately **informational** (epic #254 decision 6): there is no upload
 * endpoint — files are dropped into the server-side folder shown below, and Ingest
 * (POST /api/catalog/import) hash-matches them into the catalog (→ completed/{console}/) or sets them
 * aside in rejected/. The run state comes from the L1 jobs surface (GET /api/jobs, kind `import`),
 * which is what `useImportCatalog` invalidates; per-file results come from the `import` events.
 */
export function ImportPanel() {
  const { data: settings } = useSettings()
  const { data: jobs } = useJobs()
  const importMutation = useImportCatalog()
  const { data: eventsData } = useEvents('import', undefined, 200)
  const [filter, setFilter] = useState<Filter>('all')

  const job = jobs?.find(j => j.kind === 'import')
  const importing = job?.running ?? false
  const events = eventsData?.events ?? []
  const matchedCount = events.filter(e => e.phase === 'matched').length
  const rejectedCount = events.filter(e => e.phase === 'rejected').length
  const visible = events.filter(e => filter === 'all' || e.phase === filter)

  const importPath = settings?.importPath
  const rejectedPath = settings?.rejectedPath ?? 'rejected/'
  const error = importMutation.error

  return (
    <div className="h-full overflow-y-auto flex flex-col gap-[18px] p-4 sm:p-6">
      {/* Dropzone — informational: the drop folder lives on the server, Ingest scans it. */}
      <div className="shrink-0 text-center rounded-2xl border-[1.5px] border-dashed border-[#2e2c4e]
        px-5 sm:px-9 py-8 sm:py-[38px]
        bg-[radial-gradient(120%_100%_at_50%_0%,rgba(111,141,255,0.06),transparent)]">
        <div className="text-[28px] leading-none mb-2.5 text-link" aria-hidden="true">&#10515;</div>
        <div className="text-[15px] font-semibold text-text-body-2">Drop ROMs &amp; archives to import</div>
        <p className="mt-[7px] mx-auto max-w-[520px] text-[11.5px] leading-[1.6] text-text-3">
          Copy files into{' '}
          <span className="font-mono text-text-2 break-all" title={importPath}>{importPath ?? '…'}</span>{' '}
          on the machine running VIMM&#47;&#47;DL, then press Ingest &mdash; there is no browser upload.
          Files are hash-matched against the catalog: matches are placed into{' '}
          <span className="font-mono text-text-2">completed/{'{console}'}/</span> and marked owned;
          non-matches go to{' '}
          <span className="font-mono text-text-2 break-all" title={rejectedPath}>{rejectedPath}</span>.
        </p>

        <button
          onClick={() => importMutation.mutate()}
          disabled={importing}
          title="Hash every file in the import folder and match it to the catalog"
          className={`mt-[18px] px-5 py-2 text-xs font-semibold rounded-lg transition-colors ${
            importing
              ? 'border border-accent/45 bg-accent/[0.18] text-link-3 cursor-default'
              : 'bg-gradient-to-br from-accent to-accent-2 text-white border border-transparent hover:opacity-90'}`}
        >
          {importing ? 'Ingesting…' : 'Ingest'}
        </button>

        {importing && (
          <div className="mt-2.5 text-[11px] text-text-3 truncate">
            {job?.message ?? 'Scanning the import folder…'}
            {job?.percent != null && <span className="text-link-2"> &middot; {Math.round(job.percent)}%</span>}
          </div>
        )}
        {error && (
          <div className="mt-2.5 text-[11px] text-error">
            {error instanceof Error ? error.message : 'Failed to start the import'}
          </div>
        )}
      </div>

      {/* Recent imports — same feed as before (GET /api/events?type=import), spec list styling. */}
      <div className="shrink-0 flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="text-[10px] font-semibold uppercase tracking-[0.12em] text-link">Recent imports</span>
        <span className="text-[10px] uppercase tracking-[0.1em]">
          <span className="text-success">{matchedCount} matched</span>
          <span className="mx-1.5 text-text-4">&middot;</span>
          <span className="text-error">{rejectedCount} rejected</span>
        </span>
        <div className="flex-1" />
        <div className="flex items-center gap-1.5">
          {FILTERS.map(f => (
            <button
              key={f.value}
              onClick={() => setFilter(f.value)}
              className={`${CHIP} ${filter === f.value ? CHIP_ON : CHIP_OFF}`}
            >
              {f.label}
            </button>
          ))}
        </div>
      </div>

      <div className="shrink-0 border border-border rounded-[13px] overflow-hidden">
        {visible.map(e => <ImportRow key={e.id} event={e} />)}
        {visible.length === 0 && (
          <div className="flex flex-col items-center justify-center gap-1 h-[140px] px-6 text-center">
            <span className="text-[13px] text-text-3">
              {importing ? 'Ingesting…' : 'No import results yet'}
            </span>
            {!importing && filter === 'all' && (
              <span className="text-[11px] text-text-4">
                Drop files into the import folder and press Ingest
              </span>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
