import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { act, cleanup, renderHook } from '@testing-library/react'
import { applyLanguage } from '../../i18n'
import { useStatsFormat } from './chartKit'

// Plain spaces instead of the no-break/narrow spaces Intl puts before units, so the expectations stay readable
const plain = (text: string): string => text.replace(/[  ]/g, ' ')

describe('useStatsFormat', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(async () => {
    cleanup()
    await applyLanguage('de')
  })

  it('formats durations, shares and numbers in German', () => {
    const { result } = renderHook(() => useStatsFormat())
    const fmt = result.current
    expect(plain(fmt.hours(0.5))).toBe('30 Min.')
    expect(plain(fmt.hours(1.5))).toBe('1,5 Std.')
    expect(plain(fmt.hours(62.4))).toBe('62 Std.')
    expect(plain(fmt.minutes(45))).toBe('45 Min.')
    expect(plain(fmt.minutes(125))).toBe('2 Std. 5 Min.')
    expect(plain(fmt.minutesShort(431))).toBe('7 h 11 min')
    expect(plain(fmt.pct(49.6))).toBe('49,6 %')
    expect(plain(fmt.pct(12))).toBe('12 %')
    expect(fmt.int(1234)).toBe('1.234')
  })

  it('formats durations, shares and numbers in English', async () => {
    await applyLanguage('en')
    const { result } = renderHook(() => useStatsFormat())
    const fmt = result.current
    expect(plain(fmt.hours(0.5))).toBe('30 min')
    expect(plain(fmt.hours(1.5))).toBe('1.5 h')
    expect(plain(fmt.hours(62.4))).toBe('62 h')
    expect(plain(fmt.minutes(125))).toBe('2 h 5 min')
    expect(plain(fmt.minutesShort(431))).toBe('7 h 11 min')
    expect(plain(fmt.pct(49.6))).toBe('49.6%')
    expect(fmt.int(1234)).toBe('1,234')
  })

  it('switches with the language without a reload', async () => {
    const { result } = renderHook(() => useStatsFormat())
    expect(plain(result.current.hours(2))).toBe('2 Std.')
    await act(async () => {
      await applyLanguage('en')
    })
    expect(plain(result.current.hours(2))).toBe('2 h')
  })

  it('writes dates day first in both languages (24 hour clock)', async () => {
    const ts = new Date(2026, 9, 31, 14, 5).getTime()
    const { result } = renderHook(() => useStatsFormat())
    expect(plain(result.current.dateLong(ts))).toBe('31. Oktober 2026')
    expect(result.current.time(ts)).toBe('14:05')
    await act(async () => {
      await applyLanguage('en')
    })
    expect(plain(result.current.dateLong(ts))).toBe('31 October 2026')
    expect(result.current.time(ts)).toBe('14:05')
  })
})
