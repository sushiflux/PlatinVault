import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { flushSync } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { IconBulb, IconCheck, IconEye, IconEyeOff, IconLanguage, IconNote, IconUsers, IconX } from '@tabler/icons-react'
import type { LiveAchievementItemDto, RarityTier } from '../api/types'
import { useFormat } from '../i18n/format'
import { PinIconButton, type PinControl } from './pins/PinIconButton'
import { GhostIconButton } from './ui/GhostIconButton'
import { ProgressBar } from './ui/ProgressBar'
import { TIER_ICON_COLORS, TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'

// ---------------------------------------------------------------------------------------------------------------
// Size of the achievement list on the live trophies tab, chosen with a slider and remembered per browser
// ---------------------------------------------------------------------------------------------------------------

export type TrophyDensity = 0 | 1 | 2 | 3

// The id names the size in the texts: live:density.<id>
export const TROPHY_DENSITIES: { value: TrophyDensity; id: 'list' | 'tiles' | 'compact' | 'large' }[] = [
  { value: 0, id: 'list' },
  { value: 1, id: 'tiles' },
  { value: 2, id: 'compact' },
  { value: 3, id: 'large' },
]

const DENSITY_KEY = 'pv_trophy_density'
const DEFAULT_DENSITY: TrophyDensity = 2

export function loadTrophyDensity(): TrophyDensity {
  try {
    const raw = localStorage.getItem(DENSITY_KEY)
    const value = Number(raw)
    return raw !== null && TROPHY_DENSITIES.some((d) => d.value === value) ? (value as TrophyDensity) : DEFAULT_DENSITY
  } catch {
    return DEFAULT_DENSITY
  }
}

export function saveTrophyDensity(value: TrophyDensity) {
  try {
    localStorage.setItem(DENSITY_KEY, String(value))
  } catch {
    // Storage unavailable (private mode): the size just is not remembered
  }
}

// Grid per size; the column count follows the real width of the area, not the screen
export const TROPHY_GRID: Record<TrophyDensity, string> = {
  3: 'grid grid-cols-[repeat(auto-fill,minmax(min(100%,28rem),1fr))] gap-3',
  2: 'grid grid-cols-[repeat(auto-fill,minmax(min(100%,22rem),1fr))] gap-2.5',
  1: 'grid grid-cols-[repeat(auto-fill,minmax(8rem,1fr))] gap-2.5',
  0: 'grid grid-cols-[repeat(auto-fill,minmax(min(100%,44rem),1fr))] gap-1.5',
}

export function TrophySizeSlider({ value, onChange, className = "" }: { value: TrophyDensity; onChange: (value: TrophyDensity) => void; className?: string }) {
  const { t } = useTranslation('live')
  const densityId = TROPHY_DENSITIES.find((d) => d.value === value)?.id
  const label = densityId ? t(`density.${densityId}`) : ''
  const rootRef = useRef<HTMLDivElement>(null)

  // Switching the size re-flows the whole list below, so the page gets much taller or shorter. Without care the slider
  // moved under the dragging pointer (the browser kept another element in place or clamped the scroll position), the
  // pointer then pointed at another step, and the view flipped back and forth. So: the slider stays where it is on screen,
  // and while the pointer is down the page may not shrink (a shorter page would clamp the scroll position).
  const holdPageHeight = () => {
    const root = document.documentElement
    root.style.minHeight = `${root.scrollHeight}px`
    const release = () => {
      root.style.minHeight = ''
      window.removeEventListener('pointerup', release)
      window.removeEventListener('pointercancel', release)
    }
    window.addEventListener('pointerup', release)
    window.addEventListener('pointercancel', release)
  }
  const change = (next: TrophyDensity) => {
    const element = rootRef.current
    const before = element?.getBoundingClientRect().top
    flushSync(() => onChange(next))
    if (!element || before === undefined) return
    const root = document.documentElement
    // A taller new layout grows the hold, so it never cuts the page short
    if (root.style.minHeight) root.style.minHeight = `${Math.max(parseFloat(root.style.minHeight), root.scrollHeight)}px`
    const delta = element.getBoundingClientRect().top - before
    if (Math.abs(delta) >= 0.5) window.scrollBy({ top: delta, behavior: 'instant' })
  }
  useEffect(() => () => {
    document.documentElement.style.minHeight = ''
  }, [])

  return (
    <div ref={rootRef} className={`flex items-center gap-3 ${className}`}>
      <label htmlFor="trophy-list-size" className="shrink-0 text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
        {t('density.label')}
      </label>
      <input
        id="trophy-list-size"
        type="range"
        min={0}
        max={3}
        step={1}
        value={value}
        onPointerDown={holdPageHeight}
        onChange={(e) => change(Number(e.target.value) as TrophyDensity)}
        aria-valuetext={label}
        className="pv-range min-w-0 flex-1"
      />
      {/* Fixed width: the longest label must fit, so switching sizes never moves the slider */}
      <span className="w-[5.5rem] shrink-0 text-right text-xs font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
        {label}
      </span>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// Shared pieces
// ---------------------------------------------------------------------------------------------------------------

export interface TrophyItemProps {
  ach: LiveAchievementItemDto
  // How-to from the guide's achievement table
  tip?: string
  // Hidden achievement that is still under the spoiler cover
  covered: boolean
  showOriginal: boolean
  onReveal: () => void
  onToggleOriginal: () => void
  // Personal notes switched on in the settings. Off = no note button, no note text, no "Notiz:" line in a tooltip - nowhere
  notesEnabled: boolean
  // Opens the note editor (new note or edit)
  onNote: () => void
  // Opens the detail dialog (tiles and list)
  onOpen: () => void
  // "Im Blick" (pinned achievements): the pin button. Missing = this list has no pins (no button at all)
  pin?: PinControl
}

function hasCounter(ach: LiveAchievementItemDto): boolean {
  return !ach.achieved && ach.progressMax != null && ach.progressMax > 1
}

// Share of the players with this achievement (0-100), or null when Steam reported none (the 25 % is then only a placeholder of the backend)
function sharePercent(ach: LiveAchievementItemDto): number | null {
  return ach.hasGlobalPercent === false ? null : ach.globalPercent
}

function counterPercent(current: number, max: number): number {
  return max > 0 ? Math.min(100, Math.floor((current / max) * 100)) : 0
}

// Every text and number of the trophy views in the active language, in one place. A hook (not module functions), so the views
// render again when the language is switched.
function useTrophyText() {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  return useMemo(() => {
    const tierName = (tier: RarityTier) => t(`tiers.${tier}`)
    const number = (value: number) => fmt.number(value, 1)
    // "73,1 %" / "73.1%", or null when Steam reported no share
    const percentText = (ach: LiveAchievementItemDto): string | null => {
      const share = sharePercent(ach)
      return share === null ? null : fmt.percent(share, 1)
    }
    // Date and time of the unlock, or null when locked / when Steam gave no time
    const unlockStamp = (ach: LiveAchievementItemDto): string | null =>
      !ach.achieved || !ach.unlockTime ? null : fmt.date(ach.unlockTime, 'dateTime')
    const unlockText = (ach: LiveAchievementItemDto): string | null => {
      if (!ach.achieved) return null
      const stamp = unlockStamp(ach)
      return stamp ? t('trophy.unlockedOn', { date: stamp }) : t('trophy.unlocked')
    }
    const counterText = (ach: LiveAchievementItemDto): string => {
      const max = ach.progressMax ?? 0
      const current = ach.progressCurrent ?? 0
      return `${number(current)} / ${number(max)} (${fmt.percent(counterPercent(current, max))})`
    }
    // Everything about one achievement as plain text: the content of the hover tooltip of tiles and list rows
    const detailText = (ach: LiveAchievementItemDto, tip: string | undefined, covered: boolean, notesEnabled: boolean): string => {
      const share = percentText(ach)
      // string[]: t() returns the literal text type, which would otherwise become the array's element type
      const lines: string[] = [share === null ? tierName(ach.rarityTier) : t('trophy.detail.shareLine', { tier: tierName(ach.rarityTier), share })]
      lines.push(covered ? t('trophy.hiddenAchievementOpen') : ach.description || t('trophy.noDescription'))
      if (tip && !covered) lines.push(t('trophy.detail.howTo', { tip }))
      if (hasCounter(ach)) lines.push(t('trophy.detail.progress', { counter: counterText(ach) }))
      if (notesEnabled && ach.userNote) lines.push(t('trophy.detail.note', { note: ach.userNote }))
      lines.push(unlockText(ach) ?? t('trophy.stillOpen'))
      return lines.join('\n')
    }
    return { t, number, tierName, percentText, unlockStamp, unlockText, counterText, detailText }
  }, [t, fmt])
}

// Corner language of the trophy views: icons use the small token, cards the medium one (the global .pv-card is rounder)
const CARD_RADIUS = 'var(--radius-md)'
const ICON_RADIUS = 'var(--radius-sm)'

// The unlock marker of ALL four views (Liste, Kacheln, Kompakt, Groß) and the detail dialog: a GREEN tag with a check
// INSIDE the icon area, flush with its top left corner, so nothing sticks out. Green is the colour of "unlocked" everywhere
// (the same emerald token as the unlock date, the card tint and the progress): deep green with a white check in light,
// mint with a dark check in dark - white on #047857 = 5.5:1, #052e1f on #6ee7b7 = 9.8:1. A locked achievement has no tag
// and a grey icon. The parent of the tag clips (overflow hidden + radius), the tag only rounds its inner corner.
function UnlockTag({ size }: { size: number }) {
  return (
    <span
      className="absolute flex items-center justify-center"
      style={{
        insetBlockStart: 0,
        insetInlineStart: 0,
        inlineSize: size,
        blockSize: size,
        background: 'var(--accent-emerald)',
        color: 'light-dark(#ffffff, #052e1f)',
        borderEndEndRadius: Math.min(7, Math.round(size * 0.32)),
      }}
      aria-hidden="true"
    >
      <IconCheck size={Math.round(size * 0.64)} stroke={3.2} />
    </span>
  )
}

// Icon of the list rows and tiles
function AchievementIcon({ ach, size, center = false }: { ach: LiveAchievementItemDto; size: number; center?: boolean }) {
  return (
    <span className={`relative inline-flex shrink-0 overflow-hidden ${center ? 'self-center' : 'self-start'}`} style={{ borderRadius: ICON_RADIUS }}>
      <img
        src={ach.achieved ? ach.iconUrl : ach.iconGrayUrl || ach.iconUrl}
        alt=""
        className="object-cover"
        style={{
          inlineSize: size,
          blockSize: size,
          borderRadius: ICON_RADIUS,
          filter: ach.achieved ? 'none' : 'grayscale(85%) opacity(75%)',
          border: '1px solid var(--border-subtle)',
        }}
      />
      {ach.achieved && <UnlockTag size={size >= 52 ? 18 : 14} />}
    </span>
  )
}

// "Groß": the icon is the hero of the card. It fills its own column (the column grows with the card), is centred both ways
// and wears the tier colour as a ring and glow once unlocked. Steam serves most achievement icons at 256 x 256 (checked on
// real data), which stays crisp up to about 9 rem; an older game with a 64 px icon is not blown up beyond 1.5 x its size.
function HeroIcon({ ach, bare }: { ach: LiveAchievementItemDto; bare: boolean }) {
  const [natural, setNatural] = useState<number | null>(null)
  const tier = tierColorVar(ach.rarityTier)
  const ring = `color-mix(in srgb, ${tier} 62%, transparent)`
  return (
    <div className="flex shrink-0 items-center justify-center" style={{ inlineSize: bare ? '7.5rem' : 'clamp(6.5rem, 27%, 9.5rem)' }}>
      <span className="relative inline-flex w-full" style={{ maxInlineSize: natural ? `min(9rem, ${Math.round(natural * 1.5)}px)` : '9rem' }}>
        {/* The frame clips the tag and carries the ring (a shadow is not clipped by its own box) */}
        <span
          className="relative block w-full overflow-hidden"
          style={{
            borderRadius: CARD_RADIUS,
            border: '1px solid var(--border-subtle)',
            boxShadow: ach.achieved ? `0 0 0 2px ${ring}, 0 14px 30px -14px ${ring}` : 'none',
          }}
        >
          <img
            src={ach.achieved ? ach.iconUrl : ach.iconGrayUrl || ach.iconUrl}
            alt=""
            onLoad={(e) => setNatural(e.currentTarget.naturalWidth)}
            className="block aspect-square w-full object-cover"
            style={{ filter: ach.achieved ? 'none' : 'grayscale(85%) opacity(75%)' }}
          />
          {ach.achieved && <UnlockTag size={26} />}
        </span>
      </span>
    </div>
  )
}

function TierBadge({ tier }: { tier: RarityTier }) {
  const { tierName } = useTrophyText()
  return (
    <span
      className="pv-badge"
      style={{
        color: tierTextVar(tier),
        background: `color-mix(in srgb, ${tierColorVar(tier)} 14%, transparent)`,
        borderColor: `color-mix(in srgb, ${tierColorVar(tier)} 45%, transparent)`,
      }}
    >
      <TierIcon tier={tier} size={11} />
      {tierName(tier)}
    </span>
  )
}

// One quiet line of facts under the title (Kompakt and Groß): tier, how rare it is, secret, translated. No chips, no
// bold: the tier colour carries the meaning, the rest is plain secondary text. In the compact card the share reads "73,1 %"
// ("of the players" is only for screen readers) and, with `unlock`, the unlock date closes the line instead of a footer row.
function MetaLine({ ach, roomy = false, unlock = false }: { ach: LiveAchievementItemDto; roomy?: boolean; unlock?: boolean }) {
  const { t, tierName, percentText } = useTrophyText()
  const share = percentText(ach)
  const icon = roomy ? 13 : 12
  return (
    <div
      className={`flex flex-wrap items-center gap-x-2.5 gap-y-0.5 leading-4 ${roomy ? 'text-xs' : 'text-[11px]'}`}
    >
      <span className="inline-flex shrink-0 items-center gap-1 font-medium" style={{ color: tierTextVar(ach.rarityTier) }}>
        <TierIcon tier={ach.rarityTier} size={icon} />
        {tierName(ach.rarityTier)}
      </span>
      {share !== null && (
        <Tooltip label={t('trophy.shareTooltip', { share })} description={t('trophy.shareDescription')} className="inline-flex shrink-0">
          <span className="inline-flex items-center gap-1" style={{ color: 'var(--text-secondary)' }}>
            <IconUsers size={icon} aria-hidden="true" />
            <span>
              {share}
              <span className={roomy ? '' : 'sr-only'}> {t('trophy.ofPlayers')}</span>
            </span>
          </span>
        </Tooltip>
      )}
      {ach.isHidden && (
        <Tooltip label={t('trophy.hiddenAchievement')} className="inline-flex shrink-0">
          <span className="inline-flex items-center gap-1" style={{ color: 'var(--text-muted)' }}>
            <IconEyeOff size={icon} aria-hidden="true" />
            {t('trophy.secret')}
          </span>
        </Tooltip>
      )}
      {ach.isTranslated && (
        <Tooltip label={t('trophy.translated.tooltip')} className="inline-flex shrink-0">
          <span className="inline-flex items-center gap-1" style={{ color: 'var(--text-muted)' }}>
            <IconLanguage size={icon} aria-hidden="true" />
            {t('trophy.translated.badge')}
          </span>
        </Tooltip>
      )}
      {unlock && (ach.achieved ? <UnlockLine ach={ach} /> : <span className="sr-only">{t('trophy.stillOpen')}</span>)}
    </div>
  )
}

// Title on one line with an ellipsis; our tooltip shows the full name, but only while it is actually cut off
function TruncatedTitle({ text, expanded = false }: { text: string; expanded?: boolean }) {
  const ref = useRef<HTMLHeadingElement>(null)
  const [cut, setCut] = useState(false)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setCut(el.scrollWidth > el.clientWidth + 1)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [text, expanded])

  return (
    <Tooltip label={text} disabled={!cut || expanded} className="flex min-w-0 flex-1">
      <h4
        ref={ref}
        className={`min-w-0 flex-1 text-sm font-medium leading-5 ${expanded ? 'break-words' : 'truncate'}`}
        style={{ color: 'var(--text-primary)' }}
      >
        {text}
      </h4>
    </Tooltip>
  )
}

// Progress of a counter achievement: bar plus "244 / 10.000 (2 %)"
export function AchievementProgress({ current, max }: { current: number; max: number }) {
  const { t, number } = useTrophyText()
  const fmt = useFormat()
  const percent = Math.min(100, Math.floor((current / max) * 100))
  return (
    <div className="space-y-1" aria-label={t('trophy.progress.ariaLabel', { current: number(current), max: number(max) })}>
      <div className="flex flex-wrap items-center justify-between gap-x-2 text-[11px] font-medium" style={{ color: 'var(--text-secondary)' }}>
        <span>{t('trophy.progress.label')}</span>
        <span style={{ color: 'var(--accent-crystal)' }}>
          {number(current)} / {number(max)} ({fmt.percent(percent)})
        </span>
      </div>
      <ProgressBar percent={percent} label={t('trophy.progress.label')} />
    </div>
  )
}

function CoverNotice({ onReveal }: { onReveal: () => void }) {
  const { t } = useTranslation('live')
  return (
    <div
      className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-2.5 py-1.5 text-xs"
      style={{ background: 'var(--bg-elevated)', borderRadius: ICON_RADIUS }}
    >
      <span className="italic" style={{ color: 'var(--text-muted)' }}>
        {t('trophy.hiddenAchievement')}
      </span>
      <button
        type="button"
        onClick={onReveal}
        className="inline-flex cursor-pointer items-center gap-1 font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]"
        style={{ color: 'var(--accent-purple)' }}
      >
        <IconEye size={13} /> {t('trophy.reveal')}
      </button>
    </div>
  )
}

// The note action everywhere it appears (Kompakt and Groß): a quiet icon button in the top right corner of the card with
// our own tooltip. With a note it turns blue and carries a dot, so cards with notes stand out without any extra row.
function NoteIconButton({ ach, onNote }: { ach: LiveAchievementItemDto; onNote: () => void }) {
  const { t } = useTranslation('live')
  return (
    <GhostIconButton
      label={ach.userNote ? t('trophy.note.change') : t('trophy.note.add')}
      onClick={onNote}
      tone={ach.userNote ? 'accent' : 'muted'}
      dot={Boolean(ach.userNote)}
      className="-my-0.5 inline-flex shrink-0"
    >
      <IconNote size={16} aria-hidden="true" />
    </GhostIconButton>
  )
}

// Unlock state in one line. Short form (compact view): check and date only; the spoken text still says "Unlocked on".
function UnlockLine({ ach, roomy = false }: { ach: LiveAchievementItemDto; roomy?: boolean }) {
  const { t, unlockStamp } = useTrophyText()
  if (!ach.achieved) return <span style={{ color: 'var(--text-muted)' }}>{t('trophy.stillOpen')}</span>
  const stamp = unlockStamp(ach)
  return (
    <span className="inline-flex items-center gap-1 font-medium" style={{ color: 'var(--accent-emerald)' }}>
      <IconCheck size={12} stroke={2.6} aria-hidden="true" />
      {stamp === null ? (
        t('trophy.unlocked')
      ) : roomy ? (
        t('trophy.unlockedOn', { date: stamp })
      ) : (
        <>
          <span className="sr-only">{t('trophy.unlockedOn', { date: stamp })}</span>
          <span aria-hidden="true">{stamp}</span>
        </>
      )}
    </span>
  )
}

const cardBackground = (ach: LiveAchievementItemDto) => (ach.achieved ? 'var(--accent-emerald-bg)' : 'var(--bg-surface)')

// ---------------------------------------------------------------------------------------------------------------
// Groß: the full card, a little tighter than before (also the content of the detail dialog)
// ---------------------------------------------------------------------------------------------------------------

export function LargeCard({
  ach,
  tip,
  covered,
  showOriginal,
  onReveal,
  onToggleOriginal,
  notesEnabled,
  onNote,
  pin,
  bare = false,
}: Omit<TrophyItemProps, 'onOpen'> & { bare?: boolean }) {
  const { t } = useTrophyText()
  return (
    <div
      className={bare ? 'flex min-w-0 gap-3.5' : 'pv-card flex min-w-0 gap-3 !p-3.5'}
      style={bare ? undefined : { background: cardBackground(ach), borderRadius: CARD_RADIUS }}
    >
      <HeroIcon ach={ach} bare={bare} />

      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <div className="flex items-start gap-2">
          <h4 className="min-w-0 flex-1 break-words text-[0.95rem] font-medium leading-snug" style={{ color: 'var(--text-primary)' }}>
            {ach.displayName}
          </h4>
          {pin && <PinIconButton name={ach.displayName} pin={pin} />}
          {notesEnabled && <NoteIconButton ach={ach} onNote={onNote} />}
        </div>

        <MetaLine ach={ach} roomy />

        {covered ? (
          <CoverNotice onReveal={onReveal} />
        ) : (
          <div className="min-w-0">
            <p className="break-words text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
              {showOriginal && ach.originalDescription
                ? ach.originalDescription
                : ach.description || t('trophy.noDescription')}
            </p>
            {tip && (
              <p className="mt-1 break-words text-xs leading-relaxed" style={{ color: 'var(--text-primary)' }}>
                <strong className="font-medium" style={{ color: 'var(--accent-emerald)' }}>{t('trophy.howTo')} </strong>
                {tip}
              </p>
            )}
            {ach.isTranslated && ach.originalDescription && (
              <button
                type="button"
                onClick={onToggleOriginal}
                className="mt-0.5 cursor-pointer text-[11px] underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]"
                style={{ color: 'var(--text-muted)' }}
              >
                {showOriginal ? t('trophy.translated.showTranslation') : t('trophy.translated.showOriginal')}
              </button>
            )}
          </div>
        )}

        {hasCounter(ach) && <AchievementProgress current={ach.progressCurrent ?? 0} max={ach.progressMax ?? 1} />}

        {notesEnabled && ach.userNote && (
          <div className="pv-alert pv-alert-crystal flex flex-wrap items-center justify-between gap-x-3 gap-y-1 !px-3 !py-1.5 text-xs">
            <span className="min-w-0 break-words italic">{t('trophy.noteQuote', { note: ach.userNote })}</span>
          </div>
        )}

        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 pt-0.5 text-[11px]">
          <UnlockLine ach={ach} roomy />
        </div>
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// Kompakt: dense and calm. Title on one line, one quiet meta line (tier, rarity, secret, translated, unlock date),
// description in two lines, quiet icon buttons (tip, note) at the end of the title row, tight radii.
// ---------------------------------------------------------------------------------------------------------------

function CompactImage({ ach, size }: { ach: LiveAchievementItemDto; size: number }) {
  return (
    <img
      src={ach.achieved ? ach.iconUrl : ach.iconGrayUrl || ach.iconUrl}
      alt=""
      className="object-cover"
      style={{
        inlineSize: size,
        blockSize: size,
        borderRadius: ICON_RADIUS,
        filter: ach.achieved ? 'none' : 'grayscale(85%) opacity(75%)',
        boxShadow: '0 1px 3px rgb(0 0 0 / 0.28)',
      }}
    />
  )
}

// Title row: title, then the tip toggle and the note button, all in one line
function CompactHead({
  ach,
  tip,
  covered,
  expanded,
  tipOpen,
  onToggleTip,
  notesEnabled,
  onNote,
  pin,
}: {
  ach: LiveAchievementItemDto
  tip?: string
  covered: boolean
  expanded: boolean
  tipOpen: boolean
  onToggleTip: () => void
  notesEnabled: boolean
  onNote: () => void
  pin?: PinControl
}) {
  const { t } = useTranslation('live')
  return (
    <div className="flex items-start gap-1">
      <TruncatedTitle text={ach.displayName} expanded={expanded} />
      {tip && !covered && (
        <GhostIconButton
          label={tipOpen ? t('trophy.tip.hide') : t('trophy.tip.show')}
          onClick={onToggleTip}
          expanded={tipOpen}
          tone={tipOpen ? 'accent' : 'muted'}
          className="-my-0.5 inline-flex shrink-0"
        >
          <IconBulb size={16} aria-hidden="true" />
        </GhostIconButton>
      )}
      {pin && <PinIconButton name={ach.displayName} pin={pin} />}
      {notesEnabled && <NoteIconButton ach={ach} onNote={onNote} />}
    </div>
  )
}

// Description, tip, counter and note: the part below the meta line
function CompactDetails({
  ach,
  tip,
  covered,
  showOriginal,
  onReveal,
  expanded,
  onToggleExpanded,
  tipOpen,
  notesEnabled,
}: {
  ach: LiveAchievementItemDto
  tip?: string
  covered: boolean
  showOriginal: boolean
  onReveal: () => void
  expanded: boolean
  onToggleExpanded: () => void
  tipOpen: boolean
  notesEnabled: boolean
}) {
  const { t } = useTranslation('live')
  return (
    <>
      {covered ? (
        <CoverNotice onReveal={onReveal} />
      ) : (
        <>
          <p
            className={`break-words text-xs leading-[1.4] ${expanded ? '' : 'line-clamp-2'}`}
            style={{ color: 'var(--text-secondary)', cursor: 'pointer' }}
            onClick={onToggleExpanded}
          >
            {showOriginal && ach.originalDescription ? ach.originalDescription : ach.description || t('trophy.noDescription')}
          </p>
          {tip && tipOpen && (
            <p className="break-words text-xs leading-[1.4]" style={{ color: 'var(--text-primary)' }}>
              <strong className="font-medium" style={{ color: 'var(--accent-emerald)' }}>{t('trophy.howTo')} </strong>
              {tip}
            </p>
          )}
        </>
      )}

      {hasCounter(ach) && <AchievementProgress current={ach.progressCurrent ?? 0} max={ach.progressMax ?? 1} />}

      {notesEnabled && ach.userNote && (
        <p className="line-clamp-1 break-words text-[11px] italic" style={{ color: 'var(--accent-crystal)' }}>
          {t('trophy.noteQuote', { note: ach.userNote })}
        </p>
      )}
    </>
  )
}

export type CompactProps = Omit<TrophyItemProps, 'onOpen' | 'onToggleOriginal'>

// Kompakt: the icon is a panel flush with the left, top and bottom edge of the card, as tall as the card, with the image
// centred on a green-tinted ground (a deeper green than the mint of the text area, so the cut between image strip and
// text stays visible; grey while locked); the unlock marker is the green tag in that panel. No bottom row at all: the unlock date
// closes the meta line. The card is as high as its content (description at most two lines).
export function CompactCard({ ach, tip, covered, showOriginal, onReveal, notesEnabled, onNote, pin }: CompactProps) {
  const [expanded, setExpanded] = useState(false)
  const [tipOpen, setTipOpen] = useState(false)

  return (
    <div className="pv-card flex min-w-0 overflow-hidden !p-0" style={{ background: cardBackground(ach), borderRadius: CARD_RADIUS }}>
      <div
        className="relative flex w-[5.25rem] shrink-0 items-center justify-center self-stretch"
        style={{
          background: ach.achieved ? 'color-mix(in srgb, var(--accent-emerald) 24%, var(--bg-elevated))' : 'var(--bg-elevated)',
          borderInlineEnd: `1px solid ${ach.achieved ? 'color-mix(in srgb, var(--accent-emerald) 45%, var(--border-subtle))' : 'var(--border-subtle)'}`,
        }}
      >
        <CompactImage ach={ach} size={56} />
        {ach.achieved && <UnlockTag size={20} />}
      </div>

      <div className="flex min-w-0 flex-1 flex-col gap-1 px-3 py-2.5">
        <CompactHead
          ach={ach}
          tip={tip}
          covered={covered}
          expanded={expanded}
          tipOpen={tipOpen}
          onToggleTip={() => setTipOpen((v) => !v)}
          notesEnabled={notesEnabled}
          onNote={onNote}
          pin={pin}
        />
        <MetaLine ach={ach} unlock />
        <CompactDetails
          ach={ach}
          tip={tip}
          covered={covered}
          showOriginal={showOriginal}
          onReveal={onReveal}
          expanded={expanded}
          onToggleExpanded={() => setExpanded((v) => !v)}
          tipOpen={tipOpen}
          notesEnabled={notesEnabled}
        />
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// Kacheln: icon tiles in a dense grid; everything else in our tooltip (hover, focus) and the detail dialog (click)
// ---------------------------------------------------------------------------------------------------------------

export function TrophyTile({ ach, tip, covered, notesEnabled, onOpen, pin }: TrophyItemProps) {
  const { t, tierName, percentText, detailText } = useTrophyText()
  return (
    <div className="relative flex min-w-0">
    <Tooltip label={ach.displayName} description={detailText(ach, tip, covered, notesEnabled)} className="flex min-w-0 flex-1">
      <button
        type="button"
        onClick={onOpen}
        aria-label={t(ach.achieved ? 'trophy.itemLabelUnlocked' : 'trophy.itemLabelOpen', { name: ach.displayName, tier: tierName(ach.rarityTier) })}
        className="pv-card flex w-full min-w-0 cursor-pointer flex-col items-center gap-1.5 !p-2.5 text-center focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]"
        style={{ background: cardBackground(ach), boxShadow: 'none', borderRadius: CARD_RADIUS }}
      >
        <AchievementIcon ach={ach} size={56} center />
        <span
          className="line-clamp-2 min-h-[2.1rem] break-words text-[0.78rem] font-normal leading-tight"
          style={{ color: 'var(--text-primary)' }}
        >
          {ach.displayName}
        </span>
        <span className="flex flex-wrap items-center justify-center gap-x-1.5 gap-y-0.5 text-[10.5px] font-medium">
          <span className="inline-flex items-center gap-0.5" style={{ color: tierTextVar(ach.rarityTier) }}>
            <TierIcon tier={ach.rarityTier} size={10} />
            {tierName(ach.rarityTier)}
          </span>
          {percentText(ach) !== null && <span style={{ color: 'var(--text-secondary)' }}>{percentText(ach)}</span>}
        </span>
        {/* Reserved even without a counter, so every tile in a row has the same height */}
        <span className="block h-1 w-full" aria-hidden="true">
          {hasCounter(ach) && (
            <span className="block h-full w-full overflow-hidden rounded-full" style={{ background: 'var(--bg-elevated)' }}>
              <span
                className="block h-full"
                style={{
                  width: `${Math.min(100, Math.floor(((ach.progressCurrent ?? 0) / (ach.progressMax ?? 1)) * 100))}%`,
                  background: 'var(--accent-crystal)',
                }}
              />
            </span>
          )}
        </span>
      </button>
    </Tooltip>
    {/* In the corner of the tile, beside the centred icon: the tile keeps its size */}
    {pin && <PinIconButton name={ach.displayName} pin={pin} className="absolute end-1 top-1 inline-flex" />}
    </div>
  )
}

// Tier colour for text (gold is the darker text tone so it stays readable on the mint cards of unlocked trophies)
function tierTextVar(tier: RarityTier): string {
  return `var(--tier-${tier.toLowerCase()}-text)`
}

export function tierColorVar(tier: RarityTier): string {
  return TIER_ICON_COLORS[tier]
}

// ---------------------------------------------------------------------------------------------------------------
// Liste: one row per achievement
// ---------------------------------------------------------------------------------------------------------------

export function TrophyRow({ ach, tip, covered, notesEnabled, onOpen, pin }: TrophyItemProps) {
  const { t, tierName, number, percentText, unlockStamp, detailText } = useTrophyText()
  const unlocked = ach.achieved ? (unlockStamp(ach) ?? t('trophy.unlocked')) : null
  return (
    <div className="relative flex min-w-0">
    <Tooltip label={ach.displayName} description={detailText(ach, tip, covered, notesEnabled)} className="flex min-w-0 flex-1">
      <button
        type="button"
        onClick={onOpen}
        aria-label={t(ach.achieved ? 'trophy.itemLabelUnlocked' : 'trophy.itemLabelOpen', { name: ach.displayName, tier: tierName(ach.rarityTier) })}
        className={`pv-card flex min-h-[3.25rem] w-full min-w-0 cursor-pointer items-center gap-3 !px-3 !py-1.5 text-left ${pin ? '!pe-11' : ''} focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]`}
        style={{ background: cardBackground(ach), boxShadow: 'none', borderRadius: CARD_RADIUS }}
      >
        <AchievementIcon ach={ach} size={36} />
        <span className="line-clamp-2 min-w-0 flex-1 break-words text-sm font-normal leading-snug" style={{ color: 'var(--text-primary)' }}>
          {ach.displayName}
        </span>
        <span className="flex shrink-0 items-center gap-2">
          <TierBadge tier={ach.rarityTier} />
          <span className="w-[3.4rem] text-right text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
            {percentText(ach)}
          </span>
        </span>
        <span className="hidden w-[7.5rem] shrink-0 sm:block">
          {hasCounter(ach) && (
            <span className="block">
              <span className="block text-right text-[10.5px] font-bold" style={{ color: 'var(--accent-crystal)' }}>
                {number(ach.progressCurrent ?? 0)} / {number(ach.progressMax ?? 0)}
              </span>
              <span className="block h-1 overflow-hidden rounded-full" style={{ background: 'var(--bg-elevated)' }}>
                <span
                  className="block h-full"
                  style={{
                    width: `${counterPercent(ach.progressCurrent ?? 0, ach.progressMax ?? 1)}%`,
                    background: 'var(--accent-crystal)',
                  }}
                />
              </span>
            </span>
          )}
        </span>
        <span
          className="hidden w-[9.5rem] shrink-0 text-right text-[11px] md:block"
          style={{ color: unlocked ? 'var(--accent-emerald)' : 'var(--text-muted)', fontWeight: unlocked ? 700 : 400 }}
        >
          {unlocked ? `✓ ${unlocked}` : t('trophy.stillOpen')}
        </span>
      </button>
    </Tooltip>
    {/* Vertically centred at the end of the row, in the room the row keeps free for it */}
    {pin && <PinIconButton name={ach.displayName} pin={pin} className="absolute end-2.5 top-1/2 inline-flex -translate-y-1/2" />}
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// Detail dialog for tiles and list rows (also the way to everything on touch screens)
// ---------------------------------------------------------------------------------------------------------------

export function TrophyDetailDialog({ item, onClose }: { item: TrophyItemProps; onClose: () => void }) {
  const { t } = useTranslation('common')
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    <div className="pv-modal-backdrop" onClick={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={item.ach.displayName}
        className="pv-modal relative w-full max-w-lg p-5"
        onClick={(e) => e.stopPropagation()}
        style={{ background: cardBackground(item.ach) }}
      >
        <button type="button" onClick={onClose} className="pv-btn absolute right-3 top-3 !p-1.5" aria-label={t('close')} autoFocus>
          <IconX size={16} />
        </button>
        <div className="pr-9">
          <LargeCard {...item} bare />
        </div>
      </div>
    </div>
  )
}
