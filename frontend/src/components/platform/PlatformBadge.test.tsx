import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import i18n from '../../i18n'
import { PlatformBadge } from './PlatformBadge'
import { PLATFORM_IDS } from '../../utils/platforms'

afterEach(() => {
  cleanup()
})

describe('PlatformBadge', () => {
  it('shows the short chip text and gives screen readers the full name', async () => {
    await i18n.changeLanguage('de')
    const { container } = render(<PlatformBadge platform="playstation" variant="ps5" size="sm" />)
    const badge = container.querySelector('.pv-platform-badge')!
    expect(badge.getAttribute('data-platform')).toBe('playstation')
    expect(badge.getAttribute('data-variant')).toBe('ps5')
    expect(badge.getAttribute('data-size')).toBe('sm')
    expect(screen.getByText('PS5').getAttribute('aria-hidden')).toBe('true')
    expect(container.querySelector('.sr-only')?.textContent).toBe('PlayStation 5')
  })

  it('uses the owner-chosen chip texts for every platform', async () => {
    await i18n.changeLanguage('en')
    const expected: Record<string, string> = {
      steam: 'Steam',
      xbox: 'Xbox',
      epic: 'Epic',
      gog: 'GOG',
      nintendo: 'Switch',
      ubisoft: 'Ubisoft',
      ea: 'EA',
      battlenet: 'Battle.net',
    }
    for (const id of PLATFORM_IDS) {
      if (id === 'playstation') continue
      const { container, unmount } = render(<PlatformBadge platform={id} size="md" />)
      expect(container.querySelector('[aria-hidden="true"]')?.textContent).toBe(expected[id])
      unmount()
    }
    const ps4 = render(<PlatformBadge platform="playstation" variant="ps4" size="md" />)
    expect(ps4.container.querySelector('[aria-hidden="true"]')?.textContent).toBe('PS4')
  })
})
