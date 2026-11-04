import type { GuideTabDto } from '../../api/types'
import { buildOutline } from './outline'

/** Where a tickable row lives: the chapter that defines it. */
export interface RowLocation {
  tabId: string
  tabSlug: string
  tabTitle: string
}

/** A place that points at a defining row ("you get this in ..."): chapter, readable path and the element id of the heading to jump back to. */
export interface BackRef {
  tabId: string
  tabSlug: string
  tabTitle: string
  // "Walkthrough · Part 2 · Harbour"
  path: string
  // Element id of the nearest walkthrough heading above the referring row (null = jump to the chapter only)
  anchorId: string | null
}

/** The server's maps (`game.refTargets` forward, `game.itemLinks` back); the client works out the same from the chapters when they are missing. */
export interface ServerItemLinks {
  refTargets?: Record<string, { tabId: string; tabSlug: string; tabTitle: string }> | null
  itemLinks?: Record<string, { tabId: string; tabSlug: string; tabTitle: string; sectionIndex: number; path: string }[]> | null
}

export interface ItemLinkIndex {
  /** The chapter that defines the row with this item id (null = no such row). */
  target(ref: string | null | undefined): RowLocation | null
  /** The key whose check a row uses: its `ref` when that row exists, else its own item id. */
  resolveKey(own: string, ref: string | null | undefined): string
  /** Where rows or side quests of other places point at this row. */
  backRefs(itemId: string): BackRef[]
}

export const cleanTabTitle = (title: string) => title.replace(/^\d+\.\s*/, '')

/** Same prefix as ChapterBody, so the heading ids match. */
export const headingPrefix = (tabId: string) => `pv-wt-${tabId.replace(/[^a-zA-Z0-9_-]/g, '')}`

const clean = (value: string | null | undefined) => value?.trim() || null

/** Reads the whole guide once: which row is defined where, and which rows / side quests point at it. */
export function buildItemLinkIndex(tabs: GuideTabDto[], server?: ServerItemLinks | null): ItemLinkIndex {
  const defs = new Map<string, RowLocation>()
  const backs = new Map<string, BackRef[]>()
  const chapters = tabs.filter((tab) => !tab.isAchievementList)

  for (const tab of chapters) {
    const location: RowLocation = { tabId: tab.id, tabSlug: tab.slug, tabTitle: cleanTabTitle(tab.title) }
    for (const section of tab.sections) {
      for (const row of section.table?.rows ?? []) if (!clean(row.ref) && !defs.has(row.itemId)) defs.set(row.itemId, location)
      const puzzleId = clean(section.puzzle?.itemId)
      if (puzzleId && !defs.has(puzzleId)) defs.set(puzzleId, location)
    }
  }

  const outlines = new Map(chapters.map((tab) => [tab.id, buildOutline(tab.sections, headingPrefix(tab.id))]))
  /** The area (else part) heading above a section and the readable path to it. */
  const placeOf = (tab: GuideTabDto, index: number) => {
    const outline = outlines.get(tab.id)!
    const before = outline.entries.filter((entry) => entry.index < index)
    const last = before[before.length - 1]
    const part = [...before].reverse().find((entry) => entry.level === 1)
    const path = [cleanTabTitle(tab.title), part?.title, last && last !== part ? last.title : undefined].filter(Boolean).join(' · ')
    return { anchorId: last?.id ?? null, path }
  }
  const addBack = (ref: string, tab: GuideTabDto, index: number) => {
    const { anchorId, path } = placeOf(tab, index)
    const list = backs.get(ref) ?? []
    if (!list.some((item) => item.tabId === tab.id && item.anchorId === anchorId)) {
      list.push({ tabId: tab.id, tabSlug: tab.slug, tabTitle: cleanTabTitle(tab.title), path, anchorId })
    }
    backs.set(ref, list)
  }

  for (const tab of chapters) {
    tab.sections.forEach((section, index) => {
      const refs = [
        ...(section.table?.rows ?? []).map((row) => ({ own: row.itemId, ref: clean(row.ref) })),
        ...(section.type === 'alert' && section.kind === 'side_quests' ? (section.quests ?? []).map((quest) => ({ own: '', ref: clean(quest?.ref) })) : []),
      ]
      for (const { own, ref } of refs) {
        if (!ref || ref === own || !defs.has(ref)) continue
        addBack(ref, tab, index)
      }
    })
  }

  // The server's back-reference map fills in what the page itself did not find; the section it names is located here for the heading to jump to
  for (const [ref, links] of Object.entries(server?.itemLinks ?? {})) {
    if (backs.has(ref)) continue
    for (const link of links) {
      const tab = chapters.find((candidate) => candidate.id === link.tabId)
      if (!tab) continue
      if (link.sectionIndex >= 0 && link.sectionIndex < tab.sections.length) addBack(ref, tab, link.sectionIndex)
      else {
        const title = cleanTabTitle(tab.title)
        backs.set(ref, [...(backs.get(ref) ?? []), { tabId: tab.id, tabSlug: tab.slug, tabTitle: title, path: [title, link.path].filter(Boolean).join(' · '), anchorId: null }])
      }
    }
  }

  const locate = (ref: string): RowLocation | null => {
    const found = defs.get(ref)
    if (found) return found
    const forward = server?.refTargets?.[ref]
    return forward ? { tabId: forward.tabId, tabSlug: forward.tabSlug, tabTitle: cleanTabTitle(forward.tabTitle) } : null
  }

  return {
    target: (ref) => (ref ? locate(ref) : null),
    resolveKey: (own, ref) => {
      const key = clean(ref)
      return key && key !== own && locate(key) ? key : own
    },
    backRefs: (itemId) => backs.get(itemId) ?? [],
  }
}
