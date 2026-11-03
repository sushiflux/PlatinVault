import { Component } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import { IconAlertTriangle, IconRefresh } from '@tabler/icons-react'
import i18n from '../../i18n'

interface Props {
  children?: ReactNode
}

interface State {
  error: Error | null
  componentStack: string | null
}

// Catches uncaught React render errors and renders an Ignition-inspired diagnostic console in development.
// It is a class component, so it reads its few texts from i18n directly (errors:boundary) in the language active when it crashed.
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

    const t = i18n.getFixedT(null, 'errors')

    if (!import.meta.env.DEV) {
      return (
        <div className="min-h-screen bg-slate-950 text-slate-100 flex items-center justify-center p-6">
          <div className="max-w-md w-full rounded-2xl border border-slate-800 bg-slate-900 p-8 text-center">
            <IconAlertTriangle size={40} className="mx-auto mb-4 text-amber-400" />
            <h1 className="text-xl font-bold mb-2">{t('boundary.title')}</h1>
            <p className="text-sm text-slate-400 mb-6">{t('boundary.text')}</p>
            <button
              type="button"
              onClick={() => window.location.reload()}
              className="inline-flex items-center gap-2 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-500"
            >
              <IconRefresh size={16} />
              {t('boundary.reload')}
            </button>
          </div>
        </div>
      )
    }

    return (
      <div className="min-h-screen bg-slate-950 text-slate-100 p-6 font-sans">
        <div className="max-w-5xl mx-auto rounded-2xl border border-rose-500/40 bg-slate-900 overflow-hidden shadow-2xl">
          <div className="bg-rose-950/60 border-b border-rose-500/30 px-6 py-4 flex items-center justify-between">
            <div className="flex items-center gap-3">
              <IconAlertTriangle size={24} className="text-rose-400" />
              <div>
                <span className="text-xs font-mono uppercase tracking-wider text-rose-300">
                  React Render Exception · {error.name}
                </span>
                <h1 className="text-lg font-bold text-white">{error.message}</h1>
              </div>
            </div>
            <button
              type="button"
              onClick={() => window.location.reload()}
              className="inline-flex items-center gap-2 rounded-lg bg-rose-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-rose-500"
            >
              <IconRefresh size={15} />
              {t('boundary.devReload')}
            </button>
          </div>
          <div className="p-6 space-y-4">
            {error.stack && (
              <div>
                <h2 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
                  Stack Trace
                </h2>
                <pre className="rounded-xl bg-slate-950 p-4 text-xs font-mono text-rose-200 overflow-x-auto border border-slate-800">
                  {error.stack}
                </pre>
              </div>
            )}
            {componentStack && (
              <div>
                <h2 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
                  Component Stack
                </h2>
                <pre className="rounded-xl bg-slate-950 p-4 text-xs font-mono text-slate-300 overflow-x-auto border border-slate-800">
                  {componentStack}
                </pre>
              </div>
            )}
          </div>
        </div>
      </div>
    )
  }
}

