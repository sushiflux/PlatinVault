import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { useRef } from 'react'
import { handoffProgress, useScrollHandoff } from './useScrollHandoff'

// The scroll-linked hand-over from a row of controls to its compact copy: progress = how far the row has moved under the header, over its own
// height; written straight to the copy's style.

const HEADER = 60
const HEIGHT = 48
let rowTop = 200

function Harness({ reduce = false }: { reduce?: boolean }) {
  const rowRef = useRef<HTMLDivElement>(null)
  const { barRef, phase } = useScrollHandoff(rowRef, reduce)
  return (
    <>
      <div ref={rowRef} data-testid="row" />
      {phase !== 'off' && <div ref={barRef} data-testid="bar" />}
      <p data-testid="phase">{phase}</p>
    </>
  )
}

const progressOf = () => Number(screen.getByTestId('bar').style.getPropertyValue('--pv-cbar-p'))

async function scrollTo(top: number) {
  rowTop = top
  act(() => {
    window.dispatchEvent(new Event('scroll'))
  })
  await waitFor(() => expect(screen.getByTestId('row')).toBeTruthy())
  // The listener is throttled with a frame: wait for it
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 5))
  })
}

describe('handoffProgress', () => {
  it('is 0 at the header edge and above, 1 once the row has moved up by its own height, linear in between', () => {
    expect(handoffProgress(300, 48, 60, false)).toBe(0)
    expect(handoffProgress(60, 48, 60, false)).toBe(0)
    expect(handoffProgress(36, 48, 60, false)).toBe(0.5)
    expect(handoffProgress(12, 48, 60, false)).toBe(1)
    expect(handoffProgress(-500, 48, 60, false)).toBe(1)
  })

  it('has nothing to hand over from a row that is not laid out', () => {
    expect(handoffProgress(0, 0, 60, false)).toBe(0)
    expect(handoffProgress(-100, 0, 60, true)).toBe(0)
  })

  it('has no fade under reduced motion: it switches at half way', () => {
    expect(handoffProgress(60, 48, 60, true)).toBe(0)
    expect(handoffProgress(37, 48, 60, true)).toBe(0)
    expect(handoffProgress(36, 48, 60, true)).toBe(1)
    expect(handoffProgress(-10, 48, 60, true)).toBe(1)
  })
})

describe('useScrollHandoff', () => {
  beforeEach(() => {
    rowTop = 200
    document.documentElement.style.setProperty('--pv-header-h', `${HEADER}px`)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      const isRow = this.getAttribute('data-testid') === 'row'
      return { top: isRow ? rowTop : 0, height: isRow ? HEIGHT : 0, bottom: 0, left: 0, right: 0, width: 0, x: 0, y: 0, toJSON: () => ({}) }
    })
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => setTimeout(() => callback(0), 0) as unknown as number)
    vi.stubGlobal('cancelAnimationFrame', (id: number) => clearTimeout(id))
  })

  afterEach(() => {
    cleanup()
    document.documentElement.style.removeProperty('--pv-header-h')
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('is off (no copy at all) while the row is in view', () => {
    render(<Harness />)
    expect(screen.getByTestId('phase').textContent).toBe('off')
    expect(screen.queryByTestId('bar')).toBeNull()
  })

  it('stays off at the very moment the row touches the header edge, and starts right after it', async () => {
    render(<Harness />)
    await scrollTo(HEADER)
    expect(screen.getByTestId('phase').textContent).toBe('off')
    await scrollTo(HEADER - 4.8)
    expect(screen.getByTestId('phase').textContent).toBe('fading')
    expect(progressOf()).toBeCloseTo(0.1, 5)
  })

  it('follows the scroll position exactly, forwards and back', async () => {
    render(<Harness />)
    await scrollTo(HEADER - 12)
    expect(progressOf()).toBeCloseTo(0.25, 5)
    expect(screen.getByTestId('bar').hasAttribute('data-moving')).toBe(true)
    await scrollTo(HEADER - 24)
    expect(progressOf()).toBeCloseTo(0.5, 5)
    expect(screen.getByTestId('phase').textContent).toBe('on')
    await scrollTo(HEADER - 48)
    expect(progressOf()).toBe(1)
    // Fully there: no will-change any more
    expect(screen.getByTestId('bar').hasAttribute('data-moving')).toBe(false)
    await scrollTo(-900)
    expect(progressOf()).toBe(1)

    // Back up: the same values in reverse, and the copy is gone when the row is back at the header edge
    await scrollTo(HEADER - 24)
    expect(progressOf()).toBeCloseTo(0.5, 5)
    await scrollTo(HEADER - 12)
    expect(progressOf()).toBeCloseTo(0.25, 5)
    expect(screen.getByTestId('phase').textContent).toBe('fading')
    await scrollTo(HEADER + 1)
    await waitFor(() => expect(screen.queryByTestId('bar')).toBeNull())
    expect(screen.getByTestId('phase').textContent).toBe('off')
  })

  it('under reduced motion switches at half way, without a fade', async () => {
    render(<Harness reduce />)
    await scrollTo(HEADER - 10)
    expect(screen.getByTestId('phase').textContent).toBe('off')
    expect(screen.queryByTestId('bar')).toBeNull()
    await scrollTo(HEADER - 25)
    expect(progressOf()).toBe(1)
    expect(screen.getByTestId('phase').textContent).toBe('on')
    expect(screen.getByTestId('bar').hasAttribute('data-moving')).toBe(false)
  })

  it('removes its listeners with the page', () => {
    const remove = vi.spyOn(window, 'removeEventListener')
    const { unmount } = render(<Harness />)
    unmount()
    expect(remove.mock.calls.map((call) => call[0])).toEqual(expect.arrayContaining(['scroll', 'resize']))
  })
})
