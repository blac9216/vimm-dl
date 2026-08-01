import { useState } from 'react'
import { useSettings, useSaveSetting } from '../../api/queries'
import { SetsDialog } from '../library/SetsDialog'
import {
  SettingsCard, SettingsRow, SettingsStackRow, SettingsSubHeader, StepperRow, ToggleRow,
  selectCls, inputCls, cardButtonCls,
} from './SettingsControls'

// The Settings tab (issue #264, design handoff "8. Settings"): a 208px section sidebar over a
// 640px content pane, one card per section. Every control still persists immediately — a per-key
// POST /api/settings, save-on-blur for text — so there is no save button and no dirty state.
// Section mapping keeps every setting the old 7-card grid exposed (epic #254 decision 5): the Wii U
// common key rides along in Conversion and both archive.org S3 fields stay, though the design omits
// them.

type Section = 'conversion' | 'sources' | 'catalog' | 'integrations' | 'features'

const SECTIONS: { key: Section; label: string; short: string; icon: string }[] = [
  { key: 'conversion', label: 'PS3 Conversion', short: 'Conversion', icon: '◆' },
  { key: 'sources', label: 'Sources', short: 'Sources', icon: '⛁' },
  { key: 'catalog', label: 'Catalog', short: 'Catalog', icon: '▤' },
  { key: 'integrations', label: 'Integrations', short: 'Integrations', icon: '⚯' },
  { key: 'features', label: 'Feature Flags', short: 'Flags', icon: '⚑' },
]

const TITLES: Record<Section, { title: string; subtitle: string }> = {
  conversion: {
    title: 'PS3 Conversion',
    subtitle: 'How downloaded PS3 archives are converted into emulator-ready ISOs.',
  },
  sources: {
    title: 'Sources · archive.org',
    subtitle: 'Tuning for archive.org downloads and the per-console set links.',
  },
  catalog: {
    title: 'Catalog',
    subtitle: 'The No-Intro / Redump source the catalog syncs from.',
  },
  integrations: {
    title: 'Integrations',
    subtitle: 'API credentials for descriptions and popularity ranking.',
  },
  features: {
    title: 'Feature Flags',
    subtitle: 'Beta and developer features. Toggling reveals or hides tabs.',
  },
}

export function SettingsPanel() {
  const { data: settings } = useSettings()
  const saveMutation = useSaveSetting()
  const [section, setSection] = useState<Section>('conversion')
  const [showSets, setShowSets] = useState(false)

  if (!settings) return null

  function save(key: string, value: string) {
    saveMutation.mutate({ key, value })
  }

  function saveNum(key: string, value: number) {
    save(key, value.toString())
  }

  // Persist a text setting only when it actually changed (called on blur).
  function saveText(key: string, value: string, current: string) {
    if (value !== current) save(key, value)
  }

  const { title, subtitle } = TITLES[section]

  return (
    <div className="flex flex-col sm:flex-row h-full">
      {/* Sidebar — 208px section nav on desktop. */}
      <div className="hidden sm:flex flex-col gap-1 w-52 shrink-0 border-r border-border-subtle
        bg-surface-2/50 px-3 py-[18px] overflow-y-auto">
        <div className="px-2.5 pb-2.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-text-4">
          Settings
        </div>
        {SECTIONS.map(s => {
          const active = s.key === section
          return (
            <button
              key={s.key}
              onClick={() => setSection(s.key)}
              className={`flex items-center gap-2.5 text-left px-3 py-2.5 rounded-[10px]
                text-[12.5px] ${active
                  ? 'bg-gradient-to-br from-accent/[0.22] to-accent-2/10 text-text-heading font-semibold'
                  : 'text-text-3 font-medium hover:bg-accent/[0.06] hover:text-text-2'}`}
            >
              <span className={`text-[13px] ${active ? '' : 'opacity-70'}`} aria-hidden="true">{s.icon}</span>
              {s.label}
            </button>
          )
        })}
      </div>

      {/* Sub-`sm`: the sidebar collapses to a horizontal section switcher. */}
      <div className="flex sm:hidden gap-1.5 shrink-0 overflow-x-auto scrollbar-none px-3 py-2
        border-b border-border-subtle bg-surface-2/50">
        {SECTIONS.map(s => {
          const active = s.key === section
          return (
            <button
              key={s.key}
              onClick={() => setSection(s.key)}
              className={`flex items-center gap-1.5 shrink-0 px-2.5 py-1.5 rounded-lg border
                text-[11.5px] ${active
                  ? 'border-accent/40 bg-accent/20 text-text-heading font-semibold'
                  : 'border-border bg-surface/50 text-text-3 font-medium'}`}
            >
              <span className="text-[12px]" aria-hidden="true">{s.icon}</span>
              {s.short}
            </button>
          )
        })}
      </div>

      {/* Content pane. */}
      <div className="flex-1 overflow-y-auto px-4 sm:px-[30px] py-5 sm:py-[26px]">
        <div className="max-w-[640px]">
          <div className="text-[17px] font-bold text-text mb-1">{title}</div>
          <div className="text-xs text-text-3 mb-5">{subtitle}</div>

          {section === 'conversion' && (
            <>
              <SettingsCard>
                <SettingsRow label="Default format" description="Used when queuing PS3 games">
                  <select
                    value={settings.ps3DefaultFormat}
                    onChange={e => save('ps3_default_format', e.target.value)}
                    className={selectCls}
                  >
                    <option value="0">JB Folder (.7z)</option>
                    <option value="1">.dec.iso</option>
                  </select>
                </SettingsRow>
                <StepperRow
                  label="Max parallelism"
                  description="Workers per phase (extract + convert) — requires a restart"
                  value={settings.ps3Parallelism}
                  min={1}
                  max={8}
                  onChange={v => saveNum('ps3_parallelism', v)}
                />
                <ToggleRow
                  label="Preserve archive"
                  description="Keep the .7z file after conversion"
                  checked={settings.ps3PreserveArchive}
                  onChange={v => save('ps3_preserve_archive', v.toString())}
                />
                <SettingsSubHeader
                  label="ISO rename"
                  note=".dec.iso only"
                  hint="JB Folder conversions use PARAM.SFO metadata for naming."
                />
                <ToggleRow
                  label="Fix 'The' placement"
                  description={'"Godfather, The" → "The Godfather"'}
                  checked={settings.fixThe}
                  onChange={v => save('rename_fix_the', v.toString())}
                />
                <ToggleRow
                  label="Add serial number"
                  description={'Append serial: "Game - BLES-00043.iso"'}
                  checked={settings.addSerial}
                  onChange={v => save('rename_add_serial', v.toString())}
                />
                <ToggleRow
                  label="Strip region"
                  description={'Remove "(Europe)", "(USA)" from filename'}
                  checked={settings.stripRegion}
                  onChange={v => save('rename_strip_region', v.toString())}
                />
              </SettingsCard>

              {/* Wii U — the design omits it; it keeps its home here rather than being dropped. */}
              <div className="mt-6 mb-2.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-text-4">
                Wii U
              </div>
              <SettingsCard>
                <SettingsStackRow
                  label="Common key"
                  description={'The 16-byte Wii U common key (32 hex chars), used to decrypt downloaded titles. ' +
                    'You supply it — it is never bundled. Without it, Wii U downloads still land (encrypted) ' +
                    'but the pipeline stops at a "keys required" state. Applies immediately, no restart.'}
                >
                  <input
                    type="password"
                    autoComplete="off"
                    spellCheck={false}
                    placeholder="32 hexadecimal characters"
                    defaultValue={settings.wiiUCommonKey}
                    onBlur={e => saveText('wiiu_common_key', e.target.value, settings.wiiUCommonKey)}
                    className={inputCls}
                  />
                </SettingsStackRow>
              </SettingsCard>
            </>
          )}

          {section === 'sources' && (
            <SettingsCard>
              <StepperRow
                label="Parallelism"
                description="Concurrent archive.org downloads"
                value={settings.archiveParallelism}
                min={1}
                max={16}
                onChange={v => saveNum('archive_parallelism', v)}
              />
              <StepperRow
                label="Retries"
                description="Retry attempts on a failed download"
                value={settings.archiveRetries}
                min={0}
                max={10}
                onChange={v => saveNum('archive_retries', v)}
              />
              <StepperRow
                label="Idle timeout"
                description="Seconds with no progress before a stall is retried"
                value={settings.archiveIdle}
                min={10}
                max={600}
                step={10}
                onChange={v => saveNum('archive_idle', v)}
              />
              <SettingsSubHeader
                label="Internet Archive S3 keys"
                note="optional"
                hint="Set both to authenticate archive.org requests. Get keys at archive.org/account/s3.php."
              />
              <SettingsStackRow label="Access key">
                <input
                  type="text"
                  autoComplete="off"
                  spellCheck={false}
                  defaultValue={settings.archiveS3Access}
                  onBlur={e => saveText('archive_s3_access', e.target.value, settings.archiveS3Access)}
                  className={inputCls}
                />
              </SettingsStackRow>
              <SettingsStackRow label="Secret key">
                <input
                  type="password"
                  autoComplete="off"
                  spellCheck={false}
                  defaultValue={settings.archiveS3Secret}
                  onBlur={e => saveText('archive_s3_secret', e.target.value, settings.archiveS3Secret)}
                  className={inputCls}
                />
              </SettingsStackRow>
              <SettingsStackRow description="Edit the archive.org sets each console resolves against.">
                <button onClick={() => setShowSets(true)} className={cardButtonCls}>
                  Manage download sets
                </button>
              </SettingsStackRow>
            </SettingsCard>
          )}

          {section === 'catalog' && (
            <SettingsCard>
              <SettingsRow label="DAT source" description="Where catalog sync pulls DATs">
                <select
                  value={settings.catalogDatSource}
                  onChange={e => save('catalog_dat_source', e.target.value)}
                  className={selectCls}
                >
                  <option value="libretro">libretro mirror</option>
                  <option value="daily-bundle">Daily bundle (fresher)</option>
                </select>
              </SettingsRow>
              <div className="px-[18px] pt-1 pb-4 text-[11px] leading-relaxed text-text-3">
                The daily bundle pulls one fresh zip per group from hugo's auto-datfile-generator;
                the libretro mirror can lag weeks. Applies to the next catalog sync.
              </div>
            </SettingsCard>
          )}

          {section === 'integrations' && (
            <SettingsCard>
              <SettingsStackRow
                label={<>IGDB <span className="text-[10px] text-link">· descriptions</span></>}
                description={<>
                  Twitch app credentials from dev.twitch.tv/console/apps. Both empty → the description
                  sync is disabled. Run the IGDB sync from the Jobs tab to populate descriptions.
                </>}
              >
                <input
                  type="text"
                  autoComplete="off"
                  spellCheck={false}
                  placeholder="Client ID"
                  defaultValue={settings.igdbClientId}
                  onBlur={e => saveText('igdb_client_id', e.target.value, settings.igdbClientId)}
                  className={`${inputCls} mb-[7px]`}
                />
                <input
                  type="password"
                  autoComplete="off"
                  spellCheck={false}
                  placeholder="Client secret"
                  defaultValue={settings.igdbClientSecret}
                  onBlur={e => saveText('igdb_client_secret', e.target.value, settings.igdbClientSecret)}
                  className={inputCls}
                />
              </SettingsStackRow>
              <SettingsStackRow
                label={<>RetroAchievements <span className="text-[10px] text-link">· popularity</span></>}
                description={<>
                  Web API key from retroachievements.org → Settings → Keys, blending player-count
                  popularity into the rank for cartridge consoles. Empty → the RA sync is disabled.
                </>}
              >
                <input
                  type="password"
                  autoComplete="off"
                  spellCheck={false}
                  placeholder="API key"
                  defaultValue={settings.raApiKey}
                  onBlur={e => saveText('ra_api_key', e.target.value, settings.raApiKey)}
                  className={inputCls}
                />
              </SettingsStackRow>
            </SettingsCard>
          )}

          {section === 'features' && (
            <SettingsCard>
              <ToggleRow
                label="Import (Beta)"
                description="Ingest a local drop folder into the catalog by hash"
                checked={settings.featureImport}
                onChange={v => save('feature_import', v.toString())}
              />
              <ToggleRow
                label="Sync (Beta)"
                description="Copy ISOs to an external drive"
                checked={settings.featureSync}
                onChange={v => save('feature_sync', v.toString())}
              />
              <ToggleRow
                label="Event Audit (Developer)"
                description="Show the full event log"
                checked={settings.featureEvents}
                onChange={v => save('feature_events', v.toString())}
              />
            </SettingsCard>
          )}
        </div>
      </div>

      {showSets && <SetsDialog onClose={() => setShowSets(false)} />}
    </div>
  )
}
