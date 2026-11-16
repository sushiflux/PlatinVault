import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  DEFAULT_LIGHTING,
  DEFAULT_SIZE,
  LIGHTING_KEY,
  LIGHTING_MODES,
  SIZE_KEY,
  SIZE_STEPS,
  loadLighting,
  loadSize,
  saveLighting,
  saveSize,
} from './preferences'

beforeEach(() => localStorage.clear())
afterEach(() => vi.restoreAllMocks())

describe('cabinet shelf size', () => {
  it('is "Klein" (step 1) until a size was chosen', () => {
    expect(DEFAULT_SIZE).toBe(1)
    expect(SIZE_STEPS[DEFAULT_SIZE]).toBe('small')
    expect(loadSize()).toBe(1)
  })

  it('remembers a chosen size per browser', () => {
    saveSize(3)
    expect(localStorage.getItem(SIZE_KEY)).toBe('3')
    expect(loadSize()).toBe(3)
  })

  it('falls back to the default for anything that is not a step', () => {
    for (const bad of ['', 'abc', '-1', '5', '1.5']) {
      localStorage.setItem(SIZE_KEY, bad)
      expect(loadSize()).toBe(DEFAULT_SIZE)
    }
  })

  it('survives a refused storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied')
    })
    expect(loadSize()).toBe(DEFAULT_SIZE)
    expect(() => saveSize(2)).not.toThrow()
  })
})

describe('cabinet lighting', () => {
  it('offers spots and LED, spots is the default', () => {
    expect([...LIGHTING_MODES]).toEqual(['spots', 'led'])
    expect(DEFAULT_LIGHTING).toBe('spots')
    expect(loadLighting()).toBe('spots')
  })

  it('stores the choice under pv_cabinet_lighting', () => {
    expect(LIGHTING_KEY).toBe('pv_cabinet_lighting')
    saveLighting('led')
    expect(localStorage.getItem('pv_cabinet_lighting')).toBe('led')
    expect(loadLighting()).toBe('led')
    saveLighting('spots')
    expect(loadLighting()).toBe('spots')
  })

  it('falls back to spots for an unknown stored value', () => {
    localStorage.setItem(LIGHTING_KEY, 'disco')
    expect(loadLighting()).toBe('spots')
  })

  it('survives a refused storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied')
    })
    expect(loadLighting()).toBe('spots')
    expect(() => saveLighting('led')).not.toThrow()
  })
})
