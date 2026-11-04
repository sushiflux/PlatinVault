import { useTranslation } from 'react-i18next'
import { IconInfoCircle } from '@tabler/icons-react'
import type { LiveRarityDto } from '../api/types'
import { useFormat } from '../i18n/format'
import { ErrorDetails } from './ui/ErrorDetails'

/**
 * The calm notice above the trophy list while the rarity values (the share of players per achievement) are old ("stale") or missing
 * ("unavailable"). The server's reason is shown completely, its technical text sits behind "Technische Details", and the copy button
 * copies all of it. Renders nothing while the values are fine; the room is taken only while the notice shows (it is one block that comes
 * as a whole, nothing inside the list changes).
 */
export function RarityNotice({ rarity }: { rarity: LiveRarityDto }) {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  if (rarity.state === 'ok') return null

  const title = t(`rarity.${rarity.state}.title`)
  const message = rarity.message?.trim() || t(`rarity.${rarity.state}.fallback`)
  const stand = rarity.state === 'stale' && rarity.fetchedAt ? t('rarity.updatedAt', { date: fmt.date(rarity.fetchedAt, 'dateTime') }) : null

  return (
    <div className="pv-alert pv-alert-gold items-start !gap-3 !px-4 !py-3" role="status" data-testid="rarity-notice" data-state={rarity.state}>
      <IconInfoCircle size={20} className="mt-0.5 shrink-0" style={{ color: 'var(--accent-gold-text)' }} aria-hidden="true" />
      <div className="min-w-0 flex-1 text-sm">
        <p className="font-bold">{title}</p>
        <p className="break-words leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
          {message}
        </p>
        {stand && (
          <p className="mt-0.5 text-xs font-semibold" style={{ color: 'var(--text-secondary)' }}>
            {stand}
          </p>
        )}
        <ErrorDetails title={title} message={stand ? `${message}\n${stand}` : message} details={rarity.detail} code={`rarity: ${rarity.state}`} />
      </div>
    </div>
  )
}
