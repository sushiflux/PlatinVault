import { useEffect, useState } from 'react'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconArrowUp } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'

// Round "back to top" button bottom-right, above the footer (never over it); appears after scrolling one screen down.
// --pv-footer-h is the measured height of the page footer (AppFooter publishes it); the button keeps its distance to it. A page
// without a footer (the game page, its version and licences sit in the side navigation) leaves it unset: 1 rem from the edge.
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
          className="no-print fixed right-4 z-40 flex h-11 w-11 items-center justify-center rounded-full shadow-lg backdrop-blur-md cursor-pointer sm:right-6"
          style={{
            bottom: 'calc(var(--pv-footer-h, 0px) + 1rem)',
            background: 'var(--bg-glass)',
            color: 'var(--accent-crystal)',
            border: '1px solid var(--accent-crystal-border)',
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
