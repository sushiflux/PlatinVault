import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import type { LiveAchievementItemDto } from '../../api/types'
import { LEAVE_MS, usePinList } from './usePinList'

function ach(apiName: string, achieved = false): LiveAchievementItemDto {
  return {
    apiName,
    displayName: apiName,
    description: '',
    originalDescription: null,
    isTranslated: false,
    iconUrl: '',
    iconGrayUrl: '',
    achieved,
    unlockTime: null,
    globalPercent: 10,
    hasGlobalPercent: true,
    rarityTier: 'Gold',
    rarityLabel: '',
    isHidden: false,
    userNote: '',
  }
}

interface Props {
  appId: number
  names: string[]
  achievements: LiveAchievementItemDto[]
  removedHere?: (apiName: string) => boolean
}

const none = () => false
const render = (initial: Props, leaveMs?: number) =>
  renderHook((props: Props) => usePinList(props.appId, props.names, props.achievements, props.removedHere ?? none, leaveMs), { initialProps: initial })
const summary = (entries: { apiName: string; leaving: boolean }[]) => entries.map((entry) => `${entry.apiName}${entry.leaving ? '*' : ''}`)

describe('usePinList (the exit of an unlocked pin in the mode "remove after unlock")', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('lists the pinned names in order, with their live data', () => {
    const { result } = render({ appId: 1, names: ['B', 'A'], achievements: [ach('A'), ach('B', true)] })
    expect(summary(result.current)).toEqual(['B', 'A'])
    expect(result.current[0]!.ach?.achieved).toBe(true)
  })

  it('keeps a pin that the server removed after the unlock as a leaving card at its place, then lets it go after about 5 seconds', () => {
    const { result, rerender } = render({ appId: 1, names: ['A', 'B', 'C'], achievements: [ach('A'), ach('B'), ach('C')] })
    rerender({ appId: 1, names: ['A', 'C'], achievements: [ach('A'), ach('B', true), ach('C')] })
    expect(summary(result.current)).toEqual(['A', 'B*', 'C'])
    expect(result.current[1]!.ach?.achieved).toBe(true)

    act(() => {
      vi.advanceTimersByTime(LEAVE_MS - 500)
    })
    expect(summary(result.current)).toEqual(['A', 'B*', 'C'])
    act(() => {
      vi.advanceTimersByTime(700)
    })
    expect(summary(result.current)).toEqual(['A', 'C'])
  })

  it('lets the leaving card go after the chosen popup duration instead of 5 seconds', () => {
    const { result, rerender } = render({ appId: 1, names: ['A', 'B'], achievements: [ach('A'), ach('B')] }, 8000)
    rerender({ appId: 1, names: ['A'], achievements: [ach('A'), ach('B', true)] })
    expect(summary(result.current)).toEqual(['A', 'B*'])
    act(() => {
      vi.advanceTimersByTime(LEAVE_MS + 500)
    })
    expect(summary(result.current)).toEqual(['A', 'B*'])
    act(() => {
      vi.advanceTimersByTime(8000 - LEAVE_MS - 500 + 100)
    })
    expect(summary(result.current)).toEqual(['A'])
  })

  it('also lets it go sooner with a short duration (2 seconds)', () => {
    const { result, rerender } = render({ appId: 1, names: ['A', 'B'], achievements: [ach('A'), ach('B')] }, 2000)
    rerender({ appId: 1, names: ['A'], achievements: [ach('A'), ach('B', true)] })
    expect(summary(result.current)).toEqual(['A', 'B*'])
    act(() => {
      vi.advanceTimersByTime(2100)
    })
    expect(summary(result.current)).toEqual(['A'])
  })

  it('also recognises the unlock when it reaches the live data a moment after the pin change, but not for longer than the exit time', () => {
    const { result, rerender } = render({ appId: 1, names: ['A', 'B'], achievements: [ach('A'), ach('B')] })
    rerender({ appId: 1, names: ['B'], achievements: [ach('A'), ach('B')] })
    // Not unlocked as far as the data knows: nothing is kept yet
    expect(summary(result.current)).toEqual(['B'])
    act(() => {
      vi.advanceTimersByTime(1500)
    })
    rerender({ appId: 1, names: ['B'], achievements: [ach('A', true), ach('B')] })
    expect(summary(result.current)).toEqual(['A*', 'B'])
    // The exit time counts from the moment the pin went, not from the moment the data arrived
    act(() => {
      vi.advanceTimersByTime(LEAVE_MS - 1500 + 100)
    })
    expect(summary(result.current)).toEqual(['B'])
  })

  it('forgets a pin that never turns out to be unlocked', () => {
    const { result, rerender } = render({ appId: 1, names: ['A'], achievements: [ach('A')] })
    rerender({ appId: 1, names: [], achievements: [ach('A')] })
    act(() => {
      vi.advanceTimersByTime(LEAVE_MS + 100)
    })
    rerender({ appId: 1, names: [], achievements: [ach('A', true)] })
    expect(summary(result.current)).toEqual([])
  })

  it('shows no leaving card for a pin the player removed in this window', () => {
    const removedHere = (name: string) => name === 'A'
    const { result, rerender } = render({ appId: 1, names: ['A', 'B'], achievements: [ach('A', true), ach('B')], removedHere })
    rerender({ appId: 1, names: ['B'], achievements: [ach('A', true), ach('B')], removedHere })
    expect(summary(result.current)).toEqual(['B'])
  })

  it('shows no leaving card when another game is opened', () => {
    const { result, rerender } = render({ appId: 1, names: ['A'], achievements: [ach('A', true)] })
    rerender({ appId: 2, names: [], achievements: [ach('A', true)] })
    expect(summary(result.current)).toEqual([])
  })

  it('takes the leaving card back when the same achievement is pinned again', () => {
    const { result, rerender } = render({ appId: 1, names: ['A'], achievements: [ach('A', true)] })
    rerender({ appId: 1, names: [], achievements: [ach('A', true)] })
    expect(summary(result.current)).toEqual(['A*'])
    rerender({ appId: 1, names: ['A'], achievements: [ach('A', true)] })
    expect(summary(result.current)).toEqual(['A'])
  })
})
