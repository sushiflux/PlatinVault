import type { GuideSectionDto } from '../../api/types'

/** A heading of the walkthrough as the table of contents lists it. */
export interface OutlineEntry {
  // Element id of the heading (jump target)
  id: string
  // 1 = part of the game, 2 = area inside a part
  level: 1 | 2
  title: string
  // Index of the heading in the chapter's sections
  index: number
  // "1" for part 1, "1.2" for the second area of part 1 (empty in a chapter without levels)
  number: string
}

/** The sections of one area (or of the part's own introduction); `entry` is its heading. */
export interface OutlineGroup {
  entry?: OutlineEntry
  // Indices of the sections that belong to it (heading excluded)
  indices: number[]
}

export interface OutlinePart extends OutlineGroup {
  areas: OutlineGroup[]
}

export interface Outline {
  // True when the walkthrough uses both levels: parts get a number band, areas their own frame. A chapter with headings of one kind only
  // (older guides) keeps the plain headings and gets a flat table of contents.
  structured: boolean
  entries: OutlineEntry[]
  // The chapter in reading order; the first part has no entry when sections come before the first heading
  parts: OutlinePart[]
  // Sections of type "sources": drawn once at the very end, outside every frame
  tail: number[]
}

export const headingLevel = (section: GuideSectionDto): 1 | 2 => (section.level === 2 ? 2 : 1)

const isHeading = (section: GuideSectionDto) => section.type === 'heading' && !!section.title?.trim()

/** The table of contents is worth showing from this many headings on. */
export const TOC_MIN_ENTRIES = 2

/**
 * Reads the structure of a chapter: its headings (with numbers), the parts and areas they open, and which sections belong where. The ids are made from
 * `idPrefix` and the section index, so they are stable while the chapter does not change.
 */
export function buildOutline(sections: GuideSectionDto[], idPrefix: string): Outline {
  const headings = sections.map((section, index) => ({ section, index })).filter(({ section }) => isHeading(section))
  const structured = headings.some(({ section }) => headingLevel(section) === 2)
  const entries: OutlineEntry[] = []
  let part = 0
  let area = 0
  for (const { section, index } of headings) {
    const level = structured ? headingLevel(section) : 1
    if (level === 1) {
      part += 1
      area = 0
    } else {
      area += 1
    }
    entries.push({
      id: `${idPrefix}-h${index}`,
      level,
      title: section.title!.trim(),
      index,
      number: !structured ? '' : level === 1 ? `${part}` : part > 0 ? `${part}.${area}` : `${area}`,
    })
  }

  const parts: OutlinePart[] = [{ indices: [], areas: [] }]
  const tail: number[] = []
  const entryAt = new Map(entries.map((entry) => [entry.index, entry]))
  sections.forEach((section, index) => {
    if (section.type === 'sources') {
      tail.push(index)
      return
    }
    const entry = entryAt.get(index)
    const current = parts[parts.length - 1]!
    if (entry && structured && entry.level === 1) {
      parts.push({ entry, indices: [], areas: [] })
    } else if (entry && structured) {
      current.areas.push({ entry, indices: [] })
    } else if (entry) {
      // Without levels there are no frames: every heading simply starts a new part
      parts.push({ entry, indices: [], areas: [] })
    } else if (current.areas.length > 0) {
      current.areas[current.areas.length - 1]!.indices.push(index)
    } else {
      current.indices.push(index)
    }
  })
  return { structured, entries, parts, tail }
}
