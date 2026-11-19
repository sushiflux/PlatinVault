import { useCallback, useEffect, useState, useSyncExternalStore } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchSettings, updateSettings } from '../api/client'
import type { SettingsResponseDto, UiDesign } from '../api/types'
import { softReflow } from './softReflow'

// The design of the app (Settings > System > Design): 'light' = the calm default look, 'classic' = the earlier look. Only the look
// changes; every classic rule lives in styles/classic/ and is scoped under <html data-design="classic">.
// - public/theme-init.js sets data-design before the first paint from this browser's copy (same key), so a reload never flashes.
// - The server's setting (ui.design) wins once the settings are loaded and is copied into that key (<UiDesignSync /> in App.tsx).
// - A change applies at once (soft reflow: the reading position holds), is saved on the server and rolled back when saving fails.

export const UI_DESIGN_STORAGE_KEY = 'pv_ui_design_v1'
export const UI_DESIGNS: readonly UiDesign[] = ['light', 'classic']
export const UI_DESIGN_DEFAULT: UiDesign = 'light'

/** A known design, or null for anything else (a missing field, an unknown value of a newer or older server). */
export function parseUiDesign(value: unknown): UiDesign | null {
  return value === 'light' || value === 'classic' ? value : null
}

function readInitial(): UiDesign {
  const applied = parseUiDesign(document.documentElement.dataset.design)
  if (applied) return applied
  try {
    return parseUiDesign(localStorage.getItem(UI_DESIGN_STORAGE_KEY)) ?? UI_DESIGN_DEFAULT
  } catch {
    return UI_DESIGN_DEFAULT
  }
}

function applyToDocument(design: UiDesign): void {
  document.documentElement.dataset.design = design
}

function storeCopy(design: UiDesign): void {
  try {
    localStorage.setItem(UI_DESIGN_STORAGE_KEY, design)
  } catch {
    // Storage unavailable (private mode): the design holds for this page; the server's value still restores it next time
  }
}

let current: UiDesign = readInitial()
// Saves in flight: while one runs, a settings answer that was read before it must not switch the design back
let pendingSaves = 0
const listeners = new Set<() => void>()
// Idempotent: public/theme-init.js has normally set it already
applyToDocument(current)

function emit(): void {
  for (const listener of listeners) listener()
}

// Switches the look of the whole page; with `soft` the reading position holds and old and new cross-fade (like the UI size)
function apply(design: UiDesign, soft: boolean): void {
  if (design === current) {
    storeCopy(design)
    return
  }
  const run = () => {
    current = design
    applyToDocument(design)
    storeCopy(design)
    emit()
  }
  // softReflow needs matchMedia (missing in some test environments); without it the change simply happens at once
  if (soft && typeof window.matchMedia === 'function') softReflow(run)
  else run()
}

/** The design that is active right now. */
export function getUiDesign(): UiDesign {
  return current
}

/**
 * The server's value, once the settings are loaded: it wins over this browser's copy and is stored as the new copy. A missing or
 * unknown value keeps the local copy, and a save in flight is never undone by an older answer.
 */
export function syncUiDesignFromServer(value: unknown): void {
  const design = parseUiDesign(value)
  if (!design || pendingSaves > 0) return
  apply(design, false)
}

export type UiDesignChange = { ok: true } | { ok: false; error: unknown }

/**
 * Applies `next` at once and saves it on the server. When saving fails, the previous design comes back (unless another change
 * happened in the meantime) and the error is returned for the caller to report. `onSaved` receives the server's answer.
 */
export async function changeUiDesign(
  next: UiDesign,
  options: { save?: (design: UiDesign) => Promise<SettingsResponseDto>; onSaved?: (settings: SettingsResponseDto) => void } = {}
): Promise<UiDesignChange> {
  const save = options.save ?? ((design: UiDesign) => updateSettings({ uiDesign: design }))
  const previous = current
  pendingSaves += 1
  apply(next, true)
  try {
    const settings = await save(next)
    options.onSaved?.(settings)
    return { ok: true }
  } catch (error) {
    if (current === next) apply(previous, true)
    return { ok: false, error }
  } finally {
    pendingSaves -= 1
  }
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function getSnapshot(): UiDesign {
  return current
}

// Another window of this PlatinVault changed the design (a second tab, the overlay of the Windows program): follow it
if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key !== UI_DESIGN_STORAGE_KEY) return
    const design = parseUiDesign(event.newValue)
    if (design && design !== current) {
      current = design
      applyToDocument(design)
      emit()
    }
  })
}

export interface UiDesignApi {
  design: UiDesign
  // True while a change is being saved
  saving: boolean
  // Applies at once and saves; resolves with the outcome (never rejects)
  change: (next: UiDesign) => Promise<UiDesignChange>
}

/** The active design and the one way to change it (keeps the cached settings in step with the saved value). */
export function useUiDesign(): UiDesignApi {
  const design = useSyncExternalStore(subscribe, getSnapshot, () => UI_DESIGN_DEFAULT)
  const queryClient = useQueryClient()
  const [saving, setSaving] = useState(false)

  const change = useCallback(
    async (next: UiDesign) => {
      setSaving(true)
      // The cached settings show the new value at once, so nothing that reads them switches the design back meanwhile
      const before = queryClient.getQueryData<SettingsResponseDto>(['settings'])
      if (before) queryClient.setQueryData<SettingsResponseDto>(['settings'], { ...before, uiDesign: next })
      const result = await changeUiDesign(next, {
        onSaved: (settings) => {
          const cached = queryClient.getQueryData<SettingsResponseDto>(['settings'])
          // An older server does not know the field: keep what was chosen in the cache
          queryClient.setQueryData<SettingsResponseDto>(['settings'], { ...cached, ...settings, uiDesign: parseUiDesign(settings.uiDesign) ?? next })
        },
      })
      if (!result.ok && before) {
        const cached = queryClient.getQueryData<SettingsResponseDto>(['settings'])
        if (cached) queryClient.setQueryData<SettingsResponseDto>(['settings'], { ...cached, uiDesign: before.uiDesign })
      }
      setSaving(false)
      return result
    },
    [queryClient]
  )

  return { design, saving, change }
}

/** Renders nothing: copies the server's design into this browser once the settings are loaded. Mounted once in App.tsx. */
export function UiDesignSync(): null {
  const { data } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  const serverDesign = data?.uiDesign
  useEffect(() => {
    syncUiDesignFromServer(serverDesign)
  }, [serverDesign])
  return null
}
