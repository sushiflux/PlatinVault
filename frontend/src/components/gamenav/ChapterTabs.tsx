import { useEffect, useMemo, useRef, type KeyboardEvent } from 'react'
import { useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { useTruncated } from '../../utils/useTruncated'
import { Tooltip } from '../ui/Tooltip'
import { SlidingMarker } from './SlidingMarker'
import './gamenav.css'

// The groups of the chapter navigation, in the fixed order they are shown in (a key the server sends that is not listed is
// treated as "extras"; chapters without a key as well, as soon as the guide has groups at all)
export const CHAPTER_GROUPS = ['start', 'story', 'characters', 'combat', 'progress', 'explore', 'economy', 'extras'] as const
export type ChapterGroup = (typeof CHAPTER_GROUPS)[number]

export interface ChapterItem {
  slug: string
  // Title without its leading "3. "
  title: string
  checked: number
  total: number
  // The group key the server assigned to the chapter (null = none)
  groupKey: string | null
}

interface ChapterTabsProps {
  chapters: ChapterItem[]
  // Slug of the active chapter, or null while one of the sections (live trophies, statistics, story) is active
  activeSlug: string | null
  onSelect: (slug: string) => void
  // The side nav is collapsed to numbers: every tab then needs its tooltip
  collapsed?: boolean
}

// Prefix of the DOM ids, so a section tab can name the active chapter as the label of the panel
export const tabDomId = (slug: string) => `pv-gtab-${slug.replace(/[^a-zA-Z0-9_-]/g, '_')}`

function groupOf(key: string | null): ChapterGroup {
  return (CHAPTER_GROUPS as readonly string[]).includes(key ?? '') ? (key as ChapterGroup) : 'extras'
}

interface Section {
  key: ChapterGroup | null
  // Position in the display order (1-based), one running number across all groups
  tabs: { chapter: ChapterItem; number: number }[]
}

/**
 * The chapters in display order: grouped in the fixed group order (chapters keep their own order inside a group), empty groups
 * left out. A guide with one group only - or with no group keys at all - is one plain list without headings.
 */
export function buildSections(chapters: ChapterItem[]): { sections: Section[]; headings: boolean } {
  const hasKeys = chapters.some((chapter) => chapter.groupKey)
  const grouped = hasKeys ? CHAPTER_GROUPS.map((key) => ({ key, items: chapters.filter((chapter) => groupOf(chapter.groupKey) === key) })).filter((g) => g.items.length > 0) : []
  const headings = grouped.length > 1
  const raw = headings ? grouped.map((g) => ({ key: g.key as ChapterGroup | null, items: g.items })) : [{ key: null, items: chapters }]
  let counter = 0
  const sections: Section[] = raw.map((g) => ({ key: g.key, tabs: g.items.map((chapter) => ({ chapter, number: ++counter })) }))
  return { sections, headings }
}

function ChapterCount({ chapter }: { chapter: ChapterItem }) {
  if (chapter.total <= 0) return null
  return (
    <span className="pv-tab-count" data-done={chapter.checked >= chapter.total ? 'true' : undefined}>
      {chapter.checked}/{chapter.total}
    </span>
  )
}

interface ChapterTabProps {
  chapter: ChapterItem
  number: number
  selected: boolean
  // Roving tabindex: only one tab of the list is in the tab order
  tabbable: boolean
  collapsed: boolean
  onSelect: (slug: string) => void
}

function ChapterTab({ chapter, number, selected, tabbable, collapsed, onSelect }: ChapterTabProps) {
  const { t } = useTranslation('game')
  const titleRef = useRef<HTMLSpanElement>(null)
  const truncated = useTruncated(titleRef)
  return (
    <Tooltip
      label={`${number}. ${chapter.title}`}
      description={chapter.total > 0 ? t('chapters.progress', { checked: chapter.checked, total: chapter.total }) : t('chapters.noItems')}
      side="bottom"
      disabled={!truncated && !collapsed}
      className="pv-tab-wrap"
    >
      <button
        type="button"
        role="tab"
        id={tabDomId(chapter.slug)}
        aria-selected={selected}
        aria-label={collapsed ? `${number}. ${chapter.title}` : undefined}
        tabIndex={tabbable ? 0 : -1}
        className="pv-tab"
        onClick={() => onSelect(chapter.slug)}
      >
        <span className="pv-tab-num">{number}.</span>
        <span ref={titleRef} className="pv-tab-title">
          {chapter.title}
        </span>
        <ChapterCount chapter={chapter} />
      </button>
    </Tooltip>
  )
}

/**
 * The guide chapters as a vertical tab list (the content of the side nav): optionally in groups with small headings, a sliding
 * marker at the left edge of the active chapter, arrow keys / Home / End move through the tabs and activate the one they land
 * on. When a section tab (live trophies, statistics, story) is active, no chapter is highlighted - exactly one active highlight
 * across the nav and the trophy bar.
 */
export function ChapterTabs({ chapters, activeSlug, onSelect, collapsed = false }: ChapterTabsProps) {
  const { t } = useTranslation('game')
  const reduce = useReducedMotion() ?? false
  const listRef = useRef<HTMLDivElement>(null)
  const { sections, headings } = useMemo(() => buildSections(chapters), [chapters])
  const ordered = sections.flatMap((section) => section.tabs.map((tab) => tab.chapter.slug))
  const activeVisible = activeSlug !== null && ordered.includes(activeSlug)
  const tabbableSlug = activeVisible ? activeSlug : (ordered[0] ?? null)

  // A long guide scrolls inside the nav: the active chapter is always brought into view there (a deep link, the arrow keys) - only
  // the list scrolls, never the page
  useEffect(() => {
    const list = listRef.current
    const tab = list?.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')
    if (!list || !tab) return
    const l = list.getBoundingClientRect()
    const r = tab.getBoundingClientRect()
    const margin = 24
    if (r.top < l.top + margin) list.scrollTop -= l.top + margin - r.top
    else if (r.bottom > l.bottom - margin) list.scrollTop += r.bottom - (l.bottom - margin)
  }, [activeSlug])

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
    const tabs = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="tab"]')]
    const index = tabs.indexOf(document.activeElement as HTMLElement)
    if (index < 0) return
    event.preventDefault()
    const target = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + tabs.length) % tabs.length
    tabs[target]?.focus()
    tabs[target]?.click()
  }

  return (
    <div ref={listRef} className="pv-gnav-list" role="tablist" aria-label={t('chapters.listLabel')} aria-orientation="vertical" onKeyDown={onKeyDown}>
      {sections.map((section) => (
        <div key={section.key ?? 'all'} className="pv-gnav-group" role="presentation">
          {headings && section.key && (
            <div className="pv-gnav-group-title" role="presentation">
              {t(`chapters.groups.${section.key}`)}
            </div>
          )}
          {section.tabs.map(({ chapter, number }) => (
            <ChapterTab
              key={chapter.slug}
              chapter={chapter}
              number={number}
              selected={chapter.slug === activeSlug}
              tabbable={chapter.slug === tabbableSlug}
              collapsed={collapsed}
              onSelect={onSelect}
            />
          ))}
        </div>
      ))}
      <SlidingMarker containerRef={listRef} activeKey={activeSlug} mode="bar" className="pv-tab-indicator" reduce={reduce} />
    </div>
  )
}
