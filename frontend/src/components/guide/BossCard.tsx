import { useId, type ReactNode } from 'react'
import { IconBan, IconFlame, IconGift, IconListNumbers, IconLock, IconMapPin, IconShield, IconSkull, IconTargetArrow, IconTrophy, IconUsers, IconBolt } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideSectionDto } from '../../api/types'
import { AchievementRefList } from './achievements'
import './guide.css'

const clean = (list: string[] | null | undefined) => (list ?? []).map((entry) => entry.trim()).filter((entry) => entry.length > 0)

type ChipKind = 'weak' | 'resist' | 'immune'

function ChipGroup({ kind, label, icon, items }: { kind: ChipKind; label: string; icon: ReactNode; items: string[] }) {
  if (items.length === 0) return null
  return (
    <div className="pv-boss-block" data-group={kind}>
      <h4 className="pv-boss-label">
        {icon}
        {label}
      </h4>
      <ul className="pv-boss-chips">
        {items.map((item, i) => (
          <li key={`${item}-${i}`} className={`pv-boss-chip pv-boss-chip-${kind}`}>
            <span className="pv-boss-chip-icon" aria-hidden="true">
              {kind === 'weak' ? <IconBolt size={13} /> : kind === 'resist' ? <IconShield size={13} /> : <IconBan size={13} />}
            </span>
            {item}
          </li>
        ))}
      </ul>
    </div>
  )
}

/**
 * The stat card of an enemy in the walkthrough, text only (like the boxed enemy entries of a printed strategy guide). Always shown: the header
 * (name, place, recommended level, missable) and the hit points - with "not documented" when there is no number, so the reader sees that it is missing and
 * not just overlooked. Every other block (weaknesses, resistances, immunities, attacks, phases, party, tactics, rewards, achievements) appears only when it has
 * content, so a sparse entry stays a compact card without gaps. Colour is never the only carrier: every group has a label and a symbol.
 */
export function BossCard({ section }: { section: GuideSectionDto }) {
  const { t } = useTranslation('game')
  const name = section.title?.trim() || t('boss.nameless')
  const level = section.recommendedLevel !== null && section.recommendedLevel !== undefined && `${section.recommendedLevel}`.trim() !== '' ? `${section.recommendedLevel}` : null
  const hp = (section.hp ?? []).filter((entry) => entry && `${entry.value ?? ''}`.trim() !== '')
  const weaknesses = clean(section.weaknesses)
  const resistances = clean(section.resistances)
  const immunities = clean(section.immunities)
  const attacks = (section.attacks ?? []).filter((attack) => attack?.name?.trim())
  const phases = clean(section.phases)
  const tactics = clean(section.tactics)
  const rewards = clean(section.rewards)
  const party = section.party?.trim() || null
  const achievements = clean(section.achievements)
  const source = section.source?.trim() || null
  // Unique per rendered instance: the same boss can appear in several walkthrough parts
  const uid = useId()
  const headingId = `boss-${section.bossId || name}-${uid}`.replace(/\s+/g, '-')

  return (
    <article className="pv-card pv-boss" data-boss={section.bossId ?? undefined} aria-labelledby={headingId}>
      <header className="pv-boss-head">
        <span className="pv-boss-glyph" aria-hidden="true">
          <IconSkull size={22} />
        </span>
        <div className="pv-boss-titles">
          <span className="pv-boss-kicker">{t('boss.badge')}</span>
          <h4 id={headingId} className="pv-boss-name">
            {name}
          </h4>
        </div>
        <div className="pv-boss-meta">
          {section.location && (
            <span className="pv-boss-meta-item" data-field="location">
              <IconMapPin size={14} aria-hidden="true" />
              <span className="sr-only">{t('boss.location')}: </span>
              {section.location}
            </span>
          )}
          {level && (
            <span className="pv-boss-meta-item" data-field="level">
              <span className="pv-boss-meta-key">{t('boss.level')}</span>
              <strong>{level}</strong>
            </span>
          )}
          {section.missable && (
            <span className="pv-boss-missable" data-field="missable">
              <IconLock size={13} aria-hidden="true" /> {t('boss.missable')}
            </span>
          )}
        </div>
      </header>

      <div className="pv-boss-body">
        <section className="pv-boss-block" data-field="hp" aria-label={t('boss.hp')}>
          <h4 className="pv-boss-label">
            <IconFlame size={14} aria-hidden="true" />
            {t('boss.hp')}
          </h4>
          {hp.length > 0 ? (
            <ul className="pv-boss-hp">
              {hp.map((entry, i) => (
                <li key={i} className="pv-boss-hp-item">
                  <span className="pv-boss-hp-value">{`${entry.value}`}</span>
                  {entry.appliesTo && <span className="pv-boss-hp-for">{entry.appliesTo}</span>}
                  {entry.source && <span className="pv-boss-foot">{t('boss.hpSource', { source: entry.source })}</span>}
                </li>
              ))}
            </ul>
          ) : (
            <p className="pv-boss-hp-missing">{t('boss.hpUnknown')}</p>
          )}
        </section>

        {(weaknesses.length > 0 || resistances.length > 0 || immunities.length > 0) && (
          <div className="pv-boss-affinities">
            <ChipGroup kind="weak" label={t('boss.weaknesses')} icon={<IconTargetArrow size={14} aria-hidden="true" />} items={weaknesses} />
            <ChipGroup kind="resist" label={t('boss.resistances')} icon={<IconShield size={14} aria-hidden="true" />} items={resistances} />
            <ChipGroup kind="immune" label={t('boss.immunities')} icon={<IconBan size={14} aria-hidden="true" />} items={immunities} />
          </div>
        )}

        {(attacks.length > 0 || phases.length > 0) && (
          <div className="pv-boss-cols">
            {attacks.length > 0 && (
              <div className="pv-boss-block" data-field="attacks">
                <h4 className="pv-boss-label">
                  <IconFlame size={14} aria-hidden="true" />
                  {t('boss.attacks')}
                </h4>
                <ul className="pv-boss-attacks">
                  {attacks.map((attack, i) => (
                    <li key={`${attack.name}-${i}`}>
                      <strong>{attack.name}</strong>
                      {attack.note && <span> – {attack.note}</span>}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {phases.length > 0 && (
              <div className="pv-boss-block" data-field="phases">
                <h4 className="pv-boss-label">
                  <IconListNumbers size={14} aria-hidden="true" />
                  {t('boss.phases')}
                </h4>
                <ol className="pv-steps">
                  {phases.map((phase, i) => (
                    <li key={i}>{phase}</li>
                  ))}
                </ol>
              </div>
            )}
          </div>
        )}

        {party && (
          <div className="pv-boss-block" data-field="party">
            <h4 className="pv-boss-label">
              <IconUsers size={14} aria-hidden="true" />
              {t('boss.party')}
            </h4>
            <p className="pv-boss-text">{party}</p>
          </div>
        )}

        {tactics.length > 0 && (
          <div className="pv-boss-block" data-field="tactics">
            <h4 className="pv-boss-label">
              <IconTargetArrow size={14} aria-hidden="true" />
              {t('boss.tactics')}
            </h4>
            <ol className="pv-steps pv-boss-tactics">
              {tactics.map((tactic, i) => (
                <li key={i}>{tactic}</li>
              ))}
            </ol>
          </div>
        )}

        {rewards.length > 0 && (
          <div className="pv-boss-block" data-field="rewards">
            <h4 className="pv-boss-label">
              <IconGift size={14} aria-hidden="true" />
              {t('boss.rewards')}
            </h4>
            <ul className="pv-boss-chips">
              {rewards.map((reward, i) => (
                <li key={`${reward}-${i}`} className="pv-boss-chip pv-boss-chip-reward">
                  {reward}
                </li>
              ))}
            </ul>
          </div>
        )}

        {achievements.length > 0 && (
          <div className="pv-boss-block" data-field="achievements">
            <h4 className="pv-boss-label">
              <IconTrophy size={14} aria-hidden="true" />
              {t('boss.achievements')}
            </h4>
            <AchievementRefList apiNames={achievements} />
          </div>
        )}

        {source && <p className="pv-boss-foot pv-boss-source">{t('boss.source', { source })}</p>}
      </div>
    </article>
  )
}
