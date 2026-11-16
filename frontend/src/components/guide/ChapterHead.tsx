import type { ReactNode } from 'react'
import { IconClock, IconShieldCheck, IconTrophy } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideGroupKey } from '../../api/types'
import './guide.css'

interface ChapterHeadProps {
  gameTitle: string
  // The group of the chapter in the navigation (the context line names it); missing = only the game
  groupKey?: GuideGroupKey | null
  title: string
  // Facts of the guide (genre, missables, time, difficulty) as a calm line under the title: given for the first chapter only
  facts?: { genre?: string | null; missables?: string | null; hours?: string | null; difficulty?: string | null }
  // The action row of the chapter (research again, remove)
  children?: ReactNode
}

/**
 * The head of a chapter: a small-caps context line ("GAME · GROUP"), the chapter title and, on the right, the chapter actions. The facts of the guide
 * (the former hero card) are one quiet line below it.
 */
export function ChapterHead({ gameTitle, groupKey, title, facts, children }: ChapterHeadProps) {
  const { t } = useTranslation('game')
  const context = groupKey ? `${gameTitle} · ${t(`chapters.groups.${groupKey}`)}` : gameTitle
  const items: { key: string; icon: ReactNode; text: string }[] = []
  if (facts?.missables) items.push({ key: 'missables', icon: <IconShieldCheck size={16} aria-hidden="true" />, text: facts.missables })
  if (facts?.hours) items.push({ key: 'hours', icon: <IconClock size={16} aria-hidden="true" />, text: facts.hours })
  if (facts?.difficulty) items.push({ key: 'difficulty', icon: <IconTrophy size={16} aria-hidden="true" />, text: facts.difficulty })
  return (
    <div className="pv-chapter-intro">
      <header className="pv-chapter-head">
        <div className="pv-chapter-head__titles">
          <p className="pv-eyebrow">{context}</p>
          <h2 className="pv-page-title">{title.replace(/^\d+\.\s*/, '')}</h2>
        </div>
        {children}
      </header>
      {(facts?.genre || items.length > 0) && (
        <ul className="pv-chapter-facts" aria-label={t('chapterFacts')}>
          {facts?.genre && <li>{facts.genre}</li>}
          {items.map((item) => (
            <li key={item.key}>
              {item.icon}
              <span>{item.text}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
