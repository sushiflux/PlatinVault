import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconBook, IconEye, IconRefresh, IconSparkles } from '@tabler/icons-react'
import type { AiProviderId, GuideSectionDto, GuideTabDto } from '../../api/types'
import { AiRunButton } from '../ai/AiRunButton'
import { Switch } from '../ui/Switch'
import { Tooltip } from '../ui/Tooltip'
import './lore.css'

interface LorePanelProps {
  tab: GuideTabDto | undefined
  revealed: boolean
  busy: boolean
  onReveal: (remember: boolean) => void
  onResearch: (oneOff?: AiProviderId) => void
  // The guide page's renderer for every section that is not plain story text (alerts, puzzles, headings ...)
  renderSection: (section: GuideSectionDto, position: string) => ReactNode
}

// Optional Story & Lore tab: a research button when there is none yet, one calm spoiler gate for the whole tab, then the sections.
// Story text is set as readable running text (own measure and leading, no card); everything else comes from the guide's renderer.
export function LorePanel({ tab, revealed, busy, onReveal, onResearch, renderSection }: LorePanelProps) {
  const { t } = useTranslation('game')
  const headingId = useId()
  const [remember, setRemember] = useState(false)
  // The AI picked for this one research ("Mit Claude"); the setting itself stays as it is
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)

  if (!tab) {
    return (
      <section className="pv-lore-gate" data-tone="none" aria-labelledby={headingId}>
        <h2 id={headingId}>
          <IconBook size={20} aria-hidden="true" />
          <span>{t('lore.none')}</span>
        </h2>
        <p>{t('lore.noneHint')}</p>
        <div className="pv-lore-gate__actions">
          <AiRunButton
            tone="pv-btn-emerald"
            busy={busy}
            oneOff={oneOff}
            onOneOffChange={setOneOff}
            onClick={() => onResearch(oneOff)}
            icon={<IconSparkles size={16} />}
            label={<span>{t('lore.research')}</span>}
          />
        </div>
      </section>
    )
  }

  if (!revealed) {
    return (
      <section className="pv-lore-gate" aria-labelledby={headingId}>
        <h2 id={headingId}>
          <IconAlertTriangle size={20} aria-hidden="true" />
          <span>{t('lore.gateTitle')}</span>
        </h2>
        <p>{t('lore.gateText')}</p>
        <div className="pv-lore-gate__actions">
          <button type="button" onClick={() => onReveal(remember)} className="pv-btn pv-btn-primary">
            <IconEye size={16} aria-hidden="true" />
            <span>{t('lore.reveal')}</span>
          </button>
          <Switch checked={remember} onChange={setRemember} className="pv-lore-gate__remember">
            {t('lore.remember')}
          </Switch>
        </div>
      </section>
    )
  }

  return (
    <div className="pv-lore">
      <div className="pv-lore__bar no-print">
        <Tooltip label={t('lore.redo.label')} description={t('lore.redo.description')}>
          <AiRunButton
            quiet
            busy={busy}
            oneOff={oneOff}
            onOneOffChange={setOneOff}
            onClick={() => onResearch(oneOff)}
            icon={<IconRefresh size={16} />}
            label={<span>{t('lore.redo.button')}</span>}
          />
        </Tooltip>
      </div>
      {tab.sections.map((section, idx) => {
        const position = `${tab.id}:${idx}`
        if (section.type === 'story' && section.variant !== 'steps') {
          const paragraphs = section.paragraphs ?? (section.htmlContent ? [section.htmlContent] : [])
          return (
            <article key={`${tab.id}-lore-${idx}`} className="pv-lore__story">
              {section.title && <h2>{section.title}</h2>}
              {paragraphs.map((paragraph, i) => (
                <p key={i}>{paragraph}</p>
              ))}
            </article>
          )
        }
        return (
          <div key={`${tab.id}-lore-${idx}`} className="pv-lore__block">
            {renderSection(section, position)}
          </div>
        )
      })}
    </div>
  )
}
