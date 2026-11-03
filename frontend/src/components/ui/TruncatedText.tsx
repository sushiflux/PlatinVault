import { useRef, type CSSProperties } from 'react'
import { useTruncated } from '../../utils/useTruncated'
import { Tooltip } from './Tooltip'

interface TruncatedTextProps {
  text: string
  // Tooltip title when it differs from the text (e.g. "Spielzeit: 12 Std.")
  label?: string
  // Classes of the cut-off text itself (size, weight)
  className?: string
  style?: CSSProperties
  // Classes of the wrapper that sits in the parent's layout (default: a shrinkable flex box)
  wrapperClassName?: string
  side?: 'top' | 'bottom'
}

/**
 * One line of text that is cut off with an ellipsis when it does not fit. Our own tooltip shows the full text, and only while
 * it is actually cut off (the replacement of the native `title` attribute, which this app does not use).
 */
export function TruncatedText({ text, label, className = '', style, wrapperClassName = 'flex min-w-0', side = 'bottom' }: TruncatedTextProps) {
  const ref = useRef<HTMLSpanElement>(null)
  const truncated = useTruncated(ref)
  return (
    <Tooltip label={label ?? text} disabled={!truncated} side={side} className={wrapperClassName}>
      <span ref={ref} className={`block min-w-0 truncate ${className}`.trim()} style={style}>
        {text}
      </span>
    </Tooltip>
  )
}
