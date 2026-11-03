import { afterEach, describe, expect, it, vi } from 'vitest'
import { copyText } from './clipboard'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('copyText', () => {
  it('uses the clipboard API when it is there', async () => {
    const writeText = vi.fn(async () => {})
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    expect(await copyText('4078110')).toBe(true)
    expect(writeText).toHaveBeenCalledWith('4078110')
  })

  it('falls back to the copy command when the API is missing or refuses', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn(async () => Promise.reject(new Error('blocked'))) } })
    const exec = vi.fn(() => true)
    document.execCommand = exec
    expect(await copyText('4078110')).toBe(true)
    expect(exec).toHaveBeenCalledWith('copy')
    // the helper text area is removed again
    expect(document.querySelector('textarea')).toBeNull()
  })

  it('reports false when nothing works', async () => {
    vi.stubGlobal('navigator', {})
    document.execCommand = vi.fn(() => false)
    expect(await copyText('x')).toBe(false)
    document.execCommand = vi.fn(() => {
      throw new Error('not allowed')
    })
    expect(await copyText('x')).toBe(false)
    expect(document.querySelector('textarea')).toBeNull()
  })
})
