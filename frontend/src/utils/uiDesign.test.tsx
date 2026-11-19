import { cleanup, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SettingsResponseDto } from '../api/types'

const fetchSettings = vi.hoisted(() => vi.fn<() => Promise<SettingsResponseDto>>())
const updateSettings = vi.hoisted(() => vi.fn())
vi.mock('../api/client', () => ({ fetchSettings, updateSettings }))

const KEY = 'pv_ui_design_v1'

// The module keeps the active design in module state: every test gets a fresh copy, loaded after the stage is set
async function load() {
  vi.resetModules()
  return import('./uiDesign')
}

beforeEach(() => {
  localStorage.clear()
  delete document.documentElement.dataset.design
  fetchSettings.mockReset()
  updateSettings.mockReset()
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('uiDesign', () => {
  it('applies the stored copy to <html data-design> when it loads, light without one', async () => {
    localStorage.setItem(KEY, 'classic')
    let mod = await load()
    expect(document.documentElement.dataset.design).toBe('classic')
    expect(mod.getUiDesign()).toBe('classic')

    localStorage.clear()
    delete document.documentElement.dataset.design
    mod = await load()
    expect(document.documentElement.dataset.design).toBe('light')
  })

  it('a change applies at once, writes the copy and saves it on the server', async () => {
    const mod = await load()
    let resolve: (value: SettingsResponseDto) => void = () => {}
    const save = vi.fn(() => new Promise<SettingsResponseDto>((r) => (resolve = r)))
    const pending = mod.changeUiDesign('classic', { save })
    // Before the server answered
    expect(document.documentElement.dataset.design).toBe('classic')
    expect(localStorage.getItem(KEY)).toBe('classic')
    expect(save).toHaveBeenCalledWith('classic')
    resolve({ uiDesign: 'classic' } as SettingsResponseDto)
    await expect(pending).resolves.toEqual({ ok: true })
  })

  it('saves through updateSettings by default', async () => {
    updateSettings.mockResolvedValue({ uiDesign: 'classic' })
    const mod = await load()
    await mod.changeUiDesign('classic')
    expect(updateSettings).toHaveBeenCalledWith({ uiDesign: 'classic' })
  })

  it('the server value wins over the local copy and is stored', async () => {
    localStorage.setItem(KEY, 'light')
    const mod = await load()
    mod.syncUiDesignFromServer('classic')
    expect(document.documentElement.dataset.design).toBe('classic')
    expect(localStorage.getItem(KEY)).toBe('classic')
  })

  it('a missing or unknown server value keeps the local copy', async () => {
    localStorage.setItem(KEY, 'classic')
    const mod = await load()
    mod.syncUiDesignFromServer(undefined)
    mod.syncUiDesignFromServer(null)
    mod.syncUiDesignFromServer('neon')
    expect(document.documentElement.dataset.design).toBe('classic')
    expect(localStorage.getItem(KEY)).toBe('classic')
  })

  it('an older settings answer never undoes a save in flight', async () => {
    const mod = await load()
    let resolve: (value: SettingsResponseDto) => void = () => {}
    const pending = mod.changeUiDesign('classic', { save: () => new Promise<SettingsResponseDto>((r) => (resolve = r)) })
    mod.syncUiDesignFromServer('light')
    expect(document.documentElement.dataset.design).toBe('classic')
    resolve({ uiDesign: 'classic' } as SettingsResponseDto)
    await pending
  })

  it('works when the browser refuses storage', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied')
    })
    const mod = await load()
    expect(mod.getUiDesign()).toBe('light')
    await expect(mod.changeUiDesign('classic', { save: async () => ({ uiDesign: 'classic' }) as SettingsResponseDto })).resolves.toEqual({ ok: true })
    expect(document.documentElement.dataset.design).toBe('classic')
    mod.syncUiDesignFromServer('light')
    expect(document.documentElement.dataset.design).toBe('light')
  })

  it('a failed save brings the previous design back and reports the error', async () => {
    const mod = await load()
    const failure = new Error('Server nicht erreichbar')
    const result = await mod.changeUiDesign('classic', { save: () => Promise.reject(failure) })
    expect(result).toEqual({ ok: false, error: failure })
    expect(document.documentElement.dataset.design).toBe('light')
    expect(localStorage.getItem(KEY)).toBe('light')
  })

  it('UiDesignSync copies the server value once the settings are loaded', async () => {
    fetchSettings.mockResolvedValue({ uiDesign: 'classic' } as SettingsResponseDto)
    const mod = await load()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <mod.UiDesignSync />
      </QueryClientProvider>
    )
    await waitFor(() => expect(document.documentElement.dataset.design).toBe('classic'))
    expect(localStorage.getItem(KEY)).toBe('classic')
  })

  it('UiDesignSync keeps the local copy when the server does not send the field', async () => {
    localStorage.setItem(KEY, 'classic')
    fetchSettings.mockResolvedValue({ uiLanguage: 'de' } as SettingsResponseDto)
    const mod = await load()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <mod.UiDesignSync />
      </QueryClientProvider>
    )
    await waitFor(() => expect(fetchSettings).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 0))
    expect(document.documentElement.dataset.design).toBe('classic')
  })
})
