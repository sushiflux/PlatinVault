import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SettingsResponseDto } from '../../api/types'
import { applyLanguage } from '../../i18n'
import { syncUiDesignFromServer } from '../../utils/uiDesign'
import { DesignSection } from './DesignSection'

const fetchSettings = vi.hoisted(() => vi.fn<() => Promise<SettingsResponseDto>>())
const updateSettings = vi.hoisted(() => vi.fn())
vi.mock('../../api/client', () => ({ fetchSettings, updateSettings }))

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <DesignSection />
    </QueryClientProvider>
  )
}

beforeEach(() => {
  fetchSettings.mockReset()
  updateSettings.mockReset()
  localStorage.clear()
  syncUiDesignFromServer('light')
})

afterEach(async () => {
  cleanup()
  await applyLanguage('de')
})

describe('DesignSection', () => {
  it('shows both designs in German with the hint of the selected one and the scope line', () => {
    renderSection()
    const group = screen.getByRole('group', { name: 'Aussehen' })
    expect(within(group).getByRole('button', { name: 'Leicht' }).getAttribute('aria-pressed')).toBe('true')
    expect(within(group).getByRole('button', { name: 'Klassisch' }).getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByText('Ruhige Flächen, dünne Linien, Schrift Inter.')).toBeTruthy()
    expect(screen.getByText('Gilt für dieses PlatinVault auf allen Geräten, im Browser und im Windows-Programm.')).toBeTruthy()
  })

  it('speaks English', async () => {
    await applyLanguage('en')
    renderSection()
    const group = screen.getByRole('group', { name: 'Look' })
    expect(within(group).getByRole('button', { name: 'Minimal' }).getAttribute('aria-pressed')).toBe('true')
    expect(within(group).getByRole('button', { name: 'Classic' }).getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByText('Calm surfaces, thin lines, the Inter typeface.')).toBeTruthy()
  })

  it('switching applies at once and saves the choice', async () => {
    updateSettings.mockResolvedValue({ uiDesign: 'classic' })
    renderSection()
    fireEvent.click(screen.getByRole('button', { name: 'Klassisch' }))
    expect(document.documentElement.dataset.design).toBe('classic')
    expect(screen.getByRole('button', { name: 'Klassisch' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByText('Der frühere Look mit runderen Ecken, kräftigerer Schrift, Verläufen und Glanz.')).toBeTruthy()
    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith({ uiDesign: 'classic' }))
    // Choosing the active one again does nothing
    fireEvent.click(screen.getByRole('button', { name: 'Klassisch' }))
    expect(updateSettings).toHaveBeenCalledTimes(1)
  })

  it('a failed save brings the previous design back and shows the complete message with a copy button in a floating notice', async () => {
    updateSettings.mockRejectedValue(new Error('PlatinVault-Server nicht erreichbar (HTTP 503)'))
    renderSection()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Klassisch' }))
    })
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Das Design wurde nicht gespeichert'))
    expect(screen.getByRole('alert').textContent).toContain('Das vorherige Design ist wieder aktiv. Grund: PlatinVault-Server nicht erreichbar (HTTP 503)')
    // Laid over the page (portal in the body), not a line inside the section
    expect(screen.getByTestId('design-section').contains(screen.getByRole('alert'))).toBe(false)
    expect(within(screen.getByRole('alert')).getByTestId('copy-error')).toBeTruthy()
    expect(document.documentElement.dataset.design).toBe('light')
    expect(screen.getByRole('button', { name: 'Leicht' }).getAttribute('aria-pressed')).toBe('true')
  })
})
