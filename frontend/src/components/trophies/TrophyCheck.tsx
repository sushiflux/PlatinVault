import { useTranslation } from 'react-i18next'
import { IconCheck, IconLoader2 } from '@tabler/icons-react'
import { Tooltip } from '../ui/Tooltip'
import '../../styles/live.css'

/** What a trophy view needs to show the check of one trophy of a game added by hand. A view without it shows no check (Steam lists). */
export interface TrophyCheck {
  checked: boolean
  // The tick is on its way to the server: the control keeps its size and ignores clicks
  busy: boolean
  onToggle: () => void
  // The server ticks this one itself (the PlayStation platinum trophy once every other base trophy is ticked): the control says so and does nothing
  locked?: boolean
}

/**
 * The check of the trophy lists and the detail dialog: a square box with a check, fixed size (drawn 1.5 rem inside a 48 px target), `aria-pressed`.
 * While the answer of the server is awaited the check turns into a small spinner inside the same box. The platinum trophy is shown locked.
 */
export function TrophyCheckButton({ name, check, className = 'inline-flex shrink-0' }: { name: string; check: TrophyCheck; className?: string }) {
  const { t } = useTranslation('live')
  const label = check.checked ? t('trophy.check.unmark', { name }) : t('trophy.check.mark', { name })
  return (
    <Tooltip label={label} description={check.locked ? t('trophy.check.auto') : undefined} className={className}>
      <button
        type="button"
        className="pv-tcheck"
        aria-pressed={check.checked}
        aria-label={label}
        aria-busy={check.busy || undefined}
        aria-disabled={check.locked || undefined}
        data-busy={check.busy ? '' : undefined}
        data-locked={check.locked ? '' : undefined}
        onClick={() => {
          if (check.busy || check.locked) return
          check.onToggle()
        }}
      >
        <span className="pv-tcheck-box" aria-hidden="true">
          {check.busy ? <IconLoader2 size={15} className="motion-safe:animate-spin" /> : <IconCheck size={16} stroke={3} />}
        </span>
      </button>
    </Tooltip>
  )
}
