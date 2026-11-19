import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { applyLanguage } from '../../i18n'
import type { IgdbGameDto, IgdbSearchHitDto } from '../../api/types'
import { AddGameDialog } from './AddGameDialog'

// The add dialog against a stubbed server: with and without Twitch credentials, the search states, the platform rules, the cover field, a duplicate.

const ASTRO: IgdbSearchHitDto = {
  igdbId: 90001,
  name: 'Astro Bot',
  releaseYear: 2024,
  firstReleaseAt: '2024-09-06T00:00:00Z',
  coverUrl: null,
  platforms: [{ name: 'PlayStation 5', abbreviation: 'PS5', maps: { platform: 'playstation', variant: 'ps5' } }],
  existing: [],
}
const ASTRONEER: IgdbSearchHitDto = {
  igdbId: 90004,
  name: 'Astroneer',
  releaseYear: 2019,
  firstReleaseAt: null,
  coverUrl: null,
  platforms: [
    { name: 'PC (Microsoft Windows)', abbreviation: 'PC', maps: null },
    { name: 'Xbox One', abbreviation: 'XONE', maps: { platform: 'xbox', variant: null } },
    { name: 'PlayStation 4', abbreviation: 'PS4', maps: { platform: 'playstation', variant: 'ps4' } },
  ],
  existing: [{ slug: 'astroneer-xbox', platform: 'xbox', variant: null }],
}

const FACTS = (hit: IgdbSearchHitDto): IgdbGameDto =>
  ({
    igdbId: hit.igdbId,
    name: hit.name,
    url: `https://www.igdb.com/games/${hit.name.toLowerCase().replace(/\s+/g, '-')}`,
    summary: null,
    storyline: null,
    aggregatedRating: 94.4,
    aggregatedRatingCount: 30,
    totalRating: null,
    totalRatingCount: null,
    rating: 90.2,
    ratingCount: 100,
    genres: ['Jump & Run', 'Plattformer'],
    themes: [],
    gameModes: [],
    playerPerspectives: [],
    franchises: [],
    collections: [],
    similarGames: [],
    coverUrl: null,
    artworks: [],
    screenshots: [],
    languages: [],
    timeToBeat: { hastilyHours: 8, normallyHours: 10, completelyHours: 14, count: 312 },
    fetchedAt: '2026-11-19T00:00:00Z',
    firstReleaseAt: hit.firstReleaseAt,
    platforms: hit.platforms,
    developers: ['Team Asobi'],
    publishers: [],
  }) as IgdbGameDto

interface Server {
  twitch: boolean
  searchAnswer: 'results' | 'empty' | 'none' | 'fail'
  createAnswer: 'ok' | 'duplicate' | 'fail'
  coverAnswer: 'ok' | 'fail'
}
let server: Server
let calls: { method: string; path: string; body: string | null }[]

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function stubServer() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const path = url.split('?')[0] ?? url
      const method = init?.method ?? 'GET'
      const body = typeof init?.body === 'string' ? init.body : init?.body ? '(binary)' : null
      calls.push({ method, path: url, body })
      if (path === '/api/settings') return json({ hasTwitchKey: server.twitch })
      if (path === '/api/igdb/search') {
        if (server.searchAnswer === 'none') return new Response(null, { status: 204 })
        if (server.searchAnswer === 'fail') return json({ detail: 'IGDB antwortet mit 503.', providerMessage: 'upstream: bad gateway' }, 502)
        const q = decodeURIComponent(url.split('q=')[1] ?? '')
        return json({ results: server.searchAnswer === 'empty' ? [] : q.includes('neer') ? [ASTRONEER] : [ASTRO, ASTRONEER] })
      }
      if (path.startsWith('/api/igdb/games/')) return json(FACTS(path.endsWith('90004') ? ASTRONEER : ASTRO))
      if (path === '/api/games/manual' && method === 'POST') {
        if (server.createAnswer === 'duplicate') {
          return json({ title: 'Schon hinzugefügt', detail: '„Astro Bot“ ist für PlayStation 5 schon in deiner Sammlung (Adresse: astro-bot).', existingSlug: 'astro-bot' }, 409)
        }
        if (server.createAnswer === 'fail') return json({ detail: 'Die Datenbank ist gesperrt.' }, 500)
        return json({ id: 'g1', slug: 'new-game', title: 'New game' }, 201)
      }
      if (path.endsWith('/cover') && method === 'PUT') {
        return server.coverAnswer === 'fail' ? json({ detail: 'Das Bild ist größer als 5 MB.' }, 413) : json({ id: 'g1', slug: 'new-game' })
      }
      return json({}, 404)
    })
  )
}

function Where() {
  return <span data-testid="where">{useLocation().pathname}</span>
}

function renderDialog() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route
            path="*"
            element={
              <>
                <Where />
                <AddGameDialog onClose={onClose} />
              </>
            }
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
  return { onClose }
}

const addButton = () => screen.getByRole('button', { name: 'Hinzufügen' }) as HTMLButtonElement
const footer = () => document.querySelector('.pv-addgame-reason')!.firstElementChild!.textContent
const sent = (path: string) => calls.filter((c) => c.path.startsWith(path))

beforeEach(async () => {
  await applyLanguage('de')
  server = { twitch: true, searchAnswer: 'results', createAnswer: 'ok', coverAnswer: 'ok' }
  calls = []
  stubServer()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('AddGameDialog with Twitch credentials (IGDB)', () => {
  it('searches after a pause, lists the hits and fills in the platform IGDB names for the picked game', async () => {
    renderDialog()
    const field = await screen.findByLabelText('Spiel suchen')
    fireEvent.change(field, { target: { value: 'astro' } })
    // Nothing is asked before the pause is over
    expect(sent('/api/igdb/search')).toHaveLength(0)
    expect(await screen.findByText('IGDB wird durchsucht …')).toBeTruthy()

    fireEvent.click(await screen.findByRole('button', { name: '„Astro Bot“ wählen' }, { timeout: 3000 }))
    expect(sent('/api/igdb/search')).toHaveLength(1)
    // The picked game with its facts
    expect(await screen.findByText('Team Asobi')).toBeTruthy()
    expect(screen.getByText('Plattformer', { exact: false })).toBeTruthy()
    expect(screen.getByText('ca. 14 Std. (Ø von 312 Spielern)')).toBeTruthy()
    expect(screen.getByText('Kritiker 94')).toBeTruthy()
    // IGDB lists only PS5: PlayStation and PS5 are chosen, the dialog says what it will do
    expect(screen.getByRole('radio', { name: /^PlayStation/ }).getAttribute('aria-checked')).toBe('true')
    expect(screen.getByRole('radio', { name: /^PS5/ }).getAttribute('aria-checked')).toBe('true')
    expect(footer()).toBe('Wird als „Astro Bot“ auf PlayStation 5 hinzugefügt.')

    fireEvent.click(addButton())
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe('/games/new-game'))
    const create = sent('/api/games/manual')[0]!
    expect(JSON.parse(create.body!)).toEqual({ title: null, platform: 'playstation', platformVariant: 'ps5', igdbId: 90001 })
  })

  it('marks what IGDB lists, marks no PC shop, and needs the PlayStation version when IGDB leaves it open', async () => {
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Spiel suchen'), { target: { value: 'astroneer' } })
    fireEvent.click(await screen.findByRole('button', { name: '„Astroneer“ wählen' }, { timeout: 3000 }))
    await screen.findByText('Mit „IGDB“ markiert: Xbox, PlayStation 4.', { exact: false })
    // PC is listed, but it marks no shop
    expect(screen.getByText('Für PC wähle den Shop, in dem du das Spiel besitzt.', { exact: false })).toBeTruthy()
    const marked = screen.getAllByLabelText('von IGDB genannt')
    expect(marked.length).toBeGreaterThanOrEqual(2)
    expect(within(screen.getByRole('radio', { name: /^Steam/ })).queryByLabelText('von IGDB genannt')).toBeNull()
    expect(within(screen.getByRole('radio', { name: /^Epic Games/ })).queryByLabelText('von IGDB genannt')).toBeNull()
    // Two platforms: none chosen yet, the dialog says what is missing
    expect(footer()).toBe('Wähle die Plattform, auf der du das Spiel spielst.')
    expect(addButton().disabled).toBe(true)

    // PlayStation needs its version
    fireEvent.click(screen.getByRole('radio', { name: /^PlayStation/ }))
    expect(footer()).toBe('Wähle die PlayStation-Fassung: PS5 oder PS4.')
    expect(addButton().disabled).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: /^PS4/ }))
    expect(addButton().disabled).toBe(false)
  })

  it('judges an address that is no IGDB game on every keystroke and never sends it', async () => {
    renderDialog()
    const field = await screen.findByLabelText('Spiel suchen')
    fireEvent.change(field, { target: { value: 'https://example.com/spiel/astro' } })
    expect(await screen.findByText(/Das ist kein Link zu einem IGDB-Spiel/)).toBeTruthy()
    expect(field.getAttribute('aria-invalid')).toBe('true')
    await new Promise((resolve) => setTimeout(resolve, 500))
    expect(sent('/api/igdb/search')).toHaveLength(0)
    fireEvent.change(field, { target: { value: 'https://www.igdb.com/games/astro-bot' } })
    expect(field.getAttribute('aria-invalid')).toBeNull()
    expect(await screen.findByText('IGDB-Link erkannt: „astro-bot“.', {}, { timeout: 3000 })).toBeTruthy()
  })

  it('says what a search without a hit means, and shows a failed search completely with a copy button', async () => {
    server.searchAnswer = 'empty'
    renderDialog()
    const field = await screen.findByLabelText('Spiel suchen')
    fireEvent.change(field, { target: { value: 'zzzzz' } })
    expect(await screen.findByText(/Zu „zzzzz“ hat IGDB kein Spiel gefunden/, {}, { timeout: 3000 })).toBeTruthy()

    server.searchAnswer = 'fail'
    fireEvent.change(field, { target: { value: 'astro' } })
    const block = await screen.findByTestId('error-block', {}, { timeout: 3000 })
    expect(within(block).getByText(/Die IGDB-Suche ist fehlgeschlagen/)).toBeTruthy()
    expect(within(block).getByText('IGDB antwortet mit 503.')).toBeTruthy()
    expect(within(block).getByTestId('copy-error')).toBeTruthy()
  })

  it('says that IGDB cannot answer when the server finds no credentials after all (204)', async () => {
    server.searchAnswer = 'none'
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Spiel suchen'), { target: { value: 'astro' } })
    expect(await screen.findByText(/IGDB antwortet nicht ohne Zugangsdaten/, {}, { timeout: 3000 })).toBeTruthy()
  })

  it('offers "Ohne IGDB hinzufügen" and the way back', async () => {
    renderDialog()
    fireEvent.click(await screen.findByRole('button', { name: /Ohne IGDB hinzufügen/ }))
    expect(screen.getByLabelText('Titel')).toBeTruthy()
    expect(screen.getByText(/Ohne IGDB-Treffer lässt die KI die Spielfakten später recherchieren/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Zurück zur IGDB-Suche/ }))
    expect(screen.getByLabelText('Spiel suchen')).toBeTruthy()
  })

  it('points to the existing game on a duplicate (409) and keeps the dialog', async () => {
    server.createAnswer = 'duplicate'
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Spiel suchen'), { target: { value: 'astro' } })
    fireEvent.click(await screen.findByRole('button', { name: '„Astro Bot“ wählen' }, { timeout: 3000 }))
    await waitFor(() => expect(addButton().disabled).toBe(false))
    fireEvent.click(addButton())
    const answer = await screen.findByTestId('add-duplicate')
    expect(within(answer).getByText(/schon in deiner Sammlung/)).toBeTruthy()
    const link = within(answer).getByRole('link', { name: 'Das vorhandene Spiel öffnen' })
    expect(link.getAttribute('href')).toBe('/games/astro-bot')
    expect(screen.getByTestId('where').textContent).toBe('/')
    // Changing the platform answers nothing anymore: the old answer is gone
    fireEvent.click(screen.getByRole('radio', { name: /^Xbox/ }))
    expect(screen.queryByTestId('add-duplicate')).toBeNull()
  })

  it('shows a failed add completely with a copy button', async () => {
    server.createAnswer = 'fail'
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Spiel suchen'), { target: { value: 'astro' } })
    fireEvent.click(await screen.findByRole('button', { name: '„Astro Bot“ wählen' }, { timeout: 3000 }))
    await waitFor(() => expect(addButton().disabled).toBe(false))
    fireEvent.click(addButton())
    const block = await screen.findByTestId('error-block')
    expect(within(block).getByText(/Das Spiel konnte nicht hinzugefügt werden/)).toBeTruthy()
    expect(within(block).getByTestId('copy-error')).toBeTruthy()
  })
})

describe('AddGameDialog without Twitch credentials', () => {
  beforeEach(() => {
    server.twitch = false
  })

  it('asks for a title, a platform and (for PlayStation) the version, and says what is missing', async () => {
    renderDialog()
    const title = await screen.findByLabelText('Titel')
    expect(screen.queryByLabelText('Spiel suchen')).toBeNull()
    expect(screen.getByText(/Erscheinungsdatum, Entwickler, Genres und Dauer lässt die KI später recherchieren/)).toBeTruthy()
    // An untouched empty field stays calm
    expect(title.getAttribute('aria-invalid')).toBeNull()
    expect(footer()).toBe('Gib den Titel des Spiels ein.')
    expect(addButton().disabled).toBe(true)

    // Typing and emptying marks it at once - without leaving the field
    fireEvent.change(title, { target: { value: 'G' } })
    expect(title.getAttribute('aria-invalid')).toBeNull()
    fireEvent.change(title, { target: { value: '' } })
    expect(title.getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByText('Der Titel fehlt: Gib mindestens ein Zeichen ein.')).toBeTruthy()
    fireEvent.change(title, { target: { value: 'x'.repeat(241) } })
    expect(screen.getByText('Zu lang: 241 von höchstens 240 Zeichen.')).toBeTruthy()
    expect(footer()).toBe('Korrigiere den Titel.')

    fireEvent.change(title, { target: { value: 'Stellar Blade' } })
    expect(title.getAttribute('aria-invalid')).toBeNull()
    expect(footer()).toBe('Wähle die Plattform, auf der du das Spiel spielst.')
    fireEvent.click(screen.getByRole('radio', { name: 'PlayStation' }))
    expect(footer()).toBe('Wähle die PlayStation-Fassung: PS5 oder PS4.')
    expect(addButton().disabled).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'PS5' }))
    expect(footer()).toBe('Wird als „Stellar Blade“ auf PlayStation 5 hinzugefügt.')
    expect(addButton().disabled).toBe(false)

    fireEvent.click(addButton())
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe('/games/new-game'))
    expect(JSON.parse(sent('/api/games/manual')[0]!.body!)).toEqual({ title: 'Stellar Blade', platform: 'playstation', platformVariant: 'ps5', igdbId: null })
    // No cover was chosen: nothing is uploaded
    expect(sent('/api/games/new-game/cover')).toHaveLength(0)
  })

  it('knows the Steam choice: Steam games come from the library and are not added here', async () => {
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Titel'), { target: { value: 'Hades' } })
    fireEvent.click(screen.getByRole('radio', { name: 'Steam' }))
    expect(screen.getByText(/Steam-Spiele kommen aus deiner Steam-Bibliothek/)).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Alle Spiele öffnen' }).getAttribute('href')).toBe('/library')
    expect(addButton().disabled).toBe(true)
  })

  it('says calmly that the Nintendo Switch has no trophies', async () => {
    renderDialog()
    fireEvent.click(await screen.findByRole('radio', { name: 'Nintendo Switch' }))
    expect(screen.getByText('Auf der Nintendo Switch gibt es keine Trophäen. Dieses Spiel bekommt nur den Guide.')).toBeTruthy()
  })

  it('moves the platform with the arrow keys (radio group)', async () => {
    renderDialog()
    const first = await screen.findByRole('radio', { name: 'Steam' })
    fireEvent.click(first)
    fireEvent.keyDown(first.parentElement!, { key: 'ArrowRight' })
    expect(screen.getByRole('radio', { name: 'PlayStation' }).getAttribute('aria-checked')).toBe('true')
  })

  it('refuses a cover that breaks a rule with the reason, in a reserved line, copyable', async () => {
    renderDialog()
    await screen.findByLabelText('Titel')
    const zone = document.querySelector('.pv-cover-zone')!
    const copyButton = () => document.querySelector('.pv-field-msg-row [data-copy-error]')!
    // Before: the room is there, the copy button is not reachable
    expect(copyButton().className).toContain('invisible')
    expect(document.querySelectorAll('.pv-field-msg-row .pv-reserve-copy').length).toBeGreaterThanOrEqual(2)

    const svg = new File(['<svg/>'], 'cover.svg', { type: 'image/svg+xml' })
    fireEvent.drop(zone, { dataTransfer: { files: [svg] } })
    const message = await screen.findByText(/Der Dateityp „image\/svg\+xml“ wird nicht angenommen/)
    expect(message.closest('[role="alert"]')).not.toBeNull()
    expect(copyButton().className).not.toContain('invisible')
    expect(zone.getAttribute('data-invalid')).toBe('true')

    // A file that is too large names both sizes
    const big = new File([new Uint8Array(6 * 1024 * 1024)], 'cover.png', { type: 'image/png' })
    fireEvent.drop(zone, { dataTransfer: { files: [big] } })
    expect(await screen.findByText(/Das Bild ist 6 MB groß, erlaubt sind höchstens 5 MB/)).toBeTruthy()
    // The button waits while the file is refused
    fireEvent.change(screen.getByLabelText('Titel'), { target: { value: 'Game' } })
    fireEvent.click(screen.getByRole('radio', { name: 'Xbox' }))
    expect(footer()).toBe('Wähle ein anderes Titelbild oder entferne es.')
    expect(addButton().disabled).toBe(true)

    // A good file is taken and shown with its name, replaced or removed
    const png = new File([new Uint8Array(2048)], 'cover-game.png', { type: 'image/png' })
    fireEvent.drop(zone, { dataTransfer: { files: [png] } })
    expect(await screen.findByText('cover-game.png')).toBeTruthy()
    expect(zone.getAttribute('data-invalid')).toBeNull()
    expect(addButton().disabled).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Entfernen' }))
    expect(screen.queryByText('cover-game.png')).toBeNull()
  })

  it('uploads the chosen cover after the game is created', async () => {
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Titel'), { target: { value: 'Game' } })
    fireEvent.click(screen.getByRole('radio', { name: 'GOG' }))
    fireEvent.drop(document.querySelector('.pv-cover-zone')!, { dataTransfer: { files: [new File([new Uint8Array(10)], 'c.png', { type: 'image/png' })] } })
    await screen.findByText('c.png')
    fireEvent.click(addButton())
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe('/games/new-game'))
    const upload = sent('/api/games/new-game/cover')[0]!
    expect(upload.method).toBe('PUT')
  })

  it('keeps the game and says so when only the cover fails, with the way to retry or go to the game', async () => {
    server.coverAnswer = 'fail'
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Titel'), { target: { value: 'Game' } })
    fireEvent.click(screen.getByRole('radio', { name: 'GOG' }))
    fireEvent.drop(document.querySelector('.pv-cover-zone')!, { dataTransfer: { files: [new File([new Uint8Array(10)], 'c.png', { type: 'image/png' })] } })
    await screen.findByText('c.png')
    fireEvent.click(addButton())
    const block = await screen.findByTestId('error-block')
    expect(within(block).getByText(/Das Titelbild wurde nicht gespeichert/)).toBeTruthy()
    expect(footer()).toBe('Das Spiel ist hinzugefügt, nur das Titelbild fehlt noch.')
    expect(screen.getByTestId('where').textContent).toBe('/')
    server.coverAnswer = 'ok'
    fireEvent.click(screen.getByRole('button', { name: 'Titelbild erneut hochladen' }))
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe('/games/new-game'))
  })

  it('has a quiet link to the Twitch settings', async () => {
    const { onClose } = renderDialog()
    const link = await screen.findByRole('link', { name: /Twitch-Zugang eintragen/ })
    expect(link.getAttribute('href')).toBe('/settings?tab=twitch')
    fireEvent.click(link)
    expect(onClose).toHaveBeenCalled()
  })
})

describe('AddGameDialog layout stays rigid', () => {
  it('reserves the room of every message line from the start (the search line, the title line, the foot)', async () => {
    renderDialog()
    await screen.findByLabelText('Spiel suchen')
    const lines = () => [...document.querySelectorAll('.pv-addgame .pv-reserve')]
    const before = lines().map((line) => line.querySelectorAll('.pv-reserve-copy').length)
    // The search line reserves its texts; the foot reserves all of its sentences
    expect(before.every((count) => count >= 2)).toBe(true)

    // An error appearing does not add or remove a line: the same reserved lines are there, only their text changes
    fireEvent.change(screen.getByLabelText('Spiel suchen'), { target: { value: 'https://example.com/x' } })
    await screen.findByText(/Das ist kein Link zu einem IGDB-Spiel/)
    expect(lines().map((line) => line.querySelectorAll('.pv-reserve-copy').length)).toEqual(before)
    expect(document.querySelectorAll('.pv-addgame-reason .pv-reserve-copy').length).toBeGreaterThan(5)
  })

  it('keeps the button the same while it works (spinner over the label)', async () => {
    renderDialog()
    fireEvent.change(await screen.findByLabelText('Spiel suchen'), { target: { value: 'astro' } })
    fireEvent.click(await screen.findByRole('button', { name: '„Astro Bot“ wählen' }, { timeout: 3000 }))
    await waitFor(() => expect(addButton().disabled).toBe(false))
    fireEvent.click(addButton())
    // The label stays in the DOM (invisible), so the button keeps its width
    expect(screen.getByRole('button', { name: /Hinzufügen/ }).querySelector('.invisible')?.textContent).toBe('Hinzufügen')
  })
})
