import { useMemo, useState } from 'react'
import type { ComponentType, CSSProperties, ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import {
  IconAlertTriangle,
  IconBook,
  IconBook2,
  IconChartBar,
  IconCheck,
  IconClock,
  IconDeviceGamepad2,
  IconExternalLink,
  IconEye,
  IconEyeOff,
  IconPlayerPlay,
  IconQuestionMark,
  IconRocket,
  IconSparkles,
  IconStar,
  IconStarFilled,
  IconTrophy,
  IconUsers,
  IconWand,
  IconLoader2,
} from '@tabler/icons-react'
import type { Icon } from '@tabler/icons-react'
import { fetchBridgeTokenStatus, fetchSettings } from '../../api/client'
import type { LiveAchievementItemDto, NotificationPosition, RarityTier } from '../../api/types'
import { animationQueue } from '../../utils/animationQueue'
import { CardMeta, GuideBadge } from '../GameCardParts'
import { StatTile } from '../GameStatsPanel'
import { useReadsSteamLocally } from '../../utils/useReadsSteamLocally'
import { ProgressBar } from '../ui/ProgressBar'
import { TierIcon } from '../ui/TierIcon'
import { TROPHY_TIER_COLORS, TrophyCard } from '../TrophyNotification'

// Tabs of the settings modal a checklist row can point to
export type TourSettingsTab = 'live' | 'steam' | 'ki' | 'twitch' | 'bridge'

export interface TourStepContext {
  // Opens the settings modal of the page the tour runs on, at the tab that belongs to the clicked row
  onOpenSettings: (tab?: TourSettingsTab) => void
}

export interface TourStep {
  id: string
  title: string
  accent: 'crystal' | 'emerald' | 'gold' | 'purple'
  icon: Icon
  intro: string
  points: { lead: string; text: string }[]
  Visual: ComponentType<TourStepContext>
}

// All texts of the tour live in the "onboarding" namespace under steps.<step>.*
type OnboardingT = TFunction<'onboarding'>

// Staggered entrance of the pieces inside a step's visual
function Reveal({ i = 0, className, children }: { i?: number; className?: string; children: ReactNode }) {
  return (
    <motion.div
      className={className}
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35, delay: 0.1 + i * 0.07, ease: [0.22, 1, 0.36, 1] }}
    >
      {children}
    </motion.div>
  )
}

// A button-looking element that only illustrates where to click in the real app
function FakeButton({ className, children }: { className: string; children: ReactNode }) {
  return (
    <span className={`pv-btn pointer-events-none ${className}`} aria-hidden="true">
      {children}
    </span>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// 1. Welcome
// ---------------------------------------------------------------------------------------------------------------

const DEMO_ACHIEVEMENTS: { id: 'bronze' | 'silver' | 'gold'; tier: RarityTier; percent: number }[] = [
  { id: 'bronze', tier: 'Bronze', percent: 42.5 },
  { id: 'silver', tier: 'Silver', percent: 14.8 },
  { id: 'gold', tier: 'Gold', percent: 2.4 },
]

// Example unlock for the mock popup and the "show example popup" button; nothing here touches the server.
function demoAchievement(index: number, t: OnboardingT): LiveAchievementItemDto {
  const demo = DEMO_ACHIEVEMENTS[index % DEMO_ACHIEVEMENTS.length]!
  return {
    apiName: 'PLATINVAULT_TOUR_DEMO',
    displayName: t(`steps.live.visual.demo.${demo.id}.name`),
    description: t(`steps.live.visual.demo.${demo.id}.description`),
    originalDescription: null,
    isTranslated: false,
    iconUrl: '/favicon.svg',
    iconGrayUrl: '',
    achieved: true,
    unlockTime: new Date().toISOString(),
    globalPercent: demo.percent,
    hasGlobalPercent: true,
    rarityTier: demo.tier,
    rarityLabel: t(`steps.live.visual.demo.${demo.id}.label`),
    isHidden: false,
    userNote: '',
  }
}

function WelcomeVisual() {
  const { t } = useTranslation('onboarding')
  return (
    <div className="flex h-full flex-col justify-center gap-4">
      <Reveal i={0} className="flex flex-wrap gap-2">
        <span className="stat-pill crystal">
          <IconDeviceGamepad2 size={15} />
          {t('steps.welcome.visual.library')}
        </span>
        <span className="stat-pill safe">
          <IconCheck size={15} />
          {t('steps.welcome.visual.points')}
        </span>
        <span className="stat-pill gold">
          <IconTrophy size={15} />
          {t('steps.welcome.visual.platinum')}
        </span>
      </Reveal>

      <Reveal i={1} className="pv-card space-y-2 !p-4">
        <div className="flex items-center justify-between text-xs font-extrabold">
          <span style={{ color: 'var(--text-secondary)' }}>{t('steps.welcome.visual.progress')}</span>
          <span style={{ color: 'var(--accent-emerald)' }}>{t('steps.welcome.visual.progressValue')}</span>
        </div>
        <ProgressBar percent={60} label={t('steps.welcome.visual.progress')} />
      </Reveal>

      <Reveal i={2} className="space-y-2">
        <div className="text-xs font-extrabold uppercase tracking-wider" style={{ color: 'var(--text-muted)' }}>
          {t('steps.welcome.visual.popupExample')}
        </div>
        <TrophyCard achievement={demoAchievement(2, t)} fluid />
      </Reveal>
    </div>
  )
}

// -----------------------------------------------------------------------------------------------------------------
// 2. Games on the dashboard (they come from the Steam library; the star brings one onto the dashboard)
// -----------------------------------------------------------------------------------------------------------------

function AddGamesVisual() {
  const { t } = useTranslation('onboarding')
  return (
    <div className="flex h-full flex-col justify-center gap-3">
      {/* The switch of the dashboard: "All games" is where the star lives (an illustration, nothing here can be clicked) */}
      <Reveal i={0}>
        <div className="pv-segmented pointer-events-none w-full" aria-hidden="true">
          <button type="button" tabIndex={-1} aria-pressed={false} className="flex-1 justify-center">
            <IconStarFilled size={14} />
            <span>{t('steps.games.visual.favorites')}</span>
          </button>
          <button type="button" tabIndex={-1} aria-pressed={true} className="flex-1 justify-center">
            <IconDeviceGamepad2 size={14} />
            <span>{t('steps.games.visual.allGames')}</span>
          </button>
        </div>
      </Reveal>

      <Reveal i={1}>
        <div
          className="flex items-center gap-3 px-3 py-2"
          style={{ background: 'var(--bg-surface)', border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-md)' }}
          aria-hidden="true"
        >
          <IconDeviceGamepad2 size={20} className="shrink-0" style={{ color: 'var(--accent-crystal)' }} />
          <span className="min-w-0 flex-1 truncate text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
            Elden Ring
          </span>
          <span className="shrink-0 text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
            {t('steps.games.visual.starRow')}
          </span>
          <span
            className="flex h-8 w-8 shrink-0 items-center justify-center"
            style={{
              background: 'var(--accent-gold-bg)',
              color: 'var(--accent-gold)',
              border: '1px solid var(--accent-gold-border)',
              borderRadius: 'var(--radius-md)',
            }}
          >
            <IconStarFilled size={16} />
          </span>
        </div>
      </Reveal>

      <Reveal i={2}>
        <div
          className="mx-auto w-full max-w-[16.5rem] overflow-hidden"
          style={{
            background: 'var(--bg-surface)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-lg)',
            boxShadow: 'var(--shadow-card)',
          }}
        >
          <div
            className="relative aspect-[2/1] w-full overflow-hidden"
            style={{ background: 'radial-gradient(circle at top, var(--accent-crystal-bg), var(--bg-elevated))' }}
          >
            <div className="flex h-full items-center justify-center">
              <IconDeviceGamepad2 size={40} style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
            </div>
            <div className="absolute left-2.5 right-2.5 top-2.5 flex items-center justify-between gap-2">
              <GuideBadge tabCount={9} />
            </div>
          </div>

          <div className="space-y-2.5 p-3.5">
            <div className="space-y-0.5">
              <div className="text-[11px] font-extrabold uppercase tracking-wider" style={{ color: 'var(--accent-emerald)' }}>
                {t('steps.games.visual.genre')}
              </div>
              <div className="text-sm font-extrabold leading-snug" style={{ color: 'var(--text-primary)' }}>
                Elden Ring
              </div>
              <p className="line-clamp-2 text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                {t('steps.games.visual.tags')}
              </p>
            </div>

            <dl className="space-y-1 text-xs">
              <CardMeta
                icon={<IconClock size={14} />}
                color="var(--accent-crystal)"
                label={t('steps.games.visual.playtime')}
                value={t('steps.games.visual.playtimeValue')}
              />
            </dl>

            <div>
              <div className="mb-1 flex items-center justify-between text-xs font-extrabold">
                <span style={{ color: 'var(--text-secondary)' }}>{t('steps.games.visual.progress')}</span>
                <span style={{ color: 'var(--accent-emerald)' }}>{t('steps.games.visual.progressValue')}</span>
              </div>
              <ProgressBar percent={30} label={t('steps.games.visual.progress')} />
            </div>
          </div>
        </div>
      </Reveal>

      <p className="min-h-10 text-center text-xs leading-snug" style={{ color: 'var(--text-secondary)' }}>
        {t('steps.games.visual.caption')}
      </p>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// 3. The guide
// ---------------------------------------------------------------------------------------------------------------

// Example chapters of the mock guide; titles and rows are texts (onboarding:steps.guide.visual.tabs.<id>)
const GUIDE_TAB_IDS = ['weapons', 'armor', 'tactics', 'bestiary'] as const
const GUIDE_ROW_IDS = ['r1', 'r2', 'r3', 'r4'] as const
const TOPIC_IDS = ['weapons', 'armor', 'tactics', 'bestiary', 'levels', 'collectibles'] as const

function GuideVisual() {
  const { t } = useTranslation('onboarding')
  const [activeId, setActiveId] = useState<(typeof GUIDE_TAB_IDS)[number]>('weapons')
  const [checked, setChecked] = useState<Record<string, boolean>>({ 'weapons-0': true, 'weapons-1': true, 'armor-0': true })
  const activeTitle = t(`steps.guide.visual.tabs.${activeId}.title`)

  return (
    <div className="flex h-full flex-col justify-center gap-3">
      <Reveal i={0} className="flex flex-wrap gap-2">
        <FakeButton className="pv-btn-emerald !py-1.5 !text-xs">
          <IconWand size={14} />
          <span>{t('steps.guide.visual.complete')}</span>
        </FakeButton>
        <FakeButton className="!py-1.5 !text-xs">
          <IconSparkles size={14} />
          <span>{t('steps.guide.visual.addTab')}</span>
        </FakeButton>
      </Reveal>

      <Reveal
        i={1}
        className="overflow-hidden"
      >
        <div
          style={{
            background: 'var(--bg-surface)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-md)',
            overflow: 'hidden',
          }}
        >
          <div role="tablist" aria-label={t('steps.guide.visual.sampleChapters')} className="flex flex-wrap" style={{ borderBottom: '1px solid var(--border-subtle)' }}>
            {GUIDE_TAB_IDS.map((tabId, tabIndex) => {
              const done = GUIDE_ROW_IDS.filter((_, rowIndex) => checked[`${tabId}-${rowIndex}`]).length
              return (
                <button
                  key={tabId}
                  type="button"
                  role="tab"
                  aria-selected={tabId === activeId}
                  onClick={() => setActiveId(tabId)}
                  className="tab-btn !gap-1.5 !px-3 !py-2 !text-xs"
                >
                  <span className="font-extrabold">{tabIndex + 1}.</span>
                  <span>{t(`steps.guide.visual.tabs.${tabId}.title`)}</span>
                  <span className={`tab-badge ${done === GUIDE_ROW_IDS.length ? 'done' : ''}`}>
                    {done}/{GUIDE_ROW_IDS.length}
                  </span>
                </button>
              )
            })}
          </div>

          <ul className="space-y-1.5 p-2.5" role="tabpanel" aria-label={t('steps.guide.visual.chapterPanel', { title: activeTitle })}>
            {GUIDE_ROW_IDS.map((rowId, rowIndex) => {
              const key = `${activeId}-${rowIndex}`
              const isChecked = checked[key] === true
              return (
                <li key={key}>
                  <label
                    className="flex cursor-pointer items-center gap-3 rounded-lg px-3 py-2"
                    style={{
                      background: isChecked ? 'var(--accent-emerald-bg)' : 'var(--bg-elevated)',
                      border: `1px solid ${isChecked ? 'var(--accent-emerald-border)' : 'var(--border-subtle)'}`,
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={isChecked}
                      onChange={(e) => setChecked((prev) => ({ ...prev, [key]: e.target.checked }))}
                      className="h-5 w-5 shrink-0 cursor-pointer rounded"
                      style={{ accentColor: 'var(--accent-emerald)' }}
                    />
                    <span
                      className={`min-w-0 flex-1 truncate text-sm font-bold ${isChecked ? 'line-through' : ''}`}
                      style={{ color: 'var(--text-primary)', opacity: isChecked ? 0.75 : 1 }}
                    >
                      {t(`steps.guide.visual.tabs.${activeId}.rows.${rowId}.label`)}
                    </span>
                    <span className="shrink-0 text-xs font-semibold" style={{ color: 'var(--text-muted)' }}>
                      {t(`steps.guide.visual.tabs.${activeId}.rows.${rowId}.hint`)}
                    </span>
                  </label>
                </li>
              )
            })}
          </ul>
        </div>
      </Reveal>

      <Reveal i={2} className="space-y-1.5">
        <div className="text-xs font-extrabold uppercase tracking-wider" style={{ color: 'var(--text-muted)' }}>
          {t('steps.guide.visual.topicsHeading')}
        </div>
        <div className="flex flex-wrap gap-1.5" aria-hidden="true">
          {TOPIC_IDS.map((topic) => (
            <span key={topic} className="pv-badge pv-badge-a">
              {t(`steps.guide.visual.topics.${topic}`)}
            </span>
          ))}
        </div>
      </Reveal>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// 4. Live trophies
// ---------------------------------------------------------------------------------------------------------------

// Where the little popup marker sits inside the mock screen for each of the nine positions
function markerStyle(position: NotificationPosition): CSSProperties {
  // Full screen: the marker covers the whole mock screen
  if (position === 'fullscreen') return { position: 'absolute', inset: '8%' }
  const style: CSSProperties = { position: 'absolute' }
  const [row, col] = position === 'center' ? ['center', 'center'] : (position.split('-') as [string, string])
  if (row === 'top') style.top = '8%'
  else if (row === 'bottom') style.bottom = '8%'
  else style.top = '50%'
  if (col === 'left') style.left = '4%'
  else if (col === 'right') style.right = '4%'
  else style.left = '50%'
  const tx = col === 'center' ? '-50%' : '0'
  const ty = row === 'center' ? '-50%' : '0'
  if (tx !== '0' || ty !== '0') style.transform = `translate(${tx}, ${ty})`
  return style
}

const TIER_CHIPS: { tier: RarityTier; key: 'tierBronze' | 'tierSilver' | 'tierGold' | 'tierPlatinum' }[] = [
  { tier: 'Bronze', key: 'tierBronze' },
  { tier: 'Silver', key: 'tierSilver' },
  { tier: 'Gold', key: 'tierGold' },
  { tier: 'Platinum', key: 'tierPlatinum' },
]

function LiveVisual() {
  const { t } = useTranslation('onboarding')
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  const position = settings?.popupPosition ?? 'top-right'
  const positionText = t(`steps.live.visual.positions.${position}`)
  const [next, setNext] = useState(0)
  const [pulse, setPulse] = useState(0)

  // Purely local: the same queue the live unlocks use, fed with an example achievement (no server call)
  const showPopup = () => {
    animationQueue.clearQueue()
    animationQueue.enqueueAchievement(demoAchievement(next, t))
    setNext((n) => (n + 1) % DEMO_ACHIEVEMENTS.length)
    setPulse((p) => p + 1)
  }

  const showPlatinum = () => {
    animationQueue.clearQueue()
    animationQueue.enqueuePlatinum(t('steps.live.visual.exampleGame'), 42)
  }

  return (
    <div className="flex h-full flex-col justify-center gap-3.5">
      <Reveal i={0}>
        <div
          className="relative mx-auto aspect-[16/8] w-full max-w-[26rem] overflow-hidden"
          style={{
            background: 'radial-gradient(circle at 30% 20%, var(--accent-crystal-bg), var(--bg-surface))',
            border: '1px solid var(--border-strong)',
            borderRadius: 'var(--radius-md)',
          }}
          role="img"
          aria-label={t('steps.live.visual.screenLabel', { position: positionText })}
        >
          <div className="flex h-full items-center justify-center" aria-hidden="true">
            <IconDeviceGamepad2 size={44} style={{ color: 'var(--text-muted)', opacity: 0.55 }} />
          </div>
          <motion.div
            key={pulse}
            initial={pulse === 0 ? false : { scale: 0.7, opacity: 0.4 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 320, damping: 18 }}
            style={{ ...markerStyle(position), ...(position === 'fullscreen' ? {} : { width: '38%' }) }}
            aria-hidden="true"
          >
            <div
              className={`flex items-center gap-1.5 rounded-lg bg-slate-950/95 px-2 py-1.5 ${
                position === 'fullscreen' ? 'h-full justify-center' : ''
              }`}
              style={{ border: `1.5px solid ${TROPHY_TIER_COLORS.Gold}`, boxShadow: `0 0 12px ${TROPHY_TIER_COLORS.Gold}55` }}
            >
              <IconTrophy size={14} style={{ color: TROPHY_TIER_COLORS.Gold }} />
              <span className="h-1.5 flex-1 rounded-full bg-slate-500/70" />
            </div>
          </motion.div>
        </div>
      </Reveal>

      <Reveal i={1} className="flex flex-wrap justify-center gap-2" >
        {TIER_CHIPS.map((chip) => (
          <span
            key={chip.tier}
            className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-extrabold"
            style={{
              background: 'var(--bg-surface)',
              border: '1px solid var(--border-subtle)',
              color: 'var(--text-primary)',
            }}
          >
            <span
              className="flex h-5 w-5 items-center justify-center rounded-full text-slate-950"
              style={{ background: TROPHY_TIER_COLORS[chip.tier] }}
              aria-hidden="true"
            >
              <TierIcon tier={chip.tier} size={12} inherit />
            </span>
            {t(`steps.live.visual.${chip.key}`)}
          </span>
        ))}
      </Reveal>

      <Reveal i={2} className="flex flex-wrap justify-center gap-2">
        <button type="button" className="pv-btn pv-btn-primary" onClick={showPopup}>
          <IconPlayerPlay size={15} />
          <span>{t('steps.live.visual.showPopup')}</span>
        </button>
        <button type="button" className="pv-btn" onClick={showPlatinum}>
          <TierIcon tier="Platinum" size={15} />
          <span>{t('steps.live.visual.showPlatinum')}</span>
        </button>
      </Reveal>

      <p className="min-h-10 text-center text-xs leading-snug" style={{ color: 'var(--text-secondary)' }} aria-live="polite">
        {t('steps.live.visual.caption', { position: positionText })}
      </p>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// 5. Statistics & Story & Lore
// ---------------------------------------------------------------------------------------------------------------

function StatsLoreVisual() {
  const { t } = useTranslation('onboarding')
  const [revealed, setRevealed] = useState(false)

  return (
    <div className="flex h-full flex-col justify-center gap-3.5">
      <Reveal i={0} className="grid grid-cols-2 gap-3">
        <StatTile
          icon={<IconClock size={15} />}
          label={t('steps.stats.visual.playtime')}
          value={t('steps.stats.visual.playtimeValue')}
          hint={t('steps.stats.visual.playtimeHint')}
        />
        <StatTile
          icon={<IconTrophy size={15} />}
          label={t('steps.stats.visual.progress')}
          value={t('steps.stats.visual.progressValue')}
          hint={t('steps.stats.visual.progressHint')}
        />
        <StatTile
          icon={<IconChartBar size={15} />}
          label={t('steps.stats.visual.rarest')}
          value={t('steps.stats.visual.rarestValue')}
          hint={t('steps.stats.visual.rarestHint')}
        />
        <StatTile
          icon={<IconUsers size={15} />}
          label={t('steps.stats.visual.players')}
          value={t('steps.stats.visual.playersValue')}
          hint={t('steps.stats.visual.playersHint')}
        />
      </Reveal>

      <Reveal i={1}>
        <div className="pv-card !p-4" style={{ minHeight: '10.5rem' }}>
          <div className="mb-2.5 flex items-center justify-between gap-2">
            <span className="inline-flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-crimson)' }}>
              <IconBook size={17} aria-hidden="true" />
              {t('steps.stats.visual.loreTitle')}
            </span>
            <span className="pv-badge pv-badge-b">{t('steps.stats.visual.optional')}</span>
          </div>

          <div className="flex min-h-[5.5rem] flex-col items-center justify-center gap-2 text-center">
            {revealed ? (
              <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                {t('steps.stats.visual.loreExample')}
              </p>
            ) : (
              <>
                <IconAlertTriangle size={22} style={{ color: 'var(--accent-gold)' }} aria-hidden="true" />
                <p className="text-xs font-bold" style={{ color: 'var(--text-primary)' }}>
                  {t('steps.stats.visual.spoilerWarning')}
                </p>
              </>
            )}
          </div>

          <div className="mt-1 flex justify-center">
            <button
              type="button"
              className="pv-btn pv-btn-gold !py-1.5 !text-xs min-w-[10.5rem] justify-center"
              onClick={() => setRevealed((r) => !r)}
            >
              {revealed ? <IconEyeOff size={14} /> : <IconEye size={14} />}
              <span>{revealed ? t('steps.stats.visual.hideAgain') : t('steps.stats.visual.showSpoiler')}</span>
            </button>
          </div>
        </div>
      </Reveal>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------
// 6. Setup (live checklist from the real settings)
// ---------------------------------------------------------------------------------------------------------------

type RowState = 'done' | 'open' | 'loading' | 'unknown'

function StatusMark({ state }: { state: RowState }) {
  const { t } = useTranslation('onboarding')
  const label = t(`steps.setup.visual.state.${state}`)
  const base = 'flex h-7 w-7 shrink-0 items-center justify-center rounded-full'
  return (
    <span
      role="img"
      aria-label={label}
      className={base}
      style={
        state === 'done'
          ? {
              background: 'var(--accent-emerald-bg)',
              color: 'var(--accent-emerald)',
              border: '1.5px solid var(--accent-emerald-border)',
            }
          : { border: '2px solid var(--border-strong)', color: 'var(--text-muted)' }
      }
    >
      {state === 'done' ? (
        <IconCheck size={16} stroke={3} />
      ) : state === 'loading' ? (
        <IconLoader2 size={14} className="animate-spin" />
      ) : state === 'unknown' ? (
        <IconQuestionMark size={14} />
      ) : null}
    </span>
  )
}

interface SetupRowProps {
  i: number
  title: string
  optional?: boolean
  state: RowState
  description: string
  link?: { href: string; label: string }
  action: string
  onAction: () => void
}

function SetupRow({ i, title, optional = false, state, description, link, action, onAction }: SetupRowProps) {
  const { t } = useTranslation('onboarding')
  return (
    <Reveal i={i}>
      <div className="pv-card flex min-h-[4.75rem] items-center gap-3 !p-2.5">
        <StatusMark state={state} />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
            <span>{title}</span>
            {optional && <span className="pv-badge pv-badge-b !py-0 !text-[10px]">{t('steps.setup.visual.optional')}</span>}
          </div>
          <p className="line-clamp-2 text-xs leading-snug" style={{ color: 'var(--text-secondary)' }}>
            {description}
          </p>
          {link && (
            <a
              href={link.href}
              target="_blank"
              rel="noreferrer"
              className="mt-0.5 inline-flex items-center gap-1 text-xs font-bold hover:underline"
              style={{ color: 'var(--accent-crystal)' }}
            >
              {link.label}
              <IconExternalLink size={12} aria-hidden="true" />
            </a>
          )}
        </div>
        <button
          type="button"
          className={`pv-btn !py-1.5 !text-xs min-w-[6.25rem] shrink-0 justify-center ${state === 'done' ? '' : 'pv-btn-primary'}`}
          onClick={onAction}
        >
          {action}
        </button>
      </div>
    </Reveal>
  )
}

function SetupVisual({ onOpenSettings }: TourStepContext) {
  const { t } = useTranslation('onboarding')
  const settingsQuery = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  const bridgeQuery = useQuery({ queryKey: ['bridge-token'], queryFn: fetchBridgeTokenStatus })
  const settings = settingsQuery.data
  const bridge = bridgeQuery.data
  // No bridge needed where PlatinVault reads Steam itself (Windows program, native Linux package with a Steam folder): the row is left out
  const readsSteamLocally = useReadsSteamLocally()

  const settingsState = (done: boolean): RowState =>
    settingsQuery.isLoading ? 'loading' : settings ? (done ? 'done' : 'open') : 'unknown'
  const bridgeState: RowState = bridgeQuery.isLoading ? 'loading' : bridge ? (bridge.hasToken ? 'done' : 'open') : 'unknown'

  const hasAiKey = Boolean(settings?.hasGeminiKey || settings?.hasClaudeKey || settings?.hasOpenAiKey)
  const change = t('steps.setup.visual.change')

  return (
    <div className="flex h-full flex-col justify-center gap-2.5">
      <SetupRow
        i={0}
        title={t('steps.setup.visual.steamAccount.title')}
        state={settingsState(Boolean(settings?.steamUserId))}
        description={
          settings?.steamUserId
            ? t('steps.setup.visual.steamAccount.linked', { id: settings.steamUserId })
            : t('steps.setup.visual.steamAccount.open')
        }
        action={settings?.steamUserId ? change : t('steps.setup.visual.steamAccount.actionLink')}
        onAction={() => onOpenSettings('steam')}
      />
      <SetupRow
        i={1}
        title={t('steps.setup.visual.steamKey.title')}
        state={settingsState(Boolean(settings?.hasSteamKey))}
        description={
          settings?.hasSteamKey
            ? t('steps.setup.visual.steamKey.saved', { hint: settings.steamKeyHint })
            : t('steps.setup.visual.steamKey.open')
        }
        action={settings?.hasSteamKey ? change : t('steps.setup.visual.steamKey.action')}
        onAction={() => onOpenSettings('steam')}
      />
      <SetupRow
        i={2}
        title={t('steps.setup.visual.aiKey.title')}
        state={settingsState(hasAiKey)}
        description={hasAiKey ? t('steps.setup.visual.aiKey.saved') : t('steps.setup.visual.aiKey.open')}
        link={{ href: 'https://aistudio.google.com/apikey', label: t('steps.setup.visual.aiKey.link') }}
        action={hasAiKey ? change : t('steps.setup.visual.aiKey.action')}
        onAction={() => onOpenSettings('ki')}
      />
      {!readsSteamLocally && (
        <SetupRow
          i={3}
          title={t('steps.setup.visual.bridge.title')}
          optional
          state={bridgeState}
          description={bridge?.hasToken ? t('steps.setup.visual.bridge.saved') : t('steps.setup.visual.bridge.open')}
          action={bridge?.hasToken ? change : t('steps.setup.visual.bridge.action')}
          onAction={() => onOpenSettings('bridge')}
        />
      )}
      <SetupRow
        i={4}
        title={t('steps.setup.visual.igdb.title')}
        optional
        state={settingsState(Boolean(settings?.hasTwitchKey))}
        description={
          settings?.hasTwitchKey
            ? t('steps.setup.visual.igdb.saved', { hint: settings.twitchKeyHint })
            : t('steps.setup.visual.igdb.open')
        }
        action={settings?.hasTwitchKey ? change : t('steps.setup.visual.igdb.action')}
        onAction={() => onOpenSettings('twitch')}
      />
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------

/** The steps of the intro tour in the active language (rebuilt when the language changes). */
export function useTourSteps(): TourStep[] {
  const { t } = useTranslation('onboarding')
  // `t` is a new function whenever the language changes, so the steps are rebuilt then
  return useMemo(
    () => [
      {
        id: 'welcome',
        title: t('steps.welcome.title'),
        accent: 'crystal',
        icon: IconTrophy,
        intro: t('steps.welcome.intro'),
        points: [
          { lead: t('steps.welcome.points.live.lead'), text: t('steps.welcome.points.live.text') },
          { lead: t('steps.welcome.points.guides.lead'), text: t('steps.welcome.points.guides.text') },
          { lead: t('steps.welcome.points.stats.lead'), text: t('steps.welcome.points.stats.text') },
        ],
        Visual: WelcomeVisual,
      },
      {
        id: 'games',
        title: t('steps.games.title'),
        accent: 'emerald',
        icon: IconStar,
        intro: t('steps.games.intro'),
        points: [
          { lead: t('steps.games.points.account.lead'), text: t('steps.games.points.account.text') },
          { lead: t('steps.games.points.key.lead'), text: t('steps.games.points.key.text') },
          { lead: t('steps.games.points.star.lead'), text: t('steps.games.points.star.text') },
        ],
        Visual: AddGamesVisual,
      },
      {
        id: 'guide',
        title: t('steps.guide.title'),
        accent: 'emerald',
        icon: IconBook2,
        intro: t('steps.guide.intro'),
        points: [
          { lead: t('steps.guide.points.chapters.lead'), text: t('steps.guide.points.chapters.text') },
          { lead: t('steps.guide.points.checks.lead'), text: t('steps.guide.points.checks.text') },
          { lead: t('steps.guide.points.complete.lead'), text: t('steps.guide.points.complete.text') },
          { lead: t('steps.guide.points.addTab.lead'), text: t('steps.guide.points.addTab.text') },
        ],
        Visual: GuideVisual,
      },
      {
        id: 'live',
        title: t('steps.live.title'),
        accent: 'gold',
        icon: IconTrophy,
        intro: t('steps.live.intro'),
        points: [
          { lead: t('steps.live.points.tiers.lead'), text: t('steps.live.points.tiers.text') },
          { lead: t('steps.live.points.platinum.lead'), text: t('steps.live.points.platinum.text') },
          { lead: t('steps.live.points.try.lead'), text: t('steps.live.points.try.text') },
        ],
        Visual: LiveVisual,
      },
      {
        id: 'stats',
        title: t('steps.stats.title'),
        accent: 'purple',
        icon: IconChartBar,
        intro: t('steps.stats.intro'),
        points: [
          { lead: t('steps.stats.points.stats.lead'), text: t('steps.stats.points.stats.text') },
          { lead: t('steps.stats.points.lore.lead'), text: t('steps.stats.points.lore.text') },
          { lead: t('steps.stats.points.spoiler.lead'), text: t('steps.stats.points.spoiler.text') },
        ],
        Visual: StatsLoreVisual,
      },
      {
        id: 'setup',
        title: t('steps.setup.title'),
        accent: 'crystal',
        icon: IconRocket,
        intro: t('steps.setup.intro'),
        points: [
          { lead: t('steps.setup.points.ai.lead'), text: t('steps.setup.points.ai.text') },
          { lead: t('steps.setup.points.steam.lead'), text: t('steps.setup.points.steam.text') },
          { lead: t('steps.setup.points.bridge.lead'), text: t('steps.setup.points.bridge.text') },
        ],
        Visual: SetupVisual,
      },
    ],
    [t]
  )
}
