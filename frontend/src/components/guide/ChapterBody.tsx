import { Fragment, useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { IconChevronDown, IconListDetails } from '@tabler/icons-react'
import { useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import type { GuideSectionDto } from '../../api/types'
import { GuideHeading } from './GuideBlocks'
import { buildOutline, TOC_MIN_ENTRIES } from './outline'
import type { Outline, OutlineEntry } from './outline'
import './guide.css'

// From this width of the content area on, the table of contents sits in a sticky column next to the content (else a collapsible list on top)
const WIDE_REM = 60

/** Where headings stop being "under" the sticky bars: the bottom of the header + trophy bar, plus a little air. */
function stickyOffsetPx(): number {
  const raw = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--pv-gbar-bottom'))
  return (Number.isFinite(raw) ? raw : 128) + 28
}

/** The heading that is "current" while scrolling: the last one whose top has passed the sticky bars. Clicking a jump link sets it right away. */
export function useActiveHeading(ids: string[]) {
  const [active, setActive] = useState(ids[0] ?? '')
  const holdUntil = useRef(0)
  const reduceMotion = useReducedMotion()
  const key = ids.join('|')

  useEffect(() => {
    const list = key ? key.split('|') : []
    let frame = 0
    const update = () => {
      frame = 0
      if (performance.now() < holdUntil.current) return
      const offset = stickyOffsetPx()
      let current = list[0] ?? ''
      for (const id of list) {
        const el = document.getElementById(id)
        if (!el) continue
        if (el.getBoundingClientRect().top <= offset) current = id
        else break
      }
      setActive(current)
    }
    const onScroll = () => {
      if (!frame) frame = window.requestAnimationFrame(update)
    }
    update()
    window.addEventListener('scroll', onScroll, { passive: true })
    window.addEventListener('resize', onScroll)
    return () => {
      window.removeEventListener('scroll', onScroll)
      window.removeEventListener('resize', onScroll)
      if (frame) window.cancelAnimationFrame(frame)
    }
  }, [key])

  const jump = useCallback(
    (id: string) => {
      const el = document.getElementById(id)
      if (!el) return
      setActive(id)
      // The scroll position drives the highlight again once the (smooth) scroll is over
      holdUntil.current = performance.now() + (reduceMotion ? 150 : 900)
      el.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' })
      el.focus({ preventScroll: true })
    },
    [reduceMotion]
  )

  return { active, jump }
}

interface ChapterTocProps {
  entries: OutlineEntry[]
  active: string
  onJump: (id: string) => void
  structured: boolean
}

/**
 * The table of contents of a walkthrough: its parts and the areas below them as jump links, the current one highlighted. Beside the content (wide) it is a
 * sticky column; on a phone or a narrow window it is a collapsible "Contents" list on top. The same element serves both, only its layout changes.
 */
export function ChapterToc({ entries, active, onJump, structured }: ChapterTocProps) {
  const { t } = useTranslation('game')
  const [open, setOpen] = useState(false)
  const listId = useId()
  // The part that holds the current area is marked as well, so the reader always sees where in the story they are
  const activePartId = useMemo(() => {
    let part: string | undefined
    for (const entry of entries) {
      if (entry.level === 1) part = entry.id
      if (entry.id === active) return part
    }
    return part
  }, [entries, active])

  return (
    <nav className="pv-toc" aria-label={t('walkthrough.toc.nav')} data-open={open || undefined}>
      <h4 className="pv-toc-title">
        <IconListDetails size={15} aria-hidden="true" />
        {t('walkthrough.toc.label')}
      </h4>
      <button type="button" className="pv-toc-toggle" aria-expanded={open} aria-controls={listId} onClick={() => setOpen((value) => !value)}>
        <IconListDetails size={16} aria-hidden="true" />
        <span className="pv-toc-toggle-text">{open ? t('walkthrough.toc.hide') : t('walkthrough.toc.show')}</span>
        <span className="pv-toc-toggle-count">{t('walkthrough.toc.count', { count: entries.length })}</span>
        <IconChevronDown size={16} className={`pv-toc-chevron ${open ? 'is-open' : ''}`} aria-hidden="true" />
      </button>
      <ol id={listId} className="pv-toc-list">
        {entries.map((entry) => {
          const current = entry.id === active
          return (
            <li key={entry.id} className={`pv-toc-item pv-toc-l${entry.level}`} data-part-active={entry.id === activePartId || undefined}>
              <a
                href={`#${entry.id}`}
                className="pv-toc-link"
                aria-current={current ? 'location' : undefined}
                onClick={(event) => {
                  event.preventDefault()
                  onJump(entry.id)
                  setOpen(false)
                }}
              >
                {structured && entry.number && <span className="pv-toc-no">{entry.number}</span>}
                <span className="pv-toc-text">{entry.title}</span>
              </a>
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

interface ChapterBodyProps {
  tabId: string
  sections: GuideSectionDto[]
  // Draws every section that is not a heading (the page's own renderer)
  renderSection: (section: GuideSectionDto, index: number) => ReactNode
}

/**
 * The content of a chapter. Sections are drawn in order; a walkthrough with headings additionally gets its structure: parts (level 1) with a number band,
 * areas (level 2) as framed sub-sections, the sources outside every frame at the end, and a table of contents. A chapter without headings is drawn exactly as a
 * plain list of sections (no wrapper, so the page's own spacing stays as it was).
 */
export function ChapterBody({ tabId, sections, renderSection }: ChapterBodyProps) {
  const prefix = `pv-wt-${tabId.replace(/[^a-zA-Z0-9_-]/g, '')}`
  const outline = useMemo(() => buildOutline(sections, prefix), [sections, prefix])
  const ids = useMemo(() => outline.entries.map((entry) => entry.id), [outline])
  const { active, jump } = useActiveHeading(ids)
  const wrapRef = useRef<HTMLDivElement>(null)
  const [wide, setWide] = useState(false)
  const showToc = outline.entries.length >= TOC_MIN_ENTRIES

  // The width decision is made here (not with a container query), so no containment is applied to the sections inside
  useEffect(() => {
    const el = wrapRef.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const measure = () => {
      const rem = Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16
      setWide(el.getBoundingClientRect().width >= WIDE_REM * rem)
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [showToc])

  const entryAt = useMemo(() => new Map(outline.entries.map((entry) => [entry.index, entry])), [outline])

  const one = (index: number): ReactNode => {
    const section = sections[index]!
    const entry = entryAt.get(index)
    if (entry) return <GuideHeading key={index} section={section} entry={entry} structured={outline.structured} />
    return <Fragment key={index}>{renderSection(section, index)}</Fragment>
  }

  const content = outline.structured ? <StructuredContent outline={outline} one={one} /> : sections.map((_, index) => one(index))

  if (!showToc) return <>{content}</>
  return (
    <div ref={wrapRef} className="pv-wt" data-wide={wide || undefined}>
      <ChapterToc entries={outline.entries} active={active} onJump={jump} structured={outline.structured} />
      <div className="pv-wt-content">{content}</div>
    </div>
  )
}

function StructuredContent({ outline, one }: { outline: Outline; one: (index: number) => ReactNode }) {
  return (
    <>
      {outline.parts.map((part, p) => {
        const body = (
          <>
            {part.entry && one(part.entry.index)}
            {part.indices.map(one)}
            {part.areas.map((area) => (
              <section key={area.entry!.id} className="pv-area" aria-labelledby={`${area.entry!.id}-t`}>
                {one(area.entry!.index)}
                {area.indices.map(one)}
              </section>
            ))}
          </>
        )
        // Content before the first heading belongs to no part: it is drawn without a frame
        if (!part.entry) return part.indices.length > 0 || part.areas.length > 0 ? <div key={`intro-${p}`} className="pv-part pv-part-intro">{body}</div> : null
        return (
          <section key={part.entry.id} className="pv-part" aria-labelledby={`${part.entry.id}-t`}>
            {body}
          </section>
        )
      })}
      {outline.tail.map(one)}
    </>
  )
}
