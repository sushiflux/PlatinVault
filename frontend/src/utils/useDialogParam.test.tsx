import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it } from 'vitest'
import { useDialogParam, useFlagParam } from './useDialogParam'

function Probe() {
  const [open, setOpen] = useDialogParam('handbook')
  const location = useLocation()
  return (
    <>
      <p data-testid="state">{open ? 'open' : 'closed'}</p>
      <p data-testid="url">{`${location.pathname}${location.search}`}</p>
      <button type="button" onClick={() => setOpen(true)}>
        open
      </button>
      <button type="button" onClick={() => setOpen(false)}>
        close
      </button>
    </>
  )
}

afterEach(cleanup)

describe('useDialogParam', () => {
  it('is open when the address names the window, so a reload keeps it', () => {
    render(
      <MemoryRouter initialEntries={['/about?dialog=handbook']}>
        <Probe />
      </MemoryRouter>
    )
    expect(screen.getByTestId('state').textContent).toBe('open')
  })

  it('writes and removes its own parameter and leaves the others alone', () => {
    render(
      <MemoryRouter initialEntries={['/settings?tab=ki']}>
        <Probe />
      </MemoryRouter>
    )
    expect(screen.getByTestId('state').textContent).toBe('closed')
    fireEvent.click(screen.getByText('open'))
    expect(screen.getByTestId('url').textContent).toBe('/settings?tab=ki&dialog=handbook')
    expect(screen.getByTestId('state').textContent).toBe('open')
    fireEvent.click(screen.getByText('close'))
    expect(screen.getByTestId('url').textContent).toBe('/settings?tab=ki')
    expect(screen.getByTestId('state').textContent).toBe('closed')
  })

  it('does not react to another window in the address', () => {
    render(
      <MemoryRouter initialEntries={['/about?dialog=licenses']}>
        <Probe />
      </MemoryRouter>
    )
    expect(screen.getByTestId('state').textContent).toBe('closed')
  })
})

function FlagProbe() {
  const [open, setOpen] = useFlagParam('cabinet')
  const location = useLocation()
  return (
    <>
      <p data-testid="state">{open ? 'open' : 'closed'}</p>
      <p data-testid="url">{`${location.pathname}${location.search}`}</p>
      <button type="button" onClick={() => setOpen(true)}>
        open
      </button>
      <button type="button" onClick={() => setOpen(false)}>
        close
      </button>
    </>
  )
}

describe('useFlagParam', () => {
  it('is open when the address carries the flag, on any page', () => {
    render(
      <MemoryRouter initialEntries={['/games/alpha?tab=live&cabinet=1']}>
        <FlagProbe />
      </MemoryRouter>
    )
    expect(screen.getByTestId('state').textContent).toBe('open')
  })

  it('adds and removes ?cabinet=1 on the current page and leaves the other parameters (also a dialog) alone', () => {
    render(
      <MemoryRouter initialEntries={['/games/alpha?tab=live&dialog=handbook']}>
        <FlagProbe />
      </MemoryRouter>
    )
    expect(screen.getByTestId('state').textContent).toBe('closed')
    fireEvent.click(screen.getByText('open'))
    expect(screen.getByTestId('url').textContent).toBe('/games/alpha?tab=live&dialog=handbook&cabinet=1')
    expect(screen.getByTestId('state').textContent).toBe('open')
    fireEvent.click(screen.getByText('close'))
    expect(screen.getByTestId('url').textContent).toBe('/games/alpha?tab=live&dialog=handbook')
  })

  it('ignores any other value of the flag', () => {
    render(
      <MemoryRouter initialEntries={['/?cabinet=0']}>
        <FlagProbe />
      </MemoryRouter>
    )
    expect(screen.getByTestId('state').textContent).toBe('closed')
  })
})
