import { useEffect, useState } from 'react'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconArrowUp } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'

// "Back to top" button bottom-right (48 px target, calm solid surface, no shadow); appears after scrolling one screen down.
// --pv-dock-h is the height of the "Im Blick" dock at the bottom of the game page (unset without it): the button stands above the dock.
export function ScrollTopButton() {
  const { t } = useTranslation('shell')
  const [visible, setVisible] = useState(false)
  const reduce = useReducedMotion()

  useEffect(() => {
    const update = () => setVisible(window.scrollY > window.innerHeight * 0.8)
    update()
    window.addEventListener('scroll', update, { passive: true })
    return () => window.removeEventListener('scroll', update)
  }, [])

  return (
    <AnimatePresence>
      {visible && (
        <motion.button
          type="button"
          aria-label={t('scrollTop')}
          onClick={() => window.scrollTo({ top: 0, behavior: reduce ? 'auto' : 'smooth' })}
          className="no-print fixed right-4 z-40 flex h-12 w-12 items-center justify-center cursor-pointer sm:right-6"
          style={{
            bottom: 'calc(var(--pv-dock-h, 0px) + 1rem)',
            background: 'var(--bg-surface)',
            color: 'var(--link)',
            border: '1px solid var(--border-strong)',
            borderRadius: 'var(--radius-md)',
          }}
          initial={reduce ? { opacity: 0 } : { opacity: 0, y: 12, scale: 0.9 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={reduce ? { opacity: 0 } : { opacity: 0, y: 12, scale: 0.9 }}
          transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
        >
          <IconArrowUp size={20} />
        </motion.button>
      )}
    </AnimatePresence>
  )
}
