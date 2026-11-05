import { useState } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyLanguage } from '../../i18n'
import type { UpdateReleaseDto } from '../../api/types'
import fixture from './testdata/release-v2026.11.14.md?raw'
import { ReleaseNotesViewer } from './ReleaseNotesDialog'

const page = 'https://github.com/sushiflux/PlatinVault/releases/tag/v2026.11.14'
const release: UpdateReleaseDto = {
  tag: 'v2026.11.14',
  display: 'v2026.11.14 (15.116022)',
  isBeta: false,
  notesUrl: page,
  notes: fixture,
  publishedAt: '2026-11-03T10:00:00Z',
  hasInstaller: true,
}

const body = [
  '## v2026.11.20 (16.300 β) · Unraid',
  '',
  'Short summary.',
  '',
  '### 🔄 Change',
  '- Changed **a thing**.',
  '',
  '### 🔧 Fix',
  '- Fixed `x`, see [docs](https://example.org/d).',
  '',
  '### 🔒 Security',
  '- Safer.',
  '',
  '---',
  '',
  '## v2026.11.20 (16.300 β) · Unraid',
  '',
  'Kurze Zusammenfassung.',
  '',
  '### 🔄 Geändert',
  '- Eine Sache geändert.',
  '',
  '### 🔧 Behoben',
  '- Etwas behoben.',
].join('\n')

function Harness({ value = release, onClosed }: { value?: UpdateReleaseDto; onClosed?: () => void }) {
  const [open, setOpen] = useState(true)
  return (
    <>
      <button type="button">opener</button>
      <ReleaseNotesViewer
        release={open ? value : null}
        onClose={() => {
          setOpen(false)
          onClosed?.()
        }}
      />
    </>
  )
}

beforeEach(async () => {
  await applyLanguage('de')
})
afterEach(async () => {
  cleanup()
  vi.unstubAllGlobals()
  await applyLanguage('de')
})

describe('ReleaseNotesViewer', () => {
  it('shows version, date, summary and the categories of the real entry in German', async () => {
    render(<Harness />)
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe('Das ist neu')
    expect(screen.getByTestId('release-notes-version').textContent).toBe('v2026.11.14 (15.116022)')
    expect(dialog.textContent).toContain('veröffentlicht am 3. November 2026')
    expect(screen.getByText(/PlatinVault bringt deine Steam-Erfolge/)).toBeTruthy()
    expect(screen.getByRole('heading', { level: 3 }).textContent).toContain('Neu')
    expect(screen.getByRole('heading', { level: 3 }).textContent).toContain('13 Punkte')
    // **Bold lead:** is rendered as an element, not as asterisks
    expect(dialog.querySelector('strong')?.textContent).toBe('Open Source:')
    expect(dialog.textContent).not.toContain('**')
    expect(dialog.querySelectorAll('li')).toHaveLength(13)
  })

  it('makes no request of its own (nothing is sent to GitHub or anywhere else)', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    render(<Harness />)
    await screen.findByRole('dialog')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('shows the English block in English and the other categories with their own names', async () => {
    await applyLanguage('en')
    render(<Harness value={{ ...release, notes: body, isBeta: true, display: 'v2026.11.20 (16.300 β)' }} />)
    expect(await screen.findByText('Short summary.')).toBeTruthy()
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe('What’s new')
    const headings = screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)
    expect(headings[0]).toContain('Changed')
    expect(headings[1]).toContain('Fixed')
    expect(headings[2]).toContain('Security')
    expect(screen.getByText('Beta')).toBeTruthy()
    expect(screen.getByRole('dialog').textContent).toContain('for Unraid')
    // Links come out as safe external links, code as <code>
    const link = screen.getByRole('link', { name: 'docs' })
    expect(link.getAttribute('href')).toBe('https://example.org/d')
    expect(link.getAttribute('rel')).toContain('noopener')
    expect(screen.getByText('x').tagName).toBe('CODE')
  })

  it('has a quiet GitHub link in the footer', () => {
    render(<Harness />)
    const link = screen.getByRole('link', { name: /auf GitHub ansehen/ })
    expect(link.getAttribute('href')).toBe(page)
    expect(link.getAttribute('target')).toBe('_blank')
    expect(link.getAttribute('rel')).toBe('noopener noreferrer')
  })

  it('shows the code signing policy of a release footer as a second quiet link, and nothing for an old release', () => {
    const policy = 'https://github.com/sushiflux/PlatinVault#code-signing-policy'
    const { unmount } = render(<Harness value={{ ...release, notes: `${body}\n\n---\n\nCode signing policy: ${policy}\n` }} />)
    const link = screen.getByRole('link', { name: /Richtlinie zur Code-Signierung/ })
    expect(link.getAttribute('href')).toBe(policy)
    expect(link.getAttribute('rel')).toBe('noopener noreferrer')
    // The footer is no part of the German block
    expect(screen.getByRole('dialog').textContent).not.toContain('Code signing policy:')
    unmount()
    cleanup()
    render(<Harness value={{ ...release, notes: body }} />)
    expect(screen.getByText('Kurze Zusammenfassung.')).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Code-Signierung/ })).toBeNull()
  })

  it('Esc closes; focus starts on Schließen', async () => {
    const closed = vi.fn()
    render(<Harness onClosed={closed} />)
    await screen.findByRole('dialog')
    await waitFor(() => expect(document.activeElement?.textContent).toBe('Schließen'))
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(closed).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('a press on the backdrop closes, a press inside does not', async () => {
    const closed = vi.fn()
    render(<Harness onClosed={closed} />)
    const dialog = await screen.findByRole('dialog')
    fireEvent.mouseDown(dialog)
    expect(closed).not.toHaveBeenCalled()
    fireEvent.mouseDown(dialog.parentElement as HTMLElement)
    expect(closed).toHaveBeenCalledTimes(1)
  })

  it('the Close button closes', async () => {
    const closed = vi.fn()
    render(<Harness onClosed={closed} />)
    await screen.findByRole('dialog')
    fireEvent.click(screen.getAllByRole('button', { name: 'Schließen' }).at(-1) as HTMLElement)
    expect(closed).toHaveBeenCalledTimes(1)
  })

  it('Tab stays inside the dialog (focus trap)', async () => {
    render(<Harness />)
    const dialog = await screen.findByRole('dialog')
    const close = screen.getAllByRole('button', { name: 'Schließen' }).at(-1) as HTMLElement
    // jsdom has no layout: every element counts as visible for the trap
    for (const node of dialog.querySelectorAll('*')) Object.defineProperty(node, 'offsetParent', { configurable: true, get: () => document.body })
    close.focus()
    // The footer button is the last focusable element: Tab wraps to the first one inside the dialog
    fireEvent.keyDown(dialog, { key: 'Tab' })
    expect(dialog.contains(document.activeElement)).toBe(true)
    expect(document.activeElement).not.toBe(close)
    fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(close)
  })

  it('says so when the notes text has nothing to show', () => {
    render(<Harness value={{ ...release, notes: '<!-- nothing -->\n\n---\n' }} />)
    expect(screen.getByRole('status').textContent).toContain('keine Notizen')
  })

  it('renders hostile notes (shipped changelog or a release body) as text only: no HTML elements, no script or data links', async () => {
    await applyLanguage('en')
    const hostile = [
      '## v2026.11.19 (3.1601)',
      '',
      '<script>alert(1)</script>',
      '',
      '### ✨ New',
      '',
      '- <img src=x onerror=alert(1)> image',
      '- [click](javascript:alert(1)) js link',
      '- [data](data:text/html,<script>alert(1)</script>) data link',
      '- [ok](https://example.com/" onmouseover="alert(1)) quote link',
      '- javascript:alert(2) bare',
      '- <svg onload=alert(1)> **<b>bold</b>** `<i>code</i>`',
      '',
      '---',
      'Code signing policy: javascript:alert(3)',
    ].join('\n')
    render(<Harness value={{ ...release, notesUrl: 'javascript:alert(4)', notes: hostile }} />)
    const dialog = screen.getByTestId('release-notes')
    expect(dialog.querySelector('script, img, svg:not(.tabler-icon), b, i, iframe, object, embed')).toBeNull()
    expect(dialog.querySelectorAll('[onerror], [onload], [onmouseover]')).toHaveLength(0)
    for (const link of dialog.querySelectorAll('a')) {
      expect(link.getAttribute('href') ?? '').toMatch(/^https?:\/\//)
    }
    // The payloads are still readable as plain text
    expect(dialog.textContent).toContain('<img src=x onerror=alert(1)>')
  })
})
