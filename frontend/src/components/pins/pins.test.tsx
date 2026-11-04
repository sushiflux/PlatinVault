import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { LiveAchievementItemDto } from '../../api/types'
import { PinIconButton } from './PinIconButton'
import { PinsProvider, pinsKey, usePinCount, usePins, type PinsFailure } from './pinsContext'
import { PINS_COLLAPSED_KEY, PINS_COLUMN_QUERY, PINS_PHONE_QUERY } from './pinsLayout'
import { PinsSurface } from './PinsSurface'

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const APP_ID = 440

function ach(apiName: string, patch: Partial<LiveAchievementItemDto> = {}): LiveAchievementItemDto {
  return {
    apiName,
    displayName: `Erfolg ${apiName}`,
    description: `Beschreibung ${apiName}`,
    originalDescription: null,
    isTranslated: false,
    iconUrl: `/icons/${apiName}.png`,
    iconGrayUrl: `/icons/${apiName}-gray.png`,
    achieved: false,
    unlockTime: null,
    globalPercent: 12,
    hasGlobalPercent: true,
    rarityTier: 'Silver',
    rarityLabel: '',
    isHidden: false,
    userNote: '',
    ...patch,
  }
}

const names = (count: number) => Array.from({ length: count }, (_, index) => `A${index + 1}`)
const list = (count: number, patch: (apiName: string) => Partial<LiveAchievementItemDto> = () => ({})) => names(count).map((name) => ach(name, patch(name)))

// The server of "Im Blick" as a stateful fake: pins in order, the maximum, an optional failure for every change
interface FakeServer {
  pins: string[]
  max: number
  failWith: { status: number; detail: string } | null
  calls: { method: string; url: string; body: unknown; csrf: string | null }[]
}

function stubServer(server: FakeServer) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const target = new URL(String(url), 'http://localhost')
      const method = init?.method ?? 'GET'
      if (target.pathname === '/api/settings') return json({ achievementNotesEnabled: true })
      if (!target.pathname.startsWith('/api/live/pins')) return json({}, 404)
      const body = init?.body ? (JSON.parse(String(init.body)) as { apiName?: string; apiNames?: string[] }) : null
      server.calls.push({ method, url: `${target.pathname}${target.search}`, body, csrf: new Headers(init?.headers).get('X-Requested-With') })
      const state = () => json({ appId: APP_ID, apiNames: server.pins, max: server.max })
      if (method === 'GET') return state()
      if (server.failWith) return json({ detail: server.failWith.detail }, server.failWith.status)
      if (target.pathname.endsWith('/remove')) {
        const gone = new Set(body?.apiNames ?? [])
        server.pins = server.pins.filter((name) => !gone.has(name))
      } else if (method === 'POST') {
        if (server.pins.length >= server.max) return json({ detail: `Es sind schon ${server.max} Erfolge im Blick.` }, 400)
        if (body?.apiName && !server.pins.includes(body.apiName)) server.pins = [...server.pins, body.apiName]
      } else if (method === 'DELETE') {
        const apiName = target.searchParams.get('apiName')
        server.pins = server.pins.filter((name) => name !== apiName)
      }
      return state()
    })
  )
}

function newServer(pins: string[] = [], max = 10): FakeServer {
  return { pins, max, failWith: null, calls: [] }
}

// jsdom has no matchMedia: the wide landscape query answers `column`, everything else (reduced motion, ...) no
function stubWindow(column: boolean, phone = false) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === PINS_COLUMN_QUERY ? column : query === PINS_PHONE_QUERY ? phone : false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

// One pin button per achievement (the way the trophy lists use it), plus both places of "Im Blick"
function PinButtons({ achievements }: { achievements: LiveAchievementItemDto[] }) {
  const pins = usePins()
  if (!pins) return null
  return (
    <ul data-testid="pin-list">
      {achievements.map((entry) => (
        <li key={entry.apiName}>
          <PinIconButton
            name={entry.displayName}
            pin={{ pinned: pins.isPinned(entry.apiName), canPin: !pins.full, max: pins.max, onToggle: () => pins.toggle(entry.apiName) }}
          />
        </li>
      ))}
    </ul>
  )
}

function Harness({ achievements, tips = {}, onError }: { achievements: LiveAchievementItemDto[]; tips?: Record<string, string>; onError: (failure: PinsFailure) => void }) {
  return (
    <PinsProvider appId={APP_ID} onError={onError}>
      <PinButtons achievements={achievements} />
      <PinsSurface placement="column" achievements={achievements} tips={tips} />
      <PinsSurface placement="dock" achievements={achievements} tips={tips} />
    </PinsProvider>
  )
}

function renderHarness(achievements: LiveAchievementItemDto[], options: { onError?: (failure: PinsFailure) => void; tips?: Record<string, string>; client?: QueryClient } = {}) {
  const client = options.client ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onError = options.onError ?? vi.fn()
  const view = (items: LiveAchievementItemDto[]) => (
    <QueryClientProvider client={client}>
      <Harness achievements={items} tips={options.tips} onError={onError} />
    </QueryClientProvider>
  )
  const result = render(view(achievements))
  return { ...result, client, onError, rerenderWith: (items: LiveAchievementItemDto[]) => result.rerender(view(items)) }
}

// The pin button of the trophy list (not the x of a card, which has the same name)
const pinButton = (name: string) => within(screen.getByTestId('pin-list')).getByRole('button', { name: new RegExp(`„${name}“ (im Blick behalten|nicht mehr im Blick)`) })

beforeEach(() => {
  localStorage.clear()
  stubWindow(false)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('pinning an achievement', () => {
  it('pins and unpins with the CSRF header, updates the button and shows the card', async () => {
    const server = newServer()
    stubServer(server)
    renderHarness(list(3))

    // No pin: neither the dock nor the column exists
    await waitFor(() => expect(server.calls.length).toBeGreaterThan(0))
    expect(screen.queryByTestId('pins-dock')).toBeNull()
    expect(screen.queryByTestId('pins-column')).toBeNull()

    fireEvent.click(pinButton('Erfolg A2'))
    // Optimistic: the button and the dock change with the click, not with the server's answer
    await waitFor(() => expect(pinButton('Erfolg A2').getAttribute('aria-label')).toBe('„Erfolg A2“ nicht mehr im Blick'))
    await waitFor(() => expect(screen.getByTestId('pins-dock')).toBeTruthy())
    expect(screen.getAllByTestId('pin-card').map((card) => card.getAttribute('data-api-name'))).toEqual(['A2'])
    const post = server.calls.find((call) => call.method === 'POST')
    expect(post?.body).toEqual({ appId: APP_ID, apiName: 'A2' })
    expect(post?.csrf).toBe('PlatinVault')
    expect(screen.getByText('1/10')).toBeTruthy()

    fireEvent.click(pinButton('Erfolg A2'))
    await waitFor(() => expect(server.pins).toEqual([]))
    expect(server.calls.find((call) => call.method === 'DELETE')?.url).toBe(`/api/live/pins?appId=${APP_ID}&apiName=A2`)
    expect(server.calls.find((call) => call.method === 'DELETE')?.csrf).toBe('PlatinVault')
    await waitFor(() => expect(screen.queryByTestId('pins-dock')).toBeNull())
  })

  it('keeps the order of the pins as the server has it', async () => {
    stubServer(newServer(['A3', 'A1']))
    renderHarness(list(3))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(2))
    expect(screen.getAllByTestId('pin-card').map((card) => card.getAttribute('data-api-name'))).toEqual(['A3', 'A1'])
  })

  it('disables the button of an unpinned achievement at 10 pins and says why in its tooltip; pinned ones can still be removed', async () => {
    const server = newServer(names(10))
    stubServer(server)
    renderHarness(list(11))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(10))

    const blocked = pinButton('Erfolg A11')
    expect(blocked.getAttribute('aria-disabled')).toBe('true')
    const callsBefore = server.calls.length
    fireEvent.click(blocked)
    expect(server.calls.length).toBe(callsBefore)
    expect(server.pins).toHaveLength(10)

    // Our own tooltip (no native title) names the reason
    fireEvent.pointerEnter(blocked, { pointerType: 'mouse' })
    expect(await screen.findByText('Es sind schon 10 Erfolge im Blick – nimm zuerst einen heraus.')).toBeTruthy()
    expect(blocked.getAttribute('title')).toBeNull()

    // A pinned one stays usable
    expect(pinButton('Erfolg A3').getAttribute('aria-disabled')).toBeNull()
    fireEvent.click(pinButton('Erfolg A3'))
    await waitFor(() => expect(server.pins).not.toContain('A3'))
  })

  it('rolls back and hands the complete server message to the page when the server refuses', async () => {
    const server = newServer()
    const reason = 'Es sind schon 10 Erfolge im Blick. Nimm zuerst einen heraus, bevor du „Erfolg A1“ anheftest – dieser lange Satz wird nicht gekürzt.'
    server.failWith = { status: 400, detail: reason }
    stubServer(server)
    const onError = vi.fn()
    renderHarness(list(2), { onError })
    await waitFor(() => expect(server.calls.length).toBeGreaterThan(0))

    fireEvent.click(pinButton('Erfolg A1'))
    await waitFor(() => expect(onError).toHaveBeenCalledTimes(1))
    const failure = onError.mock.calls[0]![0] as PinsFailure
    expect(failure.message).toBe(reason)
    expect(failure.title).toBe('Erfolg konnte nicht in „Im Blick“ aufgenommen werden')
    expect(failure.code).toBe('HTTP 400')
    // Back to what the server has: nothing pinned
    await waitFor(() => expect(pinButton('Erfolg A1').getAttribute('aria-label')).toBe('„Erfolg A1“ im Blick behalten'))
    expect(screen.queryByTestId('pins-dock')).toBeNull()
  })

  it('follows a change made elsewhere (PinsChanged invalidates the query)', async () => {
    const server = newServer(['A1'])
    stubServer(server)
    const { client } = renderHarness(list(3))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(1))

    server.pins = ['A1', 'A3']
    await act(async () => {
      await client.invalidateQueries({ queryKey: pinsKey(APP_ID) })
    })
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(2))
  })
})

describe('where "Im Blick" is drawn', () => {
  it('is the dock at the bottom in portrait or a narrow window', async () => {
    stubServer(newServer(['A1']))
    stubWindow(false)
    renderHarness(list(2))
    expect(await screen.findByTestId('pins-dock')).toBeTruthy()
    expect(screen.queryByTestId('pins-column')).toBeNull()
  })

  it('is the column on the right in a wide landscape window', async () => {
    stubServer(newServer(['A1']))
    stubWindow(true)
    renderHarness(list(2))
    expect(await screen.findByTestId('pins-column')).toBeTruthy()
    expect(screen.queryByTestId('pins-dock')).toBeNull()
  })

  it('publishes the height of the dock as --pv-dock-h and takes it away again', async () => {
    stubServer(newServer(['A1']))
    const { unmount } = renderHarness(list(2))
    await screen.findByTestId('pins-dock')
    expect(document.documentElement.style.getPropertyValue('--pv-dock-h')).not.toBe('')
    unmount()
    expect(document.documentElement.style.getPropertyValue('--pv-dock-h')).toBe('')
  })
})

describe('collapsing', () => {
  it('collapses the dock to one slim line and remembers it per layout', async () => {
    stubServer(newServer(['A1', 'A2', 'A3', 'A4']))
    const first = renderHarness(list(5))
    const dock = await screen.findByTestId('pins-dock')
    expect(within(dock).getAllByTestId('pin-card')).toHaveLength(4)
    expect(within(dock).getByText('4/10')).toBeTruthy()

    fireEvent.click(within(dock).getByRole('button', { name: '„Im Blick“ einklappen' }))
    expect(screen.queryByTestId('pin-card')).toBeNull()
    // The slim line still names the pins and the count
    expect(screen.getByTestId('pins-dock').textContent).toContain('Im Blick')
    expect(screen.getByTestId('pins-dock').textContent).toContain('4/10')
    // Only the choice that was made is stored
    expect(JSON.parse(localStorage.getItem(PINS_COLLAPSED_KEY) ?? '{}')).toEqual({ dock: true })
    first.unmount()

    // Reload: still collapsed; the column (other layout) is not
    stubWindow(false)
    renderHarness(list(5))
    await screen.findByTestId('pins-dock')
    expect(screen.queryByTestId('pin-card')).toBeNull()
    cleanup()
    stubWindow(true)
    renderHarness(list(5))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(4))
  })

  it('starts the dock collapsed on a phone; the choice of the player is remembered from then on', async () => {
    stubWindow(false, true)
    stubServer(newServer(['A1', 'A2']))
    const first = renderHarness(list(3))
    const dock = await screen.findByTestId('pins-dock')
    expect(screen.queryByTestId('pin-card')).toBeNull()
    expect(dock.textContent).toContain('2/10')
    expect(localStorage.getItem(PINS_COLLAPSED_KEY)).toBeNull()

    fireEvent.click(within(dock).getByRole('button', { name: '„Im Blick“ ausklappen' }))
    expect(screen.getAllByTestId('pin-card')).toHaveLength(2)
    expect(JSON.parse(localStorage.getItem(PINS_COLLAPSED_KEY) ?? '{}')).toEqual({ dock: false })
    first.unmount()

    // Expanded by choice stays expanded on the phone
    renderHarness(list(3))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(2))
  })

  it('does not collapse the dock by default on a wider window or the column on a phone-sized query', async () => {
    stubWindow(false, false)
    stubServer(newServer(['A1']))
    renderHarness(list(2))
    await screen.findByTestId('pin-card')
  })

  it('collapses the column to small icons and expands it again', async () => {
    stubWindow(true)
    stubServer(newServer(['A1', 'A2']))
    renderHarness(list(3))
    const column = await screen.findByTestId('pins-column')
    fireEvent.click(within(column).getByRole('button', { name: '„Im Blick“ einklappen' }))
    expect(screen.getByTestId('pins-column').getAttribute('data-collapsed')).toBe('true')
    expect(screen.queryByTestId('pin-card')).toBeNull()
    expect(screen.getAllByTestId('pin-mini')).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: '„Im Blick“ ausklappen' }))
    expect(screen.getAllByTestId('pin-card')).toHaveLength(2)
    expect(JSON.parse(localStorage.getItem(PINS_COLLAPSED_KEY) ?? '{}').column).toBe(false)
  })

  it('survives a localStorage that throws', async () => {
    stubServer(newServer(['A1']))
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    renderHarness(list(2))
    const dock = await screen.findByTestId('pins-dock')
    fireEvent.click(within(dock).getByRole('button', { name: '„Im Blick“ einklappen' }))
    expect(screen.queryByTestId('pin-card')).toBeNull()
    getItem.mockRestore()
    setItem.mockRestore()
  })
})

describe('the cards', () => {
  it('shows a counter achievement with its progress and updates it live', async () => {
    stubServer(newServer(['A1']))
    const { rerenderWith } = renderHarness([ach('A1', { progressCurrent: 312, progressMax: 500 })])
    const card = await screen.findByTestId('pin-card')
    expect(within(card).getByText('312 / 500')).toBeTruthy()
    expect(within(card).getByRole('progressbar').getAttribute('aria-valuenow')).toBe('62')
    expect(card.hasAttribute('data-done')).toBe(false)
    expect(within(card).getByText('312 / 500').textContent).toBe('312 / 500')

    rerenderWith([ach('A1', { progressCurrent: 400, progressMax: 500 })])
    expect(within(screen.getByTestId('pin-card')).getByText('400 / 500')).toBeTruthy()
    // Same card, same element: nothing was rebuilt around it
    expect(screen.getByTestId('pin-card')).toBe(card)
  })

  it('shows an open achievement without a counter as "Noch offen"', async () => {
    stubServer(newServer(['A1']))
    renderHarness(list(1))
    const card = await screen.findByTestId('pin-card')
    expect(within(card).getByText('Noch offen')).toBeTruthy()
    expect(within(card).queryByRole('progressbar')).toBeNull()
  })

  it('shows an unlocked pin as done (gold card with a check) and offers "Geschaffte entfernen" for exactly those', async () => {
    const server = newServer(['A1', 'A2', 'A3'])
    stubServer(server)
    renderHarness(list(3, (name) => (name === 'A2' || name === 'A3' ? { achieved: true, unlockTime: '2026-11-04T10:00:00Z' } : {})))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(3))
    const cards = screen.getAllByTestId('pin-card')
    expect(cards.map((card) => card.hasAttribute('data-done'))).toEqual([false, true, true])
    expect(within(cards[1]!).getByText('Geschafft')).toBeTruthy()

    const clear = screen.getByRole('button', { name: 'Geschaffte entfernen' })
    expect(clear.hasAttribute('disabled')).toBe(false)
    fireEvent.click(clear)
    await waitFor(() => expect(server.pins).toEqual(['A1']))
    const call = server.calls.find((entry) => entry.url === '/api/live/pins/remove')
    expect(call?.body).toEqual({ appId: APP_ID, apiNames: ['A2', 'A3'] })
    expect(call?.csrf).toBe('PlatinVault')
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(1))
  })

  it('keeps the place of "Geschaffte entfernen" but hides it while nothing is done', async () => {
    stubServer(newServer(['A1']))
    renderHarness(list(2))
    await screen.findByTestId('pin-card')
    // Not in the accessibility tree: the button is invisible and out of reach
    expect(screen.queryByRole('button', { name: 'Geschaffte entfernen' })).toBeNull()
    const hidden = document.querySelector('.pv-pins-clear') as HTMLButtonElement
    expect(hidden.hasAttribute('data-idle')).toBe(true)
    expect(hidden.disabled).toBe(true)
  })

  it('removes a single pin with the x of its card', async () => {
    const server = newServer(['A1', 'A2'])
    stubServer(server)
    renderHarness(list(2))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(2))
    fireEvent.click(within(screen.getAllByTestId('pin-card')[0]!).getByRole('button', { name: '„Erfolg A1“ nicht mehr im Blick' }))
    await waitFor(() => expect(server.pins).toEqual(['A2']))
  })

  it('opens the detail dialog of the achievement on a click, with the guide tip, the pin button and the note', async () => {
    stubServer(newServer(['A1']))
    renderHarness([ach('A1', { displayName: 'Master of Fate', description: 'Beende das Spiel.' })], { tips: { A1: 'Im Kapitel 3 den Schlüssel holen.' } })
    const card = await screen.findByTestId('pin-card')
    fireEvent.click(within(card).getByRole('button', { name: /^Master of Fate, Silber, noch offen/ }))

    const dialog = await screen.findByRole('dialog', { name: 'Master of Fate' })
    expect(within(dialog).getByText('Beende das Spiel.')).toBeTruthy()
    expect(dialog.textContent).toContain('Im Kapitel 3 den Schlüssel holen.')
    // The pin button is part of the dialog and shows the pinned state
    expect(within(dialog).getByRole('button', { name: '„Master of Fate“ nicht mehr im Blick' })).toBeTruthy()
    expect(within(dialog).getByRole('button', { name: 'Notiz hinzufügen' })).toBeTruthy()

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('opens the note editor from the dialog of the dock', async () => {
    stubServer(newServer(['A1']))
    renderHarness([ach('A1', { displayName: 'Master of Fate' })])
    fireEvent.click(within(await screen.findByTestId('pin-card')).getByRole('button', { name: /^Master of Fate/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Master of Fate' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Notiz hinzufügen' }))
    expect(await screen.findByText('Persönliche Notiz')).toBeTruthy()
  })

  it('opens the same dialog from a small icon of the collapsed column', async () => {
    stubWindow(true)
    stubServer(newServer(['A1']))
    renderHarness([ach('A1', { displayName: 'Master of Fate' })])
    const column = await screen.findByTestId('pins-column')
    fireEvent.click(within(column).getByRole('button', { name: '„Im Blick“ einklappen' }))
    fireEvent.click(screen.getByTestId('pin-mini'))
    expect(await screen.findByRole('dialog', { name: 'Master of Fate' })).toBeTruthy()
  })
})

describe('leaving when the last pin goes', () => {
  it('the dock leaves after the last pin was removed', async () => {
    const server = newServer(['A1'])
    stubServer(server)
    renderHarness(list(1))
    await screen.findByTestId('pins-dock')
    fireEvent.click(within(screen.getByTestId('pin-card')).getByRole('button', { name: '„Erfolg A1“ nicht mehr im Blick' }))
    await waitFor(() => expect(screen.queryByTestId('pins-dock')).toBeNull())
    expect(document.documentElement.style.getPropertyValue('--pv-dock-h')).toBe('')
  })

  it('the column is marked as leaving first, then removed', async () => {
    stubWindow(true)
    stubServer(newServer(['A1']))
    renderHarness(list(1))
    await screen.findByTestId('pins-column')
    fireEvent.click(within(screen.getByTestId('pin-card')).getByRole('button', { name: '„Erfolg A1“ nicht mehr im Blick' }))
    await waitFor(() => expect(screen.getByTestId('pins-column').hasAttribute('data-leaving')).toBe(true))
    await waitFor(() => expect(screen.queryByTestId('pins-column')).toBeNull())
  })
})

describe('usePinCount', () => {
  function Count({ appId }: { appId: number | null }) {
    return <span data-testid="count">{usePinCount(appId)}</span>
  }

  it('is 0 without a game and the number of pins with one', async () => {
    stubServer(newServer(['A1', 'A2', 'A3']))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { rerender } = render(
      <QueryClientProvider client={client}>
        <Count appId={null} />
      </QueryClientProvider>
    )
    expect(screen.getByTestId('count').textContent).toBe('0')
    rerender(
      <QueryClientProvider client={client}>
        <Count appId={APP_ID} />
      </QueryClientProvider>
    )
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('3'))
  })
})

describe('"remove after unlock": the server removes the pin', () => {
  it('keeps the card as done for a moment (leaving), without an x, instead of letting it vanish', async () => {
    const server = newServer(['A1', 'A2'])
    stubServer(server)
    const { client, rerenderWith } = renderHarness(list(2))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(2))

    // A1 is unlocked; the server takes it out of the pins and says so (PinsChanged -> the query is refreshed)
    server.pins = ['A2']
    rerenderWith(list(2, (name) => (name === 'A1' ? { achieved: true } : {})))
    await act(async () => {
      await client.invalidateQueries({ queryKey: pinsKey(APP_ID) })
    })
    await waitFor(() => expect(screen.getByTestId('pins-dock').textContent).toContain('1/10'))

    const cards = screen.getAllByTestId('pin-card')
    expect(cards.map((card) => card.getAttribute('data-api-name'))).toEqual(['A1', 'A2'])
    const leaving = cards[0]!
    expect(leaving.hasAttribute('data-leaving')).toBe(true)
    expect(leaving.hasAttribute('data-done')).toBe(true)
    expect(within(leaving).getByText('Geschafft')).toBeTruthy()
    expect(within(leaving).queryByRole('button', { name: /nicht mehr im Blick/ })).toBeNull()
    // It does not count as pinned and cannot be cleared again
    expect(screen.queryByRole('button', { name: 'Geschaffte entfernen' })).toBeNull()
  })

  it('does not keep a card for a pin the player removed here', async () => {
    const server = newServer(['A1', 'A2'])
    stubServer(server)
    renderHarness(list(2, (name) => (name === 'A1' ? { achieved: true } : {})))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(2))
    fireEvent.click(screen.getByRole('button', { name: 'Geschaffte entfernen' }))
    await waitFor(() => expect(server.pins).toEqual(['A2']))
    await waitFor(() => expect(screen.getAllByTestId('pin-card')).toHaveLength(1))
    expect(screen.getByTestId('pin-card').getAttribute('data-api-name')).toBe('A2')
  })
})
