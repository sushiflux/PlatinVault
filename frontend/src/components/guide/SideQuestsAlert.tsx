import { createContext, useContext } from 'react'
import { IconArrowRight, IconListCheck, IconLock } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideSectionDto, GuideSideQuestDto } from '../../api/types'
import { useItemLinks } from './ItemLinks'
import './guide.css'

/**
 * Where the side quests of this guide are written down in full: the guide's side quest chapter. The page provides it (null = the guide has no such chapter, so
 * the callout shows the names without links). `hasRow(ref)` says whether the chapter has a row with that item id, and `open(ref)` goes to the chapter and, with a
 * row that exists, scrolls to exactly that row.
 */
export interface SideQuestTarget {
  slug: string
  title: string
  hasRow: (ref: string) => boolean
  open: (ref?: string) => void
}

export const SideQuestTargetContext = createContext<SideQuestTarget | null>(null)

const clean = (value: string | null | undefined) => value?.trim() || null

/** One line of meta below a quest: from when it can be started, whether and until when it can be missed, the reward. */
function QuestMeta({ quest }: { quest: GuideSideQuestDto }) {
  const { t } = useTranslation('game')
  const from = clean(quest.availableFrom)
  const reward = clean(quest.reward)
  const until = typeof quest.missable === 'string' ? clean(quest.missable) : null
  const missable = quest.missable === true || until !== null
  if (!from && !reward && !missable) return null
  return (
    <span className="pv-quest-meta">
      {from && <span data-meta="from">{t('sideQuests.from', { from })}</span>}
      {missable && (
        <span className="pv-quest-missable" data-meta="missable">
          <IconLock size={12} aria-hidden="true" />
          {until ? t('sideQuests.missableUntil', { until }) : t('sideQuests.missable')}
        </span>
      )}
      {reward && <span data-meta="reward">{t('sideQuests.reward', { reward })}</span>}
    </span>
  )
}

/**
 * "Side quests available here": a compact, quiet box in the main route that only names the quests that become available at this point (with one line of meta
 * each) - the full solution is in the guide's side quest chapter, to which every entry links when that chapter exists. Deliberately small and neutral so it never
 * competes with the route. Each entry keeps its `ref` (the item id of its row in that chapter) on the element, which is what the tick box is bound to (the same check as that
 * row); the title opens the chapter scrolled to that row.
 */
export function SideQuestsAlert({ section }: { section: GuideSectionDto }) {
  const { t } = useTranslation('game')
  const target = useContext(SideQuestTargetContext)
  const links = useItemLinks()
  const quests = (section.quests ?? []).filter((quest) => quest?.title?.trim())
  const heading = section.title?.trim() || t('hintKind.side_quests')
  if (quests.length === 0 && !section.htmlContent) return null
  return (
    <aside className="pv-quests" data-hint-kind="side_quests" aria-label={heading}>
      <header className="pv-quests-head">
        <IconListCheck size={16} aria-hidden="true" />
        <span className="pv-quests-title">{heading}</span>
        {target && quests.length > 0 && (
          <a
            href={`?tab=${encodeURIComponent(target.slug)}`}
            className="pv-quests-all"
            onClick={(event) => {
              event.preventDefault()
              target.open()
            }}
          >
            {t('sideQuests.goTo', { title: target.title })}
            <IconArrowRight size={13} aria-hidden="true" />
          </a>
        )}
      </header>
      {section.htmlContent && <p className="pv-quests-text">{section.htmlContent}</p>}
      {quests.length > 0 && (
        <ul className="pv-quests-list">
          {quests.map((quest, i) => {
            const ref = clean(quest.ref)
            const exact = target !== null && ref !== null && target.hasRow(ref)
            // The tick box is the very check of the quest's row in the quest chapter (one entry per item id on the server)
            const bound = links !== null && ref !== null && links.index.target(ref) !== null
            return (
              <li key={`${quest.title}-${i}`} className="pv-quest" data-quest-ref={ref ?? undefined}>
                {bound && links && (
                  <input
                    type="checkbox"
                    className="pv-quest-check"
                    checked={links.isChecked(ref!)}
                    onChange={(event) => void links.toggle(ref!, event.target.checked)}
                    aria-label={t('sideQuests.doneFor', { quest: quest.title.trim() })}
                    style={{ accentColor: 'var(--accent-emerald)' }}
                  />
                )}
                {target ? (
                  <a
                    href={`?tab=${encodeURIComponent(target.slug)}`}
                    className="pv-quest-title pv-quest-link"
                    aria-label={t('sideQuests.open', { quest: quest.title.trim(), chapter: target.title })}
                    onClick={(event) => {
                      event.preventDefault()
                      target.open(exact ? ref! : undefined)
                    }}
                  >
                    {quest.title.trim()}
                  </a>
                ) : (
                  <span className="pv-quest-title">{quest.title.trim()}</span>
                )}
                <QuestMeta quest={quest} />
              </li>
            )
          })}
        </ul>
      )}
    </aside>
  )
}
