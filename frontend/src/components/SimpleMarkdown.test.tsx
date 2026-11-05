import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { parseMarkdown, renderInline, SimpleMarkdown } from './SimpleMarkdown'

describe('parseMarkdown', () => {
  it('reads headings, paragraphs and lists', () => {
    const blocks = parseMarkdown('# Title\n\nFirst line\nsecond line\n\n- one\n- two\n  continued\n\n1. a\n2. b\n')
    expect(blocks).toEqual([
      { kind: 'heading', level: 1, text: 'Title' },
      { kind: 'paragraph', text: 'First line second line' },
      { kind: 'list', ordered: false, items: ['one', 'two continued'] },
      { kind: 'list', ordered: true, items: ['a', 'b'] },
    ])
  })

  it('keeps fenced code verbatim and honours a longer fence around backticks', () => {
    const blocks = parseMarkdown('````text\nline 1\n```\n  indented\n````\n\nafter')
    expect(blocks[0]).toEqual({ kind: 'code', text: 'line 1\n```\n  indented' })
    expect(blocks[1]).toEqual({ kind: 'paragraph', text: 'after' })
  })

  it('reads a table with its separator row', () => {
    const blocks = parseMarkdown('| Bereich | Anzahl |\n| --- | --- |\n| npm | 42 |\n| NuGet | 5 |\n')
    expect(blocks).toEqual([{ kind: 'table', header: ['Bereich', 'Anzahl'], rows: [['npm', '42'], ['NuGet', '5']] }])
  })

  it('drops HTML comments (notes to the owner) and HTML-only lines', () => {
    const blocks = parseMarkdown('<!--\nDraft note\n-->\n\n# Licence\n\n<a id="x"></a>\n\ntext')
    expect(blocks).toEqual([
      { kind: 'heading', level: 1, text: 'Licence' },
      { kind: 'paragraph', text: 'text' },
    ])
  })

  it('does not swallow the rest of the document when a fence is never closed', () => {
    const blocks = parseMarkdown('```\nopen\nstill code')
    expect(blocks).toEqual([{ kind: 'code', text: 'open\nstill code' }])
  })
})

describe('SimpleMarkdown', () => {
  it('never turns raw HTML in the source into elements', () => {
    const { container } = render(<SimpleMarkdown source={'Hello <script>alert(1)</script> <img src=x onerror=alert(1)> world'} />)
    expect(container.querySelector('script')).toBeNull()
    expect(container.querySelector('img')).toBeNull()
    expect(container.textContent).toContain('<script>alert(1)</script>')
  })

  it('links only http(s) addresses, always with rel noreferrer', () => {
    const { container } = render(
      <SimpleMarkdown source={'[good](https://example.com/a) [bad](javascript:alert(1)) and https://example.org/x, then text'} />
    )
    const links = [...container.querySelectorAll('a')]
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['https://example.com/a', 'https://example.org/x'])
    for (const link of links) expect(link.getAttribute('rel')).toContain('noreferrer')
    expect(container.textContent).toContain('[bad](javascript:alert(1))')
  })

  it('renders licence text blocks as preformatted text and inline code/bold', () => {
    const { container } = render(<SimpleMarkdown source={'**Wichtig** und `code`\n\n```text\nMIT   License\n  text\n```'} />)
    expect(container.querySelector('strong')?.textContent).toBe('Wichtig')
    expect(container.querySelector('p code')?.textContent).toBe('code')
    expect(container.querySelector('pre')?.textContent).toBe('MIT   License\n  text')
  })
})

describe('hostile input stays fast', () => {
  it('a long heading line with blanks and a long "[" run render in well under a second', () => {
    const started = performance.now()
    parseMarkdown(`## a${' '.repeat(64 * 1024)}b`)
    renderInline('['.repeat(64 * 1024))
    expect(performance.now() - started).toBeLessThan(1000)
    // A normal link still is one
    expect(render(<p>{renderInline('see [docs](https://example.org/d)')}</p>).container.querySelector('a')?.getAttribute('href')).toBe('https://example.org/d')
  })
})
