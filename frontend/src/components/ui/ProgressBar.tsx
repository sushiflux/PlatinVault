import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import '../../styles/progress.css'

interface ProgressBarProps {
  /** 0..100; values outside are clamped. */
  percent: number
  /** Accessible name; the visible numbers next to the bar usually carry the details. */
  label?: string
  /** Layout hooks only (margins, flex); the look comes from styles/progress.css. */
  className?: string
}

/**
 * The shared trophy progress line: thin, straight, square ends. The fill uses the progress colour and turns gold at 100 %
 * (data-complete), so a finished game is told apart by one gold line and nothing else. All the styling is in styles/progress.css.
 */
export function ProgressBar({ percent, label, className }: ProgressBarProps) {
  const { t } = useTranslation('game')
  const value = Number.isFinite(percent) ? Math.min(100, Math.max(0, percent)) : 0
  const complete = value >= 100

  return (
    <div
      className={className ? `pv-pbar ${className}` : 'pv-pbar'}
      role="progressbar"
      aria-label={label ?? t('progressBar.label')}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(value)}
      data-complete={complete ? 'true' : undefined}
      style={{ '--p': value } as CSSProperties}
    >
      <div className="progress-bar-track">
        <div className="progress-bar-fill" />
      </div>
    </div>
  )
}
