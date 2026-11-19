import { useId, useState } from 'react'
import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import { IconAlertCircle, IconArrowLeft, IconSettings } from '@tabler/icons-react'
import type { PlatformId, PlatformVariant } from '../../api/types'
import { settingsHref } from '../shell/ShellContext'
import { ReservedText } from '../ui/ReservedText'
import { CoverUpload } from './CoverUpload'
import { PlatformPicker } from './PlatformPicker'
import { MAX_TITLE_LENGTH, titleProblem, type CoverProblem } from './addGameRules'
import type { ReactNode } from 'react'

interface ManualEntryProps {
  title: string
  onTitle: (title: string) => void
  // A submit was tried: every problem of every field shows, also the one of an untouched empty title
  attempted: boolean
  platform: PlatformId | ''
  variant: PlatformVariant | ''
  onPlatform: (platform: PlatformId | '', variant: PlatformVariant | '') => void
  cover: File | null
  onCover: (file: File | null) => void
  onCoverProblem: (problem: CoverProblem | null) => void
  // IGDB is set up (the player chose to add this game without it): the way back to the search is offered, the AI note says what happens without a hit
  igdbAvailable: boolean
  onBackToIgdb: () => void
  // Something is running (adding, uploading): nothing can be changed
  disabled?: boolean
  // The Steam choice's note (the dialog says where Steam games come from)
  steamNote?: ReactNode
  // Closes the dialog when the quiet link to the Twitch settings is followed
  onLeave: () => void
}

/**
 * Adding a game without an IGDB hit: title (checked on every keystroke), platform (grid), cover (drop or file). Says calmly that the AI researches the facts
 * later; where there is no IGDB access, a quiet link leads to the Twitch settings, otherwise one leads back to the search.
 */
export function ManualEntry({
  title,
  onTitle,
  attempted,
  platform,
  variant,
  onPlatform,
  cover,
  onCover,
  onCoverProblem,
  igdbAvailable,
  onBackToIgdb,
  disabled = false,
  steamNote,
  onLeave,
}: ManualEntryProps) {
  const { t } = useTranslation('dashboard')
  const id = useId()
  // An untouched empty field stays calm; "required" shows after typing and emptying, or after a submit attempt
  const [touched, setTouched] = useState(false)
  const problem = titleProblem(title)
  const shown = problem && (touched || attempted) ? problem : null
  const message = shown ? (shown.kind === 'required' ? t('addGame.manual.titleRequired') : t('addGame.manual.titleTooLong', { length: shown.length, max: shown.max })) : ''
  const candidates = [t('addGame.manual.titleRequired'), t('addGame.manual.titleTooLong', { length: MAX_TITLE_LENGTH + 1000, max: MAX_TITLE_LENGTH })]

  return (
    <div className="pv-manual">
      <div className="pv-manual-field">
        <label htmlFor={`${id}-title`} className="pv-field-label">
          {t('addGame.manual.titleLabel')}
        </label>
        <input
          id={`${id}-title`}
          type="text"
          value={title}
          disabled={disabled}
          onChange={(e) => {
            setTouched(true)
            onTitle(e.target.value)
          }}
          placeholder={t('addGame.manual.titlePlaceholder')}
          autoComplete="off"
          aria-invalid={shown ? true : undefined}
          aria-describedby={`${id}-title-msg`}
          className="pv-input"
          style={shown ? { borderColor: 'var(--accent-crimson)' } : undefined}
        />
        <ReservedText id={`${id}-title-msg`} className="pv-field-msg" candidates={candidates} aria-live="polite">
          {shown && (
            <span className="pv-field-msg-in" data-tone="error">
              <IconAlertCircle size={16} aria-hidden="true" />
              <span>{message}</span>
            </span>
          )}
        </ReservedText>
      </div>

      <PlatformPicker platform={platform} variant={variant} onChange={onPlatform} disabled={disabled} steamNote={steamNote} />

      <div className="pv-manual-field">
        <span className="pv-field-label">{t('addGame.manual.coverLabel')}</span>
        <CoverUpload file={cover} onChange={onCover} onProblem={onCoverProblem} disabled={disabled} />
      </div>

      <div className="pv-note">{igdbAvailable ? t('addGame.manual.aiNoteWithIgdb') : t('addGame.manual.aiNote')}</div>

      {igdbAvailable ? (
        <button type="button" className="pv-btn-text pv-igdb-without" disabled={disabled} onClick={onBackToIgdb}>
          <IconArrowLeft size={16} aria-hidden="true" />
          <span>{t('addGame.manual.backToIgdb')}</span>
        </button>
      ) : (
        <Link to={settingsHref('twitch')} className="pv-btn-text pv-igdb-without" onClick={onLeave}>
          <IconSettings size={16} aria-hidden="true" />
          <span>{t('addGame.manual.twitchLink')}</span>
        </Link>
      )}
    </div>
  )
}
