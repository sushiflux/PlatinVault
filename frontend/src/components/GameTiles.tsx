import { useMemo, useState, useSyncExternalStore, type CSSProperties, type ReactNode } from 'react'
import { useReducedMotion } from 'motion/react'
import { Link } from 'react-router'
import { useSortable } from '@dnd-kit/sortable'
import { useTranslation } from 'react-i18next'
import {
  IconCalendar,
  IconClock,
  IconDatabase,
  IconDeviceGamepad2,
  IconHistory,
  IconLoader2,
  IconPlayerPlay,
  IconStar,
  IconStarFilled,
  IconThumbUp,
  IconTrophy,
} from '@tabler/icons-react'
import type { Icon } from '@tabler/icons-react'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import { fixedT } from '../i18n'
import { useFormat } from '../i18n/format'
import { sizeOf, type SizeSource } from '../utils/dashboardSort'
import { useLiveCount } from '../utils/useTwitchLiveCounts'
import { GuideBadge } from './GameCardParts'
import { isPlatinumGame, PLATINUM_CARD_FRAME, PlatinumChip, PlatinumEdge } from './platinum/platinum'
import { PlayButton } from './launch/PlayButton'
import type { LaunchTarget } from './launch/LaunchProvider'
import { ClampedText } from './ui/ClampedText'
import { LangStack } from './ui/LangStack'
import { ProgressBar } from './ui/ProgressBar'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'
import { TruncatedText } from './ui/TruncatedText'
import { TwitchButton } from './TwitchButton'

// A game as the cards see it: the server will add Steam's store genres (in the UI language) as steamGenres; until the field is part
// of GameSummaryDto it is declared here, optional - the cards show nothing for a missing field.
export type CardGame = GameSummaryDto & { steamGenres?: string[] }

/**
 * The genres of a game for the card's genre line and for the dashboard search.
 * Steam's store genres (steamGenres) come first. Fallback until the server sends them: the AI's tag list (genreTags, comma
 * separated), without its "100% Guide" tag, which is no genre.
 */
export function gameGenres(game: CardGame): string[] {
  const steam = (game.steamGenres ?? []).map((genre) => genre.trim()).filter(Boolean)
  if (steam.length > 0) return steam
  return (game.genreTags ?? '')
    .split(',')
    .map((genre) => genre.trim())
    .filter((genre) => genre !== '' && !/100\s*%|guide/i.test(genre))
}

// The calm genre line under a title: one line in the teal accent (--accent-emerald: 5.5:1 on the light card, 9.5:1 on the dark one; on the
// always-dark cover overlay of the smallest size the fixed light variant #6ee7b7), cut off with our tooltip when it does not fit. Its height is ALWAYS
// reserved (an empty line when a game has no genres), so cards in a row never differ in height because of it.
function GenreLine({ game, className, style }: { game: CardGame; className: string; style?: CSSProperties }) {
  const genres = gameGenres(game).join(' · ')
  return (
    <div data-genre-line className={`flex min-w-0 items-center ${className}`} style={style}>
      {genres ? <TruncatedText text={genres} wrapperClassName="flex min-w-0 flex-1" /> : <span aria-hidden="true">&nbsp;</span>}
    </div>
  )
}

// Tile size on the dashboard, chosen with a slider: 4 = large cards (default) ... 0 = text only
export type TileDensity = 0 | 1 | 2 | 3 | 4

// The key of each size's name in the "dashboard" namespace (tiles.size.<key>)
const TILE_DENSITY_KEYS = { 0: 'textOnly', 1: 'list', 2: 'veryCompact', 3: 'compact', 4: 'large' } as const satisfies Record<TileDensity, string>

// The segments of a card's action bar (Play, Favourite, Twitch; and of the small group at the end of a list row): icon + short label where the segment is wide
// enough, icon only otherwise (.pv-bar-seg in index.css). Each one is a tooltip wrapper that is the cell of the bar; the buttons are never
// inside a card link.
function FavoriteSegment({ label, favorite, busy = false, hint = false, onClick }: { label: string; favorite: boolean; busy?: boolean; hint?: boolean; onClick: () => void }) {
  const { t } = useTranslation('dashboard')
  return (
    <Tooltip label={label} className="pv-bar-cell">
      <button type="button" onClick={onClick} aria-pressed={favorite} aria-label={label} data-hint={hint ? 'dashboard-star' : undefined} className="pv-bar-seg pv-bar-seg--star">
        {busy ? <IconLoader2 size={14} className="shrink-0 animate-spin" /> : favorite ? <IconStarFilled size={14} className="shrink-0" /> : <IconStar size={14} className="shrink-0" />}
        <span className="pv-bar-label">{t('card.favoriteShort')}</span>
      </button>
    </Tooltip>
  )
}

function TwitchSegment({ name, appId }: { name: string; appId?: number | null }) {
  const { t } = useTranslation('dashboard')
  const liveCount = useLiveCount(appId)
  return <TwitchButton name={name} size={14} className="pv-bar-seg" wrapperClassName="pv-bar-cell" labelText={t('card.twitchShort')} liveCount={liveCount} />
}

// True from the given window width on (the same breakpoints as the Tailwind classes of the grids)
function useMinWidth(px: number): boolean {
  const query = `(min-width: ${px}px)`
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

const DENSITY_KEY = 'pv_dashboard_density'

export function loadTileDensity(): TileDensity {
  try {
    const value = Number(localStorage.getItem(DENSITY_KEY))
    return value in TILE_DENSITY_KEYS && localStorage.getItem(DENSITY_KEY) !== null ? (value as TileDensity) : 4
  } catch {
    return 4
  }
}

export function saveTileDensity(value: TileDensity) {
  try {
    localStorage.setItem(DENSITY_KEY, String(value))
  } catch {
    // Storage unavailable (private mode) - the size just isn't remembered
  }
}

// Container layout per size: card grids for 2-4, a list for 0-1 (two columns on wide screens)
// Up to the 2xl breakpoint (1536 px) the column counts are fixed per window width. From there on the grid fills the page
// width with as many columns as fit (auto-fill, minimum width in rem so it also follows the UI size): a wider page gets more
// columns instead of stretched cards. The minimums reproduce the old counts at the old page width (1520 px): 4 / 6 / 8 / 2 / 3.
export const TILE_CONTAINER: Record<TileDensity, string> = {
  4: 'grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-[repeat(auto-fill,minmax(20rem,1fr))]',
  3: 'grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-[repeat(auto-fill,minmax(13.5rem,1fr))]',
  2: 'grid grid-cols-3 gap-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 xl:grid-cols-8 2xl:grid-cols-[repeat(auto-fill,minmax(10rem,1fr))]',
  1: 'grid grid-cols-1 gap-2 lg:grid-cols-2 2xl:grid-cols-[repeat(auto-fill,minmax(40rem,1fr))]',
  0: 'grid grid-cols-1 gap-1.5 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-[repeat(auto-fill,minmax(25rem,1fr))]',
}

interface TileSizeSliderProps {
  value: TileDensity
  onChange: (value: TileDensity) => void
  // The sticky bar shows a second slider: its own id, and the visible "Tile size" caption becomes an aria-label
  compact?: boolean
}

export function TileSizeSlider({ value, onChange, compact = false }: TileSizeSliderProps) {
  const { t } = useTranslation('dashboard')
  const label = t(`tiles.size.${TILE_DENSITY_KEYS[value]}`)
  const id = compact ? 'dashboard-tile-size-sticky' : 'dashboard-tile-size'
  return (
    <div className="flex items-center gap-3" data-hint={compact ? undefined : 'dashboard-size'}>
      {!compact && (
        <label htmlFor={id} className="shrink-0 text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
          {/* Both languages' captions are laid on top of each other: the slider next to it has the same width in either */}
          <LangStack render={(language) => fixedT(language, 'dashboard')('tiles.sizeLabel')} />
        </label>
      )}
      <input
        id={id}
        type="range"
        min={0}
        max={4}
        step={1}
        value={value}
        onChange={(e) => onChange(Number(e.target.value) as TileDensity)}
        aria-label={compact ? t('tiles.sizeLabel') : undefined}
        aria-valuetext={label}
        className="pv-range min-w-0 flex-1"
      />
      {/* Fixed width: the longest label must fit, so switching sizes never moves the slider */}
      <span
        className={`${compact ? 'w-[5.25rem]' : 'w-[6.5rem]'} shrink-0 text-right text-xs font-extrabold`}
        style={{ color: 'var(--accent-crystal)' }}
      >
        {label}
      </span>
    </div>
  )
}

// The wording of a card that depends on the language: playtime, "last played", the rating line and the trophy count. A hook, so
// a card renders again (texts and number formats) when the language is switched.
function useCardText() {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  return useMemo(() => {
    const hours = (value: number) => t('time.hours', { value: fmt.number(value, value < 10 ? 1 : 0) })
    const playtime = (minutes: number) => (minutes < 60 ? t('time.minutes', { value: fmt.number(minutes) }) : hours(minutes / 60))
    const lastPlayed = (iso: string) => {
      const date = new Date(iso)
      const days = Math.floor((Date.now() - date.getTime()) / 86_400_000)
      if (days <= 0) return t('time.today')
      if (days === 1) return t('time.yesterday')
      if (days < 7) return t('time.daysAgo', { count: days })
      return fmt.date(date, 'date')
    }
    const trophies = (game: GameSummaryDto) => {
      if (game.trophiesTotal > 0) return `${fmt.number(game.trophiesUnlocked)} / ${fmt.number(game.trophiesTotal)}`
      return game.steamAppId ? '…' : t('card.noSteam')
    }
    const rating = (game: GameSummaryDto) => {
      const parts: string[] = []
      if (game.reviewScoreDesc) {
        parts.push(
          game.reviewPercent != null
            ? t('facts.rating.review', { description: game.reviewScoreDesc, percent: fmt.percent(game.reviewPercent) })
            : game.reviewScoreDesc
        )
      }
      // Without a review text of the card the library's share of positive reviews stands in (same figure the sort order uses)
      if (!game.reviewScoreDesc && game.sortReviewPercent != null) parts.push(t('facts.rating.percent', { percent: fmt.percent(game.sortReviewPercent) }))
      const metacritic = game.metacriticScore ?? game.sortMetacritic
      if (metacritic != null) parts.push(t('facts.rating.metacritic', { score: metacritic }))
      else if (game.igdbRating != null) parts.push(t('facts.rating.igdb', { score: Math.round(game.igdbRating) }))
      return parts.join(' · ')
    }
    // The library's own cards know only Steam's share of positive reviews and the Metacritic score
    const libraryRating = (game: SteamOwnedGameDto) => {
      const parts: string[] = []
      if (game.sortReviewPercent != null) parts.push(t('facts.rating.percent', { percent: fmt.percent(game.sortReviewPercent) }))
      if (game.sortMetacritic != null) parts.push(t('facts.rating.metacritic', { score: game.sortMetacritic }))
      return parts.join(' · ')
    }
    return { hours, playtime, lastPlayed, trophies, rating, libraryRating }
  }, [t, fmt])
}

function TrophyBar({ game }: { game: GameSummaryDto }) {
  const { t } = useTranslation('dashboard')
  return <ProgressBar percent={game.trophiesPercent} label={t('card.trophyProgress')} />
}

function Cover({ game, className }: { game: GameSummaryDto; className?: string }) {
  const [failed, setFailed] = useState(false)
  if (!game.coverUrl || failed) {
    return (
      <div
        className={`flex items-center justify-center ${className ?? ''}`}
        style={{ background: 'radial-gradient(circle at top, var(--accent-crystal-bg), var(--bg-elevated))' }}
      >
        <IconDeviceGamepad2 size={22} style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
      </div>
    )
  }
  return <img src={game.coverUrl} alt="" loading="lazy" onError={() => setFailed(true)} className={`object-cover object-center ${className ?? ''}`} />
}

// The guide's own estimate ("ca. 130-160 Stunden", "about 25 hours") in the language of the app: the short unit (Std. / h), ranges with an
// en dash, "ca." / "approx." for a single value (a range is approximate by itself, so it gets no prefix). Anything else stays as written.
const ESTIMATE_PREFIX = /^\s*(?:ca\.?|circa|approx\.?|approximately|about|around|etwa|rund|~)\s*/i
const ESTIMATE_RANGE = /(\d)\s*(?:-|–|—|bis|to)\s*(\d)/giu
const ESTIMATE_UNIT = /(?<![\p{L}])(?:Stunden|Stunde|Std|hours|hour|hrs|hr|h)(?![\p{L}])\.?/giu

export function normalizeEstimate(raw: string, unit: string, approx: string): string {
  let text = raw.trim().replace(ESTIMATE_RANGE, '$1–$2').replace(ESTIMATE_UNIT, unit)
  if (ESTIMATE_PREFIX.test(text)) {
    text = text.replace(ESTIMATE_PREFIX, '')
    if (!/\d–\d/.test(text)) text = `${approx} ${text}`
  }
  return text
}

export type FactKey = 'played' | 'last' | 'rating' | 'release' | 'size' | 'hours'
// Why a fact has no value: facts.missing.<reason> in the "dashboard" namespace
export type MissingReason = 'notPlayed' | 'steam' | 'estimate' | 'size'

// The placeholders of AiGuideContent.DefaultsFor (server) for "no estimate yet", lower case
const NO_ESTIMATE = new Set(['offen', 'open'])

/**
 * Which facts of a game have no value, and why. Every card shows all its fact rows (the same set on every card, so a row of cards
 * looks uniform); a row without a value holds its icon and a dash, and says the reason in its tooltip and to screen readers.
 */
export function missingFacts(game: CardGame): Partial<Record<FactKey, MissingReason>> {
  const missing: Partial<Record<FactKey, MissingReason>> = {}
  const neverPlayed = game.playtimeMinutes === 0
  if (game.playtimeMinutes == null || game.playtimeMinutes <= 0) missing.played = neverPlayed ? 'notPlayed' : 'steam'
  // Steam also stamps a launch without playtime; "last played" only makes sense once there is playtime
  if (!game.lastPlayedAt || neverPlayed) missing.last = neverPlayed ? 'notPlayed' : 'steam'
  if (!game.reviewScoreDesc && game.metacriticScore == null && game.igdbRating == null && game.sortReviewPercent == null && game.sortMetacritic == null) missing.rating = 'steam'
  if (!game.releaseDate && !game.releaseAt) missing.release = 'steam'
  if (sizeOf(game).kind === 'none') missing.size = 'size'
  // "Offen" / "Open" is the value the server stores while no estimate is known, in the language of the guide (data, not a UI text)
  const estimate = game.estimatedHours?.trim() ?? ''
  const hasEstimate = (game.completionHours != null && game.completionHours > 0) || (estimate !== '' && !NO_ESTIMATE.has(estimate.toLowerCase()))
  if (!hasEstimate) missing.hours = 'estimate'
  return missing
}

interface CardFact {
  key: FactKey
  Icon: Icon
  color: string
  // Name of the fact (screen readers, tooltip title)
  label: string
  // The sentence of the large card ("7 h played"); '' while the fact is missing
  value: string
  // The bare value for the dense compact card ("7 h"), complete; '' while the fact is missing
  short: string
  // The reason (localized) while the fact has no value, else null: the row then shows a dash
  missing: string | null
  // An explanation that belongs to the value (the size of a game that is not installed is an estimate), else null
  note: string | null
}

type FactRow = (key: FactKey, Icon: Icon, color: string, label: string, value: string, short: string, reason?: MissingReason, note?: string) => CardFact

// The row builder of a card: a fact with a missing reason keeps its place (dash + reason), otherwise it carries its value
function useFactRow(): FactRow {
  const { t } = useTranslation('dashboard')
  return (key, Icon, color, label, value, short, reason, note) => ({
    key,
    Icon,
    color,
    label,
    value: reason ? '' : value,
    short: reason ? '' : short,
    missing: reason ? t(`facts.missing.${reason}`) : null,
    note: reason ? null : (note ?? null),
  })
}

// The size row, the same on both kinds of cards. An installed game shows its real size on disk; a game that is not installed shows the
// install size estimated from Steam's own data ("ca.", explained in the tooltip); without either the row is a dash.
function useSizeFact(game: SizeSource, row: FactRow): CardFact {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const size = sizeOf(game)
  if (size.kind === 'none') return row('size', IconDatabase, 'var(--accent-crystal)', t('facts.size.label'), '', '', 'size')
  const formatted = fmt.bytes(size.bytes)
  if (size.kind === 'disk') {
    return row('size', IconDatabase, 'var(--accent-crystal)', t('facts.size.label'), t('facts.size.onDisk', { size: formatted }), formatted)
  }
  const approx = t('facts.size.approx', { size: formatted })
  return row('size', IconDatabase, 'var(--accent-crystal)', t('facts.size.installLabel'), t('facts.size.install', { size: formatted }), approx, undefined, t('facts.size.installNote'))
}

// The facts of a game card, always all of them in the order of the large card. A fact without a value keeps its row (a dash instead of
// the value, the reason as tooltip / screen reader text), so the cards of a row look the same. One source for the large card (a line per
// fact) and the compact card (the same lines, denser).
function useCardFacts(game: GameSummaryDto): CardFact[] {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const text = useCardText()
  const missing = missingFacts(game)
  const row = useFactRow()
  const played = game.playtimeMinutes != null && game.playtimeMinutes > 0 ? text.playtime(game.playtimeMinutes) : ''
  const when = game.lastPlayedAt ? text.lastPlayed(game.lastPlayedAt) : ''
  const rating = text.rating(game)
  // Steam's own text of the release date first (the language of the app); the library's date when the card has none yet
  const release = game.releaseDate ?? (game.releaseAt ? fmt.date(game.releaseAt) : '')
  // IGDB's average of real players first, the guide's own estimate otherwise
  let hours = { value: '', short: '' }
  if (game.completionHours != null && game.completionHours > 0) {
    const time = text.hours(game.completionHours)
    hours = { value: t('facts.duration.average', { time }), short: t('facts.duration.short', { time }) }
  } else if (!missing.hours) {
    const estimate = normalizeEstimate(game.estimatedHours, t('time.hoursUnit'), t('facts.duration.approx'))
    hours = { value: t('facts.duration.estimate', { text: estimate }), short: estimate }
  }
  return [
    row('played', IconPlayerPlay, 'var(--accent-emerald)', t('facts.played.label'), t('facts.played.value', { time: played }), played, missing.played),
    row('last', IconHistory, 'var(--accent-crystal)', t('facts.lastPlayed.label'), t('facts.lastPlayed.value', { when }), when, missing.last),
    row('rating', IconThumbUp, 'var(--accent-gold)', t('facts.rating.label'), rating, rating, missing.rating),
    row('release', IconCalendar, 'var(--text-muted)', t('facts.released.label'), t('facts.released.value', { date: release }), release, missing.release),
    useSizeFact(game, row),
    row('hours', IconClock, 'var(--accent-purple)', t('facts.duration.label'), hours.value, hours.short, missing.hours),
  ]
}

// The facts of a game of the whole library: the same rows as a favourite's (what the sort orders sort by), without the completion
// estimate - the library list has no guide behind it. Steam's own ratings come as a share of positive reviews and the Metacritic score.
function useLibraryFacts(game: SteamOwnedGameDto): CardFact[] {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const text = useCardText()
  const row = useFactRow()
  const minutes = Math.round(game.playtimeHours * 60)
  const neverPlayed = minutes <= 0
  const played = neverPlayed ? '' : text.playtime(minutes)
  const when = game.lastPlayedAt && !neverPlayed ? text.lastPlayed(game.lastPlayedAt) : ''
  const rating = text.libraryRating(game)
  const release = game.releaseAt ? fmt.date(game.releaseAt) : ''
  return [
    row('played', IconPlayerPlay, 'var(--accent-emerald)', t('facts.played.label'), t('facts.played.value', { time: played }), played, neverPlayed ? 'notPlayed' : undefined),
    row('last', IconHistory, 'var(--accent-crystal)', t('facts.lastPlayed.label'), t('facts.lastPlayed.value', { when }), when, when ? undefined : neverPlayed ? 'notPlayed' : 'steam'),
    row('rating', IconThumbUp, 'var(--accent-gold)', t('facts.rating.label'), rating, rating, rating ? undefined : 'steam'),
    row('release', IconCalendar, 'var(--text-muted)', t('facts.released.label'), t('facts.released.value', { date: release }), release, release ? undefined : 'steam'),
    useSizeFact(game, row),
  ]
}

// The dash of a missing fact: muted, and hidden from screen readers (they get the reason instead)
const MISSING_DASH = '–'

// The facts of a large card: one line each. A missing value is a dash in the muted text colour; the whole row explains why (tooltip), and
// screen readers read the reason instead of the dash. A value with a note (an estimated size) has the note as tooltip.
function CardFacts({ facts }: { facts: CardFact[] }) {
  return (
    <ul className="space-y-1 text-xs">
      {facts.map((fact) => (
        <li key={fact.key} className="min-w-0">
          {fact.missing ? (
            <Tooltip label={fact.label} description={fact.missing} side="top" className="flex h-5 w-full min-w-0 items-center gap-2">
              <span className="flex shrink-0 items-center" style={{ color: 'var(--text-muted)' }}>
                <fact.Icon size={14} aria-hidden="true" />
              </span>
              <span aria-hidden="true" className="font-semibold" style={{ color: 'var(--text-muted)' }}>
                {MISSING_DASH}
              </span>
              <span className="sr-only">
                {fact.label}: {fact.missing}
              </span>
            </Tooltip>
          ) : (
            <Tooltip label={fact.label} description={fact.note ?? undefined} disabled={!fact.note} side="top" className="flex h-5 w-full min-w-0 items-center gap-2">
              <span className="flex shrink-0 items-center" style={{ color: fact.color }}>
                <fact.Icon size={14} aria-hidden="true" />
                <span className="sr-only">{fact.label}</span>
              </span>
              <span className="flex min-w-0 font-semibold" style={{ color: 'var(--text-secondary)' }}>
                <TruncatedText text={fact.value} label={`${fact.label}: ${fact.value}`} />
              </span>
              {fact.note && <span className="sr-only">{fact.note}</span>}
            </Tooltip>
          )}
        </li>
      ))}
    </ul>
  )
}

// The same facts for the compact card, one per line (a small icon and the bare value): nothing is cut off - a value that is longer than
// the line wraps inside its slot. Hovering a line names the fact in our tooltip (and the reason, where the value is missing: the row
// then holds a muted dash). All lines are always there, so the list has the same height on every card.
function CompactFacts({ facts }: { facts: CardFact[] }) {
  // One line is 16 px (leading-4), the gap 4 px: the room of every line is kept even when a card shows dashes only
  const minHeight = `${facts.length * 16 + (facts.length - 1) * 4}px`
  return (
    <ul className="flex flex-col gap-1 text-[11px] font-semibold leading-4" style={{ color: 'var(--text-secondary)', minHeight }}>
      {facts.map((fact) => (
        <li key={fact.key} className="min-w-0">
          <Tooltip label={fact.label} description={fact.missing ?? fact.note ?? fact.short} side="top" className="flex min-w-0 items-start gap-1.5">
            <span className="mt-0.5 shrink-0" style={{ color: fact.missing ? 'var(--text-muted)' : fact.color }}>
              <fact.Icon size={12} aria-hidden="true" />
            </span>
            {fact.missing ? (
              <>
                <span aria-hidden="true" className="min-w-0" style={{ color: 'var(--text-muted)' }}>
                  {MISSING_DASH}
                </span>
                <span className="sr-only">
                  {fact.label}: {fact.missing}
                </span>
              </>
            ) : (
              <>
                <span className="sr-only">{fact.label}: </span>
                <span className="min-w-0 break-words">{fact.short}</span>
              </>
            )}
          </Tooltip>
        </li>
      ))}
    </ul>
  )
}

// The size on the dark cover overlay of the very compact card: the figure only ("~" before an estimate), light on dark
function OverlaySize({ game }: { game: SizeSource }) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const size = sizeOf(game)
  if (size.kind === 'none') return null
  return (
    <span className="shrink-0 font-semibold" style={{ color: '#cbd5e1' }}>
      <span className="sr-only">{size.kind === 'disk' ? t('facts.size.label') : t('facts.size.installLabel')}: </span>
      {size.kind === 'estimate' ? t('facts.size.approxShort', { size: fmt.bytes(size.bytes) }) : fmt.bytes(size.bytes)}
    </span>
  )
}

// The size as one small cell of a list or text row (fixed width, so the columns of the rows line up; empty when the size is unknown).
// An estimated install size carries "ca."; the name of the figure is there for screen readers.
function SizeCell({ game, className = '' }: { game: SizeSource; className?: string }) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const size = sizeOf(game)
  return (
    <span className={`shrink-0 whitespace-nowrap text-right text-[11px] font-semibold tabular-nums ${className}`} style={{ color: 'var(--text-muted)' }}>
      {size.kind !== 'none' && (
        <>
          <span className="sr-only">{size.kind === 'disk' ? t('facts.size.label') : t('facts.size.installLabel')}: </span>
          {size.kind === 'estimate' ? t('facts.size.approx', { size: fmt.bytes(size.bytes) }) : fmt.bytes(size.bytes)}
        </>
      )}
    </span>
  )
}

interface GameTileProps {
  game: CardGame
  density: TileDensity
  onToggleFavorite: (game: GameSummaryDto) => void
}

// One game on the dashboard in the chosen size. The buttons are never inside the card's links (a button inside <a> is invalid): the
// large and compact cards are a cover link, the action row, and a link around the text; the very compact card is a cover link and the
// action row; list rows are a link and the buttons beside it.
export function GameTile({ game, density, onToggleFavorite }: GameTileProps) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const text = useCardText()
  const platinum = isPlatinumGame(game)
  const facts = useCardFacts(game)
  const favoriteLabel = game.isFavorite ? t('card.removeFavorite', { title: game.title }) : t('card.addFavorite', { title: game.title })
  const target: LaunchTarget = { kind: 'game', slug: game.slug }
  const wideScreen = useMinWidth(1536)
  // The segments in the order Spielen - Favorit - Twitch - Löschen; the Play segment is simply not there without bridge / local Steam
  const segments = (
    <>
      <FavoriteSegment label={favoriteLabel} favorite={game.isFavorite} hint onClick={() => onToggleFavorite(game)} />
      <TwitchSegment name={game.title} appId={game.steamAppId} />
    </>
  )

  // List sizes: one row per game, the Play button and the icon buttons at its right end
  if (density <= 1) {
    return (
      <div
        className="group flex items-center gap-3 rounded-lg px-3 py-2"
        style={{
          background: 'var(--bg-surface)',
          border: `1px solid ${platinum ? 'var(--accent-crystal-border)' : 'var(--border-subtle)'}`,
        }}
      >
        <Link to={`/games/${game.slug}`} className="flex min-w-0 flex-1 items-center gap-3" style={{ textDecoration: 'none' }}>
          {density === 1 && <Cover game={game} className="h-12 w-8 shrink-0 rounded" />}
          {density === 1 ? (
            <div className="flex min-w-0 flex-1 flex-col">
              <ClampedText text={game.title} className="text-sm font-bold leading-5" style={{ color: 'var(--text-primary)' }} />
              <GenreLine game={game} className="h-4 text-[11px] font-semibold leading-4" style={{ color: 'var(--accent-emerald)' }} />
            </div>
          ) : (
            <TruncatedText text={game.title} className="text-sm font-bold" style={{ color: 'var(--text-primary)' }} wrapperClassName="flex min-w-0 flex-1" />
          )}
          {platinum && <TierIcon tier="Platinum" size={15} label={t('card.platinum')} />}
          <span className="inline-flex shrink-0 items-center gap-1 text-xs font-extrabold tabular-nums" style={{ color: 'var(--accent-emerald)' }}>
            <IconTrophy size={13} style={{ color: 'var(--accent-gold)' }} aria-hidden="true" />
            {text.trophies(game)}
            {game.trophiesTotal > 0 && <span style={{ color: 'var(--text-muted)' }}>({fmt.percent(game.trophiesPercent)})</span>}
          </span>
          {density === 1 && game.lastPlayedAt && game.playtimeMinutes !== 0 && (
            <span className="hidden shrink-0 text-[11px] xl:inline" style={{ color: 'var(--text-muted)' }}>
              {text.lastPlayed(game.lastPlayedAt)}
            </span>
          )}
          <SizeCell game={game} className={density === 1 ? 'hidden w-[4.75rem] md:block lg:hidden 2xl:block' : 'hidden w-[4.75rem] 2xl:block'} />
          {density === 1 && (
            <span className="hidden w-24 shrink-0 sm:block">
              <TrophyBar game={game} />
            </span>
          )}
        </Link>
        <div className="pv-bar-group">
          <PlayButton variant="segment" target={target} gameName={game.title} unavailable={!game.steamAppId} showRunning steamAppId={game.steamAppId} />
          {segments}
        </div>
      </div>
    )
  }

  const frame: CSSProperties = {
    background: 'var(--bg-surface)',
    border: '1px solid var(--border-subtle)',
    borderRadius: density === 2 ? 'var(--radius-md)' : 'var(--radius-lg)',
    boxShadow: 'var(--shadow-card)',
    ...(platinum ? PLATINUM_CARD_FRAME : null),
  }

  // Very compact: the cover with title and trophies on it, then a slim action row (the Play button only where the cards are wide enough)
  if (density === 2) {
    return (
      <div className="group relative flex flex-col overflow-hidden transition-all duration-200 hover:-translate-y-0.5" style={frame}>
        <Link to={`/games/${game.slug}`} className="relative block" style={{ textDecoration: 'none' }}>
          <Cover game={game} className="aspect-[2/3] w-full" />
          <div className="absolute inset-x-0 bottom-0 space-y-1 px-2 pb-2 pt-6" style={{ background: 'linear-gradient(transparent, rgba(2, 6, 23, 0.88))' }}>
            <ClampedText text={game.title} className="text-[11px] font-bold leading-[14px] text-white" wrapperClassName="block min-h-7 min-w-0" />
            <GenreLine game={game} className="h-3.5 text-[10px] font-semibold leading-[14px]" style={{ color: '#6ee7b7' }} />
            <div className="flex items-center gap-1 whitespace-nowrap text-[10px] font-extrabold tabular-nums" style={{ color: '#fde68a' }}>
              {platinum ? <TierIcon tier="Platinum" size={11} surface="dark" label={t('card.platinum')} /> : <IconTrophy size={11} aria-hidden="true" />}
              {text.trophies(game)}
            </div>
            {/* The size has a line of its own (the room is always reserved): beside the trophies it would not fit the narrowest cards */}
            <div className="flex min-h-3.5 items-center text-[10px] leading-[14px]">
              <OverlaySize game={game} />
            </div>
          </div>
        </Link>
        <div className="pv-card-actions pv-card-actions--sm">
          {wideScreen && <PlayButton variant="segment" target={target} gameName={game.title} unavailable={!game.steamAppId} showRunning steamAppId={game.steamAppId} />}
          {segments}
        </div>
      </div>
    )
  }

  // Large (4) and compact (3) cards: cover, action row, title + genres + facts + trophy meter
  const large = density === 4
  return (
    <div className="group relative flex flex-col overflow-hidden transition-all duration-200 hover:-translate-y-1" style={frame}>
      {/* Official vertical Steam cover (2:3); the link around it is a second way in for the mouse (the text link is the one for keyboard and screen readers) */}
      <Link to={`/games/${game.slug}`} tabIndex={-1} aria-hidden="true" className="block" style={{ textDecoration: 'none' }}>
        <div className="relative aspect-[2/3] w-full overflow-hidden" style={{ background: 'var(--bg-elevated)' }}>
          {platinum && <PlatinumEdge />}
          <Cover game={game} className="h-full w-full transition-transform duration-300 group-hover:scale-105" />
          <div className="absolute inset-x-3 top-3 flex items-start justify-between gap-2">
            {large ? <GuideBadge tabCount={game.tabCount} /> : <span />}
            {platinum && <PlatinumChip />}
          </div>
        </div>
      </Link>

      {/* The action bar: one strip of equal segments, Spielen - Favorit - Twitch - Löschen. Fixed height; without Play the three others
          share the full width */}
      <div className="pv-card-actions">
        <PlayButton variant="segment" target={target} gameName={game.title} unavailable={!game.steamAppId} showRunning steamAppId={game.steamAppId} />
        {segments}
      </div>

      <Link to={`/games/${game.slug}`} className={`flex flex-1 flex-col ${large ? 'space-y-3.5 p-4' : 'space-y-2.5 p-3'}`} style={{ textDecoration: 'none' }}>
        {/* Title first, then the genres as one calm line; both are one line, so every card has the same head */}
        <div className="space-y-0.5">
          {/* Two lines are reserved for the title on every card (same font size everywhere): a longer title is cut off at the end of
              the second line, a shorter one leaves the room empty - so the genre line and the facts below start at the same place */}
          <h3 className={`font-extrabold ${large ? 'min-h-12 text-base leading-6' : 'min-h-10 text-sm leading-5'}`} style={{ color: 'var(--text-primary)' }}>
            <ClampedText text={game.title} />
          </h3>
          <GenreLine game={game} className={`h-4 font-semibold leading-4 ${large ? 'text-xs' : 'text-[11px]'}`} style={{ color: 'var(--accent-emerald)' }} />
        </div>

        {/* The facts start right under the head on every card (so they line up across a row); cards of a row are as tall as the
            tallest one, and the meter below is pinned to the bottom edge */}
        {large ? <CardFacts facts={facts} /> : <CompactFacts facts={facts} />}

        {/* Trophy progress from Steam (the only meter on a card) */}
        <div className="mt-auto pt-1">
          {/* One line, whatever the card width: the label gives way (cut off), the figure never wraps - so the meter has the same
              height on every card */}
          <div className="mb-1 flex items-center justify-between gap-2 text-xs font-extrabold">
            <span className="inline-flex min-w-0 items-center gap-1" style={{ color: 'var(--text-secondary)' }}>
              <IconTrophy size={13} className="shrink-0" style={{ color: 'var(--accent-gold)' }} aria-hidden="true" />
              <span className="truncate">{t('card.trophies')}</span>
            </span>
            <span className="shrink-0 whitespace-nowrap tabular-nums" style={{ color: 'var(--accent-emerald)' }}>
              {game.trophiesTotal > 0 ? `${text.trophies(game)} (${fmt.percent(game.trophiesPercent)})` : text.trophies(game)}
            </span>
          </div>
          <TrophyBar game={game} />
        </div>
      </Link>
    </div>
  )
}

interface SortableGameTileProps extends GameTileProps {
  // Sorting is off while a filter is active (the order of a subset is ambiguous)
  dragDisabled: boolean
  // The card that was just dropped here: a short ring confirms the new place
  landed?: boolean
  // The card was just dropped under a resting pointer: no hover lift until the pointer really moves, so nothing hops after
  // the landing
  still?: boolean
}

// dnd-kit's CSS.Translate.toString rounds to whole pixels; the grid has fractional column widths, so a rounded translation
// puts a moving card half a pixel beside its slot and it shifts when the movement ends. Exact values do not.
const exactTranslate = (t: { x: number; y: number }) => `translate3d(${t.x}px, ${t.y}px, 0)`

// A GameTile that can be grabbed anywhere and dragged to its new place (dnd-kit): mouse with a small activation
// distance (clicks on the card, star, Twitch and delete keep working), long-press on touch, Space + arrow keys.
export function SortableGameTile({ dragDisabled, landed = false, still = false, ...tile }: SortableGameTileProps) {
  const { t } = useTranslation('dashboard')
  const reduceMotion = useReducedMotion() ?? false
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: tile.game.slug,
    disabled: dragDisabled,
    // The other cards make room with a soft ease instead of a hard slide (an instant move with reduced motion)
    transition: reduceMotion ? null : { duration: 320, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' },
  })
  const dragProps = dragDisabled ? {} : { ...attributes, ...listeners }
  return (
    <div
      ref={setNodeRef}
      {...dragProps}
      // The wrapper is a focusable group, not a button: it contains links and buttons of its own
      role={dragDisabled ? undefined : 'group'}
      aria-label={dragDisabled ? undefined : t('card.movable', { title: tile.game.title })}
      tabIndex={dragDisabled ? undefined : 0}
      className={`pv-sortable ${dragDisabled ? '' : 'pv-sortable--active'} ${isDragging ? 'pv-sortable--ghost' : ''} ${landed ? 'pv-sortable--landed' : ''} ${still ? 'pv-sortable--still' : ''}`}
      style={{ transform: transform ? exactTranslate(transform) : undefined, transition }}
      // No native drag of the link / cover image: the sorting library does the dragging
      onDragStart={(e) => e.preventDefault()}
    >
      <GameTile {...tile} />
    </div>
  )
}

interface LibraryTileProps {
  game: SteamOwnedGameDto
  density: TileDensity
  busy: boolean
  onToggleFavorite: (game: SteamOwnedGameDto) => void
}

// One game of the whole Steam library ("Alle Spiele"): games already in PlatinVault open their page, the star puts a
// game on the dashboard (tile without AI) or takes it off. Missing figures are left out (library still evaluated). The cards have the
// same action row as the dashboard cards (Play, favourite, Twitch - there is nothing to delete here).
export function LibraryTile({ game, density, busy, onToggleFavorite }: LibraryTileProps) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const text = useCardText()
  const facts = useLibraryFacts(game)
  // Games without Steam achievements say so; while the library is still being evaluated there is nothing to show (null)
  const trophies =
    game.trophiesTotal == null
      ? null
      : game.trophiesTotal === 0
        ? t('card.noAchievements')
        : `${fmt.number(game.trophiesUnlocked ?? 0)} / ${fmt.number(game.trophiesTotal)}`
  const perfect = (game.trophiesTotal ?? 0) > 0 && (game.trophiesUnlocked ?? 0) >= (game.trophiesTotal ?? 0)
  const percent = game.trophiesTotal ? Math.floor(((game.trophiesUnlocked ?? 0) / game.trophiesTotal) * 100) : 0
  const label = game.isFavorite ? t('card.removeFromDashboard', { name: game.name }) : t('card.putOnDashboard', { name: game.name })
  const target: LaunchTarget = { kind: 'library', appId: game.appId }
  const wideScreen = useMinWidth(1536)
  const segments = (
    <>
      <FavoriteSegment label={label} favorite={game.isFavorite} busy={busy} onClick={() => onToggleFavorite(game)} />
      <TwitchSegment name={game.name} appId={game.appId} />
    </>
  )
  const body = (
    <>
      {density === 1 && <Cover game={{ coverUrl: game.coverUrl } as GameSummaryDto} className="h-12 w-8 shrink-0 rounded" />}
      {density === 1 ? (
        <ClampedText text={game.name} className="text-sm font-bold leading-5" style={{ color: 'var(--text-primary)' }} wrapperClassName="block min-w-0 flex-1" />
      ) : (
        <TruncatedText text={game.name} className="text-sm font-bold" style={{ color: 'var(--text-primary)' }} wrapperClassName="flex min-w-0 flex-1" />
      )}
      {perfect && <TierIcon tier="Platinum" size={15} label={t('card.platinum')} />}
      {trophies && (
        <span className="inline-flex shrink-0 items-center gap-1 text-xs font-extrabold tabular-nums" style={{ color: 'var(--accent-emerald)' }}>
          <IconTrophy size={13} style={{ color: 'var(--accent-gold)' }} aria-hidden="true" />
          {trophies}
        </span>
      )}
      {density === 1 && game.playtimeHours > 0 && (
        <span className="hidden shrink-0 text-[11px] lg:inline" style={{ color: 'var(--text-muted)' }}>
          {text.playtime(Math.round(game.playtimeHours * 60))}
        </span>
      )}
      <SizeCell game={game} className={density === 1 ? 'hidden w-[4.75rem] md:block lg:hidden 2xl:block' : 'hidden w-[4.75rem] 2xl:block'} />
    </>
  )

  // The page of a game that is already in PlatinVault; for the others the same box without a link
  const wrap = (className: string, children: ReactNode, hidden = false) =>
    game.existingSlug ? (
      <Link to={`/games/${game.existingSlug}`} className={className} style={{ textDecoration: 'none' }} {...(hidden ? { tabIndex: -1, 'aria-hidden': true } : {})}>
        {children}
      </Link>
    ) : (
      <div className={className}>{children}</div>
    )

  if (density <= 1) {
    return (
      <div
        className="flex items-center gap-3 rounded-lg px-3 py-2"
        style={{ background: 'var(--bg-surface)', border: `1px solid ${perfect ? 'var(--accent-crystal-border)' : 'var(--border-subtle)'}` }}
      >
        {wrap('flex min-w-0 flex-1 items-center gap-3', body)}
        <div className="pv-bar-group">
          <PlayButton variant="segment" target={target} gameName={game.name} showRunning steamAppId={game.appId} />
          {segments}
        </div>
      </div>
    )
  }

  // Card sizes: cover with the name and the trophies; very compact puts the size beside the trophies on the cover, the large and compact
  // cards show the same fact rows as a favourite's (played, last played, rating, release date, size) and the trophy meter at the bottom
  const cover = <Cover game={{ coverUrl: game.coverUrl } as GameSummaryDto} className="aspect-[2/3] w-full" />
  const info =
    density === 2 ? (
      <div className="absolute inset-x-0 bottom-0 space-y-1 px-2 pb-2 pt-6" style={{ background: 'linear-gradient(transparent, rgba(2, 6, 23, 0.88))' }}>
        {/* Two lines are reserved for the name on every card, like on the dashboard cards */}
        <div className="flex min-h-7 min-w-0">
          <ClampedText text={game.name} className="text-[11px] font-bold leading-[14px] text-white" wrapperClassName="block min-w-0 flex-1" />
        </div>
        <div className="flex min-h-3.5 items-center gap-1 whitespace-nowrap text-[10px] font-extrabold tabular-nums" style={{ color: '#fde68a' }}>
          {trophies && (
            <>
              {perfect ? <TierIcon tier="Platinum" size={12} surface="dark" label={t('card.platinum')} /> : <IconTrophy size={12} aria-hidden="true" />}
              {trophies}
            </>
          )}
        </div>
        <div className="flex min-h-3.5 items-center text-[10px] leading-[14px]">
          <OverlaySize game={game} />
        </div>
      </div>
    ) : (
      <div className="flex flex-1 flex-col space-y-2.5 p-3">
        <div className="flex min-h-10 min-w-0">
          <ClampedText text={game.name} className="text-sm font-bold leading-5" style={{ color: 'var(--text-primary)' }} wrapperClassName="block min-w-0 flex-1" />
        </div>
        {density === 4 ? <CardFacts facts={facts} /> : <CompactFacts facts={facts} />}
        {/* Trophy progress from Steam: the line and the meter are always there (the figure is "…" while the library is evaluated) */}
        <div className="mt-auto pt-1">
          <div className="mb-1 flex items-center justify-between gap-2 text-xs font-extrabold">
            <span className="inline-flex min-w-0 items-center gap-1" style={{ color: 'var(--text-secondary)' }}>
              {perfect ? <TierIcon tier="Platinum" size={13} label={t('card.platinum')} /> : <IconTrophy size={13} className="shrink-0" style={{ color: 'var(--accent-gold)' }} aria-hidden="true" />}
              <span className="truncate">{t('card.trophies')}</span>
            </span>
            <span className="shrink-0 whitespace-nowrap tabular-nums" style={{ color: 'var(--accent-emerald)' }}>
              {trophies ? ((game.trophiesTotal ?? 0) > 0 ? `${trophies} (${fmt.percent(percent)})` : trophies) : '…'}
            </span>
          </div>
          <ProgressBar percent={percent} label={t('card.trophyProgress')} />
        </div>
      </div>
    )
  const frame: CSSProperties = {
    background: 'var(--bg-surface)',
    border: `1px solid ${perfect ? 'var(--accent-crystal-border)' : 'var(--border-subtle)'}`,
    borderRadius: density === 2 ? 'var(--radius-md)' : 'var(--radius-lg)',
    boxShadow: 'var(--shadow-card)',
  }

  if (density === 2) {
    return (
      <div className="group relative flex flex-col overflow-hidden transition-all duration-200 hover:-translate-y-0.5" style={frame}>
        {wrap(
          'relative block',
          <>
            {cover}
            {info}
          </>
        )}
        <div className="pv-card-actions pv-card-actions--sm">
          {wideScreen && <PlayButton variant="segment" target={target} gameName={game.name} showRunning steamAppId={game.appId} />}
          {segments}
        </div>
      </div>
    )
  }

  return (
    <div className="group relative flex flex-col overflow-hidden transition-all duration-200 hover:-translate-y-0.5" style={frame}>
      {wrap('block', cover, true)}
      <div className="pv-card-actions">
        <PlayButton variant="segment" target={target} gameName={game.name} showRunning steamAppId={game.appId} />
        {segments}
      </div>
      {wrap('flex flex-1 flex-col', info)}
    </div>
  )
}
