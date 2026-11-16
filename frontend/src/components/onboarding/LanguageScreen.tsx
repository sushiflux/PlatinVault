import { useEffect, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent, ReactNode } from 'react'
import { motion, useReducedMotion } from 'motion/react'
import { IconAward, IconBook2, IconChartBar, IconLoader2, IconSparkles, IconTrophy } from '@tabler/icons-react'
import { detectBrowserLanguage, fixedT, readStoredLanguage, SUPPORTED_LANGUAGES, UI_LANGUAGES } from '../../i18n'
import type { UiLanguage } from '../../i18n'
import { useUiLanguage } from '../../i18n/useUiLanguage'
import { readTheme, requestTheme, type ThemeName } from '../../utils/themeEvent'
import { AppVersion, useBranding } from '../FooterParts'
import { ErrorBlock } from '../ui/ErrorBlock'
import '../../styles/firstrun.css'

// Every text of the screen is drawn in all languages on top of each other in one grid cell and only the chosen one is visible: the cell
// always has the height of the longest text, so switching the choice never moves anything (project rule "Rigid layout").
function Swap({ active, children }: { active: UiLanguage; children: (language: UiLanguage) => ReactNode }) {
  return (
    <span className="grid">
      {UI_LANGUAGES.map((language) => (
        <span key={language} lang={language} className={`col-start-1 row-start-1 ${language === active ? '' : 'invisible'}`} aria-hidden={language !== active}>
          {children(language)}
        </span>
      ))}
    </span>
  )
}

const FEATURES = [
  { id: 'live', Icon: IconTrophy },
  { id: 'overlay', Icon: IconSparkles },
  { id: 'guides', Icon: IconBook2 },
  { id: 'stats', Icon: IconChartBar },
  { id: 'cabinet', Icon: IconAward },
] as const

const THEMES: ThemeName[] = ['light', 'dark']

/** What the visitor sees while the server is asked whether a language has been chosen yet (first visit in this browser only). */
export function LanguageWait() {
  const { data: branding } = useBranding()
  return (
    <div className="pv-first pv-first-wait" role="status" aria-busy="true">
      <img src="/favicon.svg" alt="" />
      <span style={{ fontSize: '1.5rem', fontWeight: 600 }}>{branding?.productName ?? document.title}</span>
      <IconLoader2 size={22} className="animate-spin" style={{ color: 'var(--link)' }} aria-hidden="true" />
    </div>
  )
}

/**
 * First-run screen: shown once, before the intro tour and before anything else, while the server has no language stored yet. The layout
 * of a sign-in page (PlatinVault has no sign-in): left the language and the colour scheme, right the brand panel. The language is
 * preselected from the browser's language; "Continue" applies it and saves it (setting ui.language, which also sets the Steam
 * achievement language). The colour scheme applies at once, like in the header.
 */
export function LanguageScreen() {
  const reduce = useReducedMotion() ?? false
  const { changeLanguage } = useUiLanguage()
  const { data: branding } = useBranding()
  const name = branding?.productName ?? 'PlatinVault'
  const [selected, setSelected] = useState<UiLanguage>(() => readStoredLanguage() ?? detectBrowserLanguage())
  const [theme, setTheme] = useState<ThemeName>(() => readTheme())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const languageGroupRef = useRef<HTMLDivElement>(null)
  const aliveRef = useRef(true)
  const tt = (language: UiLanguage) => fixedT(language, 'onboarding')

  useEffect(() => {
    aliveRef.current = true
    // The keyboard starts on the preselected choice: arrow keys switch, Enter continues
    languageGroupRef.current?.querySelector<HTMLButtonElement>('button[aria-checked="true"]')?.focus({ preventScroll: true })
    return () => {
      aliveRef.current = false
    }
  }, [])

  const chooseTheme = (next: ThemeName) => {
    setTheme(next)
    requestTheme(next)
  }

  const submit = async () => {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await changeLanguage(selected)
      // Success: the saved language now exists on the server and the gate lets the app start (this screen goes away)
    } catch (err: unknown) {
      const reason = err instanceof Error ? err.message : fixedT(selected, 'common')('unknownError')
      if (aliveRef.current) setError(tt(selected)('language.saveFailed', { reason }))
    } finally {
      if (aliveRef.current) setBusy(false)
    }
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    void submit()
  }

  // A connected choice bar is a radio group: arrows move the choice (and the focus), Enter continues
  const onGroupKeyDown = <T extends string>(event: KeyboardEvent<HTMLDivElement>, values: readonly T[], current: T, set: (value: T) => void) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      void submit()
      return
    }
    const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0
    if (!step) return
    event.preventDefault()
    const next = values[(values.indexOf(current) + step + values.length) % values.length]!
    set(next)
    // currentTarget is gone once the handler has returned: keep the group element for the focus move after the re-render
    const group = event.currentTarget
    requestAnimationFrame(() => group.querySelector<HTMLButtonElement>(`button[data-value="${next}"]`)?.focus())
  }

  const enter = (delay: number) => ({
    initial: reduce ? { opacity: 0 } : { opacity: 0, y: 14 },
    animate: { opacity: 1, y: 0 },
    transition: reduce ? { duration: 0.01 } : { duration: 0.45, delay, ease: [0.22, 1, 0.36, 1] as const },
  })

  const languageCodes = SUPPORTED_LANGUAGES.map((language) => language.code)

  return (
    <main className="pv-first" aria-labelledby="pv-first-title">
      <div className="pv-first-bar">
        <img src="/favicon.svg" alt="" />
        <span>{name}</span>
      </div>

      <div className="pv-first-grid">
        <section className="pv-first-form">
          <motion.form className="pv-first-form-in" onSubmit={onSubmit} {...enter(0)}>
            <h1 id="pv-first-title">
              <Swap active={selected}>{(language) => tt(language)('first.title', { name })}</Swap>
            </h1>
            <p className="pv-first-lead">
              <Swap active={selected}>{(language) => tt(language)('first.lead', { name })}</Swap>
            </p>

            <div className="pv-first-group">
              <span className="pv-first-label" id="pv-first-language">
                <Swap active={selected}>{(language) => tt(language)('first.languageLabel')}</Swap>
              </span>
              <div
                ref={languageGroupRef}
                className="pv-segmented"
                role="radiogroup"
                aria-labelledby="pv-first-language"
                onKeyDown={(event) => onGroupKeyDown(event, languageCodes, selected, setSelected)}
              >
                {SUPPORTED_LANGUAGES.map(({ code, endonym }) => (
                  <button
                    key={code}
                    type="button"
                    role="radio"
                    lang={code}
                    aria-checked={selected === code}
                    tabIndex={selected === code ? 0 : -1}
                    data-value={code}
                    data-language={code}
                    onClick={() => setSelected(code)}
                  >
                    {endonym}
                  </button>
                ))}
              </div>
            </div>

            <div className="pv-first-group">
              <span className="pv-first-label" id="pv-first-theme">
                <Swap active={selected}>{(language) => tt(language)('first.themeLabel')}</Swap>
              </span>
              <div
                className="pv-segmented"
                role="radiogroup"
                aria-labelledby="pv-first-theme"
                onKeyDown={(event) => onGroupKeyDown(event, THEMES, theme, chooseTheme)}
              >
                {THEMES.map((value) => (
                  <button
                    key={value}
                    type="button"
                    role="radio"
                    aria-checked={theme === value}
                    tabIndex={theme === value ? 0 : -1}
                    data-value={value}
                    onClick={() => chooseTheme(value)}
                  >
                    {tt(selected)(value === 'light' ? 'first.themeLight' : 'first.themeDark')}
                  </button>
                ))}
              </div>
            </div>

            {/* Busy state: the label stays (invisible) and a spinner covers it, so the button keeps its size */}
            <button type="submit" className="pv-btn pv-btn-primary pv-first-continue" disabled={busy} aria-busy={busy || undefined}>
              <span className={busy ? 'invisible' : ''}>
                <Swap active={selected}>{(language) => tt(language)('language.continue')}</Swap>
              </span>
              {busy && (
                <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
                  <IconLoader2 size={20} className="animate-spin" />
                </span>
              )}
            </button>

            <p className="pv-first-note">
              <Swap active={selected}>{(language) => tt(language)('first.note')}</Swap>
            </p>

            {error && (
              <div className="pv-first-error" lang={selected}>
                <ErrorBlock title={tt(selected)('first.saveFailedTitle')} message={error} defaultOpen />
              </div>
            )}
          </motion.form>
        </section>

        <section className="pv-first-brand" aria-label={tt(selected)('first.brandLabel')}>
          <motion.div className="pv-first-brand-in" {...enter(0.1)}>
            <div className="pv-first-hero">
              <img src="/favicon.svg" alt="" />
              <div>
                <h2 className="pv-first-name">{name}</h2>
                <div className="pv-first-line" aria-hidden="true" />
                <p className="pv-first-tag">{branding?.tagline ?? ''}</p>
              </div>
            </div>
            <div className="pv-first-vr" aria-hidden="true" />
            <ul className="pv-first-list">
              {FEATURES.map(({ id, Icon }) => (
                <li key={id}>
                  <Icon size={22} aria-hidden="true" />
                  <div>
                    <b>
                      <Swap active={selected}>{(language) => tt(language)(`first.features.${id}.title`)}</Swap>
                    </b>
                    <span>
                      <Swap active={selected}>{(language) => tt(language)(`first.features.${id}.text`)}</Swap>
                    </span>
                  </div>
                </li>
              ))}
            </ul>
          </motion.div>
        </section>
      </div>

      <div className="pv-first-foot">
        <AppVersion className="pv-first-version" />
      </div>
    </main>
  )
}
