import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconInfoCircle, IconSparkles } from '@tabler/icons-react'
import { AiRunButton } from '../ai/AiRunButton'
import { rememberedHow, type AiHow } from '../ai/aiProviders'
import '../../styles/live.css'

/**
 * A game added by hand without IGDB and without a trophy list (Nintendo Switch) has no research that fills in its facts - the list research would
 * have done it. This calm note offers exactly that, once, while the facts (release, developer, publisher, description) are missing:
 * "Spiel-Fakten recherchieren", with the AI choice and the way with the player's own subscription.
 */
export function ManualFactsNote({ busy, onResearch }: { busy: boolean; onResearch: (oneOff?: AiHow) => void }) {
  const { t } = useTranslation('game')
  const [oneOff, setOneOff] = useState<AiHow | undefined>(rememberedHow)
  return (
    <div className="pv-tsrc pv-accent-left no-print" data-testid="manual-facts-note">
      <div className="pv-tsrc-t">
        <span className="inline-flex items-center gap-1.5">
          <IconInfoCircle size={16} aria-hidden="true" />
          <b>{t('manualFacts.title')}</b>
        </span>
        <small>{t('manualFacts.text')}</small>
      </div>
      <AiRunButton
        manual
        quiet
        busy={busy}
        oneOff={oneOff}
        onOneOffChange={setOneOff}
        onClick={() => onResearch(oneOff)}
        icon={<IconSparkles size={16} />}
        label={<span>{t('manualFacts.research')}</span>}
      />
    </div>
  )
}
