import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconMinus, IconPlus, IconRestore, IconTextSize } from '@tabler/icons-react'
import { useFormat } from '../../i18n/format'
import { setUiScale, UI_SCALE_DEFAULT, UI_SCALE_STEPS, useUiScale } from '../../utils/uiScale'
import { softReflow } from '../../utils/softReflow'
import { iconButtonClass } from './IconButton'
import { Tooltip } from './Tooltip'

const POPOVER_WIDTH = 328
const EDGE = 8

// Every change of the size goes through here: scroll position kept on the same content, old and new layout cross-faded
function changeScale(next: number) {
  softReflow(() => setUiScale(next))
}

const FOCUSABLE = 'button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * The popover itself. Its CSS is in px on purpose (index.css .pv-scalepop): it must keep its size AND its place while the page
 * behind it re-flows, so a second click on "+" lands on the same button again. The position is taken once, when it opens.
 */
function ScalePopover({ anchor, popoverId, onClose }: { anchor: HTMLElement; popoverId: string; onClose: () => void }) {
  const { t } = useTranslation('shell')
  const format = useFormat()
  // "110 %" (German, with a no-break space so the figure never wraps away from its sign) / "110%" (English)
  const percentText = format.percent
  const { scale, canShrink, canGrow, isDefault } = useUiScale()
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  // Spoken after a change (polite); empty until the first change, so a screen reader stays silent while the popover just opens
  const [announcement, setAnnouncement] = useState('')
  const [place] = useState(() => {
    const rect = anchor.getBoundingClientRect()
    const width = Math.min(POPOVER_WIDTH, document.documentElement.clientWidth - 2 * EDGE)
    const left = Math.min(Math.max(rect.right - width, EDGE), document.documentElement.clientWidth - width - EDGE)
    return { left, top: Math.round(rect.bottom + 10), width }
  })

  const choose = useCallback((next: number) => {
    if (next === scale) return
    changeScale(next)
    setAnnouncement(t('header.scale.announce', { percent: next }))
  }, [scale, t])
  const index = UI_SCALE_STEPS.indexOf(scale as (typeof UI_SCALE_STEPS)[number])

  // Focus moves into the popover (onto the active step), Tab stays inside, Esc closes it and returns the focus to the button
  useEffect(() => {
    const popover = ref.current
    popover?.querySelector<HTMLElement>('[aria-pressed="true"]')?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (!popover) return
      if (event.key === 'Escape') {
        event.stopPropagation()
        onClose()
        anchor.focus()
        return
      }
      if (event.key !== 'Tab') return
      const items = [...popover.querySelectorAll<HTMLElement>(FOCUSABLE)]
      const first = items[0]
      const last = items[items.length - 1]
      if (!first || !last) return
      if (!popover.contains(document.activeElement)) {
        event.preventDefault()
        first.focus()
      } else if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    // A click outside closes it (the button toggles it by itself)
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node
      if (popover?.contains(target) || anchor.contains(target)) return
      onClose()
    }
    document.addEventListener('keydown', onKeyDown, true)
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      document.removeEventListener('pointerdown', onPointerDown, true)
    }
  }, [anchor, onClose])

  return createPortal(
    <motion.div
      ref={ref}
      id={popoverId}
      role="dialog"
      aria-label={t('header.scale.label')}
      className="pv-scalepop"
      style={{ left: place.left, top: place.top, width: place.width }}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: -6, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="pv-scalepop-head">
        <span className="pv-scalepop-title">{t('header.scale.label')}</span>
        <span className="pv-scalepop-value" data-changed={isDefault ? undefined : 'true'} aria-hidden="true">
          {percentText(scale)}
        </span>
      </div>

      <div className="pv-scalepop-row">
        <button
          type="button"
          className="pv-scalepop-step"
          aria-label={t('header.scale.smaller')}
          aria-disabled={!canShrink || undefined}
          onClick={() => canShrink && choose(UI_SCALE_STEPS[index - 1]!)}
        >
          <IconMinus size={18} aria-hidden="true" />
        </button>

        <div className="pv-scalepop-steps" role="group" aria-label={t('header.scale.pickStep')}>
          {UI_SCALE_STEPS.map((step) => (
            <button key={step} type="button" aria-pressed={step === scale} onClick={() => choose(step)}>
              {step}
            </button>
          ))}
        </div>

        <button
          type="button"
          className="pv-scalepop-step"
          aria-label={t('header.scale.larger')}
          aria-disabled={!canGrow || undefined}
          onClick={() => canGrow && choose(UI_SCALE_STEPS[index + 1]!)}
        >
          <IconPlus size={18} aria-hidden="true" />
        </button>
      </div>

      <p className="pv-scalepop-hint">{t('header.scale.hint')}</p>

      <button
        type="button"
        className="pv-scalepop-reset"
        aria-disabled={isDefault || undefined}
        onClick={() => {
          if (isDefault) return
          changeScale(UI_SCALE_DEFAULT)
          setAnnouncement(t('header.scale.announce', { percent: UI_SCALE_DEFAULT }))
        }}
      >
        <IconRestore size={16} aria-hidden="true" />
        <span>
          {isDefault
            ? t('header.scale.isDefault', { percent: percentText(UI_SCALE_DEFAULT) })
            : t('header.scale.resetTo', { percent: percentText(UI_SCALE_DEFAULT) })}
        </span>
      </button>

      <span className="sr-only" role="status">
        {announcement}
      </span>
    </motion.div>,
    document.body
  )
}

/**
 * The size control of the page headers: ONE icon button ("Display size") that opens a small popover with - / steps / +
 * and the reset. The button only shows the current size as a small badge while it differs from 100 %, so the header stays
 * calm. Changing the size re-flows the whole page, which is why the controls live in a popover of fixed (px) size: they do not
 * move under the mouse (see ScalePopover) - the same group in the header of every page.
 */
export function UiScaleControl() {
  const { t } = useTranslation('shell')
  const format = useFormat()
  const { scale, isDefault } = useUiScale()
  const [open, setOpen] = useState(false)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const popoverId = useId()
  const close = useCallback(() => setOpen(false), [])

  return (
    <>
      <Tooltip
        label={t('header.scale.label')}
        description={
          isDefault ? t('header.scale.descriptionDefault') : t('header.scale.descriptionCurrent', { percent: format.percent(scale) })
        }
        disabled={open}
      >
        <button
          ref={buttonRef}
          type="button"
          className={iconButtonClass()}
          aria-label={isDefault ? t('header.scale.label') : t('header.scale.labelCurrent', { percent: scale })}
          aria-haspopup="dialog"
          aria-expanded={open}
          aria-controls={open ? popoverId : undefined}
          onClick={() => setOpen((value) => !value)}
        >
          <span className="inline-flex items-center justify-center">
            <IconTextSize size={18} />
          </span>
          {!isDefault && (
            <span className="pv-hscale-badge" aria-hidden="true">
              {scale}%
            </span>
          )}
        </button>
      </Tooltip>
      <AnimatePresence>{open && buttonRef.current && <ScalePopover key="scale-popover" anchor={buttonRef.current} popoverId={popoverId} onClose={close} />}</AnimatePresence>
    </>
  )
}
