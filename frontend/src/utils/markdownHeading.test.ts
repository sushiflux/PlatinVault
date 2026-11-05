import { describe, expect, it } from 'vitest'
import { parseAtxHeading } from './markdownHeading'

// The former pattern, kept here only to show the new parser reads headings exactly as before
const OLD = /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/

describe('parseAtxHeading', () => {
  it('reads every heading shape like the former pattern', () => {
    const lines = [
      '# Title',
      '## v2026.11.19 (3.1601 β) · Unraid, Windows',
      '### ✨ New',
      '   #### Indented',
      '    # four blanks is no heading',
      '#NoBlank',
      '####### seven',
      '## Closing ##',
      '## C#',
      '## a # #',
      '## foo #bar',
      '## ###',
      '##\tTab',
      '## ',
      '#',
      'plain text',
      '- # in a list',
    ]
    for (const line of lines) {
      const old = OLD.exec(line)
      expect(parseAtxHeading(line)).toEqual(old ? { level: (old[1] ?? '').length, text: old[2] ?? '' } : null)
    }
  })

  it('stays linear on a long heading line with blanks inside (the former pattern took about a minute for 8 KB)', () => {
    const line = `## a${' '.repeat(256 * 1024)}b #`
    const started = performance.now()
    expect(parseAtxHeading(line)).toEqual({ level: 2, text: `a${' '.repeat(256 * 1024)}b` })
    expect(performance.now() - started).toBeLessThan(500)
  })
})
