import { useTranslation } from 'react-i18next'
import { IconPin, IconPinFilled } from '@tabler/icons-react'
import { GhostIconButton } from '../ui/GhostIconButton'

/** What a trophy view needs to show the pin button of one achievement ("Im Blick"). A view without it shows no button. */
export interface PinControl {
  pinned: boolean
  // False for an unpinned achievement while the maximum is reached
  canPin: boolean
  max: number
  onToggle: () => void
}

/**
 * The pin action of the trophy lists and the detail dialog: a quiet icon button like the note button. Pinned = filled pin in the accent
 * colour. At the maximum the button of an unpinned achievement is disabled and its tooltip says why (aria-disabled, so it stays focusable).
 */
export function PinIconButton({ name, pin, className = '-my-0.5 inline-flex shrink-0' }: { name: string; pin: PinControl; className?: string }) {
  const { t } = useTranslation('live')
  const blocked = !pin.pinned && !pin.canPin
  return (
    <GhostIconButton
      label={pin.pinned ? t('pins.button.unpin', { name }) : t('pins.button.pin', { name })}
      description={blocked ? t('pins.button.full', { max: pin.max }) : undefined}
      onClick={pin.onToggle}
      tone={pin.pinned ? 'accent' : 'muted'}
      disabled={blocked}
      className={className}
    >
      {pin.pinned ? <IconPinFilled size={16} aria-hidden="true" /> : <IconPin size={16} aria-hidden="true" />}
    </GhostIconButton>
  )
}
