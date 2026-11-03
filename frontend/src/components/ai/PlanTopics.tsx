import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconCheck } from '@tabler/icons-react'
import type { GuidePlanDto, GuidePlanTopicDto } from '../../api/types'
import { Tooltip } from '../ui/Tooltip'

// Key of a plan entry; extras share the topic id "extra", so their title tells them apart
export const keyOf = (topic: GuidePlanTopicDto) => (topic.id === 'extra' ? `extra:${topic.title}` : topic.id)

/** The topics the player can tick: recommended first, then the other fitting ones; chapters the guide already has are listed apart. */
export function usePlanSelection(plan: GuidePlanDto | null) {
  const [selected, setSelected] = useState<Set<string>>(new Set())

  const groups = useMemo(() => {
    if (!plan) return null
    return {
      recommended: [...plan.topics.filter((t) => t.recommended && !t.covered), ...plan.extras],
      other: plan.topics.filter((t) => !t.recommended && !t.covered),
      covered: plan.topics.filter((t) => t.covered),
    }
  }, [plan])

  const selectable = groups ? [...groups.recommended, ...groups.other] : []
  const chosen = selectable.filter((t) => selected.has(keyOf(t)))

  const toggle = (topic: GuidePlanTopicDto) =>
    setSelected((prev) => {
      const next = new Set(prev)
      const key = keyOf(topic)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  /** Ticks exactly the recommended topics of a plan (what a fresh plan starts with). */
  const tickRecommended = (fresh: GuidePlanDto) =>
    setSelected(new Set([...fresh.topics, ...fresh.extras].filter((t) => t.recommended && !t.covered).map(keyOf)))

  return { groups, selectable, chosen, selected, setSelected, toggle, tickRecommended }
}

/** A group of plan topics with checkboxes (the guide builder and the subscription flow show the same list). */
export function TopicGroup({
  title,
  hint,
  topics,
  selected,
  onToggle,
  disabled = false,
}: {
  title: string
  hint?: string
  topics: GuidePlanTopicDto[]
  selected: Set<string>
  onToggle: (topic: GuidePlanTopicDto) => void
  disabled?: boolean
}) {
  const { t } = useTranslation('game')
  if (topics.length === 0) return null
  return (
    <section className="space-y-2">
      <div>
        <h3 className="text-xs font-extrabold uppercase tracking-wider" style={{ color: 'var(--text-muted)' }}>
          {title} ({topics.length})
        </h3>
        {hint && (
          <p className="text-[11px]" style={{ color: 'var(--text-muted)' }}>
            {hint}
          </p>
        )}
      </div>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {topics.map((topic) => {
          const key = keyOf(topic)
          const isOn = selected.has(key) && !topic.covered
          return (
            <Tooltip key={key} label={topic.title} description={topic.focus} className="flex">
              <label
                className={`flex w-full items-start gap-2.5 rounded-xl p-3 text-xs ${disabled ? 'opacity-60' : 'cursor-pointer'}`}
                style={{
                  background: isOn ? 'var(--accent-emerald-bg)' : 'var(--bg-elevated)',
                  border: `1px solid ${isOn ? 'var(--accent-emerald-border)' : 'var(--border-subtle)'}`,
                }}
              >
                <input
                  type="checkbox"
                  className="mt-0.5 rounded"
                  style={{ accentColor: 'var(--accent-emerald)' }}
                  checked={isOn}
                  disabled={disabled}
                  onChange={() => onToggle(topic)}
                />
                <span className="min-w-0">
                  <span className="flex flex-wrap items-center gap-1.5 font-bold" style={{ color: 'var(--text-primary)' }}>
                    {topic.title}
                    {topic.required && !topic.covered && <span className="pv-badge pv-badge-s !py-0 !text-[10px]">{t('builder.required')}</span>}
                    {topic.covered && <IconCheck size={13} style={{ color: 'var(--accent-emerald)' }} />}
                  </span>
                  <span className="mt-0.5 line-clamp-2 block" style={{ color: 'var(--text-secondary)' }}>
                    {topic.reason && !topic.recommended ? topic.reason : topic.focus}
                  </span>
                </span>
              </label>
            </Tooltip>
          )
        })}
      </div>
    </section>
  )
}
