import type { ReactNode } from 'react'
import { IconAlertTriangle, IconBulb, IconCircleCheck, IconCompass, IconDoorExit, IconListCheck, IconStar, IconTrophy } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideAlertKind, GuideSectionDto } from '../../api/types'
import { AchievementRef } from './achievements'
import type { OutlineEntry } from './outline'
import { SideQuestsAlert } from './SideQuestsAlert'
import './guide.css'

// The typed hints of a walkthrough: symbol per kind (the colour comes from the section's variant)
const KIND_ICONS: Record<GuideAlertKind, ReactNode> = {
  must_have: <IconStar size={20} />,
  missable: <IconAlertTriangle size={20} />,
  optional: <IconCompass size={20} />,
  tip: <IconBulb size={20} />,
  achievement: <IconTrophy size={20} />,
  point_of_no_return: <IconDoorExit size={20} />,
  side_quests: <IconListCheck size={20} />,
}

const KIND_VARIANT: Record<GuideAlertKind, string> = {
  must_have: 'gold',
  missable: 'danger',
  optional: 'emerald',
  tip: 'crystal',
  achievement: 'purple',
  point_of_no_return: 'danger',
  side_quests: 'crystal',
}

const KINDS = new Set<string>(Object.keys(KIND_VARIANT))

export function isGuideAlertKind(value: unknown): value is GuideAlertKind {
  return typeof value === 'string' && KINDS.has(value)
}

/**
 * A typed hint of the walkthrough ("Important – take it along", "Missable from here", "Optional content available here", "Tip", "Achievement available
 * here", "Point of no return"): the alert look of the guide plus a label and a symbol that say what kind of hint it is, so the reader sees it at a glance
 * while scanning the route. An achievement hint shows the achievement from the game's Steam list (icon and title); a point of no return lists what to do first.
 */
export function HintAlert({ section }: { section: GuideSectionDto }) {
  const { t } = useTranslation('game')
  const kind = isGuideAlertKind(section.kind) ? section.kind : 'tip'
  // The side quest callout is its own small box (quiet, secondary to the route)
  if (kind === 'side_quests') return <SideQuestsAlert section={section} />
  const variant = section.variant || KIND_VARIANT[kind]
  const items = (section.items ?? []).map((item) => item.trim()).filter((item) => item.length > 0)
  const apiName = kind === 'achievement' ? section.apiName?.trim() : undefined
  return (
    <aside className={`pv-alert pv-alert-${variant} pv-hint pv-hint-${kind}`} data-hint-kind={kind}>
      <div className="pv-hint-icon" aria-hidden="true">
        {KIND_ICONS[kind]}
      </div>
      <div className="pv-hint-body">
        <div className="pv-hint-label">{t(`hintKind.${kind}`)}</div>
        {apiName && <AchievementRef apiName={apiName} withDescription />}
        {section.title && <div className="pv-hint-title">{section.title}</div>}
        {section.htmlContent && <p className="pv-hint-text">{section.htmlContent}</p>}
        {kind === 'point_of_no_return' && items.length > 0 && (
          <div className="pv-hint-todo" data-field="doFirst">
            <div className="pv-hint-todo-label">{t('pointOfNoReturn.doFirst')}</div>
            <ul>
              {items.map((item, i) => (
                <li key={i}>
                  <IconCircleCheck size={15} aria-hidden="true" />
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </aside>
  )
}

interface GuideHeadingProps {
  section: GuideSectionDto
  // The heading's place in the table of contents (id to jump to, number); without it the heading is a plain one
  entry?: OutlineEntry
  // The walkthrough uses parts and areas: a part gets the number band, an area its numbered sub-heading
  structured?: boolean
}

/**
 * A heading inside a chapter. In a walkthrough with parts and areas (`structured`): level 1 = a part of the game as a strong band with its number, level 2
 * = an area inside it with a numbered sub-heading. Without levels (older guides) it stays the calm heading with a side rule. Each carries the id the table of
 * contents jumps to; a one-line lead below the title is optional.
 */
export function GuideHeading({ section, entry, structured = false }: GuideHeadingProps) {
  const { t } = useTranslation('game')
  const lead = section.htmlContent ? <p className="pv-wt-lead">{section.htmlContent}</p> : null
  if (structured && entry?.level === 1) {
    return (
      <header id={entry.id} tabIndex={-1} className="pv-part-head" data-part={entry.number}>
        <span className="pv-part-no" aria-hidden="true">
          {entry.number.padStart(2, '0')}
        </span>
        <div className="pv-part-titles">
          <span className="pv-part-kicker">{t('walkthrough.part', { n: entry.number })}</span>
          <h3 id={`${entry.id}-t`} className="pv-part-title">
            {section.title}
          </h3>
          {lead}
        </div>
      </header>
    )
  }
  if (structured && entry?.level === 2) {
    return (
      <header id={entry.id} tabIndex={-1} className="pv-area-head" data-area={entry.number}>
        <span className="pv-area-no" aria-hidden="true">
          {entry.number}
        </span>
        <div className="pv-area-titles">
          <span className="sr-only">{t('walkthrough.area', { label: entry.number })}: </span>
          <h4 id={`${entry.id}-t`} className="pv-area-title">
            {section.title}
          </h4>
          {lead}
        </div>
      </header>
    )
  }
  return (
    <div id={entry?.id} tabIndex={entry ? -1 : undefined} className="pv-wt-heading">
      <h3 id={entry ? `${entry.id}-t` : undefined}>{section.title}</h3>
      {section.htmlContent && <p>{section.htmlContent}</p>}
    </div>
  )
}

/** The route of a walkthrough part: a story section with variant "steps" is a numbered list. */
export function StepsStory({ section }: { section: GuideSectionDto }) {
  const { t } = useTranslation('game')
  const steps = section.paragraphs ?? (section.htmlContent ? [section.htmlContent] : [])
  return (
    <article className="pv-card pv-steps-card">
      {section.title && <h3 className="pv-steps-title">{section.title}</h3>}
      <ol className="pv-steps" aria-label={section.title ?? t('stepsList')}>
        {steps.map((step, i) => (
          <li key={i}>{step}</li>
        ))}
      </ol>
    </article>
  )
}
