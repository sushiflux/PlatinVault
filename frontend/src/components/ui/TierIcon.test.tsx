import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { TierIcon } from './TierIcon'

afterEach(cleanup)

describe('TierIcon', () => {
  it('draws the same cup for every tier, in the tier colour', () => {
    for (const [tier, token] of [['Platinum', '--tier-platinum'], ['Gold', '--tier-gold'], ['Silver', '--tier-silver'], ['Bronze', '--tier-bronze']] as const) {
      const { container } = render(<TierIcon tier={tier} size={16} />)
      const root = container.querySelector('.pv-tier-icon') as HTMLElement
      expect(root.style.color).toBe(`var(${token})`)
      expect(root.querySelector('svg.tabler-icon-trophy-filled')).toBeTruthy()
      cleanup()
    }
  })

  it('is decorative without a label and named with one', () => {
    const { container } = render(<TierIcon tier="Gold" />)
    expect(container.querySelector('[aria-hidden="true"]')).toBeTruthy()
    cleanup()
    render(<TierIcon tier="Platinum" label="Platin" />)
    expect(screen.getByRole('img', { name: 'Platin' })).toBeTruthy()
  })

  it('only platinum gets the sparkle, and not at tiny sizes', () => {
    expect(render(<TierIcon tier="Platinum" size={16} />).container.querySelectorAll('svg').length).toBe(2)
    cleanup()
    expect(render(<TierIcon tier="Platinum" size={11} />).container.querySelectorAll('svg').length).toBe(1)
    cleanup()
    expect(render(<TierIcon tier="Gold" size={16} />).container.querySelectorAll('svg').length).toBe(1)
  })
})
