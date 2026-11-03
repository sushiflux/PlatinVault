import { useMemo } from 'react'
import type { ReactNode } from 'react'

// A small, safe Markdown renderer for the shipped Markdown documents (THIRD-PARTY-NOTICES.md). It builds React elements only -
// no HTML string is ever injected, raw HTML in the source is never interpreted, and links are limited to http(s). Supported:
// headings, paragraphs, bullet and numbered lists (one level), fenced code blocks, tables, rules, and inline `code`, **bold** and
// links (also bare URLs). HTML comments (notes to the owner) and lines that consist of HTML tags only are dropped.

export type MarkdownBlock =
  | { kind: 'heading'; level: 1 | 2 | 3 | 4 | 5 | 6; text: string }
  | { kind: 'paragraph'; text: string }
  | { kind: 'list'; ordered: boolean; items: string[] }
  | { kind: 'code'; text: string }
  | { kind: 'table'; header: string[]; rows: string[][] }
  | { kind: 'rule' }

const FENCE = /^ {0,3}(`{3,}|~{3,})(.*)$/
const HEADING = /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/
const RULE = /^ {0,3}([-*_])(?:\s*\1){2,}\s*$/
const LIST_ITEM = /^\s*(?:([-*+])|(\d+)[.)])\s+(.*)$/
const TABLE_SEPARATOR = /^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)*\|?\s*$/
const HTML_ONLY_LINE = /^\s*(?:<\/?[a-z][^>]*>\s*)+$/i
const HTML_COMMENT = /<!--[\s\S]*?-->/g

function splitRow(line: string): string[] {
  let row = line.trim()
  if (row.startsWith('|')) row = row.slice(1)
  if (row.endsWith('|')) row = row.slice(0, -1)
  return row.split('|').map((cell) => cell.trim())
}

/** Splits the Markdown source into blocks. Exported for the unit tests. */
export function parseMarkdown(source: string): MarkdownBlock[] {
  const lines = source.replace(/\r\n?/g, '\n').replace(HTML_COMMENT, '').split('\n')
  // The array is only read inside its bounds; the helper keeps the strict index typing quiet
  const at = (index: number): string => lines[index] ?? ''
  const blocks: MarkdownBlock[] = []
  let paragraph: string[] = []

  const flushParagraph = () => {
    if (paragraph.length > 0) {
      blocks.push({ kind: 'paragraph', text: paragraph.join(' ') })
      paragraph = []
    }
  }

  for (let i = 0; i < lines.length; i++) {
    const line = at(i)

    const fence = FENCE.exec(line)
    if (fence) {
      flushParagraph()
      const marker = fence[1] ?? '```'
      const code: string[] = []
      i++
      // A closing fence is the same character, at least as long, with nothing else on the line
      const closes = (candidate: string) => {
        const t = candidate.trim()
        return t.length >= marker.length && [...t].every((ch) => ch === marker[0])
      }
      while (i < lines.length && !closes(at(i))) {
        code.push(at(i))
        i++
      }
      blocks.push({ kind: 'code', text: code.join('\n') })
      continue
    }

    if (line.trim() === '') {
      flushParagraph()
      continue
    }

    if (HTML_ONLY_LINE.test(line)) continue

    const heading = HEADING.exec(line)
    if (heading) {
      flushParagraph()
      blocks.push({ kind: 'heading', level: (heading[1] ?? '#').length as 1 | 2 | 3 | 4 | 5 | 6, text: heading[2] ?? '' })
      continue
    }

    if (RULE.test(line)) {
      flushParagraph()
      blocks.push({ kind: 'rule' })
      continue
    }

    if (line.trim().startsWith('|') && TABLE_SEPARATOR.test(at(i + 1))) {
      flushParagraph()
      const header = splitRow(line)
      const rows: string[][] = []
      i += 2
      while (i < lines.length && at(i).trim().startsWith('|')) {
        rows.push(splitRow(at(i)))
        i++
      }
      i--
      blocks.push({ kind: 'table', header, rows })
      continue
    }

    const item = LIST_ITEM.exec(line)
    if (item) {
      flushParagraph()
      const ordered = item[2] !== undefined
      const items: string[] = []
      while (i < lines.length) {
        const current = LIST_ITEM.exec(at(i))
        if (current && (current[2] !== undefined) === ordered) {
          items.push(current[3] ?? '')
        } else if (current || at(i).trim() === '' || !/^\s+\S/.test(at(i))) {
          break
        } else {
          // Indented continuation line of the previous item
          items[items.length - 1] += ' ' + at(i).trim()
        }
        i++
      }
      i--
      blocks.push({ kind: 'list', ordered, items })
      continue
    }

    paragraph.push(line.trim())
  }

  flushParagraph()
  return blocks
}

// `code`, **bold**, [text](http(s)-url) and bare http(s) URLs. Anything else stays plain text (React escapes it).
const INLINE = /(`[^`\n]+`)|(\*\*[^*\n]+\*\*)|(\[[^\]\n]+\]\(https?:\/\/[^\s)]+\))|(https?:\/\/[^\s<>`)\]]*[^\s<>`)\].,;:!?])/g

function renderInline(text: string): ReactNode[] {
  const out: ReactNode[] = []
  let last = 0
  let key = 0
  for (const match of text.matchAll(INLINE)) {
    const index = match.index ?? 0
    if (index > last) out.push(text.slice(last, index))
    const token = match[0]
    if (match[1]) {
      out.push(<code key={key++}>{token.slice(1, -1)}</code>)
    } else if (match[2]) {
      out.push(<strong key={key++}>{token.slice(2, -2)}</strong>)
    } else if (match[3]) {
      const split = token.indexOf('](')
      out.push(
        <a key={key++} href={token.slice(split + 2, -1)} target="_blank" rel="noreferrer noopener">
          {token.slice(1, split)}
        </a>
      )
    } else {
      out.push(
        <a key={key++} href={token} target="_blank" rel="noreferrer noopener">
          {token}
        </a>
      )
    }
    last = index + token.length
  }
  if (last < text.length) out.push(text.slice(last))
  return out
}

function renderBlock(block: MarkdownBlock, index: number): ReactNode {
  switch (block.kind) {
    case 'heading': {
      const Tag = `h${block.level}` as const
      return (
        <Tag key={index} className={`pv-md-h pv-md-h${block.level}`}>
          {renderInline(block.text)}
        </Tag>
      )
    }
    case 'paragraph':
      return (
        <p key={index} className="pv-md-p">
          {renderInline(block.text)}
        </p>
      )
    case 'list': {
      const Tag = block.ordered ? 'ol' : 'ul'
      return (
        <Tag key={index} className="pv-md-list">
          {block.items.map((item, i) => (
            <li key={i}>{renderInline(item)}</li>
          ))}
        </Tag>
      )
    }
    case 'code':
      // Licence texts: pre-wrapped (no sideways scrolling), selectable, in the original wording
      return (
        <pre key={index} className="pv-md-code">
          {block.text}
        </pre>
      )
    case 'table':
      return (
        <div key={index} className="pv-md-table-wrap">
          <table className="pv-md-table">
            <thead>
              <tr>
                {block.header.map((cell, i) => (
                  <th key={i} scope="col">
                    {renderInline(cell)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {block.rows.map((row, r) => (
                <tr key={r}>
                  {row.map((cell, c) => (
                    <td key={c}>{renderInline(cell)}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )
    case 'rule':
      return <hr key={index} className="pv-md-rule" />
  }
}

/** Renders a Markdown document (see the list of supported syntax at the top of this file). */
export function SimpleMarkdown({ source }: { source: string }) {
  const blocks = useMemo(() => parseMarkdown(source), [source])
  return <div className="pv-md">{blocks.map(renderBlock)}</div>
}
