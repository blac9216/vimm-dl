import type { ReactNode } from 'react'

// Settings row primitives (issue #264, design handoff "8. Settings"): one card per section, rows
// divided by `border-inner`, left = 13px label over an 11px description, right = the control.
// Every control persists immediately — these are presentational only, the panel owns the mutation.

/** Section card — `border #211f3a`, radius 13, `bg rgba(20,19,36,.45)`, internal row dividers. */
export function SettingsCard({ children }: { children: ReactNode }) {
  return (
    <div className="border border-border rounded-[13px] bg-card/45 overflow-hidden
      divide-y divide-border-inner">
      {children}
    </div>
  )
}

/** In-card group header — carries the copy the design has no row for (ISO rename, S3 keys). */
export function SettingsSubHeader({ label, note, hint }: {
  label: string
  note?: string
  hint?: string
}) {
  return (
    <div className="px-[18px] py-2.5 bg-surface-2/40">
      <div className="flex items-center gap-2">
        <span className="text-[10px] font-semibold uppercase tracking-[0.12em] text-link">{label}</span>
        {note && (
          <span className="text-[9px] px-1.5 py-[1px] rounded-[5px] bg-info-bg text-link-2
            border border-info-border">
            {note}
          </span>
        )}
      </div>
      {hint && <div className="mt-1 text-[11px] leading-relaxed text-text-3">{hint}</div>}
    </div>
  )
}

/** Standard row: label + description on the left, control on the right. */
export function SettingsRow({ label, description, children }: {
  label: ReactNode
  description?: ReactNode
  children: ReactNode
}) {
  return (
    <div className="flex items-center justify-between gap-2.5 px-[18px] py-[15px]">
      <span className="min-w-0">
        <span className="block text-[13px] text-text-body">{label}</span>
        {description && <span className="block text-[11px] text-text-3">{description}</span>}
      </span>
      <span className="shrink-0">{children}</span>
    </div>
  )
}

/** Stacked row: label + description above a full-width control (text inputs, buttons). */
export function SettingsStackRow({ label, description, children }: {
  label?: ReactNode
  description?: ReactNode
  children: ReactNode
}) {
  return (
    <div className="px-[18px] py-[15px]">
      {label && <div className="text-[13px] text-text-body mb-[3px]">{label}</div>}
      {description && <div className="text-[11px] leading-relaxed text-text-3 mb-[9px]">{description}</div>}
      {children}
    </div>
  )
}

/** Toggle row — 38×20 gradient pill; the whole row is a label so any part of it flips the switch. */
export function ToggleRow({ label, description, checked, onChange }: {
  label: string
  description: string
  checked: boolean
  onChange: (checked: boolean) => void
}) {
  return (
    <label className="flex items-center justify-between gap-2.5 px-[18px] py-[13px] cursor-pointer
      hover:bg-accent/[0.06]">
      <span className="min-w-0">
        <span className="block text-[13px] text-text-body">{label}</span>
        <span className="block text-[11px] text-text-3">{description}</span>
      </span>
      <input
        type="checkbox"
        checked={checked}
        onChange={e => onChange(e.target.checked)}
        className="sr-only peer"
      />
      <span className={`relative shrink-0 w-[38px] h-5 rounded-[11px] transition-colors
        peer-focus-visible:ring-2 peer-focus-visible:ring-accent/50 ${
          checked ? 'bg-gradient-to-br from-accent to-accent-2' : 'bg-border-light'}`}>
        <span className={`absolute top-0.5 w-4 h-4 rounded-full transition-all ${
          checked ? 'left-5 bg-white' : 'left-0.5 bg-text-3'}`} />
      </span>
    </label>
  )
}

/** Stepper — 26×26 −/+ around a mono value; clamps at min/max and disables at the bounds. */
export function StepperRow({ label, description, value, min, max, step = 1, onChange }: {
  label: string
  description: string
  value: number
  min: number
  max: number
  step?: number
  onChange: (value: number) => void
}) {
  const clamp = (v: number) => Math.max(min, Math.min(max, v))
  const btn = `w-[26px] h-[26px] flex items-center justify-center rounded-[7px] bg-surface-3/60
    text-text-2 text-xs hover:text-text-heading hover:bg-surface-3 disabled:opacity-30
    disabled:hover:bg-surface-3/60 disabled:hover:text-text-2`
  return (
    <SettingsRow label={label} description={description}>
      <span className="flex items-center gap-[9px]">
        <button
          type="button"
          onClick={() => onChange(clamp(value - step))}
          disabled={value <= min}
          aria-label={`Decrease ${label}`}
          className={btn}
        >
          −
        </button>
        <span className="text-[15px] font-mono text-link-2 w-8 text-center tabular-nums">{value}</span>
        <button
          type="button"
          onClick={() => onChange(clamp(value + step))}
          disabled={value >= max}
          aria-label={`Increase ${label}`}
          className={btn}
        >
          +
        </button>
      </span>
    </SettingsRow>
  )
}

/** Select recipe — `bg #0d0c1a`, `border #2a2848`, radius 8. */
export const selectCls = `bg-input border border-border-light rounded-lg text-text-2 text-xs
  px-2.5 py-[7px] focus:outline-none focus:border-accent/40`

/** Text input recipe — same shell as the select, mono, full width. */
export const inputCls = `w-full bg-input border border-border-light rounded-lg text-text-2
  text-[11px] font-mono px-[11px] py-2 placeholder:text-text-4
  focus:outline-none focus:border-accent/40`

/** Secondary full-width button (Manage download sets). */
export const cardButtonCls = `w-full px-3 py-2.5 rounded-[9px] border border-border-light
  bg-surface-3/40 text-[12px] font-semibold text-link-3 hover:bg-surface-3/70
  hover:text-text-heading`
