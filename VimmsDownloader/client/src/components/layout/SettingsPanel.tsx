import { useState } from 'react'
import { useSettings, useSaveSetting } from '../../api/queries'
import { SetsDialog } from '../library/SetsDialog'

interface ToggleProps {
  label: string
  description: string
  checked: boolean
  onChange: (checked: boolean) => void
}

function Toggle({ label, description, checked, onChange }: ToggleProps) {
  return (
    <label className="flex items-center justify-between py-2 cursor-pointer group">
      <div>
        <div className="text-xs text-text">{label}</div>
        <div className="text-[10px] text-text-4">{description}</div>
      </div>
      <button
        onClick={() => onChange(!checked)}
        className={`relative w-8 h-4 rounded-full transition-colors ${
          checked ? 'bg-accent/40' : 'bg-surface-3'
        }`}
      >
        <div className={`absolute top-0.5 w-3 h-3 rounded-full transition-all ${
          checked
            ? 'left-4 bg-accent shadow-[0_0_6px_rgba(91,155,213,0.3)]'
            : 'left-0.5 bg-text-4'
        }`} />
      </button>
    </label>
  )
}

interface StepperProps {
  label: string
  description: string
  value: number
  min: number
  max: number
  step?: number
  onChange: (value: number) => void
}

function Stepper({ label, description, value, min, max, step = 1, onChange }: StepperProps) {
  const clamp = (v: number) => Math.max(min, Math.min(max, v))
  const btn = `w-6 h-6 flex items-center justify-center rounded bg-surface-3/50 text-text-3
    hover:bg-surface-3 hover:text-text disabled:opacity-30 text-xs`
  return (
    <div className="flex items-center justify-between">
      <div>
        <div className="text-xs text-text">{label}</div>
        <div className="text-[10px] text-text-4">{description}</div>
      </div>
      <div className="flex items-center gap-2">
        <button onClick={() => onChange(clamp(value - step))} disabled={value <= min} className={btn}>-</button>
        <span className="text-sm font-mono text-accent w-10 text-center tabular-nums">{value}</span>
        <button onClick={() => onChange(clamp(value + step))} disabled={value >= max} className={btn}>+</button>
      </div>
    </div>
  )
}

export function SettingsPanel() {
  const { data: settings } = useSettings()
  const saveMutation = useSaveSetting()
  const [showSets, setShowSets] = useState(false)

  if (!settings) return null

  function toggle(key: string, current: boolean) {
    saveMutation.mutate({ key, value: (!current).toString() })
  }

  function saveNum(key: string, value: number) {
    saveMutation.mutate({ key, value: value.toString() })
  }

  // Persist a text setting only when it actually changed (called on blur).
  function saveText(key: string, value: string, current: string) {
    if (value !== current) saveMutation.mutate({ key, value })
  }

  const inputCls = `w-full bg-surface-2/60 border border-border/40 text-text-3 text-xs rounded
    px-2 py-1 font-mono focus:outline-none focus:border-accent/30`

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center px-3 sm:px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase">Settings</span>
      </div>
      <div className="px-3 sm:px-6 py-4 sm:py-5 overflow-y-auto">
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
          {/* PS3 */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              PS3
            </div>
            <div className="space-y-3 border border-border/20 rounded p-3 bg-card/30">
              <div className="flex items-center justify-between">
                <div>
                  <div className="text-xs text-text">Default format</div>
                  <div className="text-[10px] text-text-4">Format used when adding PS3 games</div>
                </div>
                <select
                  value={settings.ps3DefaultFormat}
                  onChange={e => saveMutation.mutate({ key: 'ps3_default_format', value: e.target.value })}
                  className="bg-surface-2/60 border border-border/40 text-text-3 text-xs rounded px-2 py-1
                    focus:outline-none focus:border-accent/30"
                >
                  <option value="0">JB Folder (.7z)</option>
                  <option value="1">.dec.iso</option>
                </select>
              </div>
              <Toggle
                label="Preserve archive"
                description="Keep the .7z file after conversion"
                checked={settings.ps3PreserveArchive}
                onChange={() => toggle('ps3_preserve_archive', settings.ps3PreserveArchive)}
              />
              <Stepper
                label="Max parallelism"
                description="Workers per phase (extract + convert)"
                value={settings.ps3Parallelism}
                min={1}
                max={8}
                onChange={v => saveNum('ps3_parallelism', v)}
              />

              {/* ISO Rename — subsection */}
              <div className="pt-2 border-t border-border/10">
                <div className="flex items-center gap-2 mb-2">
                  <span className="text-[10px] text-text-3 tracking-wide uppercase">ISO Rename</span>
                  <span className="text-[9px] px-1.5 py-0.5 rounded bg-accent/10 text-accent/70 border border-accent/20">
                    .dec.iso only
                  </span>
                </div>
                <div className="text-[10px] text-text-4 mb-2">
                  JB Folder conversions use PARAM.SFO metadata for naming.
                </div>
                <div className="space-y-1">
                  <Toggle
                    label="Fix 'The' placement"
                    description={`"Godfather, The" → "The Godfather"`}
                    checked={settings.fixThe}
                    onChange={() => toggle('rename_fix_the', settings.fixThe)}
                  />
                  <Toggle
                    label="Add serial number"
                    description={`Append serial: "Game - BLES-00043.iso"`}
                    checked={settings.addSerial}
                    onChange={() => toggle('rename_add_serial', settings.addSerial)}
                  />
                  <Toggle
                    label="Strip region"
                    description={`Remove "(Europe)", "(USA)" from filename`}
                    checked={settings.stripRegion}
                    onChange={() => toggle('rename_strip_region', settings.stripRegion)}
                  />
                </div>
              </div>
            </div>
            <div className="mt-2 text-[10px] text-text-4">
              Parallelism requires restart. Higher values use more CPU and disk I/O.
            </div>
          </div>

          {/* Archive */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              Archive
            </div>
            <div className="space-y-3 border border-border/20 rounded p-3 bg-card/30">
              <Stepper
                label="Parallelism"
                description="Concurrent archive.org downloads"
                value={settings.archiveParallelism}
                min={1}
                max={16}
                onChange={v => saveNum('archive_parallelism', v)}
              />
              <Stepper
                label="Retries"
                description="Retry attempts on a failed download"
                value={settings.archiveRetries}
                min={0}
                max={10}
                onChange={v => saveNum('archive_retries', v)}
              />
              <Stepper
                label="Idle timeout"
                description="Seconds with no progress before a stall is retried"
                value={settings.archiveIdle}
                min={10}
                max={600}
                step={10}
                onChange={v => saveNum('archive_idle', v)}
              />

              {/* Internet Archive S3 keys — subsection */}
              <div className="pt-2 border-t border-border/10 space-y-2">
                <div className="text-[10px] text-text-3 tracking-wide uppercase">Internet Archive S3 keys</div>
                <div className="text-[10px] text-text-4">
                  Optional. Set both to authenticate archive.org requests. Get keys at
                  {' '}<span className="text-accent/70">archive.org/account/s3.php</span>.
                </div>
                <div>
                  <div className="text-[10px] text-text-4 mb-1">Access key</div>
                  <input
                    type="text"
                    autoComplete="off"
                    spellCheck={false}
                    defaultValue={settings.archiveS3Access}
                    onBlur={e => saveText('archive_s3_access', e.target.value, settings.archiveS3Access)}
                    className={inputCls}
                  />
                </div>
                <div>
                  <div className="text-[10px] text-text-4 mb-1">Secret key</div>
                  <input
                    type="password"
                    autoComplete="off"
                    spellCheck={false}
                    defaultValue={settings.archiveS3Secret}
                    onBlur={e => saveText('archive_s3_secret', e.target.value, settings.archiveS3Secret)}
                    className={inputCls}
                  />
                </div>
              </div>

              {/* Download sources */}
              <div className="pt-2 border-t border-border/10">
                <button
                  onClick={() => setShowSets(true)}
                  className="w-full px-2 py-1.5 rounded text-xs text-text-3 border border-border/40
                    bg-surface-2/40 hover:bg-surface-2/80 hover:text-text transition-colors"
                >
                  Manage download sources
                </button>
                <div className="mt-1 text-[10px] text-text-4">
                  Edit the archive.org sets each console resolves against.
                </div>
              </div>
            </div>
            <div className="mt-2 text-[10px] text-text-4">
              Parallelism / retries / idle apply to upcoming archive.org downloads.
            </div>
          </div>

          {/* Wii U */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              Wii U
            </div>
            <div className="space-y-3 border border-border/20 rounded p-3 bg-card/30">
              <div>
                <div className="text-xs text-text mb-1">Common key</div>
                <div className="text-[10px] text-text-4 mb-2">
                  The 16-byte Wii U common key (32 hex chars), used to decrypt downloaded titles.
                  You supply it — it is never bundled. Without it, Wii U downloads still land
                  (encrypted) but the pipeline stops at a "keys required" state.
                </div>
                <input
                  type="password"
                  autoComplete="off"
                  spellCheck={false}
                  defaultValue={settings.wiiuCommonKey}
                  onBlur={e => saveText('wiiu_common_key', e.target.value, settings.wiiuCommonKey)}
                  className={inputCls}
                />
              </div>
            </div>
            <div className="mt-2 text-[10px] text-text-4">
              Applies immediately on save (no restart). 32 hexadecimal characters.
            </div>
          </div>

          {/* IGDB (game descriptions) */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              IGDB (descriptions)
            </div>
            <div className="space-y-3 border border-border/20 rounded p-3 bg-card/30">
              <div className="text-[10px] text-text-4">
                Twitch app credentials for the IGDB API, used to fetch game descriptions. Create an app
                at <span className="text-accent/70">dev.twitch.tv/console/apps</span>. Both empty → the
                description sync is disabled.
              </div>
              <div>
                <div className="text-[10px] text-text-4 mb-1">Client ID</div>
                <input
                  type="text"
                  autoComplete="off"
                  spellCheck={false}
                  defaultValue={settings.igdbClientId}
                  onBlur={e => saveText('igdb_client_id', e.target.value, settings.igdbClientId)}
                  className={inputCls}
                />
              </div>
              <div>
                <div className="text-[10px] text-text-4 mb-1">Client secret</div>
                <input
                  type="password"
                  autoComplete="off"
                  spellCheck={false}
                  defaultValue={settings.igdbClientSecret}
                  onBlur={e => saveText('igdb_client_secret', e.target.value, settings.igdbClientSecret)}
                  className={inputCls}
                />
              </div>
            </div>
            <div className="mt-2 text-[10px] text-text-4">
              Then run the IGDB sync from the Library toolbar to populate descriptions.
            </div>
          </div>

          {/* Catalog */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              Catalog
            </div>
            <div className="space-y-3 border border-border/20 rounded p-3 bg-card/30">
              <div className="flex items-center justify-between">
                <div>
                  <div className="text-xs text-text">DAT source</div>
                  <div className="text-[10px] text-text-4">Where catalog sync pulls No-Intro/Redump DATs</div>
                </div>
                <select
                  value={settings.catalogDatSource}
                  onChange={e => saveMutation.mutate({ key: 'catalog_dat_source', value: e.target.value })}
                  className="bg-surface-2/60 border border-border/40 text-text-3 text-xs rounded px-2 py-1
                    focus:outline-none focus:border-accent/30"
                >
                  <option value="libretro">libretro mirror</option>
                  <option value="daily-bundle">Daily bundle (fresher)</option>
                </select>
              </div>
              <div className="text-[10px] text-text-4">
                The libretro mirror can lag weeks; the daily bundle pulls one fresh zip per group from
                hugo's auto-datfile-generator. Applies to the next catalog sync.
              </div>
            </div>
          </div>

          {/* Feature Flags */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              Feature Flags
            </div>
            <div className="space-y-1 border border-border/20 rounded p-3 bg-card/30">
              <Toggle
                label="Library (Beta)"
                description="Enable the Library tab to browse the canonical No-Intro/Redump game catalog"
                checked={settings.featureLibrary}
                onChange={() => toggle('feature_library', settings.featureLibrary)}
              />
              <Toggle
                label="Import (Beta)"
                description="Enable the Import tab to ingest a local drop folder into the catalog by hash"
                checked={settings.featureImport}
                onChange={() => toggle('feature_import', settings.featureImport)}
              />
              <Toggle
                label="Sync (Beta)"
                description="Enable the Sync tab for copying ISOs to an external drive"
                checked={settings.featureSync}
                onChange={() => toggle('feature_sync', settings.featureSync)}
              />
              <Toggle
                label="Event Audit (Developer)"
                description="Enable the Events tab showing the full event log"
                checked={settings.featureEvents}
                onChange={() => toggle('feature_events', settings.featureEvents)}
              />
            </div>
          </div>
        </div>
      </div>

      {showSets && <SetsDialog onClose={() => setShowSets(false)} />}
    </div>
  )
}
