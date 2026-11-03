import { Fragment, useEffect, useRef, useState } from 'react'
import { CopyErrorButton } from '../ui/CopyErrorButton'
import type { FormEvent, KeyboardEvent, ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { motion, useReducedMotion } from 'motion/react'
import { IconCheck, IconLoader2 } from '@tabler/icons-react'
import { fetchBranding } from '../../api/client'
import { detectBrowserLanguage, fixedT, readStoredLanguage, SUPPORTED_LANGUAGES, UI_LANGUAGES } from '../../i18n'
import type { UiLanguage } from '../../i18n'
import { useUiLanguage } from '../../i18n/useUiLanguage'
import { LanguageFlag } from '../ui/LanguageFlag'

// Both languages are drawn on top of each other in one grid cell and only the active one is visible: the cell always has the
// height of the longer text, so switching the choice never moves anything (project rule "Rigid layout").
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

function BrandMark() {
  const { data: branding } = useQuery({ queryKey: ['branding'], queryFn: fetchBranding })
  return (
    <div className="pv-lang-brand">
      <img src="/favicon.svg" alt="" />
      <span>{branding?.productName ?? document.title}</span>
    </div>
  )
}

/** What the visitor sees while the server is asked whether a language has been chosen yet (first visit in this browser only). */
export function LanguageWait() {
  return (
    <div className="pv-lang" role="status" aria-busy="true">
      <img src="/favicon.svg" alt="" className="pv-lang-watermark" aria-hidden="true" />
      <div className="pv-lang-inner">
        <div className="pv-lang-head">
          <BrandMark />
          <IconLoader2 size={22} className="mt-8 animate-spin" style={{ color: '#7dd3fc' }} aria-hidden="true" />
        </div>
      </div>
    </div>
  )
}

/**
 * First-run language screen: shown once, before the intro tour and before anything else, while the server has no language
 * stored yet. Two big choices (text first), preselected from the browser's language; "Continue" applies the choice, saves it
 * (setting ui.language, which also sets the Steam achievement language) and lets the app start.
 */
export function LanguageScreen() {
  const reduce = useReducedMotion() ?? false
  const { changeLanguage } = useUiLanguage()
  const [selected, setSelected] = useState<UiLanguage>(() => readStoredLanguage() ?? detectBrowserLanguage())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const formRef = useRef<HTMLFormElement>(null)
  const aliveRef = useRef(true)

  useEffect(() => {
    aliveRef.current = true
    // The keyboard starts on the preselected choice: arrow keys switch, Enter continues
    formRef.current?.querySelector<HTMLInputElement>('input[type="radio"]:checked')?.focus({ preventScroll: true })
    return () => {
      aliveRef.current = false
    }
  }, [])

  const submit = async () => {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await changeLanguage(selected)
      // Success: the saved language now exists on the server and the gate lets the app start (this screen goes away)
    } catch (err: unknown) {
      const reason = err instanceof Error ? err.message : fixedT(selected, 'common')('unknownError')
      if (aliveRef.current) setError(fixedT(selected, 'onboarding')('language.saveFailed', { reason }))
    } finally {
      if (aliveRef.current) setBusy(false)
    }
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    void submit()
  }

  // Enter on a radio button continues (browsers differ on whether that submits the form)
  const onRadioKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      void submit()
    }
  }

  const enter = (delay: number) => ({
    initial: reduce ? { opacity: 0 } : { opacity: 0, y: 18 },
    animate: { opacity: 1, y: 0 },
    transition: reduce ? { duration: 0.01 } : { duration: 0.5, delay, ease: [0.22, 1, 0.36, 1] as const },
  })

  return (
    <main className="pv-lang" aria-labelledby="pv-lang-title">
      <img src="/favicon.svg" alt="" className="pv-lang-watermark" aria-hidden="true" />
      <div className="pv-lang-inner">
        <motion.div className="pv-lang-head" {...enter(0)}>
          <BrandMark />
        </motion.div>

        <form ref={formRef} className="pv-lang-card" onSubmit={onSubmit}>
          <motion.div {...enter(0.08)}>
            {/* Kicker and title are written in every language of the app, one after the other */}
            <span className="pv-lang-kicker">
              {SUPPORTED_LANGUAGES.map((language, index) => (
                <Fragment key={language.code}>
                  {index > 0 && ' · '}
                  <span lang={language.code}>{fixedT(language.code, 'onboarding')('language.kicker')}</span>
                </Fragment>
              ))}
            </span>
            <h1 id="pv-lang-title" className="pv-lang-title">
              {SUPPORTED_LANGUAGES.map((language) => (
                <span key={language.code} lang={language.code}>
                  {fixedT(language.code, 'onboarding')('language.title')}
                </span>
              ))}
            </h1>
            <div className="pv-lang-rule" aria-hidden="true" />
          </motion.div>

          <fieldset className="pv-lang-options">
            <legend className="sr-only">{SUPPORTED_LANGUAGES.map((language) => fixedT(language.code, 'onboarding')('language.groupLabel')).join(' · ')}</legend>
            {SUPPORTED_LANGUAGES.map(({ code: language, endonym }, index) => (
              <motion.label
                key={language}
                className="pv-lang-option"
                initial={reduce ? { opacity: 0 } : { opacity: 0, y: 22, scale: 0.97 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                transition={reduce ? { duration: 0.01 } : { type: 'spring', stiffness: 260, damping: 24, delay: 0.2 + index * 0.08 }}
              >
                <input
                  type="radio"
                  name="pv-ui-language"
                  value={language}
                  checked={selected === language}
                  onChange={() => setSelected(language)}
                  onKeyDown={onRadioKeyDown}
                  lang={language}
                  aria-describedby={`pv-lang-hint-${language}`}
                />
                <span className="pv-lang-option-top">
                  <span className="pv-lang-code" aria-hidden="true">
                    <LanguageFlag language={language} />
                    {language.toUpperCase()}
                  </span>
                  <span className="pv-lang-check" aria-hidden="true">
                    <IconCheck size={16} stroke={3} />
                  </span>
                </span>
                <span className="pv-lang-name" lang={language}>
                  {endonym}
                </span>
                <span id={`pv-lang-hint-${language}`} className="pv-lang-hint" lang={language}>
                  {fixedT(language, 'onboarding')('language.optionHint')}
                </span>
              </motion.label>
            ))}
          </fieldset>

          <motion.div {...enter(0.42)}>
            <p className="pv-lang-note">
              <Swap active={selected}>{(language) => fixedT(language, 'onboarding')('language.note')}</Swap>
            </p>

            <div className="pv-lang-actions">
              <button type="submit" className="pv-lang-continue" disabled={busy} aria-busy={busy || undefined}>
                <Swap active={selected}>{(language) => fixedT(language, 'onboarding')('language.continue')}</Swap>
                {busy && (
                  <span className="absolute inset-0 flex items-center justify-center rounded-[inherit]" style={{ background: '#38bdf8' }} aria-hidden="true">
                    <IconLoader2 size={20} className="animate-spin" />
                  </span>
                )}
              </button>
            </div>

            <div className="pv-lang-error" role="alert" lang={selected}>
              {error}
              {error && <CopyErrorButton className="ms-2 align-middle" report={{ message: error }} />}
            </div>
          </motion.div>
        </form>
      </div>
    </main>
  )
}
