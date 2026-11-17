import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PlatinumCoverFx, PlatinumLineFx } from './platinum'

const reducedMotion = vi.hoisted(() => ({ value: false }))
vi.mock('motion/react', async (importOriginal) => ({
  ...(await importOriginal<typeof import('motion/react')>()),
  useReducedMotion: () => reducedMotion.value,
}))

afterEach(() => {
  cleanup()
  reducedMotion.value = false
})

describe('PlatinumCoverFx', () => {
  it('shows the plaque with its name and keeps every effect element hidden from assistive tech', () => {
    const { container } = render(<PlatinumCoverFx seed="ffr" />)
    expect(screen.getByText('Platin')).toBeTruthy()
    const fx = container.querySelector('.pv-plat-fx')
    expect(fx?.getAttribute('aria-hidden')).toBe('true')
    expect(container.querySelectorAll('.pv-plat-anim').length).toBeGreaterThan(0)
    // Every decorative star is inside the aria-hidden wrapper
    for (const el of container.querySelectorAll('.pv-plat-anim')) expect(el.closest('[aria-hidden="true"]')).not.toBeNull()
  })

  it('keeps the name for screen readers when the plaque is only an icon', () => {
    const { container } = render(<PlatinumCoverFx seed="ffr" compact />)
    expect(container.querySelector('.pv-plat-chip .sr-only')?.textContent).toBe('Platin')
  })

  it('renders no animated element with reduced motion, only the plaque', () => {
    reducedMotion.value = true
    const { container } = render(<PlatinumCoverFx seed="ffr" />)
    expect(container.querySelectorAll('.pv-plat-anim').length).toBe(0)
    expect(container.querySelector('.pv-plat-fx')?.getAttribute('data-pv-fx')).toBe('static')
    expect(container.querySelector('.pv-plat-chip')?.getAttribute('data-animated')).toBeNull()
    expect(screen.getByText('Platin')).toBeTruthy()
  })
})

describe('PlatinumLineFx', () => {
  it('is decorative: aria-hidden, with an animated sweep and twinkles', () => {
    const { container } = render(<PlatinumLineFx seed="ffr" />)
    const line = container.querySelector('.pv-plat-line')!
    expect(line.getAttribute('aria-hidden')).toBe('true')
    expect(line.getAttribute('data-pv-fx')).toBe('animated')
    expect(line.querySelectorAll('.pv-plat-twinkle').length).toBeGreaterThan(0)
    expect(line.querySelector('.pv-plat-bar-shine')).not.toBeNull()
  })

  it('has a decorative rosette at the line end with a glint and a spark', () => {
    const { container } = render(<PlatinumLineFx seed="ffr" />)
    const medal = container.querySelector('.pv-plat-line .pv-plat-medal')!
    expect(medal).not.toBeNull()
    expect(medal.closest('[aria-hidden="true"]')).not.toBeNull()
    expect(medal.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    expect(medal.querySelector('.pv-plat-medal-glint.pv-plat-anim')).not.toBeNull()
    expect(medal.querySelector('.pv-plat-medal-spark.pv-plat-anim')).not.toBeNull()
  })

  it('shows the rosette static with reduced motion', () => {
    reducedMotion.value = true
    const { container } = render(<PlatinumLineFx seed="ffr" />)
    expect(container.querySelector('.pv-plat-medal circle')).not.toBeNull()
    expect(container.querySelector('.pv-plat-medal .pv-plat-anim')).toBeNull()
    expect(container.querySelector('.pv-plat-medal-glint')).toBeNull()
  })

  it('gives every rosette its own gradient ids', () => {
    const { container } = render(
      <>
        <PlatinumLineFx seed="a" />
        <PlatinumLineFx seed="b" />
      </>,
    )
    const ids = [...container.querySelectorAll('.pv-plat-medal linearGradient')].map((g) => g.id)
    expect(ids.length).toBe(4)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('is a static gold line with reduced motion', () => {
    reducedMotion.value = true
    const { container } = render(<PlatinumLineFx seed="ffr" />)
    expect(container.querySelector('.pv-plat-line-gold')).not.toBeNull()
    expect(container.querySelectorAll('.pv-plat-anim').length).toBe(0)
    expect(container.querySelector('.pv-plat-line')?.getAttribute('data-pv-fx')).toBe('static')
  })
})
