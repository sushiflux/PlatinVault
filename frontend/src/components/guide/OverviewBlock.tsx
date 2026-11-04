import { IconDoorExit, IconKey, IconListDetails, IconLock, IconTrophy } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideSectionDto } from '../../api/types'
import { AchievementRefList } from './achievements'
import './guide.css'

const clean = (list: string[] | null | undefined) => (list ?? []).map((entry) => entry.trim()).filter((entry) => entry.length > 0)

/**
 * "At a glance" at the start of a walkthrough part: the recommended level, what there is to find (counts), key items, achievements, what can be missed and
 * whether the part ends at a point of no return. Empty groups are left out, so a part with little to say gets a short box; a box with nothing in it at all is not drawn.
 */
export function OverviewBlock({ section }: { section: GuideSectionDto }) {
  const { t } = useTranslation('game')
  const level = section.recommendedLevel !== null && section.recommendedLevel !== undefined && `${section.recommendedLevel}`.trim() !== '' ? `${section.recommendedLevel}` : null
  const counts = (section.counts ?? []).filter((entry) => entry?.label?.trim())
  const keyItems = clean(section.keyItems)
  const missables = clean(section.missables)
  const achievements = clean(section.achievements)
  const ponr = section.endsAtPointOfNoReturn === true
  if (!level && counts.length === 0 && keyItems.length === 0 && missables.length === 0 && achievements.length === 0 && !ponr) return null

  return (
    <aside className="pv-card pv-overview" aria-label={section.title?.trim() || t('overview.title')} data-overview="true">
      <header className="pv-overview-head">
        <span className="pv-overview-glyph" aria-hidden="true">
          <IconListDetails size={18} />
        </span>
        <h4 className="pv-overview-title">{section.title?.trim() || t('overview.title')}</h4>
      </header>

      {(level || counts.length > 0) && (
        <dl className="pv-overview-tiles">
          {level && (
            <div className="pv-overview-tile" data-field="level">
              <dt>{t('overview.level')}</dt>
              <dd>{level}</dd>
            </div>
          )}
          {counts.map((entry, i) => (
            <div key={`${entry.label}-${i}`} className="pv-overview-tile" data-field="count">
              <dt>{entry.label}</dt>
              {entry.count !== null && entry.count !== undefined && `${entry.count}`.trim() !== '' && <dd>{`${entry.count}`}</dd>}
            </div>
          ))}
        </dl>
      )}

      {keyItems.length > 0 && (
        <div className="pv-boss-block" data-field="keyItems">
          <h4 className="pv-boss-label">
            <IconKey size={14} aria-hidden="true" />
            {t('overview.keyItems')}
          </h4>
          <ul className="pv-boss-chips">
            {keyItems.map((item, i) => (
              <li key={`${item}-${i}`} className="pv-boss-chip pv-boss-chip-reward">
                {item}
              </li>
            ))}
          </ul>
        </div>
      )}

      {missables.length > 0 && (
        <div className="pv-boss-block" data-field="missables">
          <h4 className="pv-boss-label">
            <IconLock size={14} aria-hidden="true" />
            {t('overview.missables')}
          </h4>
          <ul className="pv-boss-chips">
            {missables.map((item, i) => (
              <li key={`${item}-${i}`} className="pv-boss-chip pv-boss-chip-immune">
                <span className="pv-boss-chip-icon" aria-hidden="true">
                  <IconLock size={13} />
                </span>
                {item}
              </li>
            ))}
          </ul>
        </div>
      )}

      {achievements.length > 0 && (
        <div className="pv-boss-block" data-field="achievements">
          <h4 className="pv-boss-label">
            <IconTrophy size={14} aria-hidden="true" />
            {t('overview.achievements')}
          </h4>
          <AchievementRefList apiNames={achievements} />
        </div>
      )}

      {ponr && (
        <p className="pv-overview-ponr" data-field="ponr">
          <IconDoorExit size={16} aria-hidden="true" />
          <span>{t('overview.pointOfNoReturn')}</span>
        </p>
      )}
    </aside>
  )
}
