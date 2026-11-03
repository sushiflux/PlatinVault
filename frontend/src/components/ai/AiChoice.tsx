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
import { providersWithKey, readLastAiChoice, useProviderStateText, writeLastAiChoice } from './aiProviders'

// The dialog of the selection mode "ask before every request": every AI action calls pickAi() first.
//   const pickAi = useAiPicker()
//   const provider = await pickAi(oneOffChoice)   // null = the player cancelled, undefined = no choice (use the settings)
//   if (provider === null) return
// In the modes "Standard-KI" and "mixed" pickAi() answers at once with the one-off choice (or undefined) and shows nothing.

export type PickAi = (oneOff?: string) => Promise<string | undefined | null>

const AiChoiceContext = createContext<PickAi | null>(null)

export function useAiPicker(): PickAi {
  const pick = useContext(AiChoiceContext)
  if (!pick) throw new Error('useAiPicker needs <AiChoiceProvider> above it')
  return pick
}

interface PendingChoice {
  status: AiStatusDto
  resolve: (provider: string | null) => void
}

export function AiChoiceProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [pending, setPending] = useState<PendingChoice | null>(null)
  const pendingRef = useRef<PendingChoice | null>(null)

  const pick = useCallback<PickAi>(
    async (oneOff) => {
      // Fresh states (a provider may have run out of quota a moment ago); the cached ones if the server cannot be asked
      let status: AiStatusDto | undefined
      try {
        status = await queryClient.fetchQuery({ queryKey: AI_STATUS_KEY, queryFn: fetchAiStatus, staleTime: 0 })
      } catch {
        status = queryClient.getQueryData<AiStatusDto>(AI_STATUS_KEY)
      }
      if (!status || status.mode !== 'ask') return oneOff
      // Without any key there is nothing to choose: the server answers "store an AI key" and the notice offers the settings
      if (providersWithKey(status).length === 0) return undefined

      return new Promise<string | null>((resolve) => {
        // A second request replaces an open one; the first counts as cancelled
        pendingRef.current?.resolve(null)
        const next = { status, resolve }
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
        <AnimatePresence>{pending && <AiChooserPanel key="ai-chooser" status={pending.status} onClose={close} />}</AnimatePresence>,
        document.body
      )}
    </AiChoiceContext.Provider>
  )
}

const FOCUSABLE = 'button:not(:disabled), input:not(:disabled), a[href]'

/** The provider to preselect: the last one used if it still works, else the first one that works, else the last one / the first one. */
export function preselectedProvider(keyed: AiProviderStatusDto[], last: AiProviderId | null): AiProviderStatusDto | undefined {
  const lastOne = keyed.find((p) => p.id === last)
  return (lastOne?.state === 'ok' ? lastOne : undefined) ?? keyed.find((p) => p.state === 'ok') ?? lastOne ?? keyed[0]
}

function AiChooserPanel({ status, onClose }: { status: AiStatusDto; onClose: (provider: string | null) => void }) {
  const { t } = useTranslation('ai')
  const { t: tc } = useTranslation()
  const stateText = useProviderStateText()
  const reduce = useReducedMotion() ?? false
  const keyed = providersWithKey(status)
  const last = readLastAiChoice()
  const [selected, setSelected] = useState<AiProviderId | undefined>(() => preselectedProvider(keyed, last)?.id)
  const panelRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const introId = useId()

  // What the player would have got by habit but cannot rely on right now
  const habit = keyed.find((p) => p.id === (last ?? status.defaultProvider))
  const troubled = habit && habit.state !== 'ok' ? habit : undefined

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    panelRef.current?.querySelector<HTMLElement>('input[type="radio"]:checked')?.focus()
    return () => previouslyFocused?.focus?.()
  }, [])

  const chosen = keyed.find((p) => p.id === selected)
  const confirm = () => {
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
        <div className="flex items-start gap-4">
          <div
            className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl"
            style={{ background: 'var(--accent-emerald-bg)', color: 'var(--accent-emerald)', border: '1px solid var(--accent-emerald-border)' }}
            aria-hidden="true"
          >
            <IconSparkles size={22} />
          </div>
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="text-base font-extrabold leading-snug" style={{ color: 'var(--text-primary)' }}>
              {t('chooser.title')}
            </h2>
            <p id={introId} className="mt-1 text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
              {t('chooser.intro')}
            </p>
          </div>
        </div>

        {troubled && (
          <p
            className="mt-4 rounded-lg px-3 py-2 text-xs font-semibold leading-snug"
            style={{ background: 'var(--accent-gold-bg)', color: 'var(--text-primary)', border: '1px solid var(--accent-gold-border)' }}
            role="status"
          >
            {t('chooser.unhealthy', { provider: t(`providers.${troubled.id}`), state: stateText(troubled) })}
          </p>
        )}

        <div role="radiogroup" aria-label={t('chooser.groupLabel')} className="mt-4 space-y-2">
          {keyed.map((provider) => {
            const isOn = provider.id === selected
            return (
              <label
                key={provider.id}
                className="flex cursor-pointer items-center gap-3 rounded-xl p-3 text-sm focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-[var(--accent-crystal)]"
                style={{
                  background: isOn ? 'var(--accent-emerald-bg)' : 'var(--bg-elevated)',
                  border: `1px solid ${isOn ? 'var(--accent-emerald-border)' : 'var(--border-subtle)'}`,
                }}
              >
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
                <span
                  aria-hidden="true"
                  className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full"
                  style={{ border: `2px solid ${isOn ? 'var(--accent-emerald)' : 'var(--text-muted)'}` }}
                >
                  <span className="h-2 w-2 rounded-full" style={{ background: isOn ? 'var(--accent-emerald)' : 'transparent' }} />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5 font-extrabold" style={{ color: 'var(--text-primary)' }}>
                    {t(`providers.${provider.id}`)}
                    {provider.id === last && <span className="pv-badge pv-badge-b !py-0 !text-[10px]">{t('chooser.lastUsed')}</span>}
                    {provider.id === status.defaultProvider && <span className="pv-badge pv-badge-a !py-0 !text-[10px]">{t('button.standardTag')}</span>}
                    {!provider.webSearch && <span className="pv-badge pv-badge-s !py-0 !text-[10px]">{t('noWebSearch')}</span>}
                  </span>
                  <span
                    className="mt-0.5 block text-xs font-semibold"
                    style={{ color: provider.state === 'ok' ? 'var(--text-muted)' : 'var(--accent-gold-text)' }}
                  >
                    {stateText(provider)} · {provider.model}
                  </span>
                </span>
              </label>
            )
          })}
        </div>

        <div className="mt-6 flex flex-wrap justify-end gap-2">
          <button type="button" className="pv-btn" onClick={() => onClose(null)}>
            {tc('cancel')}
          </button>
          <button type="button" className="pv-btn pv-btn-emerald" disabled={!chosen} onClick={confirm}>
            <IconSparkles size={15} />
            {/* All labels stacked: the button keeps its width whichever AI is selected */}
            <StackedLabel
              active={Math.max(0, keyed.findIndex((p) => p.id === selected))}
              labels={keyed.map((p) => t('chooser.start', { provider: t(`providers.${p.id}`) }))}
            />
          </button>
        </div>
      </motion.div>
    </motion.div>
  )
}
