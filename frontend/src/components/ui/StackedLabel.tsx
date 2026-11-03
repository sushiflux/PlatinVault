import type { ReactNode } from 'react'

interface StackedLabelProps {
  // Index of the label that is shown right now
  active: number
  // All labels the control can ever show (in the active language)
  labels: ReactNode[]
}

/**
 * A label that changes with the state of its control ("Load library" / "Loading …"). All labels are drawn on top of each other in
 * one grid cell and only the active one is visible, so the control is always as wide as its widest label and never changes size
 * when the label switches (project rule "Rigid layout"). The hidden ones are removed from the accessibility tree.
 */
export function StackedLabel({ active, labels }: StackedLabelProps) {
  return (
    <span className="grid justify-items-center">
      {labels.map((label, index) => (
        <span
          key={index}
          className={`col-start-1 row-start-1 ${index === active ? '' : 'invisible'}`}
          aria-hidden={index === active ? undefined : true}
        >
          {label}
        </span>
      ))}
    </span>
  )
}
