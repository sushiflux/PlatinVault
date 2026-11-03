import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { connectProgramWindow, readProgramMessage, resetProgramWindowForTests, useProgramWindow } from './programWindow'
import type { WebViewChannel } from './programWindow'

function fakeChannel() {
  const listeners = new Set<(event: { data: unknown }) => void>()
  const sent: unknown[] = []
  const channel: WebViewChannel = {
    postMessage: (message) => sent.push(message),
    addEventListener: (_type, listener) => listeners.add(listener),
    removeEventListener: (_type, listener) => listeners.delete(listener),
  }
  const send = (data: unknown) => listeners.forEach((listener) => listener({ data }))
  return { channel, sent, send, listeners }
}

describe('programWindow: the channel to the Windows program', () => {
  afterEach(() => resetProgramWindowForTests())

  it('reads only its own messages', () => {
    expect(readProgramMessage({ pvHost: { overlay: true } })).toEqual({ host: { overlay: true } })
    expect(readProgramMessage({ pvHost: { overlay: 'yes' } })).toEqual({ host: { overlay: false } })
    expect(readProgramMessage({ pvSettingsChanged: true })).toEqual({ settingsChanged: true })
    expect(readProgramMessage({ pvOverlay: {} })).toBeNull()
    expect(readProgramMessage('pvHost')).toBeNull()
    expect(readProgramMessage(null)).toBeNull()
  })

  it('asks the program once and follows its answer; a browser (no channel) stays "not in the program"', () => {
    const { result } = renderHook(() => useProgramWindow())
    expect(result.current.overlay).toBe(false)
    expect(connectProgramWindow(() => {}, null)).toBeTypeOf('function')

    const fake = fakeChannel()
    const settingsChanged = vi.fn()
    const stop = connectProgramWindow(settingsChanged, fake.channel)
    expect(fake.sent).toEqual([{ pvHostHello: true }])

    act(() => fake.send({ pvHost: { overlay: true } }))
    expect(result.current.overlay).toBe(true)
    act(() => fake.send({ pvSettingsChanged: true }))
    expect(settingsChanged).toHaveBeenCalledTimes(1)
    act(() => fake.send({ pvHost: { overlay: false } }))
    expect(result.current.overlay).toBe(false)

    stop()
    expect(fake.listeners.size).toBe(0)
  })
})
