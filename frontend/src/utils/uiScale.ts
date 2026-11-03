import { useSyncExternalStore } from 'react'

// UI size ("Kleiner" / "Größer" in the page header). The size is the root font size: html { font-size: calc(100% * var(--pv-ui-scale)) },
// so everything written in rem (Tailwind spacing and type, most of index.css) follows, while px values - above all the
// 1520 px page width - stay put. public/theme-init.js applies the stored value before the first paint (same key, same
// property), this module keeps it in sync afterwards.

export const UI_SCALE_STORAGE_KEY = 'pv_ui_scale_v1'
// Percent of the browser's own font size. 80 % is the smallest step that keeps the 11 px captions readable.
export const UI_SCALE_STEPS = [80, 90, 100, 110, 125] as const
export const UI_SCALE_DEFAULT = 100
// Fired on window right after the new size is applied (before React has re-rendered): anything that measures the page for
// sticky positions listens and re-measures at once, so the first frame after a change is already right
export const UI_SCALE_APPLIED_EVENT = 'pv-ui-scale-applied'

const MIN_STEP = UI_SCALE_STEPS[0]
const MAX_STEP = UI_SCALE_STEPS[UI_SCALE_STEPS.length - 1]!

// Whatever is stored (or typed into the console) ends up on the nearest valid step
export function snapUiScale(value: unknown): number {
  const n = typeof value === 'string' ? Number.parseInt(value, 10) : typeof value === 'number' ? value : Number.NaN
  if (!Number.isFinite(n)) return UI_SCALE_DEFAULT
  return UI_SCALE_STEPS.reduce((best, step) => (Math.abs(step - n) < Math.abs(best - n) ? step : best), UI_SCALE_DEFAULT as number)
}

function readInitial(): number {
  try {
    const applied = document.documentElement.dataset.uiScale
    if (applied) return snapUiScale(applied)
    return snapUiScale(localStorage.getItem(UI_SCALE_STORAGE_KEY))
  } catch {
    return UI_SCALE_DEFAULT
  }
}

function applyToDocument(percent: number): void {
  const root = document.documentElement
  root.style.setProperty('--pv-ui-scale', String(percent / 100))
  root.dataset.uiScale = String(percent)
}

let current = readInitial()
const listeners = new Set<() => void>()
// Idempotent: the early script in index.html has normally set it already; this also fixes a stored value that is not a step
applyToDocument(current)

function emit(): void {
  for (const listener of listeners) listener()
}

export function setUiScale(next: number): void {
  const snapped = snapUiScale(next)
  if (snapped === current) return
  current = snapped
  applyToDocument(snapped)
  try {
    localStorage.setItem(UI_SCALE_STORAGE_KEY, String(snapped))
  } catch {
    // Storage unavailable (private mode): the size holds for this tab only
  }
  window.dispatchEvent(new Event(UI_SCALE_APPLIED_EVENT))
  emit()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  // Another tab changed the size: follow it
  const onStorage = (event: StorageEvent) => {
    if (event.key !== UI_SCALE_STORAGE_KEY) return
    const snapped = snapUiScale(event.newValue)
    if (snapped === current) return
    current = snapped
    applyToDocument(snapped)
    window.dispatchEvent(new Event(UI_SCALE_APPLIED_EVENT))
    emit()
  }
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

function getSnapshot(): number {
  return current
}

export interface UiScaleApi {
  // Current size in percent
  scale: number
  canShrink: boolean
  canGrow: boolean
  isDefault: boolean
  // Each one returns the size that is active afterwards (the caller announces it)
  shrink: () => number
  grow: () => number
  reset: () => number
}

function move(direction: -1 | 1): number {
  const index = UI_SCALE_STEPS.indexOf(current as (typeof UI_SCALE_STEPS)[number])
  const next: number = UI_SCALE_STEPS[Math.min(Math.max(index + direction, 0), UI_SCALE_STEPS.length - 1)]!
  setUiScale(next)
  return next
}

const shrink = () => move(-1)
const grow = () => move(1)
const reset = () => {
  setUiScale(UI_SCALE_DEFAULT)
  return UI_SCALE_DEFAULT
}

export function useUiScale(): UiScaleApi {
  const scale = useSyncExternalStore(subscribe, getSnapshot, () => UI_SCALE_DEFAULT)
  return {
    scale,
    canShrink: scale > MIN_STEP,
    canGrow: scale < MAX_STEP,
    isDefault: scale === UI_SCALE_DEFAULT,
    shrink,
    grow,
    reset,
  }
}
