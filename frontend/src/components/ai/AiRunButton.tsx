import { useEffect, useId, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { IconCheck, IconChevronDown, IconLoader2 } from '@tabler/icons-react'
import type { AiProviderId, AiStatusDto } from '../../api/types'
import { StackedLabel } from '../ui/StackedLabel'
import { Tooltip } from '../ui/Tooltip'
import { AI_PROVIDERS, providersWithKey, useProviderStateText } from './aiProviders'
import { useAiStatus } from './useAiStatus'

interface AiRunButtonProps {
  // The one-off choice for this action ("Mit Claude"), kept by the caller so its handler can use it; undefined = the settings decide
  oneOff: AiProviderId | undefined
  onOneOffChange: (provider: AiProviderId | undefined) => void
  type?: 'button' | 'submit'
  form?: string
  onClick?: () => void
  busy?: boolean
  disabled?: boolean
  icon: ReactNode
  // The label of the action ("Create chapter"); pass a <StackedLabel> if it changes with state
  label: ReactNode
  // Colour of the button, e.g. "pv-btn-emerald"
  tone?: string
}

/** The text that says which AI an action would use right now, e.g. for tooltips ("Uses Claude."); null while nothing is known yet. */
export function useAiWhoText(oneOff?: AiProviderId): string | null {
  const { t } = useTranslation('ai')
  const { data: status } = useAiStatus()
  if (!status) return null
  if (oneOff) return t('uses.oneOff', { provider: t(`providers.${oneOff}`) })
  if (status.mode === 'ask') return t('uses.ask')
  if (status.mode === 'mixed') return t('uses.mixed')
  return status.activeProvider ? t('uses.provider', { provider: t(`providers.${status.activeProvider}`) }) : null
}

/**
 * The button of an AI action. In the modes "Standard-KI" and "mixed" it grows a second part that names the AI which will do the work
 * ("Claude ▾") and opens a small menu to use another AI for this one action - the setting itself never changes. In the mode "ask" there is
 * no second part: the click opens the chooser dialog (the caller's handler asks with useAiPicker()). The second part reserves the
 * width of its longest name, so the button never changes size when another AI is picked.
 */
export function AiRunButton({ oneOff, onOneOffChange, type = 'button', form, onClick, busy = false, disabled = false, icon, label, tone = '' }: AiRunButtonProps) {
  const { t } = useTranslation('ai')
  const { data: status } = useAiStatus()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLSpanElement>(null)
  const segmentRef = useRef<HTMLButtonElement>(null)
  const menuId = useId()
  const stateText = useProviderStateText()

  const showSegment = status !== undefined && status.mode !== 'ask'
  const keyed = providersWithKey(status)

  useEffect(() => {
    if (!open) return
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open])

  // Which name the second part shows: all names are drawn stacked so the part is as wide as the longest one
  const names: string[] = [...AI_PROVIDERS.map((p) => t(`providers.${p}`)), t('button.mixedName'), t('button.noKeyName')]
  const activeIndex = whoIndex(status, oneOff, keyed.length)

  const choose = (provider: AiProviderId | undefined) => {
    onOneOffChange(provider)
    setOpen(false)
    segmentRef.current?.focus()
  }

  return (
    <span ref={rootRef} className="relative inline-flex max-w-full">
      <button
        type={type}
        form={form}
        onClick={onClick}
        disabled={disabled || busy}
        aria-busy={busy || undefined}
        className={`pv-btn ${tone} relative min-w-0 ${showSegment ? '!rounded-e-none' : ''}`.trim()}
      >
        <span className={`inline-flex items-center gap-[inherit] ${busy ? 'invisible' : ''}`}>
          {icon}
          {label}
        </span>
        {busy && (
          <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
            <IconLoader2 size={16} className="animate-spin" />
          </span>
        )}
      </button>

      {showSegment && (
        <>
          <Tooltip label={t('button.menuLabel')} description={t('button.menuDescription')}>
            <button
              ref={segmentRef}
              type="button"
              disabled={busy || keyed.length === 0}
              aria-haspopup="menu"
              aria-expanded={open}
              aria-controls={open ? menuId : undefined}
              aria-label={`${t('button.menuLabel')}: ${names[activeIndex]}`}
              onClick={() => setOpen((value) => !value)}
              onKeyDown={(event) => {
                if (event.key === 'Escape' && open) {
                  event.stopPropagation()
                  setOpen(false)
                }
              }}
              className={`pv-btn ${tone} whitespace-nowrap !rounded-s-none !border-s-0 !gap-1 !ps-2.5 !pe-2`.trim()}
            >
              <StackedLabel active={activeIndex} labels={names} />
              <IconChevronDown size={14} aria-hidden="true" className={`shrink-0 transition-transform ${open ? 'rotate-180' : ''}`} />
            </button>
          </Tooltip>

          {open && status && (
            <div
              id={menuId}
              role="menu"
              aria-label={t('button.menuLabel')}
              className="absolute bottom-full end-0 z-50 mb-1.5 w-max min-w-[15rem] max-w-[min(22rem,90vw)] rounded-xl p-1.5"
              style={{ background: 'var(--bg-surface)', border: '1px solid var(--border-strong, var(--border-subtle))', boxShadow: '0 14px 36px -10px rgba(8, 13, 23, 0.45)' }}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  event.stopPropagation()
                  setOpen(false)
                  segmentRef.current?.focus()
                }
              }}
            >
              {status.mode === 'mixed' && (
                <MenuItem checked={oneOff === undefined} onSelect={() => choose(undefined)} title={t('button.standardEntry', { what: t('button.mixedName') })} />
              )}
              {keyed.map((provider) => {
                const checked = status.mode === 'mixed' ? oneOff === provider.id : (oneOff ?? status.activeProvider) === provider.id
                return (
                  <MenuItem
                    key={provider.id}
                    checked={checked}
                    onSelect={() => choose(status.mode !== 'mixed' && provider.id === status.activeProvider ? undefined : provider.id)}
                    title={t(`providers.${provider.id}`)}
                    tag={provider.id === status.defaultProvider ? t('button.standardTag') : undefined}
                    detail={[provider.state === 'ok' ? null : stateText(provider), provider.webSearch ? null : t('noWebSearch')].filter(Boolean).join(' · ') || undefined}
                    warn={provider.state !== 'ok'}
                  />
                )
              })}
            </div>
          )}
        </>
      )}
    </span>
  )
}

function whoIndex(status: AiStatusDto | undefined, oneOff: AiProviderId | undefined, keyedCount: number): number {
  const noKey = AI_PROVIDERS.length + 1
  if (!status || keyedCount === 0) return noKey
  if (oneOff) return AI_PROVIDERS.indexOf(oneOff)
  if (status.mode === 'mixed') return AI_PROVIDERS.length
  const current = status.activeProvider ?? status.defaultProvider
  return current ? AI_PROVIDERS.indexOf(current) : noKey
}

function MenuItem({
  checked,
  onSelect,
  title,
  tag,
  detail,
  warn = false,
}: {
  checked: boolean
  onSelect: () => void
  title: string
  tag?: string
  detail?: string
  warn?: boolean
}) {
  return (
    <button
      type="button"
      role="menuitemradio"
      aria-checked={checked}
      onClick={onSelect}
      className="flex w-full cursor-pointer items-start gap-2 rounded-lg px-2.5 py-2 text-start text-xs hover:bg-[var(--bg-elevated)] focus-visible:bg-[var(--bg-elevated)]"
      style={{ color: 'var(--text-primary)' }}
    >
      <IconCheck size={14} aria-hidden="true" className={`mt-0.5 shrink-0 ${checked ? '' : 'invisible'}`} style={{ color: 'var(--accent-emerald)' }} />
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-x-2 font-extrabold">
          {title}
          {tag && <span className="pv-badge pv-badge-a !py-0 !text-[10px]">{tag}</span>}
        </span>
        {detail && (
          <span className="mt-0.5 block font-semibold" style={{ color: warn ? 'var(--accent-gold-text)' : 'var(--text-muted)' }}>
            {detail}
          </span>
        )}
      </span>
    </button>
  )
}
