import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { IconLoader2 } from '@tabler/icons-react'
import { Tooltip } from './Tooltip'

export type IconButtonTone = 'default' | 'primary' | 'emerald' | 'gold'

const TONE_CLASS: Record<IconButtonTone, string> = {
  // Quiet by default (sits inside a toolbar); the accent tones keep the colour the text button had
  default: 'pv-icon-btn--ghost',
  primary: 'pv-btn-primary',
  emerald: 'pv-btn-emerald',
  gold: 'pv-btn-gold',
}

// Class list for anything that should look like an icon button but is not a <button> (router links, download anchors)
export function iconButtonClass(tone: IconButtonTone = 'default'): string {
  return `pv-btn pv-icon-btn relative ${TONE_CLASS[tone]}`
}

interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'title' | 'aria-label'> {
  // Name of the action: the aria-label and the first line of our tooltip (never a native title tooltip)
  label: string
  description?: string
  tone?: IconButtonTone
  // Work in progress: a spinner covers the icon and the button stays exactly as big as before
  busy?: boolean
  children: ReactNode
}

// Icon-only button with our own tooltip on hover and keyboard focus.
export function IconButton({
  label,
  description,
  tone = 'default',
  busy = false,
  disabled,
  className = '',
  type = 'button',
  children,
  ...rest
}: IconButtonProps) {
  return (
    <Tooltip label={label} description={description}>
      <button
        {...rest}
        type={type}
        aria-label={label}
        aria-busy={busy || undefined}
        disabled={disabled || busy}
        className={`${iconButtonClass(tone)} ${className}`.trim()}
      >
        <span className={`inline-flex items-center justify-center ${busy ? 'invisible' : ''}`}>{children}</span>
        {busy && (
          <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
            <IconLoader2 size={17} className="animate-spin" />
          </span>
        )}
      </button>
    </Tooltip>
  )
}
