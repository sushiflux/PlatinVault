import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AnimatePresence, motion, usePresence, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconChevronDown, IconChevronLeft, IconChevronRight, IconChevronUp, IconPinFilled, IconChecks } from '@tabler/icons-react'
import type { LiveAchievementItemDto } from '../../api/types'
import { UI_SCALE_APPLIED_EVENT } from '../../utils/uiScale'
import { useAchievementNotes } from '../../utils/useAchievementNotes'
import { usePopupMs } from '../../utils/usePopupMs'
import { AchievementNoteDialog } from '../AchievementNoteDialog'
import { TrophyDetailDialog, type TrophyItemProps } from '../TrophyViews'
import { Tooltip } from '../ui/Tooltip'
import { PinCard } from './PinCard'
import { usePins, type PinsApi } from './pinsContext'
import { usePinsCollapsed, usePinsPlacement, type PinsPlacement } from './pinsLayout'
import { usePinList, type PinEntry } from './usePinList'
import './pins.css'

interface PinsSurfaceProps {
  /** Which of the two places this instance is; it draws only when the window's layout is that one. */
  placement: PinsPlacement
  /** The live achievements of the game (the page's own query, so counters and unlocks are as fresh as the rest of the page). */
  achievements: LiveAchievementItemDto[] | undefined
  /** How-tos from the guide, shown in the detail dialog. */
  tips: Record<string, string>
}

/**
 * "Im Blick" - the pinned achievements of the game, on every tab of its page. The page mounts this twice (the column inside the
 * body row, the dock after it); only the one that matches the window's layout draws. Nothing is drawn while the game has no pin.
 */
export function PinsSurface({ placement, achievements, tips }: PinsSurfaceProps) {
  const pins = usePins()
  const actual = usePinsPlacement()
  if (!pins || actual !== placement) return null
  return <PinsView key={pins.appId} placement={placement} pins={pins} achievements={achievements} tips={tips} />
}

function PinsView({ placement, pins, achievements, tips }: PinsSurfaceProps & { pins: PinsApi }) {
  const { enabled: notesEnabled } = useAchievementNotes()
  // A done card stays as long as a trophy popup does (setting "Anzeigedauer der Trophäen-Popups")
  const entries = usePinList(pins.appId, pins.names, achievements, pins.wasRemovedHere, usePopupMs())
  const [collapsed, toggleCollapsed] = usePinsCollapsed(placement)

  // The detail dialog of a card (guide tip, note), also the note editor opened from it
  const [detailName, setDetailName] = useState<string | null>(null)
  const [noteName, setNoteName] = useState<string | null>(null)
  const [revealed, setRevealed] = useState<Record<string, boolean>>({})
  const [original, setOriginal] = useState<Record<string, boolean>>({})
  const byName = useMemo(() => new Map((achievements ?? []).map((ach) => [ach.apiName, ach])), [achievements])
  const detailAch = detailName ? byName.get(detailName) : undefined
  const noteAch = noteName ? byName.get(noteName) : undefined

  const itemFor = (ach: LiveAchievementItemDto): TrophyItemProps => ({
    ach,
    tip: tips[ach.apiName],
    covered: ach.isHidden && !ach.achieved && !revealed[ach.apiName],
    showOriginal: Boolean(original[ach.apiName]),
    onReveal: () => setRevealed((previous) => ({ ...previous, [ach.apiName]: true })),
    onToggleOriginal: () => setOriginal((previous) => ({ ...previous, [ach.apiName]: !previous[ach.apiName] })),
    notesEnabled,
    onNote: () => setNoteName(ach.apiName),
    onOpen: () => undefined,
    pin: { pinned: pins.isPinned(ach.apiName), canPin: !pins.full, max: pins.max, onToggle: () => pins.toggle(ach.apiName) },
  })

  // "Geschaffte entfernen": the pinned achievements that are unlocked (a card that is leaving is not pinned any more)
  const doneNames = entries.filter((entry) => !entry.leaving && entry.ach?.achieved).map((entry) => entry.apiName)
  const removeDone = () => pins.removeMany(doneNames)
  const open = useCallback((apiName: string) => setDetailName(apiName), [])

  return (
    <>
      {/* Comes with the first pin and goes with the last one: both with a short animation (a plain fade with reduced motion) */}
      <AnimatePresence>
        {entries.length > 0 &&
          (placement === 'dock' ? (
            <Dock key="dock" entries={entries} pins={pins} collapsed={collapsed} onToggle={toggleCollapsed} doneCount={doneNames.length} onClear={removeDone} onOpen={open} />
          ) : (
            <Column key="column" entries={entries} pins={pins} collapsed={collapsed} onToggle={toggleCollapsed} doneCount={doneNames.length} onClear={removeDone} onOpen={open} />
          ))}
      </AnimatePresence>

      {detailAch && <TrophyDetailDialog item={itemFor(detailAch)} onClose={() => setDetailName(null)} />}
      {notesEnabled && noteAch && <AchievementNoteDialog key={noteAch.apiName} ach={noteAch} appId={pins.appId} onClose={() => setNoteName(null)} />}
    </>
  )

}

interface SurfaceProps {
  entries: PinEntry[]
  pins: PinsApi
  collapsed: boolean
  onToggle: () => void
  doneCount: number
  onClear: () => void
  onOpen: (apiName: string) => void
}

/** "Geschaffte entfernen": calm action, there while at least one pinned achievement is unlocked (its place is kept otherwise). */
function ClearDoneButton({ doneCount, onClear }: { doneCount: number; onClear: () => void }) {
  const { t } = useTranslation('live')
  const idle = doneCount === 0
  return (
    <Tooltip label={t('pins.clearDone.label')} description={t('pins.clearDone.description')} disabled={idle} className="inline-flex shrink-0">
      <button
        type="button"
        className="pv-pins-clear"
        data-idle={idle ? '' : undefined}
        // Out of reach for the keyboard and for screen readers while there is nothing to remove
        tabIndex={idle ? -1 : undefined}
        aria-hidden={idle ? true : undefined}
        disabled={idle}
        aria-label={t('pins.clearDone.label')}
        onClick={onClear}
      >
        <IconChecks size={15} aria-hidden="true" />
        <span>{t('pins.clearDone.label')}</span>
      </button>
    </Tooltip>
  )
}

function TitleLine({ pins }: { pins: PinsApi }) {
  const { t } = useTranslation('live')
  return (
    <span className="pv-pins-title">
      <IconPinFilled size={16} aria-hidden="true" />
      <span>{t('pins.title')}</span>
      <span className="pv-pins-count" aria-label={t('pins.countLabel', { n: pins.names.length, max: pins.max })}>
        {pins.names.length}/{pins.max}
      </span>
    </span>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// The dock at the bottom of the window
// ---------------------------------------------------------------------------------------------------------------

function Dock({ entries, pins, collapsed, onToggle, doneCount, onClear, onOpen }: SurfaceProps) {
  const { t } = useTranslation('live')
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLElement>(null)
  const rowRef = useRef<HTMLDivElement>(null)
  const listId = 'pv-pins-dock-row'

  // The dock tells the page how high it is (--pv-dock-h): the chapter navigation, the notices and the "back to top" button keep clear of it
  useEffect(() => {
    const element = ref.current
    if (!element) return
    const root = document.documentElement
    const publish = () => root.style.setProperty('--pv-dock-h', `${element.getBoundingClientRect().height}px`)
    publish()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(publish)
    observer?.observe(element)
    window.addEventListener(UI_SCALE_APPLIED_EVENT, publish)
    return () => {
      observer?.disconnect()
      window.removeEventListener(UI_SCALE_APPLIED_EVENT, publish)
      root.style.removeProperty('--pv-dock-h')
    }
  }, [])

  // The mouse wheel scrolls the row sideways (the page is not scrolled by a wheel turn over the dock)
  useEffect(() => {
    const row = rowRef.current
    if (!row || collapsed) return
    const onWheel = (event: WheelEvent) => {
      if (event.deltaX !== 0 || event.ctrlKey || row.scrollWidth <= row.clientWidth) return
      event.preventDefault()
      row.scrollLeft += event.deltaY
    }
    row.addEventListener('wheel', onWheel, { passive: false })
    return () => row.removeEventListener('wheel', onWheel)
  }, [collapsed])

  const toggleLabel = collapsed ? t('pins.expand') : t('pins.collapse')
  return (
    <motion.section
      ref={ref}
      className="pv-gdock no-print"
      aria-label={t('pins.region')}
      data-testid="pins-dock"
      data-collapsed={collapsed ? 'true' : undefined}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: 24 }}
      animate={{ opacity: 1, y: 0 }}
      exit={reduce ? { opacity: 0 } : { opacity: 0, y: 24 }}
      transition={{ duration: reduce ? 0 : 0.2, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="pv-gdock-bar">
        <button type="button" className="pv-gdock-toggle" aria-expanded={!collapsed} aria-controls={collapsed ? undefined : listId} aria-label={toggleLabel} onClick={onToggle}>
          <TitleLine pins={pins} />
          {collapsed ? <IconChevronUp size={18} aria-hidden="true" /> : <IconChevronDown size={18} aria-hidden="true" />}
        </button>
        <ClearDoneButton doneCount={doneCount} onClear={onClear} />
      </div>
      {!collapsed && (
        <div ref={rowRef} id={listId} className="pv-gdock-row" role="list" aria-label={t('pins.list')}>
          <AnimatePresence initial={false}>
            {entries.map((entry) => (
              <PinCard key={entry.apiName} entry={entry} placement="dock" onOpen={onOpen} onRemove={pins.toggle} />
            ))}
          </AnimatePresence>
        </div>
      )}
    </motion.section>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// The column on the right (landscape, wide)
// ---------------------------------------------------------------------------------------------------------------

function Column({ entries, pins, collapsed, onToggle, doneCount, onClear, onOpen }: SurfaceProps) {
  const { t } = useTranslation('live')
  const reduce = useReducedMotion() ?? false
  // The exit mirrors the entrance (pins.css): the column shrinks to nothing and fades, then it is removed. Without animation (reduced
  // motion) it goes at once; a timer is the safety net when the browser never reports the end of the animation.
  const [present, safeToRemove] = usePresence()
  useEffect(() => {
    if (present || !safeToRemove) return
    const timer = window.setTimeout(safeToRemove, reduce ? 0 : 400)
    return () => window.clearTimeout(timer)
  }, [present, safeToRemove, reduce])
  const toggleLabel = collapsed ? t('pins.expand') : t('pins.collapse')
  return (
    <aside className="pv-gpins no-print" data-leaving={present ? undefined : ''} aria-label={t('pins.region')} data-testid="pins-column" data-collapsed={collapsed ? 'true' : undefined}>
      <div className="pv-gnav-head">
        {collapsed ? (
          <Tooltip label={t('pins.title')} description={t('pins.countLabel', { n: pins.names.length, max: pins.max })}>
            <span className="pv-pins-title">
              <IconPinFilled size={16} aria-hidden="true" />
              <span className="sr-only">{t('pins.title')}</span>
              <span className="pv-pins-count">{pins.names.length}</span>
            </span>
          </Tooltip>
        ) : (
          <TitleLine pins={pins} />
        )}
        <Tooltip label={toggleLabel} side="bottom">
          <button type="button" className="pv-gnav-icon-btn" aria-label={toggleLabel} aria-expanded={!collapsed} onClick={onToggle}>
            {collapsed ? <IconChevronLeft size={16} aria-hidden="true" /> : <IconChevronRight size={16} aria-hidden="true" />}
          </button>
        </Tooltip>
      </div>
      <div className="pv-gpins-list" role="list" aria-label={t('pins.list')}>
        {collapsed ? (
          entries.map((entry) => <MiniPin key={entry.apiName} entry={entry} onOpen={onOpen} />)
        ) : (
          <AnimatePresence initial={false}>
            {entries.map((entry) => (
              <PinCard key={entry.apiName} entry={entry} placement="column" onOpen={onOpen} onRemove={pins.toggle} />
            ))}
          </AnimatePresence>
        )}
      </div>
      {!collapsed && (
        <div className="pv-gpins-foot">
          <ClearDoneButton doneCount={doneCount} onClear={onClear} />
        </div>
      )}
    </aside>
  )
}

/** The collapsed column: only the icon of the achievement (a gold edge once it is done); a click opens the same detail dialog. */
function MiniPin({ entry, onOpen }: { entry: PinEntry; onOpen: (apiName: string) => void }) {
  const { t } = useTranslation('live')
  const { ach, apiName } = entry
  const name = ach?.displayName ?? apiName
  const done = Boolean(ach?.achieved)
  return (
    <div role="listitem">
    <Tooltip label={name} description={ach ? (done ? t('pins.card.done') : t('pins.card.open')) : undefined} side="bottom" className="inline-flex">
      <button
        type="button"
        className="pv-pin-mini"
        data-done={done ? '' : undefined}
        data-testid="pin-mini"
        aria-label={ach ? (done ? t('pins.card.labelDone', { name, tier: t(`tiers.${ach.rarityTier}`) }) : t('pins.card.labelOpen', { name, tier: t(`tiers.${ach.rarityTier}`) })) : t('pins.card.labelUnknown', { name })}
        onClick={() => onOpen(apiName)}
      >
        {ach && <img src={ach.achieved ? ach.iconUrl : ach.iconGrayUrl || ach.iconUrl} alt="" style={{ filter: done ? 'none' : 'grayscale(85%) opacity(75%)' }} />}
      </button>
    </Tooltip>
    </div>
  )
}
