import { describe, expect, it } from 'vitest'
import fixture from './testdata/release-v2026.11.14.md?raw'
import { categoryOf, detectLanguage, hasNotesContent, itemCount, parseReleaseNotes, pickLanguageBlock, splitLanguageBlocks, stripLeadingSymbols, takePolicyFooter } from './releaseNotes'

describe('stripLeadingSymbols / categoryOf', () => {
  it('removes the emoji (also with a variation selector) and recognises both languages', () => {
    expect(stripLeadingSymbols('✨ New')).toBe('New')
    expect(stripLeadingSymbols('🗑️ Removed')).toBe('Removed')
    expect(stripLeadingSymbols('⚠️ Veraltet')).toBe('Veraltet')
    const cases: Array<[string, string]> = [
      ['✨ New', 'new'],
      ['✨ Neu', 'new'],
      ['🔄 Change', 'change'],
      ['🔄 Changed', 'change'],
      ['🔄 Geändert', 'change'],
      ['🔧 Fix', 'fix'],
      ['🔧 Fixed', 'fix'],
      ['🔧 Behoben', 'fix'],
      ['🔒 Security', 'security'],
      ['🔒 Sicherheit', 'security'],
      ['🗑️ Removed', 'removed'],
      ['🗑️ Entfernt', 'removed'],
      ['⚠️ Deprecated', 'deprecated'],
      ['⚠️ Veraltet', 'deprecated'],
      ["What's Changed", 'other'],
      ['Known issues', 'other'],
    ]
    for (const [heading, category] of cases) expect(categoryOf(heading), heading).toBe(category)
  })
})

describe('splitLanguageBlocks / language choice', () => {
  it('splits the English and the German block at the rule', () => {
    const parts = splitLanguageBlocks(fixture)
    expect(parts).toHaveLength(2)
    expect(parts[0]).toContain('### ✨ New')
    expect(parts[1]).toContain('### ✨ Neu')
  })

  it('keeps a rule inside a code fence and treats CRLF like LF', () => {
    expect(splitLanguageBlocks('a\n\n```\n---\n```\n\n---\n\nb')).toHaveLength(2)
    expect(splitLanguageBlocks('a\r\n\r\n---\r\n\r\nb')).toEqual(['a', 'b'])
    expect(splitLanguageBlocks('')).toEqual([])
  })

  it('detects the language by the category headings, otherwise by common words', () => {
    expect(detectLanguage('### ✨ New\n- x')).toBe('en')
    expect(detectLanguage('### 🔧 Behoben\n- x')).toBe('de')
    expect(detectLanguage('Das ist eine Notiz und der Text wird nicht leer')).toBe('de')
    expect(detectLanguage('This is a note and the text is not empty')).toBe('en')
    expect(detectLanguage('12345')).toBeNull()
  })

  it('picks the block of the language; one block is used for both; undetectable blocks go by position', () => {
    expect(pickLanguageBlock(['only'], 'de')).toBe('only')
    expect(pickLanguageBlock(['### New\n- a', '### Neu\n- b'], 'de')).toBe('### Neu\n- b')
    expect(pickLanguageBlock(['### New\n- a', '### Neu\n- b'], 'en')).toBe('### New\n- a')
    expect(pickLanguageBlock(['111', '222'], 'en')).toBe('111')
    expect(pickLanguageBlock(['111', '222'], 'de')).toBe('222')
    expect(pickLanguageBlock([], 'en')).toBe('')
  })
})

describe('parseReleaseNotes with the real v2026.11.14 entry', () => {
  it('English: title, summary, one New section with all bullets', () => {
    const notes = parseReleaseNotes(fixture, 'en')
    expect(notes.title).toBe('v2026.11.14 (15.116022)')
    expect(notes.platforms).toBeNull()
    expect(notes.summary).toHaveLength(1)
    const summary = notes.summary[0]
    expect(summary?.kind === 'paragraph' && summary.text.startsWith('PlatinVault brings your Steam achievements')).toBe(true)
    // A multi-line paragraph becomes one line
    expect(summary?.kind === 'paragraph' && summary.text.includes('\n')).toBe(false)
    expect(notes.sections.map((section) => section.category)).toEqual(['new'])
    const section = notes.sections[0]
    expect(section?.title).toBe('New')
    expect(section?.blocks).toHaveLength(1)
    const list = section?.blocks[0]
    expect(list?.kind).toBe('list')
    if (list?.kind === 'list') {
      expect(list.items).toHaveLength(13)
      expect(list.items[0]).toBe(
        '**Open source:** the complete source code is public under the GNU AGPL v3. The licence and a link to the source code are in the app under "Licences" and on the installer\'s licence page.'
      )
      // Indented continuation lines are joined to their bullet
      expect(list.items.every((item) => !item.includes('\n') && !item.startsWith(' '))).toBe(true)
    }
    expect(section && itemCount(section)).toBe(13)
    expect(hasNotesContent(notes)).toBe(true)
  })

  it('German: the German block with its own summary and headings', () => {
    const notes = parseReleaseNotes(fixture, 'de')
    expect(notes.title).toBe('v2026.11.14 (15.116022)')
    const summary = notes.summary[0]
    expect(summary?.kind === 'paragraph' && summary.text.startsWith('PlatinVault bringt deine Steam-Erfolge')).toBe(true)
    expect(notes.sections).toHaveLength(1)
    expect(notes.sections[0]?.category).toBe('new')
    expect(notes.sections[0]?.title).toBe('Neu')
    const list = notes.sections[0]?.blocks[0]
    expect(list?.kind === 'list' && list.items[0]?.startsWith('**Open Source:** Der komplette Quellcode')).toBe(true)
  })
})

describe('parseReleaseNotes other shapes', () => {
  const entry = [
    '## v2026.11.20 (16.300 β) · Unraid, Windows',
    '',
    '### 🔄 Change',
    '',
    '- The **update dialog** was redone, see [the docs](https://example.org/docs).',
    '',
    '### 🔧 Fix',
    '',
    '- Fixed `restore` on a full disk.',
    '',
    '### 🔒 Security',
    '',
    '- Headers.',
    '',
    '### 🗑️ Removed',
    '',
    '- Old thing.',
    '',
    '### ⚠️ Deprecated',
    '',
    '- Older thing.',
    '',
    '### Known issues',
    '',
    'Free text paragraph.',
    '',
    '- bullet',
  ].join('\n')

  it('reads title and platforms, every category in document order, unknown headings as a generic section', () => {
    const notes = parseReleaseNotes(entry, 'en')
    expect(notes.title).toBe('v2026.11.20 (16.300 β)')
    expect(notes.platforms).toBe('Unraid, Windows')
    expect(notes.summary).toEqual([])
    expect(notes.sections.map((section) => section.category)).toEqual(['change', 'fix', 'security', 'removed', 'deprecated', 'other'])
    const other = notes.sections[5]
    expect(other?.title).toBe('Known issues')
    expect(other?.blocks).toEqual([
      { kind: 'paragraph', text: 'Free text paragraph.' },
      { kind: 'list', items: ['bullet'] },
    ])
  })

  it('works with only one language block and without a title', () => {
    const notes = parseReleaseNotes('### Fixed\n\n- one\n- two', 'de')
    expect(notes.title).toBeNull()
    expect(notes.sections).toEqual([{ category: 'fix', title: 'Fixed', blocks: [{ kind: 'list', items: ['one', 'two'] }] }])
  })

  it("keeps GitHub's generated notes readable (## What's Changed, links, deeper headings)", () => {
    const notes = parseReleaseNotes(
      ["## What's Changed", '* Fix thing by @someone in https://github.com/o/r/pull/1', '#### Detail', 'text', '', '**Full Changelog**: https://github.com/o/r/compare/a...b'].join('\n'),
      'en'
    )
    expect(notes.title).toBeNull()
    expect(notes.sections).toHaveLength(1)
    expect(notes.sections[0]?.category).toBe('other')
    expect(notes.sections[0]?.title).toBe("What's Changed")
    expect(notes.sections[0]?.blocks[0]).toEqual({ kind: 'list', items: ['Fix thing by @someone in https://github.com/o/r/pull/1'] })
    expect(notes.sections[0]?.blocks[1]).toEqual({ kind: 'paragraph', text: '**Detail**' })
  })

  it('drops HTML comments and empty sections, keeps a code block, and never throws on odd input', () => {
    const notes = parseReleaseNotes('<!-- note -->\n### New\n\n### Fixed\n\n```\ncode --- here\n```\n', 'en')
    expect(notes.sections).toEqual([{ category: 'fix', title: 'Fixed', blocks: [{ kind: 'code', text: 'code --- here' }] }])
    expect(parseReleaseNotes('', 'en')).toEqual({ title: null, platforms: null, summary: [], sections: [], policyUrl: null })
    expect(hasNotesContent(parseReleaseNotes('   \n  ', 'de'))).toBe(false)
    const plain = parseReleaseNotes('Just some text without any structure.', 'en')
    expect(plain.summary).toEqual([{ kind: 'paragraph', text: 'Just some text without any structure.' }])
    expect(() => parseReleaseNotes('```\nunclosed', 'en')).not.toThrow()
  })

  it('never turns text into HTML: markup stays plain text in the strings', () => {
    const notes = parseReleaseNotes('### New\n- <img src=x onerror=alert(1)> and <script>x</script>', 'en')
    const list = notes.sections[0]?.blocks[0]
    expect(list?.kind === 'list' && list.items[0]).toBe('<img src=x onerror=alert(1)> and <script>x</script>')
  })
})

describe('footer "Code signing policy"', () => {
  const policy = 'https://github.com/sushiflux/PlatinVault#code-signing-policy'
  const withFooter = `${fixture.trimEnd()}\n\n---\n\nCode signing policy: ${policy}\n`

  it('is cut off the end: the German block keeps only its own categories, the policy link is separate', () => {
    const de = parseReleaseNotes(withFooter, 'de')
    expect(de.policyUrl).toBe(policy)
    expect(de.sections.map((section) => section.title)).toEqual(['Neu'])
    expect(JSON.stringify(de)).not.toContain('Code signing policy')
    const en = parseReleaseNotes(withFooter, 'en')
    expect(en.policyUrl).toBe(policy)
    expect(en.sections[0]?.title).toBe('New')
    expect(JSON.stringify(en)).not.toContain('Code signing policy')
  })

  it('does not change what an old release (no footer) parses to', () => {
    const old = parseReleaseNotes(fixture, 'de')
    expect(old.policyUrl).toBeNull()
    expect({ ...parseReleaseNotes(withFooter, 'de'), policyUrl: null }).toEqual(old)
  })

  it('also works with CRLF, without the rule, and for a body with only one language block', () => {
    expect(parseReleaseNotes(withFooter.replace(/\n/g, '\r\n'), 'de').policyUrl).toBe(policy)
    expect(parseReleaseNotes(`### Fixed\n- a\n\nCode signing policy: ${policy}`, 'en')).toMatchObject({ policyUrl: policy, sections: [{ category: 'fix' }] })
  })

  it('only accepts an https address, never shows other text as footer', () => {
    expect(takePolicyFooter('### New\n- a\n\n---\n\nCode signing policy: javascript:alert(1)').policyUrl).toBeNull()
    expect(takePolicyFooter('### New\n- a\n\n---\n\nCode signing policy: http://example.org').policyUrl).toBeNull()
    // The line in the middle of the text is just text
    expect(takePolicyFooter(`Code signing policy: ${policy}\n\n### New\n- a`).policyUrl).toBeNull()
  })
})

describe('hostile release bodies (text from outside) stay fast', () => {
  // The former patterns: kept here only to show the new code cuts the footer exactly as before
  const OLD_FOOTER = /(?:\n[ \t]*(?:[-*_][ \t]*){3,}[ \t]*)?\n+[ \t]*Code signing policy:[ \t]*(\S+)[ \t]*$/i
  const policy = 'https://github.com/sushiflux/PlatinVault#code-signing-policy'

  it('cuts the footer exactly like the former single pattern', () => {
    const bodies = [
      `### New\n- a\n\n---\n\nCode signing policy: ${policy}`,
      `### New\n- a\n---\nCode signing policy: ${policy}`,
      `### New\n- a\n\n * * _ \n\n\nCode signing policy: ${policy}  `,
      `### New\n- a\n\nCode signing policy: ${policy}`,
      `---\nCode signing policy: ${policy}`,
      `\nCode signing policy: ${policy}`,
      `Code signing policy: ${policy}`,
      `### New\n- a\n\n---\ntext\n\nCode signing policy: ${policy}`,
      `### New\n- a\n\nCode signing policy: ${policy} trailing`,
    ]
    for (const body of bodies) {
      const match = OLD_FOOTER.exec(body)
      const expected = match ? { body: body.slice(0, match.index), url: match[1] } : { body, url: undefined }
      const result = takePolicyFooter(body)
      expect(result.body).toBe(expected.body)
      expect(result.policyUrl).toBe(expected.url === policy ? policy : null)
    }
  })

  it('parses 64 KB bodies built to make backtracking patterns explode in well under a second each', () => {
    const size = 64 * 1024
    const bodies = [
      // A heading with blanks inside: the former heading pattern needed about a minute for 8 KB of this
      `## a${' '.repeat(size)}b`,
      // Blank lines before the end (footer search), headings without a category word, a long "[" run (link search)
      `${'\n'.repeat(size)}x`,
      '## \n'.repeat(size / 4),
      `## ${' '.repeat(size)}x`,
      `- ${'['.repeat(size)}`,
    ]
    for (const body of bodies) {
      const started = performance.now()
      parseReleaseNotes(body, 'de')
      parseReleaseNotes(body, 'en')
      expect(performance.now() - started).toBeLessThan(1000)
    }
  })
})
