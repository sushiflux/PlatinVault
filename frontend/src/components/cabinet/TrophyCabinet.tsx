import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { CopyErrorButton } from '../ui/CopyErrorButton'
import { errorCodeOf } from '../../utils/errorReport'
import type { AnimationEvent, CSSProperties, KeyboardEvent } from 'react'
import { Link } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import type { MotionProps } from 'motion/react'
import { useTranslation } from 'react-i18next'
import {
  IconAlertTriangle,
  IconArrowRight,
  IconCalendarEvent,
  IconChevronLeft,
  IconChevronRight,
  IconClock,
  IconDeviceGamepad2,
  IconDiamond,
  IconDoor,
  IconDoorEnter,
  IconHourglassHigh,
  IconTrophy,
  IconUsers,
  IconX,
  IconZoomIn,
  IconZoomOut,
} from '@tabler/icons-react'
import { addCabinetPresented, fetchCabinetPresented } from '../../api/client'
import type { ShowcaseGameDto } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { TierTrophy } from '../platinum/TierTrophy'
import { IconButton, iconButtonClass } from '../ui/IconButton'
import { Switch } from '../ui/Switch'
import { Tooltip } from '../ui/Tooltip'
import { isFullyVisible, pageScrollTarget, revealScrollTarget, sameEdges, wallEdges, wallLayout, type WallEdges } from './wall'
import { announcedNames, landingSchedule, needsBaseline, pendingGames, type LandingSchedule } from './presentation'
import { preloadCovers, showcaseQueryOptions } from './showcaseQuery'
import {
  LIGHTING_MODES,
  SIZE_STEPS,
  loadLighting,
  loadShowEmpty,
  loadSize,
  saveLighting,
  saveShowEmpty,
  saveSize,
  type Lighting,
} from './preferences'
import './cabinet.css'

const EASE = [0.22, 1, 0.36, 1] as const

// The server-side memory of which completed games the cabinet has already presented
const PRESENTED_KEY = ['showcase', 'presented'] as const

// The doors: closed -> opening -> open -> closing -> closed
type DoorPhase = 'closed' | 'opening' | 'open' | 'closing'

const MAX_PER_SHELF = 80
// A cabinet always shows at least this many lit shelves, filled or not
const MIN_SHELVES = 3

// ---------------------------------------------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------------------------------------------

// Portrait: the details come up from the bottom (sheet); landscape: they slide in from the right
function usePortrait(): boolean {
  const query = '(orientation: portrait)'
  return useSyncExternalStore(
    (notify) => {
      const list = window.matchMedia(query)
      list.addEventListener('change', notify)
      return () => list.removeEventListener('change', notify)
    },
    () => window.matchMedia(query).matches,
    () => false
  )
}

// The wording of the cabinet that depends on the language: hours, percentages, dates and the nameplate stamp. A hook, so the
// cabinet renders again (texts and formats) when the language is switched.
function useCabinetText() {
  const { t } = useTranslation('cabinet')
  const fmt = useFormat()
  return useMemo(
    () => ({
      hours: (hours: number) => t('hours', { value: fmt.number(hours, hours < 10 ? 1 : 0) }),
      percent: (percent: number) => fmt.percent(percent, 1),
      date: (iso: string) => fmt.date(iso, 'dateLong'),
      // The date and the time of day (local time zone) as two parts: the default shelf size shows them on two short lines,
      // the larger ones as "12.10.2026 · 21:34 Uhr" on one
      stampParts: (iso: string): [string, string] | null => {
        const date = new Date(iso)
        if (Number.isNaN(date.getTime())) return null
        return [fmt.date(date, 'date'), t('plaque.time', { time: fmt.date(date, 'time') })]
      },
      days: (days: number) => (days <= 0 ? t('info.sameDay') : t('info.days', { count: days })),
    }),
    [t, fmt]
  )
}

// Newest platinum first; games without a date go to the end (equal dates by name in the collation of the active language)
function byPlatinumDesc(locale: string) {
  return (a: ShowcaseGameDto, b: ShowcaseGameDto): number =>
    (Date.parse(b.platinumAt ?? '') || 0) - (Date.parse(a.platinumAt ?? '') || 0) || a.name.localeCompare(b.name, locale)
}

// Smallest size the title may shrink to (relative to its normal size) before it is cut with an ellipsis
const MIN_FIT_SCALE = 0.78

// Title on the plaque. Normally balanced over at most two lines (hyphenated per the page language); with `fit` (smallest
// shelf size) it is ONE line that shrinks a little to fit the plate, then ends in an ellipsis. The full name is in the
// tooltip and in the detail panel.
function PlaqueName({ name, fit, stamp }: { name: string; fit: boolean; stamp: string }) {
  const ref = useRef<HTMLSpanElement>(null)

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    const apply = () => {
      element.style.fontSize = ''
      if (!fit) return
      const available = element.clientWidth
      const needed = element.scrollWidth
      if (available > 0 && needed > available) {
        element.style.fontSize = `calc(var(--cab-name) * ${Math.max(MIN_FIT_SCALE, available / needed).toFixed(3)})`
      }
    }
    apply()
    const observer = new ResizeObserver(apply)
    observer.observe(element)
    return () => observer.disconnect()
  }, [fit, name])

  return (
    <Tooltip label={name} description={stamp || undefined} side="top" className="pv-cab-tip">
      <span ref={ref} className="pv-cab-plaque-name">
        {name}
      </span>
    </Tooltip>
  )
}

function Cover({ game }: { game: ShowcaseGameDto }) {
  const [failed, setFailed] = useState(false)
  if (!game.coverUrl || failed) {
    return (
      <span className="pv-cab-poster-blank" aria-hidden="true">
        <IconDeviceGamepad2 size="2.2em" />
      </span>
    )
  }
  return <img src={game.coverUrl} alt="" loading="lazy" onError={() => setFailed(true)} />
}

// ---------------------------------------------------------------------------------------------------------------
// One game on the shelf
// ---------------------------------------------------------------------------------------------------------------

// A game that moves into its place when the cabinet opens: 'wait' = its place is still free (a quiet glass card is shown),
// 'land' = the game is set down on the shelf. Undefined = an ordinary game that was there before.
type Intro = 'wait' | 'land' | undefined

// Where the figure comes from and how it settles: it is set down from slightly above with a soft spring; the plaque follows.
// Reduced motion: no movement at all, a calm fade.
function introStage(intro: Intro, reduce: boolean): MotionProps {
  if (!intro) return {}
  if (reduce) return { initial: false, animate: { opacity: intro === 'land' ? 1 : 0 }, transition: { duration: 0.55, ease: EASE } }
  return intro === 'land'
    ? {
        initial: false,
        animate: { opacity: 1, y: 0, scale: 1 },
        transition: {
          y: { type: 'spring', stiffness: 190, damping: 14, mass: 0.9 },
          scale: { type: 'spring', stiffness: 240, damping: 16 },
          opacity: { duration: 0.22 },
        },
      }
    : { initial: false, animate: { opacity: 0, y: '-2em', scale: 0.84 }, transition: { duration: 0 } }
}

function introPlaque(intro: Intro, reduce: boolean): MotionProps {
  if (!intro) return {}
  if (reduce) return { initial: false, animate: { opacity: intro === 'land' ? 1 : 0 }, transition: { duration: 0.55, ease: EASE } }
  return intro === 'land'
    ? { initial: false, animate: { opacity: 1, y: 0 }, transition: { duration: 0.45, delay: 0.42, ease: EASE } }
    : { initial: false, animate: { opacity: 0, y: '0.6em' }, transition: { duration: 0 } }
}

// Four-point star, the same as the sparkle burst of the platinum moment
const LAND_SPARK = 'M4 0L4.9 3.1L8 4L4.9 4.9L4 8L3.1 4.9L0 4L3.1 3.1L4 0Z'
const LAND_SPARKS = [
  { x: '-2.1em', y: '-3.6em', s: 0.7, d: 0 },
  { x: '2.3em', y: '-3.2em', s: 0.55, d: 0.05 },
  { x: '-2.8em', y: '-0.6em', s: 0.5, d: 0.1 },
  { x: '2.9em', y: '-0.9em', s: 0.75, d: 0.03 },
  { x: '-1.2em', y: '1em', s: 0.5, d: 0.12 },
  { x: '1.5em', y: '0.9em', s: 0.6, d: 0.08 },
]

// The moment the game touches the shelf: a warm flash on the glass and a few sparks - drawn over the place, nothing moves
function LandingBurst() {
  return (
    <span className="pv-cab-land" aria-hidden="true">
      <motion.span
        className="pv-cab-land-glow"
        initial={{ opacity: 0, scale: 0.55 }}
        animate={{ opacity: [0, 0.95, 0], scale: [0.55, 1.1, 1.35] }}
        transition={{ duration: 1.05, delay: 0.3, times: [0, 0.3, 1], ease: 'easeOut' }}
      />
      {LAND_SPARKS.map((spark, i) => (
        <motion.svg
          key={i}
          viewBox="0 0 8 8"
          className="pv-cab-land-spark"
          style={{ inlineSize: `${spark.s}em`, blockSize: `${spark.s}em` }}
          initial={{ opacity: 0, scale: 0, x: 0, y: 0 }}
          animate={{ opacity: [0, 1, 0], scale: [0, 1.1, 0.2], x: spark.x, y: spark.y, rotate: 90 }}
          transition={{ duration: 0.9, delay: 0.34 + spark.d, ease: 'easeOut' }}
        >
          <path d={LAND_SPARK} fill="#fde68a" />
        </motion.svg>
      ))}
    </span>
  )
}

// The lamps do not start in lockstep: a few fixed, uneven delays (0 - 0.24 s), one after the other by slot
function lampDelay(index: number): string {
  return `${(((index * 7) % 5) * 0.06).toFixed(2)}s`
}

// LED strip: ONE light per shelf. The shelves come on one after the other from top to bottom (0.1 s apart), but inside a
// shelf the strip and every cover under it get exactly the same delay, so the whole row comes on as one.
function rowDelay(row: number): string {
  return `${(row * 0.1).toFixed(2)}s`
}

// Light timing of one place: uneven lamp delays for the spots, the shelf delay (the same for the whole row) for the LED strip
function slotLightStyle(index: number, row: number): CSSProperties {
  return { '--ld': lampDelay(index), '--rd': rowDelay(row) } as CSSProperties
}

// The continuous LED strip along the top of one shelf (the CSS shows it only in LED mode)
function LedStrip({ row }: { row: number }) {
  return <div className="pv-cab-led" aria-hidden="true" style={{ '--rd': rowDelay(row) } as CSSProperties} />
}

function Slot({
  game,
  selected,
  index,
  row,
  fit,
  onToggle,
  intro,
  isNew = false,
  reduce = false,
}: {
  game: ShowcaseGameDto
  fit: boolean
  selected: boolean
  index: number
  row: number
  onToggle: (appId: number) => void
  intro?: Intro
  isNew?: boolean
  reduce?: boolean
}) {
  const { t } = useTranslation('cabinet')
  const text = useCabinetText()
  const parts = game.platinumAt ? text.stampParts(game.platinumAt) : null
  return (
    <motion.button
      type="button"
      className="pv-cab-slot"
      layout="position"
      data-app-id={game.appId}
      data-new={isNew ? '' : undefined}
      aria-pressed={selected}
      // While its place is still free the game is not there yet: nothing to focus or click
      inert={intro === 'wait'}
      style={slotLightStyle(index, row)}
      onClick={() => onToggle(game.appId)}
    >
      <span className="pv-cab-lamp" aria-hidden="true" />
      <span className="pv-cab-beam" aria-hidden="true" />
      <motion.span className="pv-cab-stage" {...introStage(intro, reduce)}>
        <span className="pv-cab-poster" aria-hidden="true">
          <Cover game={game} />
          {isNew && (
            <motion.span
              className="pv-cab-new"
              initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.5 }}
              animate={{ opacity: 1, scale: 1 }}
              transition={reduce ? { duration: 0.4, delay: 0.3 } : { type: 'spring', stiffness: 320, damping: 16, delay: 0.95 }}
            >
              {t('presented.badge')}
            </motion.span>
          )}
        </span>
        {intro === 'land' && !reduce && <LandingBurst />}
      </motion.span>
      <motion.span className="pv-cab-front" {...introPlaque(intro, reduce)}>
        <span className="pv-cab-plaque">
          <PlaqueName name={game.name} fit={fit} stamp={parts ? t('plaque.stamp', { date: parts[0], time: parts[1] }) : ''} />
          {/* Always rendered so every caption keeps its height; empty when the time is unknown */}
          <span className="pv-cab-plaque-date">
            {parts && (
              <>
                <IconTrophy size="1em" aria-hidden="true" />
                <span>{parts[0]}</span>
                <span className="pv-cab-sep" aria-hidden="true">
                  {' · '}
                </span>
                <span className="pv-cab-time">{parts[1]}</span>
              </>
            )}
          </span>
        </span>
      </motion.span>
    </motion.button>
  )
}

// One cell of the shelf. A game that is about to move in gets a cell with two layers: the free place (it fades out once the game
// has landed) and the game itself on top - so the place is reserved from the first frame and nothing else on the shelf ever shifts.
function SlotCell(props: Parameters<typeof Slot>[0]) {
  const { intro } = props
  if (!intro) return <Slot {...props} />
  return (
    <div className="pv-cab-slot-wrap">
      <motion.div
        className="pv-cab-reserved"
        initial={false}
        animate={{ opacity: intro === 'wait' ? 1 : 0 }}
        transition={{ duration: props.reduce ? 0.4 : 0.7, delay: intro === 'wait' ? 0 : 0.2 }}
      >
        <EmptySlot reserved />
      </motion.div>
      <Slot {...props} />
    </div>
  )
}

// A still empty place on the shelf: a thin outlined placeholder (CSS shows it only in the "show empty places" view, or while a game
// is about to move into it)
function EmptySlot({ reserved = false, index = 0, row = 0 }: { reserved?: boolean; index?: number; row?: number }) {
  return (
    <div
      className={`pv-cab-slot pv-cab-slot--empty${reserved ? ' pv-cab-slot--reserved' : ''}`}
      aria-hidden="true"
      style={slotLightStyle(index, row)}
    >
      {/* A place that is about to receive a game (reserved) has no lamp of its own: the lamp of the game lights it */}
      {!reserved && <span className="pv-cab-lamp pv-cab-ghost" />}
      {!reserved && <span className="pv-cab-beam pv-cab-ghost" />}
      <span className="pv-cab-stage pv-cab-ghost">
        <span className="pv-cab-poster pv-cab-poster--ghost" />
      </span>
      <span className="pv-cab-front pv-cab-ghost">
        {/* The same reserved lines as a real caption, so a placeholder is exactly as big as a filled place */}
        <span className="pv-cab-plaque">
          <span className="pv-cab-plaque-name">&nbsp;</span>
          <span className="pv-cab-plaque-date" />
        </span>
      </span>
    </div>
  )
}

// Discreet line drawing behind the wall: a few faint circles and lines, irregular, never mirrored, never in front of anything
function CabinetDecoration() {
  return (
    <svg className="pv-cab-deco" viewBox="0 0 1200 700" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
      <g>
        <circle cx="1010" cy="120" r="150" />
        <circle cx="1010" cy="120" r="210" />
        <circle cx="1010" cy="120" r="280" />
        <path d="M0 560 L1200 430" />
        <path d="M0 600 L1200 470" />
        <path d="M-20 90 L220 -20" />
        <circle cx="130" cy="640" r="90" />
        <path d="M60 700 L420 520" />
      </g>
    </svg>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// Information card of the selected game (only what is known is shown)
// ---------------------------------------------------------------------------------------------------------------

function InfoCard({ game }: { game: ShowcaseGameDto }) {
  const { t } = useTranslation('cabinet')
  const fmt = useFormat()
  const text = useCabinetText()
  return (
    <motion.div
      key={game.appId}
      style={{ display: 'flex', flex: 1, flexDirection: 'column', gap: '1em' }}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, ease: EASE }}
    >
      <div className="pv-cab-info-top">
        <div className="pv-cab-info-poster" aria-hidden="true">
          <Cover game={game} />
        </div>
        <div style={{ minWidth: 0 }}>
          <h3>{game.name}</h3>
          {game.platinumAt && (
            <p className="pv-cab-info-date">
              <IconCalendarEvent size="1.15em" aria-hidden="true" />
              <span>{t('info.platinumOn', { date: text.date(game.platinumAt) })}</span>
            </p>
          )}
        </div>
      </div>

      <ul className="pv-cab-facts">
        <li className="pv-cab-fact">
          <IconTrophy size="1.2em" aria-hidden="true" />
          <div>
            <div className="pv-cab-fact-label">{t('info.trophies')}</div>
            <div className="pv-cab-fact-value">{t('info.unlocked', { n: fmt.number(game.trophiesTotal) })}</div>
          </div>
        </li>
        {game.daysToPlatinum != null && (
          <li className="pv-cab-fact">
            <IconHourglassHigh size="1.2em" aria-hidden="true" />
            <div>
              <div className="pv-cab-fact-label">{t('info.duration')}</div>
              <div className="pv-cab-fact-value">{text.days(game.daysToPlatinum)}</div>
            </div>
          </li>
        )}
        {game.playtimeHours != null && (
          <li className="pv-cab-fact">
            <IconClock size="1.2em" aria-hidden="true" />
            <div>
              <div className="pv-cab-fact-label">{t('info.playtime')}</div>
              <div className="pv-cab-fact-value">{text.hours(game.playtimeHours)}</div>
            </div>
          </li>
        )}
        {game.averageRarityPercent != null && (
          <li className="pv-cab-fact">
            <IconUsers size="1.2em" aria-hidden="true" />
            <div>
              <div className="pv-cab-fact-label">{t('info.averageRarity')}</div>
              <div className="pv-cab-fact-value">{t('info.ofPlayers', { percent: text.percent(game.averageRarityPercent) })}</div>
            </div>
          </li>
        )}
        {game.rarestName && (
          <li className="pv-cab-fact">
            <IconDiamond size="1.2em" aria-hidden="true" />
            <div style={{ minWidth: 0 }}>
              <div className="pv-cab-fact-label">{t('info.rarest')}</div>
              <div className="pv-cab-rarest">
                <span className="pv-cab-rarest-icon" aria-hidden="true">
                  {game.rarestIconUrl ? <img src={game.rarestIconUrl} alt="" loading="lazy" /> : <IconTrophy size="1.4em" />}
                </span>
                <div style={{ minWidth: 0 }}>
                  <div className="pv-cab-fact-value">{game.rarestName}</div>
                  {game.rarestPercent != null && (
                    <div className="pv-cab-fact-sub">{t('info.ofPlayers', { percent: text.percent(game.rarestPercent) })}</div>
                  )}
                </div>
              </div>
            </div>
          </li>
        )}
      </ul>

      {game.existingSlug && (
        <Link to={`/games/${game.existingSlug}`} className="pv-btn pv-btn-primary pv-cab-link">
          <span>{t('info.toGame')}</span>
          <IconArrowRight size="1.1em" aria-hidden="true" />
        </Link>
      )}
    </motion.div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// The cabinet
// ---------------------------------------------------------------------------------------------------------------

function CabinetDialog() {
  const { t } = useTranslation('cabinet')
  const { t: tc } = useTranslation('common')
  const fmt = useFormat()
  const text = useCabinetText()
  const reduce = useReducedMotion() ?? false
  const portrait = usePortrait()
  const dialogRef = useRef<HTMLElement>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const selectedRef = useRef<number | null>(null)
  const [selectedId, setSelectedId] = useState<number | null>(null)
  // How many places of the chosen size are visible side by side (more games than that: the wall scrolls sideways)
  const [perShelf, setPerShelf] = useState(5)
  // Sideways scrolling of the wall: is there more to the left / right (arrows, edge fades)
  const [edges, setEdges] = useState<WallEdges>({ overflow: false, canPrev: false, canNext: false })
  const edgeFrameRef = useRef<number | null>(null)
  // How many shelves of the chosen size fit into the height of the case (the cabinet is always filled to the bottom)
  const [fitRows, setFitRows] = useState(MIN_SHELVES)
  const [size, setSize] = useState(loadSize)
  const [lighting, setLighting] = useState<Lighting>(loadLighting)
  const [showEmpty, setShowEmpty] = useState(loadShowEmpty)
  // The doors: closed -> opening -> open -> closing -> closed. The button (or a click on the glass) only changes what is
  // WANTED; the machine moves one step at a time and never reverses an animation halfway, so rapid clicks cannot break it.
  // The cabinet starts CLOSED and dark; the doors only open once the data is in and the covers are decoded
  const [phase, setPhase] = useState<DoorPhase>('closed')
  const [wantOpen, setWantOpen] = useState(true)
  // The lights: off while the doors are closed or opening, flickering on once they are open (spots: two short flashes, under 3
  // in any second; LED: one gentle rise), then steady. Reduced motion: no flicker, the lights just fade.
  const [lights, setLights] = useState<'off' | 'flicker' | 'on'>('off')
  // The wall is settled a moment after it shows: only then do new games move into their places
  const [settled, setSettled] = useState(false)

  const queryClient = useQueryClient()
  // A little fresher than for the prefetch: a game finished a minute ago should be in the cabinet when it is opened
  const { data, error, refetch, isFetching } = useQuery({ ...showcaseQueryOptions, staleTime: 15_000 })
  // Which games were presented before (single user: the same on the Windows window and in the browser)
  const presentedQuery = useQuery({ queryKey: PRESENTED_KEY, queryFn: fetchCabinetPresented, staleTime: 0, gcTime: 0, retry: 1 })
  const presentedState = presentedQuery.data
  const presentedPending = presentedQuery.isPending

  // Data loaded AND the first covers decoded (a slow cover waits at most 1.5 s): then the opening may start
  const [ready, setReady] = useState(false)
  useEffect(() => {
    // The cabinet opens only when it knows which places are still free (no flash of a game that is about to move in)
    if (ready || !data || presentedPending) return
    let cancelled = false
    void preloadCovers([...data.games].sort(byPlatinumDesc(fmt.locale))).then(() => {
      if (!cancelled) setReady(true)
    })
    return () => {
      cancelled = true
    }
  }, [data, ready, fmt.locale, presentedPending])
  // The wall is built in several steps while it is still hidden (first without games, then with all of them), and the browser may
  // re-snap the scroll position on the way: it always shows from its start
  useLayoutEffect(() => {
    if (ready && scrollRef.current) scrollRef.current.scrollLeft = 0
  }, [ready])
  // A calm loader, but only if it takes a moment: an instant opening (second time, after the prefetch) never shows it
  const [showLoader, setShowLoader] = useState(false)
  useEffect(() => {
    if (ready || error) {
      setShowLoader(false)
      return
    }
    const timer = window.setTimeout(() => setShowLoader(true), 180)
    return () => window.clearTimeout(timer)
  }, [ready, error])

  useEffect(() => {
    selectedRef.current = selectedId
  }, [selectedId])

  // Closes the panel and hands the focus back to the game it belonged to
  const closePanel = useCallback(() => {
    const id = selectedRef.current
    setSelectedId(null)
    if (id !== null) {
      requestAnimationFrame(() => dialogRef.current?.querySelector<HTMLElement>(`[data-app-id="${id}"]`)?.focus())
    }
  }, [])

  // closed + wanted open -> opening; open + wanted closed -> the panel and the lights go first, then the doors slide shut
  useEffect(() => {
    if (phase === 'closed' && wantOpen && ready) {
      setPhase(reduce ? 'open' : 'opening')
    } else if (phase === 'open' && !wantOpen) {
      setSelectedId(null)
      setLights('off')
      setPhase(reduce ? 'closed' : 'closing')
    }
  }, [phase, wantOpen, reduce, ready])

  // open and wanted -> the lights come on (flicker; reduced motion: a plain fade)
  useEffect(() => {
    if (phase !== 'open' || !wantOpen || lights !== 'off') return
    if (reduce) {
      const frame = requestAnimationFrame(() => setLights('on'))
      return () => cancelAnimationFrame(frame)
    }
    setLights('flicker')
  }, [phase, wantOpen, lights, reduce])

  useEffect(() => {
    if (lights !== 'flicker') return
    const timer = window.setTimeout(() => setLights('on'), 1300)
    return () => window.clearTimeout(timer)
  }, [lights])

  const onDoorAnimationEnd = (e: AnimationEvent<HTMLDivElement>) => {
    if (e.animationName === 'pv-door-r') setPhase('open')
    else if (e.animationName === 'pv-door-r-close') setPhase('closed')
  }
  // A click while it is still opening jumps to the finished, lit state; a click on the closed glass opens the doors
  const skipOpening = () => {
    setPhase('open')
    setLights('on')
    setWantOpen(true)
  }
  const onCasePointerDown = () => {
    if (phase === 'closed') setWantOpen(true)
    else if (phase === 'opening' || lights === 'flicker') skipOpening()
  }

  // Data loaded and covers decoded: the wall shows (fades in), a moment later it is settled and new games may move in
  useEffect(() => {
    if (!ready || settled) return
    if (reduce) {
      const frame = requestAnimationFrame(() => setSettled(true))
      return () => cancelAnimationFrame(frame)
    }
    const timer = window.setTimeout(() => setSettled(true), 420)
    return () => window.clearTimeout(timer)
  }, [ready, settled, reduce])

  // Esc closes the details panel (the cabinet itself is a page, left through the navigation)
  useEffect(() => {
    const onKeyDown = (e: globalThis.KeyboardEvent) => {
      if (e.key !== 'Escape' || selectedRef.current === null) return
      closePanel()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [closePanel])

  // The wall's arrows and edge fades follow what the scroller shows (read in the next frame, once per frame at most)
  const updateEdges = useCallback(() => {
    if (edgeFrameRef.current !== null) return
    edgeFrameRef.current = requestAnimationFrame(() => {
      edgeFrameRef.current = null
      const element = scrollRef.current
      if (!element) return
      const next = wallEdges(element.scrollLeft, element.clientWidth, element.scrollWidth)
      setEdges((current) => (sameEdges(current, next) ? current : next))
    })
  }, [])
  useEffect(
    () => () => {
      if (edgeFrameRef.current !== null) cancelAnimationFrame(edgeFrameRef.current)
      edgeFrameRef.current = null
    },
    []
  )

  // How many places fit next to each other follows the width of the case, how many shelves the height; everything beyond
  // that is reached by scrolling the wall sideways
  useLayoutEffect(() => {
    const element = scrollRef.current
    if (!element) return
    const measure = () => {
      const style = getComputedStyle(element)
      const fontPx = parseFloat(style.fontSize) || 16
      // The slot width of the chosen size comes from the stylesheet (--cab-slot, in em)
      const slotEm = parseFloat(style.getPropertyValue('--cab-slot')) || 7.2
      const widthEm = element.clientWidth / fontPx
      // One shelf = space above + poster (2:3) + caption, the same sum the stylesheet uses (--cab-shelf-min)
      const posterEm = parseFloat(style.getPropertyValue('--cab-poster')) || 5.4
      const frontEm = parseFloat(style.getPropertyValue('--cab-front')) || 4.4
      const topEm = parseFloat(style.getPropertyValue('--cab-shelf-top')) || 1.2
      const shelfPx = (posterEm * 1.5 + topEm + frontEm) * fontPx
      setFitRows(Math.max(1, Math.floor(element.clientHeight / shelfPx)))
      setPerShelf(Math.min(MAX_PER_SHELF, Math.max(3, Math.floor(widthEm / slotEm))))
      updateEdges()
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [size, updateEdges])

  const games = useMemo(() => [...(data?.games ?? [])].sort(byPlatinumDesc(fmt.locale)), [data, fmt.locale])
  // ---- Presenting new games ----------------------------------------------------------------------------------------------
  // Games at 100 % that the cabinet has not shown yet move into their (reserved) places one after another once the lights are
  // on. A game counts as presented only after its landing really played; leaving early keeps what landed. With no stored state
  // the whole current collection becomes the baseline silently (see presentation.ts).
  const pending = useMemo(() => pendingGames(games, presentedState), [games, presentedState])
  const pendingIds = useMemo(() => pending.map((game) => game.appId), [pending])
  const pendingKey = pendingIds.join(',')
  const [started, setStarted] = useState<ReadonlySet<number>>(() => new Set())
  const [announcement, setAnnouncement] = useState('')
  const startedRef = useRef(new Set<number>())
  const queueRef = useRef<number[]>([])
  const queuedRef = useRef(new Set<number>())
  const unsavedRef = useRef<number[]>([])
  const savingRef = useRef(false)
  const stepTimerRef = useRef<number | null>(null)
  const landTimersRef = useRef<number[]>([])
  const scheduleRef = useRef<LandingSchedule | null>(null)
  const canRunRef = useRef(false)
  const baselineRef = useRef(false)
  const namesRef = useRef(new Map<number, string>())
  const announcedRef = useRef(false)

  const autoScrollRef = useRef<number | null>(null)
  // Scrolls the wall so the place of a game is completely in view; returns how long to wait before it can land (0 = it is in view)
  const revealPlace = useCallback(
    (appId: number): number => {
      const element = scrollRef.current
      const slot = element?.querySelector<HTMLElement>(`.pv-cab-slot[data-app-id="${appId}"]`)
      if (!element || !slot) return 0
      if (autoScrollRef.current !== null) {
        if (Math.abs(element.scrollLeft - autoScrollRef.current) > 2) return 150 // still on its way
        autoScrollRef.current = null
      }
      const box = element.getBoundingClientRect()
      const item = (slot.closest('.pv-cab-slot-wrap') ?? slot).getBoundingClientRect()
      if (isFullyVisible(item, box)) return 0
      const left = revealScrollTarget(item.left - box.left + element.scrollLeft, element.clientWidth, element.scrollWidth, perShelfRef.current)
      autoScrollRef.current = left
      element.scrollTo({ left, behavior: reduce ? 'auto' : 'smooth' })
      return reduce ? 60 : 520
    },
    [reduce]
  )

  const flushPresented = useCallback(() => {
    if (savingRef.current || unsavedRef.current.length === 0) return
    const ids = unsavedRef.current.splice(0)
    savingRef.current = true
    let failed = false
    addCabinetPresented(ids)
      .then((result) => queryClient.setQueryData(PRESENTED_KEY, result))
      .catch(() => {
        // Best effort: the games stay in the list and go along with the next save (or are shown again next time)
        failed = true
        unsavedRef.current.push(...ids)
      })
      .finally(() => {
        savingRef.current = false
        // Games that landed while this request was on its way (not after a failure: no retry loop)
        if (!failed && unsavedRef.current.length > 0) flushRef.current()
      })
  }, [queryClient])
  const flushRef = useRef<() => void>(() => {})
  useEffect(() => {
    flushRef.current = flushPresented
  }, [flushPresented])

  // First use: no stored state yet -> everything that is in the cabinet now counts as presented, nothing is animated
  useEffect(() => {
    if (!data || baselineRef.current || !needsBaseline(presentedState)) return
    baselineRef.current = true
    addCabinetPresented(data.games.map((game) => game.appId))
      .then((result) => queryClient.setQueryData(PRESENTED_KEY, result))
      .catch(() => {
        // Not saved: the next opening tries again; until then nothing is animated (no state = nothing pending)
      })
  }, [data, presentedState, queryClient])

  useEffect(() => {
    for (const game of pending) namesRef.current.set(game.appId, game.name)
    for (const id of pendingIds) {
      if (!queuedRef.current.has(id)) {
        queuedRef.current.add(id)
        queueRef.current.push(id)
      }
    }
    canRunRef.current = settled && lights === 'on' && wantOpen
    if (!canRunRef.current || stepTimerRef.current !== null || queueRef.current.length === 0) return

    if (!scheduleRef.current) {
      const queued = queueRef.current
      scheduleRef.current = landingSchedule(queued.length, reduce)
      if (!announcedRef.current) {
        announcedRef.current = true
        const names = announcedNames(queued.map((id) => namesRef.current.get(id) ?? ''))
        setAnnouncement(t('presented.announce', { count: queued.length, names: new Intl.ListFormat(fmt.locale, { style: 'long', type: 'conjunction' }).format(names) }))
      }
    }
    const schedule = scheduleRef.current
    const step = () => {
      stepTimerRef.current = null
      if (!canRunRef.current) return // left the page or the doors are closing: the rest waits for the next time the cabinet is lit
      const nextId = queueRef.current[0]
      if (nextId === undefined) {
        scheduleRef.current = null
        return
      }
      // A place that is scrolled out of sight: bring it into view first (one column of context before it), then let the game land
      const wait = revealPlace(nextId)
      if (wait > 0) {
        stepTimerRef.current = window.setTimeout(step, wait)
        return
      }
      const id = queueRef.current.shift()
      if (id === undefined) return
      startedRef.current.add(id)
      setStarted(new Set(startedRef.current))
      landTimersRef.current.push(
        window.setTimeout(() => {
          unsavedRef.current.push(id)
          flushPresented()
        }, schedule.landMs)
      )
      stepTimerRef.current = window.setTimeout(step, schedule.stepMs)
    }
    stepTimerRef.current = window.setTimeout(step, schedule.firstDelayMs)
  }, [pendingKey, settled, lights, wantOpen, reduce, pending, pendingIds, flushPresented, revealPlace, fmt.locale, t])

  // Leaving the cabinet: no further game starts; what already landed is saved, what was still on its way is shown again next time
  useEffect(
    () => () => {
      if (stepTimerRef.current !== null) window.clearTimeout(stepTimerRef.current)
      stepTimerRef.current = null
      for (const timer of landTimersRef.current) window.clearTimeout(timer)
      landTimersRef.current = []
      flushPresented()
    },
    [flushPresented]
  )

  // One flat grid (a game keeps its element and its lamp while the size changes): as many shelves as fit the height, as many columns
  // as the games need; more columns than fit the width = the whole wall scrolls sideways
  const wall = useMemo(() => wallLayout(games.length, perShelf, fitRows), [games.length, perShelf, fitRows])
  const gridRows = wall.rows
  const columns = wall.columns
  const perShelfRef = useRef(perShelf)
  useEffect(() => {
    perShelfRef.current = perShelf
  }, [perShelf])
  // The wall got longer or shorter, or the size changed: arrows and fades follow
  useLayoutEffect(() => {
    updateEdges()
  }, [columns, perShelf, size, gridRows, ready, updateEdges])

  const scrollPage = useCallback(
    (direction: -1 | 1) => {
      const element = scrollRef.current
      if (!element) return
      const left = pageScrollTarget(element.scrollLeft, element.clientWidth, element.scrollWidth, perShelfRef.current, direction)
      element.scrollTo({ left, behavior: reduce ? 'auto' : 'smooth' })
    },
    [reduce]
  )

  // The mouse wheel over the shelves moves the wall sideways (a trackpad swipe sideways works natively). Snapping is paused
  // while the wheel turns and settles on a column afterwards; vertical scrolling is left alone when the shelves themselves
  // need it (a very low window)
  useEffect(() => {
    const element = scrollRef.current
    if (!element) return
    let timer: number | null = null
    const onWheel = (e: WheelEvent) => {
      if (e.ctrlKey || e.deltaX !== 0 && Math.abs(e.deltaX) >= Math.abs(e.deltaY)) return
      if (element.scrollHeight > element.clientHeight + 1 || element.scrollWidth <= element.clientWidth + 1) return
      const max = element.scrollWidth - element.clientWidth
      if ((e.deltaY < 0 && element.scrollLeft <= 0) || (e.deltaY > 0 && element.scrollLeft >= max - 1)) return
      e.preventDefault()
      const unit = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? element.clientWidth : 1
      element.style.scrollSnapType = 'none'
      element.scrollLeft += e.deltaY * unit
      if (timer !== null) window.clearTimeout(timer)
      timer = window.setTimeout(() => {
        element.style.scrollSnapType = ''
        timer = null
      }, 140)
    }
    element.addEventListener('wheel', onWheel, { passive: false })
    return () => {
      element.removeEventListener('wheel', onWheel)
      if (timer !== null) window.clearTimeout(timer)
      element.style.scrollSnapType = ''
    }
  }, [])

  // Left / right arrow keys move from game to game (the browser scrolls a focused game into view by itself)
  const onWallKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
    const target = e.target as HTMLElement
    if (!target.matches('.pv-cab-slot[data-app-id]')) return
    const slots = [...e.currentTarget.querySelectorAll<HTMLElement>('.pv-cab-slot[data-app-id]:not([inert])')]
    const next = slots[slots.indexOf(target) + (e.key === 'ArrowRight' ? 1 : -1)]
    if (!next) return
    e.preventDefault()
    next.focus()
  }

  // Nothing is shown up front: the panel exists only after a game was clicked (clicking it again closes it)
  const selected = games.find((g) => g.appId === selectedId) ?? null
  const toggle = useCallback((appId: number) => setSelectedId((current) => (current === appId ? null : appId)), [])

  const gameCount = games.length
  // A game moves in (wait -> land) once its turn has come; every other game is simply there
  const pendingSet = useMemo(() => new Set(pendingIds), [pendingIds])
  const introOf = (appId: number): 'wait' | 'land' | undefined => (started.has(appId) ? 'land' : pendingSet.has(appId) ? 'wait' : undefined)

  return (
    <section
      ref={dialogRef}
      className="pv-cab"
      aria-labelledby="pv-cab-title"
      data-size={size}
      data-lighting={lighting}
      data-empty={showEmpty ? 'on' : 'off'}
      data-ready={ready ? 'on' : 'off'}
      data-lights={lights}
      data-doors={phase}
    >
      <CabinetDecoration />
      {/* One polite announcement of what is new in the cabinet (the places themselves are silent while they fill) */}
      <p className="sr-only" role="status" aria-live="polite">
        {announcement}
      </p>
      <header className="pv-cab-head">
        <div className="pv-cab-title">
          <h1 id="pv-cab-title">{t('title')}</h1>
          <p>{t('subtitle')}</p>
        </div>

        {data && gameCount > 0 && (
          <div className="pv-cab-stats">
            <Tooltip label={t('stats.games', { count: gameCount })} description={t('stats.gamesHint')} side="bottom">
              <span className="pv-cab-stat" tabIndex={0} aria-label={t('stats.games', { count: gameCount })}>
                <b>{fmt.number(gameCount)}</b>
                <span>{t('figure.games', { count: gameCount })}</span>
              </span>
            </Tooltip>
            <Tooltip label={t('stats.trophies', { count: data.totalTrophies })} description={t('stats.trophiesHint')} side="bottom">
              <span className="pv-cab-stat" tabIndex={0} aria-label={t('stats.trophiesTotal', { count: data.totalTrophies })}>
                <b>{fmt.number(data.totalTrophies)}</b>
                <span>{t('figure.trophies', { count: data.totalTrophies })}</span>
              </span>
            </Tooltip>
            {data.totalPlaytimeHours != null && (
              <Tooltip label={t('stats.playtime', { time: text.hours(data.totalPlaytimeHours) })} description={t('stats.playtimeHint')} side="bottom">
                <span className="pv-cab-stat" tabIndex={0} aria-label={t('stats.playtimeTotal', { time: text.hours(data.totalPlaytimeHours) })}>
                  <b>{text.hours(data.totalPlaytimeHours)}</b>
                  <span>{t('figure.playtime')}</span>
                </span>
              </Tooltip>
            )}
          </div>
        )}

        <div className="pv-cab-tools">
          <Tooltip label={t('size.label')} description={t('size.description')} side="bottom" className="pv-cab-size-wrap">
            <span className="pv-cab-size">
              <IconZoomOut size={18} aria-hidden="true" />
              <input
                type="range"
                min={0}
                max={4}
                step={1}
                value={size}
                onChange={(e) => {
                  const next = Number(e.target.value)
                  setSize(next)
                  saveSize(next)
                }}
                aria-label={t('size.label')}
                aria-valuetext={t(`size.step.${SIZE_STEPS[size] ?? 'small'}`)}
                className="pv-range"
              />
              <IconZoomIn size={18} aria-hidden="true" />
            </span>
          </Tooltip>

          {/* One connected bar: the lighting of the cabinet (remembered per browser) */}
          <div className="pv-segmented pv-cab-lighting" role="group" aria-label={t('view.lighting')}>
            {LIGHTING_MODES.map((mode) => (
              <Tooltip key={mode} label={t(`view.${mode}.name`)} description={t(`view.${mode}.hint`)} side="bottom" className="pv-seg-tip">
                <button
                  type="button"
                  aria-pressed={lighting === mode}
                  onClick={() => {
                    setLighting(mode)
                    saveLighting(mode)
                  }}
                >
                  {t(`view.${mode}.name`)}
                </button>
              </Tooltip>
            ))}
          </div>

          <Tooltip label={t('view.showEmpty.name')} description={t('view.showEmpty.hint')} side="bottom" className="pv-cab-empties-wrap">
            <Switch
              checked={showEmpty}
              onChange={(next) => {
                setShowEmpty(next)
                saveShowEmpty(next)
              }}
              surface="dark"
              className="pv-cab-empties"
            >
              {t('view.showEmpty.name')}
            </Switch>
          </Tooltip>

          {/* The glass doors: pressed = closed. The name stays the same, the tooltip says what the next press does. */}
          <Tooltip
            label={wantOpen ? t('doors.close.label') : t('doors.open.label')}
            description={wantOpen ? t('doors.close.description') : t('doors.open.description')}
            side="bottom"
            className="pv-cab-doors-wrap"
          >
            <button
              type="button"
              className={`${iconButtonClass()} pv-cab-doors`}
              aria-label={t('doors.closedState')}
              aria-pressed={!wantOpen}
              onClick={() => setWantOpen((current) => !current)}
            >
              {wantOpen ? <IconDoor size={22} aria-hidden="true" /> : <IconDoorEnter size={22} aria-hidden="true" />}
            </button>
          </Tooltip>
        </div>
      </header>

      <div className="pv-cab-body">
        <div className="pv-cab-case" onPointerDownCapture={onCasePointerDown}>
          <div
            ref={scrollRef}
            className="pv-cab-scroll"
            tabIndex={-1}
            inert={!ready || phase === 'closed' || phase === 'closing'}
            onScroll={updateEdges}
            onKeyDown={onWallKeyDown}
          >
            <div className="pv-cab-shelves" style={{ '--rows': gridRows, '--cols': columns, '--visible': perShelf } as CSSProperties}>
              <div className="pv-cab-boards" aria-hidden="true">
                {Array.from({ length: gridRows }, (_, r) => (
                  <div key={r} className="pv-cab-shelf">
                    <LedStrip row={r} />
                    <div className="pv-cab-board" />
                  </div>
                ))}
              </div>
              <div className="pv-cab-grid">
                {games.map((game, index) => (
                  <SlotCell
                    key={game.appId}
                    game={game}
                    index={index}
                    row={Math.floor(index / columns)}
                    fit={size === 0}
                    selected={selected?.appId === game.appId}
                    onToggle={toggle}
                    intro={introOf(game.appId)}
                    isNew={started.has(game.appId)}
                    reduce={reduce}
                  />
                ))}
                {Array.from({ length: Math.max(0, gridRows * columns - games.length) }, (_, k) => (
                  <EmptySlot key={`empty-${k}`} index={games.length + k} row={Math.floor((games.length + k) / columns)} />
                ))}
              </div>
            </div>
          </div>

          {/* More games than places: soft edge fades and arrows (the wheel, a swipe and the arrow keys scroll the wall as well). They are
              always in the DOM and only change their look, so nothing ever jumps. The wall snaps to whole columns, so the fades never
              cut a place in half - they only hint that there is more. */}
          <div className="pv-cab-edge pv-cab-edge--start" data-on={edges.canPrev ? '' : undefined} aria-hidden="true" />
          <div className="pv-cab-edge pv-cab-edge--end" data-on={edges.canNext ? '' : undefined} aria-hidden="true" />
          {(['prev', 'next'] as const).map((side) => (
            <Tooltip key={side} label={t(`scroll.${side}`)} side="top" className={`pv-cab-arrow-wrap pv-cab-arrow-wrap--${side}`}>
              <button
                type="button"
                className={`pv-cab-arrow pv-cab-arrow--${side}`}
                aria-label={t(`scroll.${side}`)}
                aria-disabled={!(side === 'prev' ? edges.canPrev : edges.canNext)}
                data-overflow={edges.overflow ? '' : undefined}
                inert={!ready || !edges.overflow || phase === 'closed' || phase === 'closing'}
                onClick={() => {
                  if (side === 'prev' ? edges.canPrev : edges.canNext) scrollPage(side === 'prev' ? -1 : 1)
                }}
              >
                {side === 'prev' ? <IconChevronLeft size={22} aria-hidden="true" /> : <IconChevronRight size={22} aria-hidden="true" />}
              </button>
            </Tooltip>
          ))}

          {/* A quiet line while it loads, but only if it takes a moment (no layout) */}
          {showLoader && (
            <div className="pv-cab-loader" role="status">
              <span className="pv-cab-loader-line" aria-hidden="true" />
              <span className="pv-cab-loader-text">{t('loading')}</span>
            </div>
          )}

          {error && !data && (
            <div className="pv-cab-empty">
              <div className="pv-cab-empty-card" role="alert">
                <IconAlertTriangle size={28} style={{ color: 'var(--accent-crimson)' }} aria-hidden="true" />
                <h2>{t('error.title')}</h2>
                <p>{error instanceof Error ? error.message : t('error.unknown')}</p>
                <CopyErrorButton
                  color="var(--text-secondary)"
                  report={{ title: t('error.title'), message: error instanceof Error ? error.message : t('error.unknown'), code: errorCodeOf(error) }}
                />
                <button type="button" className="pv-btn" onClick={() => void refetch()} disabled={isFetching}>
                  {tc('retry')}
                </button>
              </div>
            </div>
          )}

          {data && gameCount === 0 && (
            <div className="pv-cab-empty">
              <div className="pv-cab-empty-card">
                <TierTrophy tier="Platinum" calm className="pv-cab-empty-trophy" />
                <h2>{t('empty.title')}</h2>
                <p>{t('empty.text')}</p>
              </div>
            </div>
          )}

          {/* Two glass doors that slide sideways in the case (CSS animations by state, one per pane; nothing else moves them) */}
          <div className="pv-cab-door pv-cab-door--l" aria-hidden="true" />
          <div className="pv-cab-door pv-cab-door--r" aria-hidden="true" onAnimationEnd={onDoorAnimationEnd} />
        </div>

        {/* The details come in over the case only after a game was clicked; from the right, or from below on portrait */}
        <AnimatePresence>
          {selected && (
            <motion.aside
              key="panel"
              className={portrait ? 'pv-cab-info pv-cab-info--sheet' : 'pv-cab-info'}
              aria-label={t('info.panel', { name: selected.name })}
              tabIndex={-1}
              initial={reduce ? { opacity: 0 } : portrait ? { opacity: 0, y: '100%' } : { opacity: 0, x: '100%' }}
              animate={{ opacity: 1, x: 0, y: 0 }}
              exit={reduce ? { opacity: 0 } : portrait ? { opacity: 0, y: '100%' } : { opacity: 0, x: '100%' }}
              transition={{ duration: reduce ? 0.12 : 0.38, ease: EASE }}
            >
              <IconButton label={t('info.close')} className="pv-cab-info-close" onClick={closePanel} autoFocus>
                <IconX size={20} aria-hidden="true" />
              </IconButton>
              <InfoCard game={selected} />
            </motion.aside>
          )}
        </AnimatePresence>
      </div>
    </section>
  )
}

/**
 * The trophy cabinet: the page /cabinet, a fixed dark display surface with every game at 100 %, the shelf wall scrolling sideways.
 * Reached from the tree navigation (the shell prefetches its data on hover).
 */
export function TrophyCabinet() {
  return <CabinetDialog />
}
