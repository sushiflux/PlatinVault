import type { ReactNode } from 'react'
import { IconAlertTriangle, IconBulb, IconCompass, IconStar } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideAlertKind, GuideSectionDto } from '../../api/types'
import './guide.css'

// The four typed hints of a walkthrough: symbol per kind (the colour comes from the section's variant)
const KIND_ICONS: Record<GuideAlertKind, ReactNode> = {
  must_have: <IconStar size={20} />,
  missable: <IconAlertTriangle size={20} />,
  optional: <IconCompass size={20} />,
  tip: <IconBulb size={20} />,
}

const KIND_VARIANT: Record<GuideAlertKind, string> = {
  must_have: 'gold',
  missable: 'danger',
  optional: 'emerald',
  tip: 'crystal',
}

export function isGuideAlertKind(value: unknown): value is GuideAlertKind {
  return value === 'must_have' || value === 'missable' || value === 'optional' || value === 'tip'
}

/**
 * A typed hint of the walkthrough ("Important – take it along", "Missable from here", "Optional content available here", "Tip"): the alert
 * look of the guide plus a label and a symbol that say what kind of hint it is, so the reader sees it at a glance while scanning the route.
 */
export function HintAlert({ section }: { section: GuideSectionDto }) {
  const { t } = useTranslation('game')
  const kind = isGuideAlertKind(section.kind) ? section.kind : 'tip'
  const variant = section.variant || KIND_VARIANT[kind]
  return (
    <aside className={`pv-alert pv-alert-${variant} pv-hint pv-hint-${kind}`} data-hint-kind={kind}>
      <div className="pv-hint-icon" aria-hidden="true">
        {KIND_ICONS[kind]}
      </div>
      <div className="pv-hint-body">
        <div className="pv-hint-label">{t(`hintKind.${kind}`)}</div>
        {section.title && <div className="pv-hint-title">{section.title}</div>}
        {section.htmlContent && <p className="pv-hint-text">{section.htmlContent}</p>}
      </div>
    </aside>
  )
}

/** A sub-heading inside a chapter (a story chapter or area of the walkthrough) with an optional one-line lead. */
export function GuideHeading({ section }: { section: GuideSectionDto }) {
  return (
    <div className="pv-wt-heading">
      <h3>{section.title}</h3>
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
