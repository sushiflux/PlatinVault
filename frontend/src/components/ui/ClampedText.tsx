import { useRef, type CSSProperties } from 'react'
import { useClamped } from '../../utils/useClamped'
import { Tooltip } from './Tooltip'

interface ClampedTextProps {
  text: string
  // Classes of the text itself (size, weight, line height)
  className?: string
  style?: CSSProperties
  // Classes of the wrapper that sits in the parent's layout (default: a shrinkable block)
  wrapperClassName?: string
  side?: 'top' | 'bottom'
}

/**
 * A text of at most TWO lines: it wraps, and only what does not fit on the second line is replaced by an ellipsis. Our own tooltip
 * shows the whole text, and only while it really is cut off (the multi-line twin of TruncatedText; no native `title`).
 * The caller reserves the two lines (min-height) where neighbours must stay level.
 */
export function ClampedText({ text, className = '', style, wrapperClassName = 'block min-w-0', side = 'bottom' }: ClampedTextProps) {
  const ref = useRef<HTMLSpanElement>(null)
  const clamped = useClamped(ref)
  return (
    <Tooltip label={text} disabled={!clamped} side={side} className={wrapperClassName}>
      <span ref={ref} className={`min-w-0 break-words line-clamp-2 ${className}`.trim()} style={style}>
        {text}
      </span>
    </Tooltip>
  )
}
