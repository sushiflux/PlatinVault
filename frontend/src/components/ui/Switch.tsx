import type { ReactNode } from 'react'
import { IconCheck, IconX } from '@tabler/icons-react'

interface SwitchProps {
  checked: boolean
  onChange: (next: boolean) => void
  // The label. The whole row is the click target, and it is the accessible name of the switch (put a second, quieter line in
  // it when a setting needs one, e.g. the cabinet menu).
  children: ReactNode
  // Layout of the row (the first child is the track, the second the label); the colours and the track are not themeable here
  className?: string
  disabled?: boolean
  // "dark" pins the dark-surface colours for a surface that stays dark in the light theme (the trophy cabinet)
  surface?: 'theme' | 'dark'
}

// The one on/off switch of the app: green = on, red = off (tokens --c-toggle-*, see index.css), a check or cross in the knob so
// the state does not depend on colour alone. A real button with role="switch" and aria-checked: Space and Enter toggle it, it has a
// visible focus ring, the knob slide stops with reduced motion. Fixed size, so toggling never moves anything around it.
// Use it for every setting that is simply on or off; lists of choices and checklists stay checkboxes.
export function Switch({ checked, onChange, children, className = '', disabled = false, surface = 'theme' }: SwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      data-surface={surface === 'dark' ? 'dark' : undefined}
      onClick={() => onChange(!checked)}
      className={`pv-switch ${className}`}
    >
      <span className="pv-switch-track" aria-hidden="true">
        <span className="pv-switch-knob">
          {checked ? <IconCheck size="0.64em" stroke={3.6} /> : <IconX size="0.64em" stroke={3.6} />}
        </span>
      </span>
      <span className="min-w-0">{children}</span>
    </button>
  )
}
