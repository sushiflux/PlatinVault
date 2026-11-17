import { createContext, useCallback, useContext, useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { IconSparkles } from '@tabler/icons-react'
import { fetchAiStatus } from '../../api/client'
import type { AiProviderId, AiProviderStatusDto, AiStatusDto } from '../../api/types'
import { StackedLabel } from '../ui/StackedLabel'
import { AI_STATUS_KEY } from './useAiStatus'
import './ai.css'
import { MANUAL_AI, providersWithKey, readLastAiChoice, readLastWasManual, rememberAiWay, useProviderStateText, writeLastAiChoice } from './aiProviders'
import type { AiHow } from './aiProviders'

// The dialog of the selection mode "ask before every request": every AI action calls pickAi() first.
//   const pickAi = useAiPicker()
//   const provider = await pickAi(oneOffChoice)   // null = the player cancelled, undefined = no choice (use the settings)
//   if (provider === null) return
// In the modes "Standard-KI" and "mixed" pickAi() answers at once with the one-off choice (or undefined) and shows nothing.
//
// Options for the actions that can also be done with the player's own AI subscription ("Mein KI-Abo", the value 'manual'):
//   manual: the chooser lists "Mein KI-Abo" after the providers; a one-off 'manual' passes through; without any API key there is nothing
//           to choose and 'manual' comes back at once
//   always: the chooser opens in EVERY mode (the one-off choice, else the setting's provider / the last choice is preselected)
// Whenever such an action gets a real choice (at least one API key), the way taken is remembered: "Mein KI-Abo" taken last time is
// preselected in the chooser and is the starting choice of the buttons (rememberedHow()); an API provider taken clears it again.

export interface PickAiOptions {
  manual?: boolean
  always?: boolean
}

export type PickAi = (oneOff?: string, options?: PickAiOptions) => Promise<string | undefined | null>

const AiChoiceContext = createContext<PickAi | null>(null)

export function useAiPicker(): PickAi {
  const pick = useContext(AiChoiceContext)
  if (!pick) throw new Error('useAiPicker needs <AiChoiceProvider> above it')
  return pick
}

interface PendingChoice {
  status: AiStatusDto
  // Offers "Mein KI-Abo"
  manual: boolean
  // The choice of the button that is preselected when it is on offer
  preferred: string | undefined
  resolve: (provider: string | null) => void
}

export function AiChoiceProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [pending, setPending] = useState<PendingChoice | null>(null)
  const pendingRef = useRef<PendingChoice | null>(null)

  const pick = useCallback<PickAi>(
    async (oneOff, options) => {
      const manual = options?.manual === true
      // A one-off 'manual' only means something to an action that offers the subscription way
      const how = oneOff === MANUAL_AI && !manual ? undefined : oneOff
      // Fresh states (a provider may have run out of quota a moment ago); the cached ones if the server cannot be asked
      let status: AiStatusDto | undefined
      try {
        status = await queryClient.fetchQuery({ queryKey: AI_STATUS_KEY, queryFn: fetchAiStatus, staleTime: 0 })
      } catch {
        status = queryClient.getQueryData<AiStatusDto>(AI_STATUS_KEY)
      }
      if (!status) return how
      const keyed = providersWithKey(status)
      // Without any API key the subscription is the only way: no chooser, and nothing to remember (it was no choice)
      if (manual && keyed.length === 0) return MANUAL_AI
      if (status.mode !== 'ask' && options?.always !== true) {
        if (manual) rememberAiWay(how)
        return how
      }
      // Without any key there is nothing to choose: the server answers "store an AI key" and the notice offers the settings
      if (keyed.length === 0) return undefined

      return new Promise<string | null>((resolve) => {
        // A second request replaces an open one; the first counts as cancelled
        pendingRef.current?.resolve(null)
        const next = {
          status,
          manual,
          preferred: how,
          resolve: (chosen: string | null) => {
            // Cancelling keeps what was remembered
            if (manual && chosen !== null) rememberAiWay(chosen)
            resolve(chosen)
          },
        }
        pendingRef.current = next
        setPending(next)
      })
    },
    [queryClient]
  )

  const close = (provider: string | null) => {
    pendingRef.current?.resolve(provider)
    pendingRef.current = null
    setPending(null)
  }

  return (
    <AiChoiceContext.Provider value={pick}>
      {children}
      {createPortal(
        <AnimatePresence>{pending && <AiChooserPanel key="ai-chooser" status={pending.status} manual={pending.manual} preferred={pending.preferred} onClose={close} />}</AnimatePresence>,
        document.body
      )}
    </AiChoiceContext.Provider>
  )
}

const FOCUSABLE = 'button:not(:disabled), input:not(:disabled), a[href]'

/** The provider to preselect: the last one used if it still works, else the first one that works, else the last one / the first one. */
export function preselectedProvider(keyed: AiProviderStatusDto[], last: AiProviderId | null, preferred?: string): AiProviderStatusDto | undefined {
  const lastOne = keyed.find((p) => p.id === last)
  const wanted = keyed.find((p) => p.id === preferred)
  return (wanted?.state === 'ok' ? wanted : undefined) ?? (lastOne?.state === 'ok' ? lastOne : undefined) ?? keyed.find((p) => p.state === 'ok') ?? wanted ?? lastOne ?? keyed[0]
}

function AiChooserPanel({
  status,
  manual,
  preferred,
  onClose,
}: {
  status: AiStatusDto
  manual: boolean
  preferred: string | undefined
  onClose: (provider: string | null) => void
}) {
  const { t } = useTranslation('ai')
  const { t: tc } = useTranslation()
  const stateText = useProviderStateText()
  const reduce = useReducedMotion() ?? false
  const keyed = providersWithKey(status)
  const last = readLastAiChoice()
  // "Mein KI-Abo" taken last time (only where it is on offer) is the habit before any provider
  const lastManual = manual && readLastWasManual()
  // Outside the mode "ask" the setting's provider is the habit; a one-off choice of the button counts before it
  const wish = preferred ?? (lastManual ? MANUAL_AI : status.mode === 'ask' ? undefined : (status.defaultProvider ?? undefined))
  const [selected, setSelected] = useState<AiHow | undefined>(() => (manual && wish === MANUAL_AI ? MANUAL_AI : preselectedProvider(keyed, last, wish)?.id))
  const panelRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const introId = useId()

  // What the player would have got by habit but cannot rely on right now (the subscription never runs out)
  const habit = lastManual ? undefined : keyed.find((p) => p.id === (last ?? status.defaultProvider))
  const troubled = habit && habit.state !== 'ok' ? habit : undefined

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    panelRef.current?.querySelector<HTMLElement>('input[type="radio"]:checked')?.focus()
    return () => previouslyFocused?.focus?.()
  }, [])

  const chosen = keyed.find((p) => p.id === selected)
  const confirm = () => {
    if (selected === MANUAL_AI) {
      // Not the "last AI" (that one stays the last API provider); pickAi remembers the way itself
      onClose(MANUAL_AI)
      return
    }
    if (!chosen) return
    writeLastAiChoice(chosen.id)
    onClose(chosen.id)
  }

  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.stopPropagation()
      onClose(null)
      return
    }
    if (e.key !== 'Tab' || !panelRef.current) return
    const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE))
    if (items.length === 0) return
    const first = items[0]!
    const lastItem = items[items.length - 1]!
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault()
      lastItem.focus()
    } else if (!e.shiftKey && document.activeElement === lastItem) {
      e.preventDefault()
      first.focus()
    }
  }

  return (
    <motion.div
      className="pv-modal-backdrop"
      style={{ zIndex: 80 }}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: reduce ? 0 : 0.16 }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose(null)
      }}
    >
      <motion.div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={introId}
        onKeyDown={handleKeyDown}
        className="pv-modal w-full max-w-md p-6"
        initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.96, y: 10 }}
        animate={reduce ? { opacity: 1 } : { opacity: 1, scale: 1, y: 0 }}
        exit={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.97, y: 6 }}
        transition={reduce ? { duration: 0 } : { type: 'spring', stiffness: 420, damping: 32 }}
      >
        <div className="flex items-start gap-3">
          <span className="mt-0.5 shrink-0" style={{ color: 'var(--accent-crystal)' }} aria-hidden="true">
            <IconSparkles size={22} />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="text-[1.0625rem] font-semibold leading-snug" style={{ color: 'var(--text-primary)' }}>
              {t('chooser.title')}
            </h2>
            <p id={introId} className="mt-1 leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
              {t('chooser.intro')}
            </p>
          </div>
        </div>

        {troubled && (
          <p className="pv-note pv-note--warn mt-4" role="status">
            {t('chooser.unhealthy', { provider: t(`providers.${troubled.id}`), state: stateText(troubled) })}
          </p>
        )}

        <div role="radiogroup" aria-label={t('chooser.groupLabel')} className="pv-ai-choice mt-4">
          {keyed.map((provider) => {
            const isOn = provider.id === selected
            return (
              <label key={provider.id} className="pv-ai-choice__row" data-selected={isOn || undefined}>
                <input
                  type="radio"
                  name="ai-provider"
                  className="sr-only"
                  checked={isOn}
                  onChange={() => setSelected(provider.id)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault()
                      confirm()
                    }
                  }}
                />
                <span aria-hidden="true" className="pv-ai-choice__dot" />
                <span className="min-w-0 flex-1">
                  <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5 font-semibold">
                    {t(`providers.${provider.id}`)}
                    {provider.id === last && !lastManual && <span className="pv-badge pv-badge-b !py-0 !text-[11px]">{t('chooser.lastUsed')}</span>}
                    {provider.id === status.defaultProvider && <span className="pv-badge pv-badge-a !py-0 !text-[11px]">{t('button.standardTag')}</span>}
                    {!provider.webSearch && <span className="pv-badge pv-badge-s !py-0 !text-[11px]">{t('noWebSearch')}</span>}
                  </span>
                  <span className="mt-0.5 block text-sm" style={{ color: provider.state === 'ok' ? 'var(--text-muted)' : 'var(--accent-gold-text)' }}>
                    {stateText(provider)} · {provider.model}
                  </span>
                </span>
              </label>
            )
          })}
          {manual && (
            <label className="pv-ai-choice__row" data-selected={selected === MANUAL_AI || undefined}>
              <input
                type="radio"
                name="ai-provider"
                className="sr-only"
                checked={selected === MANUAL_AI}
                onChange={() => setSelected(MANUAL_AI)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault()
                    confirm()
                  }
                }}
              />
              <span aria-hidden="true" className="pv-ai-choice__dot" />
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5 font-semibold">
                  {t('chooser.manual.title')}
                  {lastManual && <span className="pv-badge pv-badge-b !py-0 !text-[11px]">{t('chooser.lastUsed')}</span>}
                </span>
                <span className="mt-0.5 block text-sm" style={{ color: 'var(--text-muted)' }}>
                  {t('chooser.manual.hint')}
                </span>
              </span>
            </label>
          )}
        </div>

        <div className="mt-6 flex flex-wrap justify-end gap-2">
          <button type="button" className="pv-btn" onClick={() => onClose(null)}>
            {tc('cancel')}
          </button>
          <button type="button" className="pv-btn pv-btn-primary" disabled={!chosen && selected !== MANUAL_AI} onClick={confirm}>
            <IconSparkles size={18} />
            {/* All labels stacked: the button keeps its width whichever AI is selected */}
            <StackedLabel
              active={selected === MANUAL_AI ? keyed.length : Math.max(0, keyed.findIndex((p) => p.id === selected))}
              labels={[...keyed.map((p) => t('chooser.start', { provider: t(`providers.${p.id}`) })), ...(manual ? [t('chooser.manual.start')] : [])]}
            />
          </button>
        </div>
      </motion.div>
    </motion.div>
  )
}
