import { useRef, type KeyboardEvent, type ReactNode } from 'react'
import { useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { Tooltip } from '../ui/Tooltip'
import { tabDomId } from './ChapterTabs'
import { SlidingMarker } from './SlidingMarker'
import './gamenav.css'

export interface SectionItem {
  // The slug the page uses for this section
  slug: string
  // Short label next to the icon (the icon alone on narrow bars)
  label: string
  // Full name and one sentence for the tooltip
  fullLabel: string
  description: string
  icon: ReactNode
  // The accent colour of the area (from the app's tokens): coloured icon and a soft tint at rest, filled in this colour when active
  tone: SectionTone
  // Coach-mark anchor of the onboarding hints (data-hint)
  hint?: string
}

export type SectionTone = 'gold' | 'crystal' | 'purple'

interface SectionTabsProps {
  items: SectionItem[]
  // Slug of the active section, or null while a guide chapter is active
  activeSlug: string | null
  onSelect: (slug: string) => void
}

/**
 * The three main areas of the game page (live trophies, statistics, story) as a segmented control on the right of the trophy
 * bar. Each has its own accent colour (coloured icon and a soft tint at rest) so they read as areas and not as tools or chapters;
 * the active one is filled in its colour by a pill that slides over and takes the colour along. When a guide chapter is active
 * none of them is highlighted (exactly one active mark across the nav and the bar). Icon-only on narrow bars (the tooltip names each one).
 */
export function SectionTabs({ items, activeSlug, onSelect }: SectionTabsProps) {
  const { t } = useTranslation('game')
  const reduce = useReducedMotion() ?? false
  const listRef = useRef<HTMLDivElement>(null)
  const activeIndex = items.findIndex((item) => item.slug === activeSlug)
  const tabbableIndex = activeIndex >= 0 ? activeIndex : 0

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
    const tabs = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="tab"]')]
    const index = tabs.indexOf(document.activeElement as HTMLElement)
    if (index < 0) return
    event.preventDefault()
    const target = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length
    tabs[target]?.focus()
    tabs[target]?.click()
  }

  return (
    <div ref={listRef} className="pv-seg" data-tone={items[activeIndex]?.tone} role="tablist" aria-label={t('sections.groupLabel')} onKeyDown={onKeyDown}>
      <SlidingMarker containerRef={listRef} activeKey={activeSlug} mode="pill" className="pv-seg-thumb" reduce={reduce} />
      {items.map((item, index) => {
          const selected = index === activeIndex
          return (
            <Tooltip key={item.slug} label={item.fullLabel} description={item.description} className={`pv-seg-cell pv-seg-tone-${item.tone}`}>
              <button
                type="button"
                role="tab"
                id={tabDomId(item.slug)}
                aria-selected={selected}
                aria-label={item.fullLabel}
                tabIndex={index === tabbableIndex ? 0 : -1}
                data-hint={item.hint}
                className={`pv-seg-tab pv-seg-tone-${item.tone}`}
                onClick={() => onSelect(item.slug)}
              >
                <span className="pv-seg-content">
                  {item.icon}
                  <span className="pv-seg-label">{item.label}</span>
                </span>
              </button>
            </Tooltip>
          )
      })}
    </div>
  )
}
