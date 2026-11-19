import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconBrandSteam, IconCheck, IconDeviceGamepad2, IconExternalLink, IconKey, IconLoader2, IconPlus, IconStar } from '@tabler/icons-react'
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
  // Opens the "add a game by hand" dialog (a game of another platform, no Steam needed); without it the line is not shown
  onAddGame?: () => void
}

// The number of a step in a small square; a done step shows a check mark, a step that is being checked a spinner. The size is the same in
// every state, so a step turning "done" never moves anything.
function StepMark({ n, state }: { n: number; state: StepState }) {
  const { t } = useTranslation('dashboard')
  return (
    <span role="img" aria-label={`${n}: ${t(`empty.state.${state}`)}`} className="pv-step-mark" data-state={state}>
      {state === 'done' ? <IconCheck size={18} aria-hidden="true" /> : state === 'loading' ? <IconLoader2 size={16} className="animate-spin" aria-hidden="true" /> : n}
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
    <li className="pv-step">
      <StepMark n={n} state={state} />
      <div className="min-w-0">
        <div className="pv-step-title">
          <Icon size={18} aria-hidden="true" />
          <span>{title}</span>
        </div>
        <p className="pv-step-text">{text}</p>
        {link && (
          <a href={link.href} target="_blank" rel="noreferrer" className="pv-step-link">
            {link.label}
            <IconExternalLink size={14} aria-hidden="true" />
          </a>
        )}
      </div>
      {/* On a phone the action sits under the text, aligned with it */}
      <div className="pv-step-act">{children}</div>
    </li>
  )
}

// Buttons of the steps: the same width in every state (the label changes with the state)
const ACTION = 'pv-btn pv-step-btn'

/**
 * The calm help that takes the place of the tiles while no game is on the dashboard. Games come from the Steam library only, so
 * it says how: 1. connect the Steam account, 2. enter the Steam Web API key, 3. bring a game onto the dashboard with the star in
 * "All games" (its guide is created on the game page). The steps show the real state (settings) and the next open one is the
 * primary button. Everything is fixed in size: a step turning "done" changes a mark and a button label, nothing moves.
 */
export function DashboardEmptyHelp({ hasLibrary, onOpenSettings, onShowLibrary, onAddGame }: DashboardEmptyHelpProps) {
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
      className="pv-card pv-empty"
      initial={reduceMotion ? false : { opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="pv-empty-head">
        <span className="pv-empty-ic" aria-hidden="true">
          <IconDeviceGamepad2 size={24} />
        </span>
        <div className="min-w-0">
          <h2 id="dashboard-empty-title" className="pv-empty-title">
            {t('empty.title')}
          </h2>
          <p className="pv-empty-intro">{t('empty.intro')}</p>
        </div>
      </div>

      <ol aria-label={t('empty.stepsLabel')} className="pv-empty-steps">
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
                className={ACTION}
                onClick={(e) => e.preventDefault()}
              >
                {t('empty.star.action')}
                <span className="sr-only">: {t('empty.star.locked')}</span>
              </button>
            </Tooltip>
          )}
        </StepRow>
      </ol>

      {/* The other way in: a game of another platform (or one that is not in the Steam library) by hand - no Steam account needed */}
      {onAddGame && (
        <div className="pv-step" data-manual="">
          <span className="pv-step-mark" aria-hidden="true">
            <IconPlus size={18} />
          </span>
          <div className="min-w-0">
            <div className="pv-step-title">
              <IconDeviceGamepad2 size={18} aria-hidden="true" />
              <span>{t('empty.manual.title')}</span>
            </div>
            <p className="pv-step-text">{t('empty.manual.text')}</p>
          </div>
          <div className="pv-step-act">
            <button type="button" className={ACTION} onClick={onAddGame}>
              {t('empty.manual.action')}
            </button>
          </div>
        </div>
      )}
    </motion.section>
  )
}
