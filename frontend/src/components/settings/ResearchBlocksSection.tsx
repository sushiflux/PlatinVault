import { useTranslation } from 'react-i18next'
import { IconCheck } from '@tabler/icons-react'
import type { ResearchBlocksDto, ResearchBlocksPolicyDto } from '../../api/types'
import { ReservedText } from '../ui/ReservedText'
import { SettingsRow, SettingsTopic } from './SettingsParts'
import { Tooltip } from '../ui/Tooltip'

// The ids come from the server (its policy object); the labels of the known ids are texts in the "ai" namespace.
type SingleGroup = 'scope' | 'spoilers' | 'presentation'

const EMPTY: ResearchBlocksDto = { scope: null, focus: [], spoilers: null, presentation: null }

export function emptyResearchBlocks(): ResearchBlocksDto {
  return { ...EMPTY, focus: [] }
}

export function countResearchBlocks(value: ResearchBlocksDto): number {
  return (value.scope ? 1 : 0) + value.focus.length + (value.spoilers ? 1 : 0) + (value.presentation ? 1 : 0)
}

export function sameResearchBlocks(a: ResearchBlocksDto, b: ResearchBlocksDto): boolean {
  return a.scope === b.scope && a.spoilers === b.spoilers && a.presentation === b.presentation && a.focus.length === b.focus.length && a.focus.every((id) => b.focus.includes(id))
}

interface ResearchBlocksSectionProps {
  value: ResearchBlocksDto
  onChange: (value: ResearchBlocksDto) => void
  policy: ResearchBlocksPolicyDto | undefined
}

// "Recherche-Bausteine": predefined building blocks instead of free text. One choice for scope, spoilers and presentation (click again to
// remove it), several for the focus. Every option has a tooltip with what it does, and the chosen single option shows it in a line
// whose room is reserved, so choosing never moves anything.
export function ResearchBlocksSection({ value, onChange, policy }: ResearchBlocksSectionProps) {
  const { t } = useTranslation('ai')
  const count = countResearchBlocks(value)

  const label = (group: 'scope' | 'focus' | 'spoilers' | 'presentation', id: string) =>
    t(`blocks.${group}.${id}.label` as 'blocks.scope.compact.label', { defaultValue: id })
  const description = (group: 'scope' | 'focus' | 'spoilers' | 'presentation', id: string) =>
    t(`blocks.${group}.${id}.description` as 'blocks.scope.compact.description', { defaultValue: '' })

  const toggleSingle = (group: SingleGroup, id: string) => onChange({ ...value, [group]: value[group] === id ? null : id })
  const toggleFocus = (id: string) =>
    onChange({ ...value, focus: value.focus.includes(id) ? value.focus.filter((f) => f !== id) : [...value.focus, id] })

  return (
    <div data-testid="research-blocks">
      <SettingsTopic>{t('blocks.title')}</SettingsTopic>
      <p className="pv-set-muted" style={{ margin: '0.375rem 0 0.25rem' }}>
        {t('blocks.intro')}
      </p>

      {(['scope', 'spoilers', 'presentation'] as const).map((group) => {
        const ids = policy?.[group] ?? []
        const chosen = value[group]
        return (
          <div key={group} className="m-0 min-w-0 border-0 p-0" data-group={group}>
            <SettingsRow
              wide
              title={
                <span>
                  {t(`blocks.${group}.group`)} <span className="pv-set-muted" style={{ fontSize: '0.875rem', fontWeight: 400 }}>({t('blocks.single')})</span>
                </span>
              }
              help={
                <ReservedText candidates={[...ids.map((id) => description(group, id)), t('blocks.optionalHint')]}>
                  {chosen ? description(group, chosen) : t('blocks.optionalHint')}
                </ReservedText>
              }
            >
              <div className="pv-segmented w-full" role="group" aria-label={t(`blocks.${group}.group`)}>
                {ids.map((id) => (
                  <Tooltip key={id} label={label(group, id)} description={description(group, id)} className="pv-seg-tip">
                    <button type="button" aria-pressed={chosen === id} onClick={() => toggleSingle(group, id)} data-block={`${group}:${id}`}>
                      {label(group, id)}
                    </button>
                  </Tooltip>
                ))}
              </div>
            </SettingsRow>
          </div>
        )
      })}

      <div className="m-0 min-w-0 border-0 p-0" data-group="focus">
        <SettingsRow
          wide
          title={
            <span>
              {t('blocks.focus.group')} <span className="pv-set-muted" style={{ fontSize: '0.875rem', fontWeight: 400 }}>({t('blocks.multiple')})</span>
            </span>
          }
        >
          {/* Several can be chosen: one connected list, the check mark of a chosen entry always takes its room */}
          <div className="pv-segmented pv-segmented--stack" style={{ maxInlineSize: '28rem' }} role="group" aria-label={t('blocks.focus.group')}>
            {(policy?.focus ?? []).map((id) => {
              const pressed = value.focus.includes(id)
              return (
                <Tooltip key={id} label={label('focus', id)} description={description('focus', id)} className="pv-seg-tip">
                  <button type="button" aria-pressed={pressed} onClick={() => toggleFocus(id)} data-block={`focus:${id}`}>
                    <IconCheck size={17} aria-hidden="true" className={pressed ? '' : 'invisible'} />
                    <span>{label('focus', id)}</span>
                  </button>
                </Tooltip>
              )
            })}
          </div>
        </SettingsRow>
      </div>

      <SettingsRow
        title={
          <ReservedText
            role="status"
            style={{ color: count > 0 ? 'var(--accent-emerald)' : 'var(--text-muted)', fontWeight: 500 }}
            candidates={[t('blocks.summaryNone'), t('blocks.summary', { count: 8 }), t('blocks.summary', { count: 11 })]}
          >
            {count === 0 ? t('blocks.summaryNone') : t('blocks.summary', { count })}
          </ReservedText>
        }
      >
        <button type="button" className="pv-btn-text pv-btn-text--calm" disabled={count === 0} onClick={() => onChange(emptyResearchBlocks())}>
          {t('blocks.clear')}
        </button>
      </SettingsRow>
    </div>
  )
}
