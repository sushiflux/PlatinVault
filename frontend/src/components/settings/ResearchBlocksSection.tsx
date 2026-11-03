import { useTranslation } from 'react-i18next'
import { IconBlocks, IconCheck } from '@tabler/icons-react'
import type { ResearchBlocksDto, ResearchBlocksPolicyDto } from '../../api/types'
import { ReservedText } from '../ui/ReservedText'
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
    <div className="pv-card space-y-4" data-testid="research-blocks">
      <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-emerald)' }}>
        <IconBlocks size={18} />
        <span>{t('blocks.title')}</span>
      </div>
      <p className="text-[11px] leading-snug" style={{ color: 'var(--text-muted)' }}>
        {t('blocks.intro')}
      </p>

      {(['scope', 'spoilers', 'presentation'] as const).map((group) => {
        const ids = policy?.[group] ?? []
        const chosen = value[group]
        return (
          <fieldset key={group} className="space-y-1.5" data-group={group}>
            <legend className="mb-1.5 flex w-full items-baseline justify-between gap-2 text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
              <span>{t(`blocks.${group}.group`)}</span>
              <span className="text-[10px] font-semibold" style={{ color: 'var(--text-muted)' }}>
                {t('blocks.single')}
              </span>
            </legend>
            <div className="pv-segmented w-full" role="group" aria-label={t(`blocks.${group}.group`)}>
              {ids.map((id) => (
                <Tooltip key={id} label={label(group, id)} description={description(group, id)} className="pv-seg-tip flex flex-1">
                  <button
                    type="button"
                    aria-pressed={chosen === id}
                    onClick={() => toggleSingle(group, id)}
                    className="flex-1 justify-center text-center leading-tight"
                    data-block={`${group}:${id}`}
                  >
                    {label(group, id)}
                  </button>
                </Tooltip>
              ))}
            </div>
            <ReservedText
              className="text-[11px] leading-snug"
              style={{ color: 'var(--text-muted)' }}
              candidates={[...ids.map((id) => description(group, id)), t('blocks.optionalHint')]}
            >
              {chosen ? description(group, chosen) : t('blocks.optionalHint')}
            </ReservedText>
          </fieldset>
        )
      })}

      <fieldset className="space-y-1.5" data-group="focus">
        <legend className="mb-1.5 flex w-full items-baseline justify-between gap-2 text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
          <span>{t('blocks.focus.group')}</span>
          <span className="text-[10px] font-semibold" style={{ color: 'var(--text-muted)' }}>
            {t('blocks.multiple')}
          </span>
        </legend>
        <div className="flex flex-wrap gap-2" role="group" aria-label={t('blocks.focus.group')}>
          {(policy?.focus ?? []).map((id) => {
            const pressed = value.focus.includes(id)
            return (
              <Tooltip key={id} label={label('focus', id)} description={description('focus', id)}>
                <button
                  type="button"
                  aria-pressed={pressed}
                  onClick={() => toggleFocus(id)}
                  className="pv-btn !px-2.5 !py-1 !text-xs aria-pressed:!border-[var(--accent-emerald-border)] aria-pressed:!bg-[var(--accent-emerald-bg)] aria-pressed:!text-[var(--accent-emerald)]"
                  data-block={`focus:${id}`}
                >
                  {/* The check mark always takes its room, so a chip is as wide ticked as unticked */}
                  <IconCheck size={13} aria-hidden="true" className={pressed ? '' : 'invisible'} />
                  <span>{label('focus', id)}</span>
                </button>
              </Tooltip>
            )
          })}
        </div>
      </fieldset>

      <div className="flex items-center justify-between gap-3 pt-1" style={{ borderTop: '1px solid var(--border-subtle)' }}>
        <ReservedText
          className="pt-2 text-xs font-semibold"
          style={{ color: count > 0 ? 'var(--accent-emerald)' : 'var(--text-muted)' }}
          candidates={[t('blocks.summaryNone'), t('blocks.summary', { count: 8 }), t('blocks.summary', { count: 11 })]}
          role="status"
        >
          {count === 0 ? t('blocks.summaryNone') : t('blocks.summary', { count })}
        </ReservedText>
        <button type="button" className="pv-btn mt-2 shrink-0 !px-2.5 !py-1 !text-xs" disabled={count === 0} onClick={() => onChange(emptyResearchBlocks())}>
          {t('blocks.clear')}
        </button>
      </div>
    </div>
  )
}
