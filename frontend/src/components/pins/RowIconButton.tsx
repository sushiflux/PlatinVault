import type { ReactNode } from 'react'
import { Tooltip } from '../ui/Tooltip'
import '../../styles/live.css'

interface RowIconButtonProps {
  // What the button does: tooltip text and the accessible name (the glyph alone says nothing to a screen reader)
  label: string
  onClick: () => void
  children: ReactNode
  // md = 3 x 2 rem drawn, still a 48 x 48 px target (dense cards); lg = 3 rem square (rows with room: pin, remove)
  size?: 'md' | 'lg'
  // "On" = the thing behind the button is set (pinned, a note exists): link colour
  on?: boolean
  // For toggles that show or hide something (sets aria-expanded)
  expanded?: boolean
  // Small dot in the corner, e.g. "there is content"
  dot?: boolean
  // Second tooltip line, e.g. why the button cannot be used right now
  description?: string
  // Not usable right now: dimmed, the click does nothing. aria-disabled instead of the disabled attribute, so the button keeps its focus and
  // its tooltip and can still say why it is not available
  disabled?: boolean
  // Classes of the tooltip wrapper (placement inside the parent layout)
  className?: string
}

/**
 * The boxless icon action of the trophy lists, notes, pins and the "Im Blick" cards. One look everywhere (styles/live.css .pv-rowbtn):
 * no frame, a calm tint and the link colour on hover, a visible focus ring, our own tooltip. The 48 px target is kept by an invisible
 * strip above and below the 2 rem high glyph box, so a dense card does not grow.
 */
export function RowIconButton({ label, onClick, children, size = 'md', on = false, expanded, dot = false, description, disabled = false, className = 'inline-flex shrink-0' }: RowIconButtonProps) {
  return (
    <Tooltip label={label} description={description} className={className}>
      <button
        type="button"
        onClick={disabled ? undefined : onClick}
        aria-label={label}
        aria-expanded={expanded}
        aria-disabled={disabled || undefined}
        className={`pv-rowbtn ${size === 'lg' ? 'pv-rowbtn--lg' : ''}`}
        data-on={on ? '' : undefined}
      >
        {children}
        {dot && <span className="pv-rowbtn-dot" aria-hidden="true" />}
      </button>
    </Tooltip>
  )
}
