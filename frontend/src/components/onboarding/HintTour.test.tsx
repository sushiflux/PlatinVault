import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyLanguage } from '../../i18n'
import { HintTour } from './HintTour'

// The chapter hint points at the navigation of the open game on a wide screen and at the menu button of the header on a phone (under
// 768 px, where the navigation is a drawer): on a phone its text must not talk about a panel on the left.

function stubMedia(phone: boolean) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: phone && query.includes('767'),
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }))
  )
}

beforeEach(async () => {
  await applyLanguage('de')
  // jsdom has no layout: the control needs a size, or no hint is shown for it
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ x: 10, y: 10, left: 10, top: 10, right: 110, bottom: 50, width: 100, height: 40, toJSON: () => ({}) })
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} unobserve() {} })
  vi.stubGlobal('requestAnimationFrame', () => 0)
  vi.stubGlobal('cancelAnimationFrame', () => {})
  Element.prototype.scrollIntoView = () => {}
})

afterEach(() => {
  cleanup()
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderGameHints() {
  const target = document.createElement('div')
  target.setAttribute('data-hint', 'game-nav')
  document.body.appendChild(target)
  render(<HintTour screen="game" open onClose={() => {}} onShowIntro={() => {}} />)
}

describe('HintTour, chapter hint', () => {
  it('names the navigation on a wide screen', async () => {
    stubMedia(false)
    renderGameHints()
    expect((await screen.findByText(/Jedes Spiel hat links seine eigene Navigation/)).textContent).not.toMatch(/Knopf/)
  })

  it('says it is a button that opens the list on a phone, in German and English', async () => {
    stubMedia(true)
    renderGameHints()
    expect((await screen.findByText(/Dieser Knopf öffnet die Navigation/)).textContent).not.toMatch(/Seitenleiste|links/i)
    cleanup()
    await applyLanguage('en')
    renderGameHints()
    expect((await screen.findByText(/This button opens the navigation/)).textContent).not.toMatch(/sidebar|left/i)
  })
})
