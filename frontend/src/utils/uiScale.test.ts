import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { setUiScale, snapUiScale, UI_SCALE_DEFAULT, UI_SCALE_STEPS, UI_SCALE_STORAGE_KEY, useUiScale } from './uiScale'

describe('snapUiScale', () => {
  it('keeps every valid step', () => {
    for (const step of UI_SCALE_STEPS) expect(snapUiScale(step)).toBe(step)
  })

  it('reads stored strings and snaps odd values to the nearest step', () => {
    expect(snapUiScale('110')).toBe(110)
    expect(snapUiScale('96')).toBe(100)
    expect(snapUiScale(60)).toBe(80)
    expect(snapUiScale(400)).toBe(125)
  })

  it('falls back to the default for anything that is not a number', () => {
    for (const bad of [null, undefined, '', 'abc', Number.NaN, {}]) expect(snapUiScale(bad)).toBe(UI_SCALE_DEFAULT)
  })
})

describe('useUiScale', () => {
  beforeEach(() => {
    act(() => setUiScale(UI_SCALE_DEFAULT))
    localStorage.clear()
  })

  it('walks through the steps, applies the root property and stores the value', () => {
    const { result } = renderHook(() => useUiScale())
    expect(result.current.scale).toBe(100)
    expect(result.current.isDefault).toBe(true)

    act(() => {
      result.current.grow()
    })
    expect(result.current.scale).toBe(110)
    expect(document.documentElement.style.getPropertyValue('--pv-ui-scale')).toBe('1.1')
    expect(document.documentElement.dataset.uiScale).toBe('110')
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBe('110')

    act(() => {
      result.current.grow()
    })
    expect(result.current.scale).toBe(125)
    expect(result.current.canGrow).toBe(false)
  })

  it('stops at both ends and resets to the standard size', () => {
    const { result } = renderHook(() => useUiScale())
    for (let n = 0; n < UI_SCALE_STEPS.length + 2; n++) {
      act(() => {
        result.current.shrink()
      })
    }
    expect(result.current.scale).toBe(80)
    expect(result.current.canShrink).toBe(false)

    let after = 0
    act(() => {
      after = result.current.reset()
    })
    expect(after).toBe(100)
    expect(result.current.scale).toBe(100)
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBe('100')
  })
})
