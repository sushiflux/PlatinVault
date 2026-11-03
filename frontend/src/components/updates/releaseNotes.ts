// Parses the release notes of one version into structured data for the notes dialog. The input is the body of the GitHub release,
// which is the entry of CHANGELOG.md in two blocks (English, a "---" rule, German - see scripts/release-notes.sh):
//
//   ## v2026.11.14 (15.116022)            <- title (version, optionally " · platforms")
//   Summary paragraph(s)                  <- only the first entry of a product has one
//   ### ✨ New                            <- one section per category, bullets below
//   - **Bold lead:** text ...
//
// Pure functions only: no HTML is ever produced here, the dialog renders the strings itself. Unknown headings become a generic
// section, so a hand-written release body (or GitHub's generated "What's Changed") still shows up in a readable form.

export type NotesLanguage = 'de' | 'en'

export type NotesCategory = 'new' | 'change' | 'fix' | 'security' | 'removed' | 'deprecated' | 'other'

export type NotesBlock =
  | { kind: 'paragraph'; text: string }
  | { kind: 'list'; items: string[] }
  | { kind: 'code'; text: string }

export interface NotesSection {
  category: NotesCategory
  /** The heading as written, without the leading emoji ("New", "Neu", "What's Changed") */
  title: string
  blocks: NotesBlock[]
}

export interface ParsedReleaseNotes {
  /** The entry heading ("v2026.11.14 (15.116022)") without the platforms, null when the body has none */
  title: string | null
  /** The platforms after " · " in the heading ("Unraid, Windows"), null when there are none */
  platforms: string | null
  /** Everything before the first section: the short "what the app can do" paragraph of a first release */
  summary: NotesBlock[]
  sections: NotesSection[]
  /** The code signing policy address of the release footer ("---" + "Code signing policy: <https url>" after both language blocks), null when there is none */
  policyUrl: string | null
}

const RULE = /^ {0,3}([-*_])(?:\s*\1){2,}\s*$/
const FENCE = /^ {0,3}(`{3,}|~{3,})/
const HEADING = /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/
const LIST_ITEM = /^\s*(?:[-*+]|\d+[.)])\s+(.*)$/
const HTML_COMMENT = /<!--[\s\S]*?-->/g

// Category words in both languages. The match is on the heading text after the emoji, at its start.
const CATEGORY_WORDS: Array<[NotesCategory, RegExp]> = [
  ['new', /^(new|neu|added|features?|hinzugef[uü]gt)\b/i],
  ['change', /^(changed?|changes|ge[aä]ndert|[aä]nderungen?|improved|improvements?|updated?|verbessert)\b/i],
  ['fix', /^(fix|fixes|fixed|bug ?fix(es)?|behoben|fehlerbehebung(en)?|korrigiert)\b/i],
  ['security', /^(security|sicherheit)\b/i],
  ['removed', /^(removed|entfernt)\b/i],
  ['deprecated', /^(deprecated|veraltet)\b/i],
]

/** Removes a leading emoji / symbol run (and variation selectors) from a heading: "✨ New" -> "New", "🗑️ Removed" -> "Removed". */
export function stripLeadingSymbols(text: string): string {
  return text.replace(/^[^\p{L}\p{N}]+/u, '').trim()
}

export function categoryOf(heading: string): NotesCategory {
  const label = stripLeadingSymbols(heading)
  for (const [category, pattern] of CATEGORY_WORDS) {
    if (pattern.test(label)) return category
  }
  return 'other'
}

// The footer every release body can end with: a rule, then one line "Code signing policy: <url>". Old releases have none.
const POLICY_FOOTER = /(?:\n[ \t]*(?:[-*_][ \t]*){3,}[ \t]*)?\n+[ \t]*Code signing policy:[ \t]*(\S+)[ \t]*$/i

/** Cuts the footer off the end of the body. Only an https address counts; anything else is dropped without being shown. */
export function takePolicyFooter(markdown: string): { body: string; policyUrl: string | null } {
  const text = markdown.replace(/\r\n?/g, '\n').trimEnd()
  const match = POLICY_FOOTER.exec(text)
  if (!match) return { body: text, policyUrl: null }
  const url = match[1] ?? ''
  return { body: text.slice(0, match.index), policyUrl: /^https:\/\/[^\s<>"]+$/.test(url) ? url : null }
}

/** Splits the body at its top-level "---" rules (rules inside code fences stay); empty parts are dropped. */
export function splitLanguageBlocks(markdown: string): string[] {
  const parts: string[][] = [[]]
  let fence: string | null = null
  for (const line of markdown.replace(/\r\n?/g, '\n').split('\n')) {
    const marker = FENCE.exec(line)?.[1]
    if (marker) {
      if (fence === null) fence = marker
      else if (marker[0] === fence[0] && marker.length >= fence.length) fence = null
    }
    if (fence === null && !marker && RULE.test(line)) {
      parts.push([])
      continue
    }
    parts[parts.length - 1]?.push(line)
  }
  return parts.map((part) => part.join('\n').trim()).filter((part) => part.length > 0)
}

const GERMAN_HEADING = /^#{2,4}\s+\P{L}*(neu|ge[aä]ndert|behoben|sicherheit|entfernt|veraltet)\b/imu
const ENGLISH_HEADING = /^#{2,4}\s+\P{L}*(new|changed|fixed|fix|security|removed|deprecated)\b/imu
const GERMAN_WORDS = /\b(und|der|die|das|nicht|eine?|mit|für|wird|werden)\b/gi
const ENGLISH_WORDS = /\b(and|the|not|with|for|is|are|you|your|to)\b/gi

/** Which language a block is written in: by its category headings, otherwise by common words; null when it cannot tell. */
export function detectLanguage(block: string): NotesLanguage | null {
  const german = GERMAN_HEADING.test(block)
  const english = ENGLISH_HEADING.test(block)
  if (german !== english) return german ? 'de' : 'en'
  const germanWords = block.match(GERMAN_WORDS)?.length ?? 0
  const englishWords = block.match(ENGLISH_WORDS)?.length ?? 0
  if (germanWords === englishWords) return null
  return germanWords > englishWords ? 'de' : 'en'
}

/** The block for the UI language. One block = that one; several: the one written in the language, else the first (English) or last (German). */
export function pickLanguageBlock(blocks: string[], language: NotesLanguage): string {
  const first = blocks[0]
  if (first === undefined) return ''
  if (blocks.length === 1) return first
  const match = blocks.find((block) => detectLanguage(block) === language)
  if (match !== undefined) return match
  return language === 'en' ? first : (blocks[blocks.length - 1] ?? first)
}

function parseBlock(markdown: string): ParsedReleaseNotes {
  const result: ParsedReleaseNotes = { title: null, platforms: null, summary: [], sections: [], policyUrl: null }
  const lines = markdown.replace(/\r\n?/g, '\n').replace(HTML_COMMENT, '').split('\n')

  let paragraph: string[] = []
  let list: string[] | null = null
  let code: string[] | null = null
  let fence = ''

  const target = (): NotesBlock[] => result.sections[result.sections.length - 1]?.blocks ?? result.summary
  const flushParagraph = () => {
    if (paragraph.length > 0) target().push({ kind: 'paragraph', text: paragraph.join(' ') })
    paragraph = []
  }
  const flushList = () => {
    if (list && list.length > 0) target().push({ kind: 'list', items: list })
    list = null
  }
  const flush = () => {
    flushParagraph()
    flushList()
  }

  for (const line of lines) {
    if (code !== null) {
      const closing = FENCE.exec(line)?.[1]
      if (closing && closing[0] === fence[0] && closing.length >= fence.length) {
        target().push({ kind: 'code', text: code.join('\n') })
        code = null
      } else {
        code.push(line)
      }
      continue
    }

    const opening = FENCE.exec(line)?.[1]
    if (opening) {
      flush()
      code = []
      fence = opening
      continue
    }

    if (line.trim() === '') {
      flushParagraph()
      continue
    }

    const heading = HEADING.exec(line)
    if (heading) {
      flush()
      const level = (heading[1] ?? '#').length
      const text = heading[2] ?? ''
      const isFirst = result.title === null && result.sections.length === 0 && result.summary.length === 0
      if (level <= 2 && isFirst && /^v?\d/i.test(text)) {
        // "v2026.11.12 (13.557 β) · Unraid, Windows"
        const dot = text.indexOf(' · ')
        result.title = dot >= 0 ? text.slice(0, dot).trim() : text
        result.platforms = dot >= 0 ? text.slice(dot + 3).trim() || null : null
      } else if (level <= 3) {
        result.sections.push({ category: categoryOf(text), title: stripLeadingSymbols(text) || text, blocks: [] })
      } else {
        paragraph.push(`**${text}**`)
        flushParagraph()
      }
      continue
    }

    if (RULE.test(line)) {
      flush()
      continue
    }

    const item = LIST_ITEM.exec(line)
    if (item) {
      flushParagraph()
      list ??= []
      list.push(item[1] ?? '')
      continue
    }

    if (list && /^\s+\S/.test(line)) {
      // Indented continuation of the last bullet
      list[list.length - 1] += ' ' + line.trim()
      continue
    }
    flushList()
    paragraph.push(line.trim())
  }
  // An unclosed fence still shows what it collected
  if (code !== null && code.length > 0) target().push({ kind: 'code', text: code.join('\n') })
  flush()

  result.sections = result.sections.filter((section) => section.blocks.length > 0)
  return result
}

/**
 * Release notes of one version in the UI language. `markdown` may hold both language blocks (English, "---", German) or just one.
 * Never throws: text that is not Markdown at all comes back as a summary of plain paragraphs.
 */
export function parseReleaseNotes(markdown: string, language: NotesLanguage): ParsedReleaseNotes {
  const { body, policyUrl } = takePolicyFooter(markdown)
  return { ...parseBlock(pickLanguageBlock(splitLanguageBlocks(body), language)), policyUrl }
}

/** True when the parsed notes have anything to show. */
export function hasNotesContent(notes: ParsedReleaseNotes): boolean {
  return notes.summary.length > 0 || notes.sections.length > 0
}

/** Number of bullet points in a section (the count chip in the dialog). */
export function itemCount(section: NotesSection): number {
  return section.blocks.reduce((sum, block) => sum + (block.kind === 'list' ? block.items.length : 0), 0)
}
