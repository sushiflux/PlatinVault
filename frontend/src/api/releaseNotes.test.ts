import { describe, expect, it } from 'vitest'
import { hasReleaseNotes, releasePageUrl } from './releaseNotes'

describe('hasReleaseNotes', () => {
  it('needs a notes text with content', () => {
    expect(hasReleaseNotes({ notes: '### New\n- a' })).toBe(true)
    expect(hasReleaseNotes({ notes: null })).toBe(false)
    expect(hasReleaseNotes({ notes: '' })).toBe(false)
    expect(hasReleaseNotes({ notes: '  \n ' })).toBe(false)
  })
})

describe('releasePageUrl', () => {
  it('accepts a GitHub page and refuses everything else', () => {
    const page = 'https://github.com/sushiflux/PlatinVault/releases/tag/v2026.11.14'
    expect(releasePageUrl({ notesUrl: page })).toBe(page)
    for (const value of [null, '', 'not a url', 'http://github.com/o/r', 'https://evil.example/o/r', 'https://github.com.evil.example/o/r', 'javascript:alert(1)']) {
      expect(releasePageUrl({ notesUrl: value }), String(value)).toBeNull()
    }
  })
})
