import type { ReactNode } from 'react'
import { IconBook2, IconTrophy } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { TruncatedText } from './ui/TruncatedText'

// Shows on each card that the game has live trophies plus a guide, and how many guide chapters exist.
export function GuideBadge({ tabCount }: { tabCount: number }) {
  const { t } = useTranslation('dashboard')
  return (
    <span
      className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-[11px] font-extrabold shadow-sm backdrop-blur-md"
      style={{
        background: 'var(--bg-glass)',
        color: 'var(--accent-crystal)',
        border: '1px solid var(--border-subtle)',
      }}
    >
      <IconTrophy size={13} style={{ color: 'var(--accent-gold)' }} aria-hidden="true" />
      <IconBook2 size={13} aria-hidden="true" />
      <span>{t('card.guideChapters', { count: tabCount })}</span>
    </span>
  )
}

// One line of card metadata: icon, label for screen readers, value truncated to one line.
export function CardMeta({ icon, color, label, value }: { icon: ReactNode; color: string; label: string; value: string }) {
  const text = value?.trim() || '–'
  return (
    <div className="flex h-5 min-w-0 items-center gap-2">
      <dt className="flex shrink-0 items-center" style={{ color }}>
        {icon}
        <span className="sr-only">{label}</span>
      </dt>
      <dd className="flex min-w-0 font-semibold" style={{ color: 'var(--text-secondary)' }}>
        <TruncatedText text={text} label={value?.trim() ? `${label}: ${value.trim()}` : undefined} />
      </dd>
    </div>
  )
}
