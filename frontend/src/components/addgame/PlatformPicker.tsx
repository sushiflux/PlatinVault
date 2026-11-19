import { useId, useRef, type KeyboardEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { PlatformId, PlatformVariant } from '../../api/types'
import { PLATFORM_IDS, platformLabelKey, variantsOf } from '../../utils/platforms'
import { ReservedText } from '../ui/ReservedText'
import { isMarked, type IgdbMarks } from './addGameRules'

interface PlatformPickerProps {
  platform: PlatformId | ''
  variant: PlatformVariant | ''
  onChange: (platform: PlatformId | '', variant: PlatformVariant | '') => void
  // What IGDB lists for the picked game (marks the platforms with "IGDB"); null = no IGDB game picked (nothing is marked)
  marks?: IgdbMarks | null
  disabled?: boolean
  // What to show next to the Steam choice (the add dialog: a note that Steam games come from the library with a link)
  steamNote?: ReactNode
}

// The choices in the order of the grid: 3 x 3 on a wide screen, a connected vertical list on a phone (CSS); Steam is part of it so the grid is full
// and says why a Steam game is not added here
const ORDER = PLATFORM_IDS

/**
 * The platform choice of the add dialog: a connected 3 x 3 grid (a connected list on a phone), as a radio group with arrow keys. With a variant
 * platform (PlayStation) a connected PS5 | PS4 bar follows, because the two trophy lists are separate. The platforms IGDB lists for the picked game carry
 * a small "IGDB" mark; "PC" marks no shop (IGDB names no shops). Nintendo Switch says calmly that it has no trophies (guide only).
 */
export function PlatformPicker({ platform, variant, onChange, marks = null, disabled = false, steamNote }: PlatformPickerProps) {
  const { t } = useTranslation(['dashboard', 'common'])
  const labelId = useId()
  const variantLabelId = useId()
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  // The PS5 | PS4 bar is part of the picker's slot at all times (invisible unless PlayStation is chosen), so its room is always there
  const variantList = variantsOf('playstation')
  const variantsOn = platform !== '' && variantsOf(platform).length > 0

  const nameOf = (id: PlatformId, v?: PlatformVariant | null) => t(`common:platforms.${platformLabelKey(id, v)}`)
  const markedNames = marks ? marks.choices.map((c) => nameOf(c.platform, c.variant)) : []

  // The line under the grid: what IGDB marked (and the PC hint), or how to choose; all of its texts are known, so its room is reserved
  const hintCandidates = [
    t('dashboard:addGame.platform.free'),
    t('dashboard:addGame.platform.noneMarked'),
    `${t('dashboard:addGame.platform.marked', { names: PLATFORM_IDS.map((id) => nameOf(id)).join(', ') })} ${t('dashboard:addGame.platform.pc')}`,
  ]
  let hint: string = t('dashboard:addGame.platform.free')
  if (marks) {
    const parts: string[] = []
    if (markedNames.length > 0) parts.push(t('dashboard:addGame.platform.marked', { names: markedNames.join(', ') }))
    else if (!marks.listsPc) parts.push(t('dashboard:addGame.platform.noneMarked'))
    if (marks.listsPc) parts.push(t('dashboard:addGame.platform.pc'))
    hint = parts.join(' ')
  }

  const focusIndex = (index: number) => {
    const next = (index + ORDER.length) % ORDER.length
    const id = ORDER[next]!
    onChange(id, '')
    refs.current[next]?.focus()
  }
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const current = platform ? ORDER.indexOf(platform) : -1
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault()
      focusIndex(current + 1)
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault()
      focusIndex(current <= 0 ? ORDER.length - 1 : current - 1)
    }
  }

  const firstFocusable = platform ? ORDER.indexOf(platform) : 0

  return (
    <div className="pv-pf">
      <span className="pv-field-label" id={labelId}>
        {t('dashboard:addGame.platform.label')}
      </span>
      <div className="pv-pf-grid" role="radiogroup" aria-labelledby={labelId} onKeyDown={onKeyDown}>
        {ORDER.map((id, index) => {
          const checked = platform === id
          const marked = marks ? isMarked(marks, id) : false
          return (
            <button
              key={id}
              ref={(node) => {
                refs.current[index] = node
              }}
              type="button"
              role="radio"
              aria-checked={checked}
              tabIndex={index === firstFocusable ? 0 : -1}
              disabled={disabled}
              className="pv-pf-cell"
              data-platform={id}
              onClick={() => onChange(id, id === platform ? variant : '')}
            >
              <span>{nameOf(id)}</span>
              {marked && (
                <span className="pv-igdb-mark" aria-label={t('dashboard:addGame.platform.igdbMarkLabel')}>
                  {t('dashboard:addGame.platform.igdbMark')}
                </span>
              )}
            </button>
          )
        })}
      </div>
      <ReservedText className="pv-field-hint" candidates={hintCandidates}>
        {hint}
      </ReservedText>

      {/* One slot for what only some platforms bring: the PS5 | PS4 bar (PlayStation), the Nintendo note, the Steam note. Everything that can ever
          show here lies on top of each other in the same cell (the bar for real but invisible while it is not needed, the notes as text copies),
          so the slot is as tall as the tallest of them at this width and choosing another platform moves nothing below it. */}
      <div className="pv-pf-slot">
        <div className="pv-pf-sub" data-off={variantsOn ? undefined : ''} aria-hidden={variantsOn ? undefined : true} inert={!variantsOn}>
          <span className="pv-field-label" id={variantLabelId}>
            {t('dashboard:addGame.platform.variantLabel')}
          </span>
          <div className="pv-segmented pv-pf-variants" role="radiogroup" aria-labelledby={variantLabelId}>
            {variantList.map((v) => (
              <button key={v} type="button" role="radio" aria-checked={variant === v} disabled={disabled} onClick={() => onChange('playstation', v)}>
                <span>{t(`common:platformChips.${v}`)}</span>
                {marks && isMarked(marks, 'playstation', v) && (
                  <span className="pv-igdb-mark" aria-label={t('dashboard:addGame.platform.igdbMarkLabel')}>
                    {t('dashboard:addGame.platform.igdbMark')}
                  </span>
                )}
              </button>
            ))}
          </div>
          <p className="pv-field-hint">{t('dashboard:addGame.platform.variantHint')}</p>
        </div>

        {platform === 'nintendo' && (
          <div className="pv-note pv-note--warn pv-pf-note" role="status">
            {t('dashboard:addGame.platform.nintendoNote')}
          </div>
        )}
        {platform === 'steam' && steamNote && (
          <div className="pv-note pv-note--warn pv-pf-note" role="status">
            {steamNote}
          </div>
        )}
        <div className="pv-note pv-note--warn pv-pf-note pv-pf-ghost" aria-hidden="true" data-text={t('dashboard:addGame.platform.nintendoNote')} />
        <div
          className="pv-note pv-note--warn pv-pf-note pv-pf-ghost"
          aria-hidden="true"
          data-text={`${t('dashboard:addGame.platform.steamNote')} ${t('dashboard:addGame.platform.openLibrary')}`}
        />
      </div>
    </div>
  )
}
