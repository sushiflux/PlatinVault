import { useCallback, useEffect, useRef, useState } from 'react'
import { AnimatePresence, MotionConfig, motion } from 'motion/react'
import type { Variants } from 'motion/react'
import { IconArrowLeft, IconArrowRight, IconCheck, IconX } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { Tooltip } from '../ui/Tooltip'
import { useTourSteps } from './steps'
import type { TourSettingsTab } from './steps'

interface OnboardingTourProps {
  open: boolean
  // Hidden (progress kept) while another modal opened from the tour, e.g. the settings, is on screen
  paused?: boolean
  // Called for "Skip", Esc and "Let's go" - the caller remembers that the tour has been seen
  onClose: () => void
  // The checklist in the last step says which settings tab fits the clicked row
  onOpenSettings: (tab?: TourSettingsTab) => void
}

// Same focus ring everywhere in the tour (token colour instead of the browser default)
const FOCUS_RING =
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]'

const FOCUSABLE =
  'a[href], button:not(:disabled), textarea:not(:disabled), input:not(:disabled), select:not(:disabled), [tabindex]:not([tabindex="-1"])'

// Direction aware slide: the next step comes in from the right, the previous one from the left
const slide: Variants = {
  enter: (direction: number) => ({ opacity: 0, x: direction * 28 }),
  center: { opacity: 1, x: 0 },
  exit: (direction: number) => ({ opacity: 0, x: direction * -28 }),
}

// Six-step walkthrough in a fixed-size modal, so no step ever resizes or moves the frame around it.
export function OnboardingTour({ open, paused = false, onClose, onOpenSettings }: OnboardingTourProps) {
  const { t } = useTranslation('onboarding')
  const tourSteps = useTourSteps()
  const [index, setIndex] = useState(0)
  const [direction, setDirection] = useState(1)
  const panelRef = useRef<HTMLDivElement>(null)

  const visible = open && !paused
  const last = tourSteps.length - 1
  const step = tourSteps[index]!

  const goTo = useCallback((target: number) => {
    setIndex((current) => {
      const clamped = Math.max(0, Math.min(last, target))
      if (clamped !== current) setDirection(clamped > current ? 1 : -1)
      return clamped
    })
  }, [last])

  const finish = useCallback(() => {
    setIndex(0)
    setDirection(1)
    onClose()
  }, [onClose])

  // Remember what had the focus when the tour opened and give it back when the tour closes
  useEffect(() => {
    if (!open) return
    const previouslyFocused = document.activeElement as HTMLElement | null
    return () => {
      previouslyFocused?.focus?.()
    }
  }, [open])

  // Focus moves into the tour whenever it becomes visible (also when it comes back after the settings were closed)
  useEffect(() => {
    if (!visible) return
    panelRef.current?.querySelector<HTMLElement>('[data-autofocus]')?.focus()
  }, [visible])

  // Esc skips, arrow keys navigate, Tab stays inside the tour
  useEffect(() => {
    if (!visible) return
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        finish()
      } else if (e.key === 'ArrowRight') {
        goTo(index + 1)
      } else if (e.key === 'ArrowLeft') {
        goTo(index - 1)
      } else if (e.key === 'Tab' && panelRef.current) {
        const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE))
        if (items.length === 0) return
        const first = items[0]!
        const lastItem = items[items.length - 1]!
        const active = document.activeElement
        if (!panelRef.current.contains(active)) {
          e.preventDefault()
          first.focus()
        } else if (e.shiftKey && active === first) {
          e.preventDefault()
          lastItem.focus()
        } else if (!e.shiftKey && active === lastItem) {
          e.preventDefault()
          first.focus()
        }
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [visible, index, finish, goTo])

  if (!visible) return null

  const isLast = index === last
  const StepIcon = step.icon
  const accent = step.accent

  return (
    <MotionConfig reducedMotion="user">
      <div className="pv-modal-backdrop">
        <motion.div
          ref={panelRef}
          role="dialog"
          aria-modal="true"
          aria-label={t('tour.ariaLabel')}
          initial={{ opacity: 0, y: 18, scale: 0.98 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
          className="pv-modal flex w-full max-w-4xl flex-col overflow-hidden"
          style={{ height: 'min(42rem, calc(100dvh - 2rem))' }}
        >
          <header
            className="flex items-center justify-between gap-3 px-5 py-3 sm:px-6"
            style={{ background: 'var(--bg-elevated)', borderBottom: '1px solid var(--border-subtle)' }}
          >
            <div className="flex items-center gap-3">
              <img src="/favicon.svg" alt="" className="h-8 w-8 rounded-lg shadow-sm" />
              <span className="pv-badge pv-badge-a">
                {t('tour.stepOf', { current: index + 1, total: tourSteps.length })}
              </span>
            </div>
            <Tooltip label={t('tour.skipHint')} description="Esc" side="bottom">
              <button type="button" className={`pv-btn !py-1.5 ${FOCUS_RING}`} onClick={finish}>
                <span>{t('tour.skip')}</span>
                <IconX size={15} aria-hidden="true" />
              </button>
            </Tooltip>
          </header>

          <p className="sr-only" aria-live="polite">
            {t('tour.stepAnnouncement', { current: index + 1, total: tourSteps.length, title: step.title })}
          </p>

          <div className="relative min-h-0 flex-1 overflow-y-auto overflow-x-hidden">
            <AnimatePresence mode="wait" custom={direction} initial={false}>
              <motion.div
                key={step.id}
                custom={direction}
                variants={slide}
                initial="enter"
                animate="center"
                exit="exit"
                transition={{ duration: 0.22, ease: 'easeOut' }}
                className="grid min-h-full grid-cols-1 gap-5 p-5 sm:p-6 md:h-full md:grid-cols-[minmax(0,5fr)_minmax(0,6fr)] md:gap-7"
              >
                <div className="flex min-w-0 flex-col gap-3.5 md:justify-start md:pt-6">
                  <span
                    className="flex h-11 w-11 items-center justify-center rounded-xl"
                    style={{
                      background: `var(--accent-${accent}-bg)`,
                      color: `var(--accent-${accent})`,
                      border: `1px solid var(--accent-${accent}-border)`,
                    }}
                  >
                    <StepIcon size={22} aria-hidden="true" />
                  </span>
                  <h2 className="text-xl font-extrabold tracking-tight sm:text-2xl" style={{ color: 'var(--text-primary)' }}>
                    {step.title}
                  </h2>
                  <p className="text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                    {step.intro}
                  </p>
                  <ul className="space-y-2 text-sm">
                    {step.points.map((point) => (
                      <li key={point.lead} className="flex items-start gap-2.5 leading-snug" style={{ color: 'var(--text-secondary)' }}>
                        <IconCheck
                          size={16}
                          stroke={2.5}
                          className="mt-0.5 shrink-0"
                          style={{ color: `var(--accent-${accent})` }}
                          aria-hidden="true"
                        />
                        <span>
                          <strong style={{ color: 'var(--text-primary)' }}>{point.lead}</strong> – {point.text}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>

                <div
                  className="min-h-[22rem] min-w-0 overflow-y-auto p-4 md:min-h-0"
                  style={{
                    background: 'var(--bg-elevated)',
                    border: '1px solid var(--border-subtle)',
                    borderRadius: 'var(--radius-lg)',
                  }}
                >
                  <step.Visual onOpenSettings={onOpenSettings} />
                </div>
              </motion.div>
            </AnimatePresence>
          </div>

          <footer
            className="flex items-center justify-between gap-3 px-5 py-3 sm:px-6"
            style={{ background: 'var(--bg-elevated)', borderTop: '1px solid var(--border-subtle)' }}
          >
            <button
              type="button"
              className={`pv-btn min-w-[7.75rem] justify-center disabled:cursor-not-allowed disabled:opacity-40 ${FOCUS_RING}`}
              onClick={() => goTo(index - 1)}
              disabled={index === 0}
            >
              <IconArrowLeft size={15} aria-hidden="true" />
              <span>{t('tour.back')}</span>
            </button>

            <nav className="hidden items-center sm:flex" aria-label={t('tour.stepsNav')}>
              {tourSteps.map((s, i) => (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => goTo(i)}
                  aria-label={t('tour.stepDot', { n: i + 1, title: s.title })}
                  aria-current={i === index ? 'step' : undefined}
                  className={`flex h-6 items-center justify-center rounded px-1 cursor-pointer ${FOCUS_RING}`}
                >
                  <span
                    className="block h-2.5 rounded-full transition-all duration-200"
                    style={{
                      width: i === index ? '1.5rem' : '0.625rem',
                      background: i === index ? 'var(--accent-crystal)' : 'var(--border-strong)',
                    }}
                  />
                </button>
              ))}
            </nav>

            {isLast ? (
              <button
                type="button"
                className={`pv-btn pv-btn-emerald min-w-[7.75rem] justify-center ${FOCUS_RING}`}
                onClick={finish}
                data-autofocus
              >
                <IconCheck size={15} aria-hidden="true" />
                <span>{t('tour.finish')}</span>
              </button>
            ) : (
              <button
                type="button"
                className={`pv-btn pv-btn-primary min-w-[7.75rem] justify-center ${FOCUS_RING}`}
                onClick={() => goTo(index + 1)}
                data-autofocus
              >
                <span>{t('tour.next')}</span>
                <IconArrowRight size={15} aria-hidden="true" />
              </button>
            )}
          </footer>
        </motion.div>
      </div>
    </MotionConfig>
  )
}
