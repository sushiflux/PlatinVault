import type { ReactNode } from 'react'
import { Tooltip } from './Tooltip'

interface GhostIconButtonProps {
  // What the button does: tooltip text and the accessible name (the glyph alone says nothing to a screen reader)
  label: string
  onClick: () => void
  children: ReactNode
  // sm = 24 px (inside a dense card), md = 32 px (header of a panel)
  size?: 'sm' | 'md'
  // accent = the thing behind the button is "set" (e.g. a note exists), shown in the crystal colour
  tone?: 'muted' | 'accent'
  // For toggles that show or hide something (sets aria-expanded)
  expanded?: boolean
  // Small dot in the corner, e.g. "there is content"
  dot?: boolean
  // Second tooltip line, e.g. why the button cannot be used right now
  description?: string
  // Not usable right now: dimmed, the click does nothing. aria-disabled instead of the disabled attribute, so the button keeps its focus and
  // its tooltip (a disabled button gets no pointer events in some browsers) and can still say why it is not available
  disabled?: boolean
  // Classes of the tooltip wrapper (placement inside the parent layout)
  className?: string
}

// The quiet icon-only action of the app (note, refresh, ...): no border, a soft background on hover, our own tooltip.
// One look everywhere, so the same kind of action never turns into a lookalike per page.
export function GhostIconButton({
  label,
  onClick,
  children,
  size = 'sm',
  tone = 'muted',
  expanded,
  dot = false,
  description,
  disabled = false,
  className = 'inline-flex shrink-0',
}: GhostIconButtonProps) {
  return (
    <Tooltip label={label} description={description} className={className}>
      <button
        type="button"
        onClick={disabled ? undefined : onClick}
        aria-label={label}
        aria-expanded={expanded}
        aria-disabled={disabled || undefined}
        className={`relative inline-flex items-center justify-center transition-colors focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--accent-crystal)] ${
          disabled ? 'cursor-not-allowed opacity-55' : 'cursor-pointer hover:bg-[var(--bg-elevated)]'
        } ${size === 'md' ? 'h-8 w-8' : 'h-6 w-6'}`}
        style={{ borderRadius: 'var(--radius-sm)', color: tone === 'accent' ? 'var(--accent-crystal)' : 'var(--text-muted)' }}
      >
        {children}
        {dot && (
          <span
            className="absolute right-[3px] top-[3px] h-1.5 w-1.5 rounded-full"
            style={{ background: 'var(--accent-crystal)', boxShadow: '0 0 0 1.5px var(--bg-surface)' }}
            aria-hidden="true"
          />
        )}
      </button>
    </Tooltip>
  )
}
