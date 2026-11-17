import { useId, useMemo, useState, type ReactNode } from 'react'
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
  IconTrophy,
} from '@tabler/icons-react'
import type { Icon } from '@tabler/icons-react'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import { fixedT } from '../i18n'
import { useFormat } from '../i18n/format'
import { sizeOf, type SizeSource } from '../utils/dashboardSort'
import { useLiveCount, useLiveCountWatch } from '../utils/useTwitchLiveCounts'
import { isPlatinumGame, PlatinumCoverFx, PlatinumLineFx } from './platinum/platinum'
import { RatingStrip } from './RatingStrip'
import { PlayButton } from './launch/PlayButton'
import type { LaunchTarget } from './launch/LaunchProvider'
import { ClampedText } from './ui/ClampedText'
import { LangStack } from './ui/LangStack'
import { ProgressBar } from './ui/ProgressBar'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'
import { TruncatedText } from './ui/TruncatedText'
import { TwitchButton } from './TwitchButton'
import '../styles/dashboard.css'

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

// The calm genre line under a title: one line in the muted text colour, cut off with our tooltip when it does not fit. Its height is
// ALWAYS reserved (an empty line when a game has no genres), so cards in a row never differ in height because of it.
function GenreLine({ game, className }: { game: CardGame; className: string }) {
  const genres = gameGenres(game).join(' · ')
  return (
    <div data-genre-line className={`flex min-w-0 items-center ${className}`}>
      {genres ? <TruncatedText text={genres} wrapperClassName="flex min-w-0 flex-1" /> : <span aria-hidden="true">&nbsp;</span>}
    </div>
  )
}

// Tile size on the dashboard, chosen with a slider: 4 = large cards (default) ... 0 = text only
export type TileDensity = 0 | 1 | 2 | 3 | 4

// The key of each size's name in the "dashboard" namespace (tiles.size.<key>)
const TILE_DENSITY_KEYS = { 0: 'textOnly', 1: 'list', 2: 'veryCompact', 3: 'compact', 4: 'large' } as const satisfies Record<TileDensity, string>

// The segments of a card's action row (Play, Favourite, Twitch): frameless text buttons, icon + short label where the cell is wide
// enough, icon only otherwise (.pv-bar-seg in styles/dashboard.css). Each one is a tooltip wrapper that is the cell of the row; the
// buttons are never inside a card link.
function FavoriteSegment({ label, favorite, busy = false, hint = false, onClick }: { label: string; favorite: boolean; busy?: boolean; hint?: boolean; onClick: () => void }) {
  const { t } = useTranslation('dashboard')
  return (
    <Tooltip label={label} className="pv-bar-cell">
      <button type="button" onClick={onClick} aria-pressed={favorite} aria-label={label} data-hint={hint ? 'dashboard-star' : undefined} className="pv-bar-seg pv-bar-seg--star">
        {busy ? <IconLoader2 size={18} className="shrink-0 animate-spin" /> : favorite ? <IconStarFilled size={18} className="shrink-0" /> : <IconStar size={18} className="shrink-0" />}
        <span className="pv-bar-label">{t('card.favoriteShort')}</span>
      </button>
    </Tooltip>
  )
}

// The same star as a boxless icon button for the first column of a list row
function FavoriteStar({ label, favorite, busy = false, hint = false, onClick }: { label: string; favorite: boolean; busy?: boolean; hint?: boolean; onClick: () => void }) {
  return (
    <Tooltip label={label} className="pv-dl-starcell">
      <button
        type="button"
        onClick={onClick}
        aria-pressed={favorite}
        aria-label={label}
        data-hint={hint ? 'dashboard-star' : undefined}
        className="pv-btn pv-icon-btn pv-icon-btn--ghost pv-dl-star"
      >
        {busy ? <IconLoader2 size={20} className="animate-spin" /> : favorite ? <IconStarFilled size={20} /> : <IconStar size={20} />}
      </button>
    </Tooltip>
  )
}

function TwitchSegment({ name, appId }: { name: string; appId?: number | null }) {
  const { t } = useTranslation('dashboard')
  const liveCount = useLiveCount(appId)
  // While this segment is (nearly) on screen the game's count is fetched
  const watch = useLiveCountWatch(appId, name)
  return <TwitchButton name={name} size={18} className="pv-bar-seg" wrapperClassName="pv-bar-cell" labelText={t('card.twitchShort')} liveCount={liveCount} linkRef={watch} />
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

// The grid size of each card density (the columns fill the page width: a wider page gets more columns instead of stretched cards)
const GRID_SIZE = { 4: 'large', 3: 'compact', 2: 'veryCompact' } as const

/**
 * The container of the tiles in the chosen size: a card grid for the sizes 2-4, one hairline list with a head row for 0-1 (text only
 * and list). The tiles themselves are the children.
 */
export function TileContainer({ density, children }: { density: TileDensity; children: ReactNode }) {
  const { t } = useTranslation('dashboard')
  if (density <= 1) {
    return (
      <div className="pv-dl" data-text={density === 0 ? 'true' : undefined}>
        {/* The column heads are for the eye; every value carries its own name for screen readers */}
        <div className="pv-dl-head" aria-hidden="true">
          <div className="pv-dl-grid">
            <span />
            <span>{t('list.title')}</span>
            <span className="pv-dl-c-play">{t('list.playtime')}</span>
            <span className="pv-dl-c-last">{t('list.lastPlayed')}</span>
            <span className="pv-dl-c-trophy">{t('list.trophies')}</span>
            <span className="pv-dl-c-size">{t('list.size')}</span>
            <span className="pv-dl-c-act" />
          </div>
        </div>
        {children}
      </div>
    )
  }
  return (
    <div className="pv-tiles" data-size={GRID_SIZE[density as 2 | 3 | 4]}>
      {children}
    </div>
  )
}

interface TileSizeSliderProps {
  value: TileDensity
  onChange: (value: TileDensity) => void
  // A copy of the control (the compact bar) needs its own id for the label, and no coach-mark anchor
  id?: string
  copy?: boolean
}

export function TileSizeSlider({ value, onChange, id = 'dashboard-tile-size', copy = false }: TileSizeSliderProps) {
  const { t } = useTranslation('dashboard')
  const label = t(`tiles.size.${TILE_DENSITY_KEYS[value]}`)
  return (
    <div className="pv-size" data-hint={copy ? undefined : 'dashboard-size'}>
      <label htmlFor={id} className="pv-size-label">
        {/* Both languages' captions are laid on top of each other: the slider next to it has the same width in either */}
        <LangStack render={(language) => fixedT(language, 'dashboard')('tiles.sizeLabel')} />
      </label>
      <input
        id={id}
        type="range"
        min={0}
        max={4}
        step={1}
        value={value}
        onChange={(e) => onChange(Number(e.target.value) as TileDensity)}
        aria-valuetext={label}
        className="pv-size-range"
      />
      {/* Fixed width: the longest label must fit, so switching sizes never moves the slider */}
      <output htmlFor={id} className="pv-size-value">
        {label}
      </output>
    </div>
  )
}

// The wording of a card that depends on the language: playtime, "last played" and the trophy count. A hook, so
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
    return { hours, playtime, lastPlayed, trophies }
  }, [t, fmt])
}

function Cover({ game, className }: { game: Pick<GameSummaryDto, 'coverUrl'>; className?: string }) {
  const [failed, setFailed] = useState(false)
  if (!game.coverUrl || failed) {
    return (
      <div className={`pv-cover-empty ${className ?? ''}`}>
        <IconDeviceGamepad2 size={22} aria-hidden="true" />
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

export type FactKey = 'played' | 'last' | 'release' | 'size' | 'hours'
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
  // Name of the fact (screen readers, tooltip title)
  label: string
  // The sentence of the large card ("7 h played"); '' while the fact is missing
  value: string
  // The bare value for the dense compact card and the list rows ("7 h"), complete; '' while the fact is missing
  short: string
  // The reason (localized) while the fact has no value, else null: the row then shows a dash
  missing: string | null
  // An explanation that belongs to the value (the size of a game that is not installed is an estimate), else null
  note: string | null
}

type FactRow = (key: FactKey, Icon: Icon, label: string, value: string, short: string, reason?: MissingReason, note?: string) => CardFact

// The row builder of a card: a fact with a missing reason keeps its place (dash + reason), otherwise it carries its value
function useFactRow(): FactRow {
  const { t } = useTranslation('dashboard')
  return (key, Icon, label, value, short, reason, note) => ({
    key,
    Icon,
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
  if (size.kind === 'none') return row('size', IconDatabase, t('facts.size.label'), '', '', 'size')
  const formatted = fmt.bytes(size.bytes)
  if (size.kind === 'disk') {
    return row('size', IconDatabase, t('facts.size.label'), t('facts.size.onDisk', { size: formatted }), formatted)
  }
  const approx = t('facts.size.approx', { size: formatted })
  return row('size', IconDatabase, t('facts.size.installLabel'), t('facts.size.install', { size: formatted }), approx, undefined, t('facts.size.installNote'))
}

// The facts of a game card, always all of them in the order of the large card. A fact without a value keeps its row (a dash instead of
// the value, the reason as tooltip / screen reader text), so the cards of a row look the same. One source for the large card (a line per
// fact), the compact card (the same lines, denser) and the list rows (the bare values).
function useCardFacts(game: GameSummaryDto): CardFact[] {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const text = useCardText()
  const missing = missingFacts(game)
  const row = useFactRow()
  const played = game.playtimeMinutes != null && game.playtimeMinutes > 0 ? text.playtime(game.playtimeMinutes) : ''
  const when = game.lastPlayedAt ? text.lastPlayed(game.lastPlayedAt) : ''
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
    row('played', IconPlayerPlay, t('facts.played.label'), t('facts.played.value', { time: played }), played, missing.played),
    row('last', IconHistory, t('facts.lastPlayed.label'), t('facts.lastPlayed.value', { when }), when, missing.last),
    row('release', IconCalendar, t('facts.released.label'), t('facts.released.value', { date: release }), release, missing.release),
    useSizeFact(game, row),
    row('hours', IconClock, t('facts.duration.label'), hours.value, hours.short, missing.hours),
  ]
}

// The facts of a game of the whole library: the same rows as a favourite's (what the sort orders sort by), without the completion
// estimate - the library list has no guide behind it. The ratings are not a fact row: the tile's rating strip shows them.
function useLibraryFacts(game: SteamOwnedGameDto): CardFact[] {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const text = useCardText()
  const row = useFactRow()
  const minutes = Math.round(game.playtimeHours * 60)
  const neverPlayed = minutes <= 0
  const played = neverPlayed ? '' : text.playtime(minutes)
  const when = game.lastPlayedAt && !neverPlayed ? text.lastPlayed(game.lastPlayedAt) : ''
  const release = game.releaseAt ? fmt.date(game.releaseAt) : ''
  return [
    row('played', IconPlayerPlay, t('facts.played.label'), t('facts.played.value', { time: played }), played, neverPlayed ? 'notPlayed' : undefined),
    row('last', IconHistory, t('facts.lastPlayed.label'), t('facts.lastPlayed.value', { when }), when, when ? undefined : neverPlayed ? 'notPlayed' : 'steam'),
    row('release', IconCalendar, t('facts.released.label'), t('facts.released.value', { date: release }), release, release ? undefined : 'steam'),
    useSizeFact(game, row),
  ]
}

// The dash of a missing fact: muted, and hidden from screen readers (they get the reason instead)
const MISSING_DASH = '–'

// The facts of a large card: one line each. A missing value is a dash in the muted text colour; the whole row explains why (tooltip), and
// screen readers read the reason instead of the dash. A value with a note (an estimated size) has the note as tooltip.
function CardFacts({ facts }: { facts: CardFact[] }) {
  return (
    <ul className="pv-facts">
      {facts.map((fact) => (
        <li key={fact.key} className="pv-fact">
          {fact.missing ? (
            <Tooltip label={fact.label} description={fact.missing} side="top" className="pv-fact-in">
              <fact.Icon size={16} className="pv-fact-ic" aria-hidden="true" />
              <span aria-hidden="true" className="pv-fact-none">
                {MISSING_DASH}
              </span>
              <span className="sr-only">
                {fact.label}: {fact.missing}
              </span>
            </Tooltip>
          ) : (
            <Tooltip label={fact.label} description={fact.note ?? undefined} disabled={!fact.note} side="top" className="pv-fact-in">
              <fact.Icon size={16} className="pv-fact-ic" aria-hidden="true" />
              {/* The icon and the tooltip name the fact on screen; screen readers get the sentence */}
              <span className="sr-only">{fact.value}</span>
              <span className="flex min-w-0" aria-hidden="true">
                <TruncatedText text={fact.short} label={`${fact.label}: ${fact.short}`} />
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
  // The room of every line is kept even when a card shows dashes only
  return (
    <ul className="pv-facts pv-facts--compact" style={{ minBlockSize: `${facts.length * 1.25 + (facts.length - 1) * 0.25}rem` }}>
      {facts.map((fact) => (
        <li key={fact.key} className="pv-fact">
          <Tooltip label={fact.label} description={fact.missing ?? fact.note ?? fact.short} side="top" className="pv-fact-in">
            <fact.Icon size={14} className="pv-fact-ic" aria-hidden="true" />
            {fact.missing ? (
              <>
                <span aria-hidden="true" className="pv-fact-none">
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

// A value cell of a list row: the bare value, or the muted dash with the reason for screen readers and the tooltip. Every value is
// named for screen readers (the visible column heads are hidden from them).
function FactCell({ fact, className }: { fact: CardFact | undefined; className: string }) {
  if (!fact) return <span className={className} />
  if (fact.missing) {
    return (
      <span className={className}>
        <Tooltip label={fact.label} description={fact.missing} side="top">
          <span aria-hidden="true" className="pv-dim">
            {MISSING_DASH}
          </span>
          <span className="sr-only">
            {fact.label}: {fact.missing}
          </span>
        </Tooltip>
      </span>
    )
  }
  return (
    <span className={className}>
      <span className="sr-only">{fact.label}: </span>
      {fact.short}
    </span>
  )
}

// The trophy cell of a list row: "Platin" in gold for a finished game, else the count with the share
function TrophyCell({ platinum, text, percent }: { platinum: boolean; text: string; percent: string | null }) {
  const { t } = useTranslation('dashboard')
  return (
    <span className="pv-dl-c-trophy">
      <span className="sr-only">{t('list.trophies')}: </span>
      {platinum ? (
        <span className="pv-dl-plat">
          <TierIcon tier="Platinum" size={16} />
          {t('card.platinum')}
        </span>
      ) : (
        <>
          {text}
          {percent && <span className="pv-dim"> ({percent})</span>}
        </>
      )}
    </span>
  )
}

// The trophy line of a card: label (gold trophy; "Platin" for a finished game), the count on the right, and the thin progress line below
// In the compact cards (150-250 px wide) the label is left to the icon and screen readers (the line and the count say the rest).
function TileProgress({ platinum, text, value, narrow = false, seed }: { platinum: boolean; text: string; value: number; narrow?: boolean; seed: string }) {
  const { t } = useTranslation('dashboard')
  return (
    <div className="pv-tile-prog">
      <div className="pv-tile-prog-row">
        <span className="pv-tile-prog-k" data-platin={platinum ? 'true' : undefined}>
          {platinum ? <TierIcon tier="Platinum" size={16} /> : <IconTrophy size={16} aria-hidden="true" />}
          <span className={narrow ? 'sr-only' : 'truncate'}>{platinum ? t('card.platinum') : t('card.trophies')}</span>
        </span>
        <span className="pv-tile-prog-v">{text}</span>
      </div>
      <ProgressBar percent={value} label={t('card.trophyProgress')} overlay={platinum ? <PlatinumLineFx seed={seed} /> : undefined} />
    </div>
  )
}

interface GameTileProps {
  game: CardGame
  density: TileDensity
  onToggleFavorite: (game: GameSummaryDto) => void
}

// One game on the dashboard in the chosen size. The buttons are never inside the card's links (a button inside <a> is invalid): the
// cards are a cover link, a link around the text and the action row; list rows are a link and the buttons beside it.
export function GameTile({ game, density, onToggleFavorite }: GameTileProps) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const text = useCardText()
  const platinum = isPlatinumGame(game)
  const facts = useCardFacts(game)
  const titleId = useId()
  const favoriteLabel = game.isFavorite ? t('card.removeFavorite', { title: game.title }) : t('card.addFavorite', { title: game.title })
  const target: LaunchTarget = { kind: 'game', slug: game.slug }
  const trophyValue = game.trophiesTotal > 0 ? `${text.trophies(game)} (${fmt.percent(game.trophiesPercent)})` : text.trophies(game)
  const href = `/games/${game.slug}`
  const segments = (
    <>
      <FavoriteSegment label={favoriteLabel} favorite={game.isFavorite} hint onClick={() => onToggleFavorite(game)} />
      <TwitchSegment name={game.title} appId={game.steamAppId} />
    </>
  )

  // List sizes: one hairline row per game - star, title, the values, and the Play / Twitch icons at the right end
  if (density <= 1) {
    const by = (key: FactKey) => facts.find((fact) => fact.key === key)
    return (
      <div className="pv-dl-row" data-platin={platinum ? 'true' : undefined}>
        <div className="pv-dl-grid">
          <FavoriteStar label={favoriteLabel} favorite={game.isFavorite} hint onClick={() => onToggleFavorite(game)} />
          <div className="pv-dl-c-title">
            {density === 1 && <Cover game={game} className="pv-dl-cover" />}
            <div className="flex min-w-0 flex-col">
              <Link to={href} className="pv-dl-link">
                <TruncatedText text={game.title} wrapperClassName="pv-dl-linktext" />
              </Link>
              {density === 1 && <GenreLine game={game} className="pv-dl-gen" />}
            </div>
          </div>
          <FactCell fact={by('played')} className="pv-dl-c-play" />
          <FactCell fact={by('last')} className="pv-dl-c-last" />
          <TrophyCell platinum={platinum} text={text.trophies(game)} percent={game.trophiesTotal > 0 ? fmt.percent(game.trophiesPercent) : null} />
          <FactCell fact={by('size')} className="pv-dl-c-size" />
          <div className="pv-dl-c-act">
            <div className="pv-bar-group">
              <PlayButton variant="segment" target={target} gameName={game.title} unavailable={!game.steamAppId} showRunning steamAppId={game.steamAppId} />
              <TwitchSegment name={game.title} appId={game.steamAppId} />
            </div>
          </div>
        </div>
      </div>
    )
  }

  const large = density === 4
  return (
    <div className="pv-tile" data-platin={platinum ? 'true' : undefined}>
      {/* The cover is a second way in for the mouse (the text link is the one for keyboard and screen readers) */}
      <Link to={href} tabIndex={-1} aria-hidden="true" className="pv-tile-cover">
        <Cover game={game} className="h-full w-full" />
        {platinum && <PlatinumCoverFx seed={game.slug} compact={density === 2} />}
      </Link>

      {/* Cover, then the ratings, then the action bar, then the text: the order of the card */}
      <RatingStrip game={game} />

      {/* The action row: three equal cells, Spielen - Favorit - Twitch, fixed height; without Play the other two share the width */}
      <div className="pv-tile-act">
        <PlayButton variant="segment" target={target} gameName={game.title} unavailable={!game.steamAppId} showRunning steamAppId={game.steamAppId} />
        {segments}
      </div>

      <Link to={href} aria-labelledby={titleId} className="pv-tile-link">
        {/* Title first, then the genres as one calm line; both are one line, so every card has the same head */}
        <div className="pv-tile-head">
          <h3 id={titleId} className="pv-tile-title">
            <ClampedText text={game.title} />
          </h3>
          {density !== 2 && <GenreLine game={game} className="pv-tile-gen" />}
        </div>

        {/* The facts start right under the head on every card (so they line up across a row); cards of a row are as tall as the
            tallest one, and the trophy line below is pinned to the bottom edge */}
        {large ? <CardFacts facts={facts} /> : density === 3 ? <CompactFacts facts={facts} /> : null}

        {/* Trophy progress from Steam (the only meter on a card) */}
        <TileProgress seed={game.slug} platinum={platinum} text={density === 2 ? text.trophies(game) : trophyValue} value={game.trophiesPercent} narrow={density <= 3} />
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
  const facts = useLibraryFacts(game)
  const titleId = useId()
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
  const trophyValue = trophies ? ((game.trophiesTotal ?? 0) > 0 ? `${trophies} (${fmt.percent(percent)})` : trophies) : '…'
  const segments = (
    <>
      <FavoriteSegment label={label} favorite={game.isFavorite} busy={busy} onClick={() => onToggleFavorite(game)} />
      <TwitchSegment name={game.name} appId={game.appId} />
    </>
  )
  const coverGame = { coverUrl: game.coverUrl }

  // The page of a game that is already in PlatinVault; for the others the same box without a link
  const wrap = (className: string, children: ReactNode, hidden = false, labelledBy?: string) =>
    game.existingSlug ? (
      <Link to={`/games/${game.existingSlug}`} className={className} {...(hidden ? { tabIndex: -1, 'aria-hidden': true } : {})} {...(labelledBy ? { 'aria-labelledby': labelledBy } : {})}>
        {children}
      </Link>
    ) : (
      <div className={className}>{children}</div>
    )

  if (density <= 1) {
    const by = (key: FactKey) => facts.find((fact) => fact.key === key)
    return (
      <div className="pv-dl-row" data-platin={perfect ? 'true' : undefined}>
        <div className="pv-dl-grid">
          <FavoriteStar label={label} favorite={game.isFavorite} busy={busy} onClick={() => onToggleFavorite(game)} />
          <div className="pv-dl-c-title">
            {density === 1 && <Cover game={coverGame} className="pv-dl-cover" />}
            {game.existingSlug ? (
              <Link to={`/games/${game.existingSlug}`} className="pv-dl-link">
                <TruncatedText text={game.name} wrapperClassName="pv-dl-linktext" />
              </Link>
            ) : (
              <span className="pv-dl-link pv-dl-link--plain">
                <TruncatedText text={game.name} wrapperClassName="pv-dl-linktext" />
              </span>
            )}
          </div>
          <FactCell fact={by('played')} className="pv-dl-c-play" />
          <FactCell fact={by('last')} className="pv-dl-c-last" />
          <TrophyCell platinum={perfect} text={trophies ?? '…'} percent={(game.trophiesTotal ?? 0) > 0 ? fmt.percent(percent) : null} />
          <FactCell fact={by('size')} className="pv-dl-c-size" />
          <div className="pv-dl-c-act">
            <div className="pv-bar-group">
              <PlayButton variant="segment" target={target} gameName={game.name} showRunning steamAppId={game.appId} />
              <TwitchSegment name={game.name} appId={game.appId} />
            </div>
          </div>
        </div>
      </div>
    )
  }

  // Card sizes: the cover, the name, the same fact rows as a favourite's (played, last played, release date, size) and the
  // thin trophy line at the bottom
  const large = density === 4
  return (
    <div className="pv-tile" data-platin={perfect ? 'true' : undefined}>
      {wrap(
        'pv-tile-cover',
        <>
          <Cover game={coverGame} className="h-full w-full" />
          {perfect && <PlatinumCoverFx seed={String(game.appId)} compact={density === 2} />}
        </>,
        true
      )}
      <RatingStrip game={game} />
      <div className="pv-tile-act">
        <PlayButton variant="segment" target={target} gameName={game.name} showRunning steamAppId={game.appId} />
        {segments}
      </div>
      {wrap(
        'pv-tile-link',
        <>
          <div className="pv-tile-head">
            <h3 id={titleId} className="pv-tile-title">
              <ClampedText text={game.name} />
            </h3>
          </div>
          {large ? <CardFacts facts={facts} /> : density === 3 ? <CompactFacts facts={facts} /> : null}
          {/* The line and the thin progress line are always there (the figure is "…" while the library is evaluated) */}
          <TileProgress seed={String(game.appId)} platinum={perfect} text={density === 2 ? (trophies ?? '…') : trophyValue} value={percent} narrow={density <= 3} />
        </>,
        false,
        titleId
      )}
    </div>
  )
}
