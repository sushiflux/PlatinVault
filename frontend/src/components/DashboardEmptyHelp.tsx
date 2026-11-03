import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconBrandSteam, IconCheck, IconDeviceGamepad2, IconExternalLink, IconKey, IconLoader2, IconStar } from '@tabler/icons-react'
import type { Icon } from '@tabler/icons-react'
import { fetchSettings, steamLoginUrl } from '../api/client'
import type { SettingsTab } from './SettingsModal'
import { Tooltip } from './ui/Tooltip'

// The page where the Steam Web API key is handed out (the settings link to the same one)
const STEAM_KEY_URL = 'https://steamcommunity.com/dev/apikey'

type StepState = 'done' | 'open' | 'loading'

interface DashboardEmptyHelpProps {
  // The Steam library can be read (account and key are right): step 3 can be used
  hasLibrary: boolean
  // Opens the settings at the Steam tab (account and key live there)
  onOpenSettings: (tab: SettingsTab) => void
  // Switches the dashboard to "All games"
  onShowLibrary: () => void
}

// The number of a step in a circle; a done step shows a check mark, a step that is being checked a spinner. The size is the same in
// every state, so a step turning "done" never moves anything.
function StepMark({ n, state }: { n: number; state: StepState }) {
  const { t } = useTranslation('dashboard')
  return (
    <span
      role="img"
      aria-label={`${n}: ${t(`empty.state.${state}`)}`}
      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-extrabold"
      style={
        state === 'done'
          ? { background: 'var(--accent-emerald-bg)', color: 'var(--accent-emerald)', border: '1.5px solid var(--accent-emerald-border)' }
          : { border: '2px solid var(--border-strong)', color: 'var(--text-secondary)' }
      }
    >
      {state === 'done' ? <IconCheck size={17} stroke={3} aria-hidden="true" /> : state === 'loading' ? <IconLoader2 size={15} className="animate-spin" aria-hidden="true" /> : n}
    </span>
  )
}

interface StepRowProps {
  n: number
  Icon: Icon
  state: StepState
  title: string
  text: string
  // A small link under the text (opens in a new tab)
  link?: { href: string; label: string }
  children: ReactNode
}

function StepRow({ n, Icon, state, title, text, link, children }: StepRowProps) {
  return (
    <li
      className="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-3 gap-y-2.5 rounded-xl p-3.5 sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:gap-x-4"
      style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }}
    >
      <StepMark n={n} state={state} />
      <div className="min-w-0">
        <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
          <Icon size={16} className="shrink-0" style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
          <span>{title}</span>
        </div>
        <p className="mt-0.5 text-xs leading-snug" style={{ color: 'var(--text-secondary)' }}>
          {text}
        </p>
        {link && (
          <a
            href={link.href}
            target="_blank"
            rel="noreferrer"
            className="mt-1 inline-flex items-center gap-1 text-xs font-bold hover:underline"
            style={{ color: 'var(--accent-crystal)' }}
          >
            {link.label}
            <IconExternalLink size={12} aria-hidden="true" />
          </a>
        )}
      </div>
      {/* On a phone the action sits under the text, aligned with it */}
      <div className="col-start-2 flex min-h-9 items-center sm:col-start-3">{children}</div>
    </li>
  )
}

// Buttons of the steps: the same width in every state (the label changes with the state)
const ACTION = 'pv-btn !py-1.5 !text-xs min-w-[10.5rem] justify-center'

/**
 * The calm help that takes the place of the tiles while no game is on the dashboard. Games come from the Steam library only, so
 * it says how: 1. connect the Steam account, 2. enter the Steam Web API key, 3. bring a game onto the dashboard with the star in
 * "All games" (its guide is created on the game page). The steps show the real state (settings) and the next open one is the
 * primary button. Everything is fixed in size: a step turning "done" changes a mark and a button label, nothing moves.
 */
export function DashboardEmptyHelp({ hasLibrary, onOpenSettings, onShowLibrary }: DashboardEmptyHelpProps) {
  const { t } = useTranslation('dashboard')
  const reduceMotion = useReducedMotion() ?? false
  const settingsQuery = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  const settings = settingsQuery.data
  const stateOf = (done: boolean): StepState => (settingsQuery.isLoading ? 'loading' : done ? 'done' : 'open')

  const accountState = stateOf(Boolean(settings?.steamUserId))
  const keyState = stateOf(Boolean(settings?.hasSteamKey))
  // The first step that is not done is the one to do next: its button is the primary one
  const next: 1 | 2 | 3 = accountState !== 'done' ? 1 : keyState !== 'done' ? 2 : 3
  const buttonClass = (step: 1 | 2 | 3) => `${ACTION} ${next === step ? 'pv-btn-primary' : ''}`

  return (
    <motion.section
      aria-labelledby="dashboard-empty-title"
      data-dashboard-empty=""
      className="pv-card mx-auto w-full max-w-3xl space-y-5 !p-5 sm:!p-7"
      initial={reduceMotion ? false : { opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="flex items-start gap-3.5">
        <span
          className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl"
          style={{ background: 'var(--accent-crystal-bg)', color: 'var(--accent-crystal)', border: '1px solid var(--accent-crystal-border)' }}
          aria-hidden="true"
        >
          <IconDeviceGamepad2 size={24} />
        </span>
        <div className="min-w-0">
          <h2 id="dashboard-empty-title" className="text-lg font-extrabold tracking-tight" style={{ color: 'var(--text-primary)' }}>
            {t('empty.title')}
          </h2>
          <p className="mt-0.5 text-sm leading-snug" style={{ color: 'var(--text-secondary)' }}>
            {t('empty.intro')}
          </p>
        </div>
      </div>

      <ol aria-label={t('empty.stepsLabel')} className="space-y-2.5">
        <StepRow n={1} Icon={IconBrandSteam} state={accountState} title={t('empty.account.title')} text={t('empty.account.text')}>
          {settings?.steamUserId ? (
            <button type="button" className={buttonClass(1)} onClick={() => onOpenSettings('steam')}>
              {t('empty.account.change')}
            </button>
          ) : (
            <a href={steamLoginUrl()} className={buttonClass(1)}>
              {t('empty.account.login')}
            </a>
          )}
        </StepRow>

        <StepRow n={2} Icon={IconKey} state={keyState} title={t('empty.key.title')} text={t('empty.key.text')} link={{ href: STEAM_KEY_URL, label: t('empty.key.link') }}>
          <button type="button" className={buttonClass(2)} onClick={() => onOpenSettings('steam')}>
            {settings?.hasSteamKey ? t('empty.key.change') : t('empty.key.action')}
          </button>
        </StepRow>

        <StepRow n={3} Icon={IconStar} state="open" title={t('empty.star.title')} text={t('empty.star.text')}>
          {hasLibrary ? (
            <button type="button" className={buttonClass(3)} onClick={onShowLibrary}>
              {t('empty.star.action')}
            </button>
          ) : (
            // Not usable yet: it says why instead of just being grey
            <Tooltip label={t('empty.star.locked')} side="top" className="inline-flex">
              <button
                type="button"
                aria-disabled="true"
                className={`${ACTION} cursor-not-allowed`}
                // Locked, but still readable (no faded text): muted colour and a dashed edge say "not yet"
                style={{ color: 'var(--text-muted)', background: 'transparent', borderStyle: 'dashed' }}
                onClick={(e) => e.preventDefault()}
              >
                {t('empty.star.action')}
                <span className="sr-only">: {t('empty.star.locked')}</span>
              </button>
            </Tooltip>
          )}
        </StepRow>
      </ol>
    </motion.section>
  )
}
