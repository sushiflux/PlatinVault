import { Component, useEffect, useRef, useState } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import { IconAlertTriangle, IconChevronRight, IconCopy, IconRefresh } from '@tabler/icons-react'
import i18n from '../../i18n'
import { copyText } from '../../utils/clipboard'
import { buildErrorReport } from '../../utils/errorReport'
import '../../styles/status.css'

interface Props {
  children?: ReactNode
}

interface State {
  error: Error | null
  componentStack: string | null
}

// The crash page is ALWAYS in English (project rule), whatever the interface language is; the texts come from errors:boundary
const t = (key: string, options?: object) => (i18n.getFixedT('en', 'errors') as unknown as (k: string, o?: object) => string)(key, options)

/** Everything a bug report needs, as one text: the error, where it threw, and the environment. */
export function crashReport(error: Error, componentStack: string | null): string {
  const details = [
    error.stack ? `${error.name}: ${error.message}\n${error.stack}` : `${error.name}: ${error.message}`,
    componentStack ? `Component stack:${componentStack}` : '',
    `URL: ${window.location.href}`,
    `User agent: ${navigator.userAgent}`,
    `Viewport: ${window.innerWidth}x${window.innerHeight} (pixel ratio ${window.devicePixelRatio})`,
  ]
    .filter(Boolean)
    .join('\n\n')
  return buildErrorReport({ title: t('boundary.title'), message: error.message, details, code: error.name, language: 'en' })
}

/** "Copy details" with a "Copied" confirmation in a fixed slot, so nothing moves. Plain elements only: it must work when the rest is broken. */
function CrashCopy({ text }: { text: () => string }) {
  const [state, setState] = useState<'idle' | 'done' | 'failed'>('idle')
  const timer = useRef<number | undefined>(undefined)
  useEffect(() => () => window.clearTimeout(timer.current), [])
  const copy = async () => {
    const ok = await copyText(text())
    setState(ok ? 'done' : 'failed')
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setState('idle'), 2500)
  }
  return (
    <>
      <button type="button" onClick={() => void copy()}>
        <IconCopy size={18} aria-hidden="true" />
        {t('boundary.copy')}
      </button>
      <span className="pv-crash-ok" role="status" style={state === 'failed' ? { color: 'var(--cr-gold)' } : undefined}>
        {state === 'done' ? i18n.getFixedT('en', 'errors')('copy.done') : state === 'failed' ? i18n.getFixedT('en', 'errors')('copy.failed') : ''}
      </span>
    </>
  )
}

/** The calm page of a Nightly, Beta or Release build: a concrete message, folded technical details, reload and copy. */
export function CrashPage({ error, componentStack }: { error: Error; componentStack: string | null }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="pv-crash" lang="en" role="alert">
      <div className="pv-crash-in">
        <IconAlertTriangle size={36} aria-hidden="true" style={{ color: 'var(--cr-gold)' }} />
        <h1 style={{ marginTop: '0.75rem' }}>{t('boundary.title')}</h1>
        <div className="pv-crash-bar" aria-hidden="true" />
        <p>{t('boundary.text')}</p>
        <p style={{ color: 'var(--cr-text)', overflowWrap: 'anywhere' }}>{error.message}</p>
        <div className="pv-crash-acts">
          <button type="button" className="pv-crash-primary" onClick={() => window.location.reload()}>
            <IconRefresh size={18} aria-hidden="true" />
            {t('boundary.reload')}
          </button>
          <CrashCopy text={() => crashReport(error, componentStack)} />
        </div>
        <div className="pv-crash-details">
          <button type="button" aria-expanded={open} aria-controls="pv-crash-tech" onClick={() => setOpen((value) => !value)} style={{ paddingInline: 0 }}>
            <IconChevronRight size={18} aria-hidden="true" style={{ transform: open ? 'rotate(90deg)' : undefined }} />
            {t('boundary.details')}
          </button>
          {open && (
            <pre id="pv-crash-tech">{`${error.name}: ${error.message}${error.stack ? `\n${error.stack}` : ''}${componentStack ? `\n\nComponent stack:${componentStack}` : ''}`}</pre>
          )}
        </div>
      </div>
    </div>
  )
}

interface Frame {
  fn: string
  location: string
}

/** Parses a V8 / Firefox stack into its frames (function + location), so each one can be read on its own. */
function parseFrames(stack: string | undefined): Frame[] {
  if (!stack) return []
  const frames: Frame[] = []
  for (const line of stack.split('\n').slice(1)) {
    const v8 = /^\s*at\s+(?:(.*?)\s+\()?(.*?)\)?\s*$/.exec(line)
    const gecko = /^(.*?)@(.*)$/.exec(line.trim())
    const match = v8 ?? gecko
    if (match && (match[2] ?? '').length > 0) frames.push({ fn: match[1] || '(anonymous)', location: match[2]! })
  }
  return frames
}

/**
 * The development console: fixed dark, deliberately not theme-aware, as much information as possible (message, which function threw,
 * every parsed stack frame, the React component stack, user agent, URL, viewport) and the copy button with all of it.
 */
export function DevConsole({ error, componentStack }: { error: Error; componentStack: string | null }) {
  const [tab, setTab] = useState<'frames' | 'react' | 'env'>('frames')
  const [selected, setSelected] = useState(0)
  const frames = parseFrames(error.stack)
  const first = frames[0]
  const env = [
    ['URL', window.location.href],
    ['User agent', navigator.userAgent],
    ['Viewport', `${window.innerWidth} x ${window.innerHeight} (pixel ratio ${window.devicePixelRatio})`],
  ]
  const tabs = [
    ['frames', `Stack frames (${frames.length})`],
    ['react', 'Component stack'],
    ['env', 'Environment'],
  ] as const
  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 p-6 font-sans">
      <div className="max-w-5xl mx-auto rounded-md border border-rose-500/40 bg-slate-900 overflow-hidden shadow-2xl">
        <div className="bg-rose-950/60 border-b border-rose-500/30 px-6 py-4 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3 min-w-0">
            <IconAlertTriangle size={24} className="text-rose-400 shrink-0" />
            <div className="min-w-0">
              <span className="text-xs font-mono uppercase tracking-wider text-rose-300">React Render Exception · {error.name}</span>
              <h1 className="text-lg font-semibold text-white break-words">{error.message}</h1>
              {first && (
                <p className="text-xs font-mono text-rose-200 break-all">
                  thrown in <strong className="font-semibold">{first.fn}</strong> · {first.location}
                </p>
              )}
            </div>
          </div>
          <div className="flex items-center gap-2">
            <CopyDev text={() => crashReport(error, componentStack)} />
            <button
              type="button"
              onClick={() => window.location.reload()}
              className="inline-flex min-h-12 items-center gap-2 rounded-md bg-rose-600 px-4 text-sm font-medium text-white hover:bg-rose-500"
            >
              <IconRefresh size={16} />
              {t('boundary.devReload')}
            </button>
          </div>
        </div>
        <div className="p-6 space-y-4">
          <div role="tablist" className="flex flex-wrap border border-slate-700 rounded-md overflow-hidden w-fit max-w-full">
            {tabs.map(([id, label]) => (
              <button
                key={id}
                type="button"
                role="tab"
                aria-selected={tab === id}
                onClick={() => setTab(id)}
                className={`min-h-12 px-4 text-sm border-slate-700 [&:not(:first-child)]:border-l ${tab === id ? 'bg-slate-800 text-white font-semibold' : 'text-slate-300 hover:bg-slate-800/60'}`}
              >
                {label}
              </button>
            ))}
          </div>
          {tab === 'frames' && (
            <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
              <ul className="rounded-md bg-slate-950 border border-slate-800 divide-y divide-slate-800 max-h-96 overflow-auto">
                {frames.length === 0 && <li className="p-3 text-sm text-slate-400">No stack frames in this error.</li>}
                {frames.map((frame, index) => (
                  <li key={index}>
                    <button
                      type="button"
                      onClick={() => setSelected(index)}
                      aria-current={selected === index}
                      className={`w-full min-h-12 text-left px-3 py-2 text-xs font-mono ${selected === index ? 'bg-slate-800 text-white' : 'text-slate-300 hover:bg-slate-900'}`}
                    >
                      <span className="block font-semibold">{frame.fn}</span>
                      <span className="block text-slate-400 break-all">{frame.location}</span>
                    </button>
                  </li>
                ))}
              </ul>
              <pre className="rounded-md bg-slate-950 p-4 text-xs font-mono text-rose-200 overflow-auto border border-slate-800 max-h-96 whitespace-pre-wrap break-words">
                {error.stack ?? `${error.name}: ${error.message}`}
              </pre>
            </div>
          )}
          {tab === 'react' && (
            <pre className="rounded-md bg-slate-950 p-4 text-xs font-mono text-slate-300 overflow-x-auto border border-slate-800">{componentStack ?? 'No component stack.'}</pre>
          )}
          {tab === 'env' && (
            <dl className="rounded-md bg-slate-950 p-4 text-xs font-mono border border-slate-800 grid gap-2">
              {env.map(([key, value]) => (
                <div key={key}>
                  <dt className="text-slate-400">{key}</dt>
                  <dd className="text-slate-200 break-all">{value}</dd>
                </div>
              ))}
            </dl>
          )}
        </div>
      </div>
    </div>
  )
}

function CopyDev({ text }: { text: () => string }) {
  const [done, setDone] = useState(false)
  const timer = useRef<number | undefined>(undefined)
  useEffect(() => () => window.clearTimeout(timer.current), [])
  return (
    <button
      type="button"
      onClick={() => {
        void copyText(text()).then((ok) => {
          setDone(ok)
          window.clearTimeout(timer.current)
          timer.current = window.setTimeout(() => setDone(false), 2500)
        })
      }}
      className="inline-flex min-h-12 min-w-[9.5rem] items-center justify-center gap-2 rounded-md border border-slate-600 px-4 text-sm text-slate-100 hover:bg-slate-800"
    >
      <IconCopy size={16} />
      {done ? 'Copied' : t('boundary.copy')}
    </button>
  )
}

// Catches uncaught React render errors: a calm page in release channels, the full diagnostic console in development. It is a class
// component (React has no hook for this), so it reads its few texts from i18n directly (errors:boundary), always in English.
export class ErrorBoundary extends Component<Props, State> {
  public state: State = { error: null, componentStack: null }

  public static getDerivedStateFromError(error: Error): Partial<State> {
    return { error }
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    console.error('Uncaught render error:', error, errorInfo)
    this.setState({ componentStack: errorInfo.componentStack ?? null })
  }

  public render(): ReactNode {
    const { error, componentStack } = this.state
    if (!error) return this.props.children
    if (!import.meta.env.DEV) return <CrashPage error={error} componentStack={componentStack} />
    return <DevConsole error={error} componentStack={componentStack} />
  }
}
