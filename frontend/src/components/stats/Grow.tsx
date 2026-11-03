import type { CSSProperties, ReactNode } from 'react'
import { motion, useReducedMotion } from 'motion/react'

// A bar that grows from its left baseline. scaleX keeps it cheap (no layout), and under reduced motion it just is there.
export function Grow({
  children,
  delay = 0,
  className,
  style,
}: {
  children?: ReactNode
  delay?: number
  className?: string
  style?: CSSProperties
}) {
  const reduce = useReducedMotion() ?? false
  return (
    <motion.div
      className={className}
      style={{ transformOrigin: 'left center', ...style }}
      initial={reduce ? false : { scaleX: 0 }}
      animate={{ scaleX: 1 }}
      transition={{ duration: 0.6, delay, ease: [0.22, 1, 0.36, 1] }}
    >
      {children}
    </motion.div>
  )
}
