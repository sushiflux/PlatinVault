import type { HTMLAttributes, ReactNode } from 'react'

interface ReservedTextProps extends HTMLAttributes<HTMLDivElement> {
  // Every text this line can ever show (in the active language): the line is as tall as the tallest of them at the current
  // width, so a message appearing, changing or disappearing never moves or resizes anything around it
  candidates: string[]
  // The text shown right now (empty = nothing)
  children?: ReactNode
}

/**
 * A line of text (hint, validation message, status) whose room is reserved from the start. All candidate texts are drawn
 * invisibly on top of each other from a data attribute (CSS content), so they take room but are no DOM text - tests and screen
 * readers never see them. See .pv-reserve in index.css and the project rule "Rigid layout".
 */
export function ReservedText({ candidates, children, className = '', ...rest }: ReservedTextProps) {
  return (
    <div {...rest} className={`pv-reserve ${className}`.trim()}>
      <span>{children}</span>
      {candidates.map((text, index) => (
        <span key={index} className="pv-reserve-copy" aria-hidden="true" data-text={text} />
      ))}
    </div>
  )
}
