import { useEffect, useId, useState, type ReactNode, type RefObject } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import {
  IconAlertCircle,
  IconCalendar,
  IconChevronRight,
  IconCircleCheck,
  IconClock,
  IconDeviceGamepad2,
  IconExternalLink,
  IconLoader2,
  IconPencil,
  IconSearch,
  IconTag,
  IconUsers,
} from '@tabler/icons-react'
import { fetchIgdbGame, searchIgdbGames } from '../../api/client'
import type { IgdbGameDto, IgdbSearchHitDto } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { describeError } from '../../utils/apiErrorText'
import { platformLabelKey } from '../../utils/platforms'
import { ErrorBlock } from '../ui/ErrorBlock'
import { ReservedText } from '../ui/ReservedText'
import { MAX_SEARCH_LENGTH, parseSearchInput, type SearchInput } from './addGameRules'

// Wait this long after the last keystroke before IGDB is asked (no request per keystroke)
export const IGDB_SEARCH_DELAY_MS = 350

export type SearchState =
  | { kind: 'idle' }
  | { kind: 'invalid'; input: SearchInput }
  | { kind: 'searching'; input: SearchInput }
  | { kind: 'results'; input: SearchInput; query: string; hits: IgdbSearchHitDto[] }
  // The server answered 204: no Twitch credentials after all
  | { kind: 'unavailable' }
  | { kind: 'failed'; error: unknown }

/**
 * The state of the IGDB search for what is typed: an answer for an older text is dropped, an unusable text (empty, too long, an address that is not an
 * IGDB game) is judged on every keystroke and never sent, everything else is asked after a short pause. `enabled` false = nothing runs (a game is picked).
 */
export function useIgdbSearch(raw: string, enabled: boolean, attempt = 0): SearchState {
  const [state, setState] = useState<SearchState>({ kind: 'idle' })
  useEffect(() => {
    if (!enabled) return
    const input = parseSearchInput(raw)
    if (input.kind === 'empty') {
      setState({ kind: 'idle' })
      return
    }
    if (input.kind === 'tooLong' || input.kind === 'badLink') {
      setState({ kind: 'invalid', input })
      return
    }
    setState({ kind: 'searching', input })
    const controller = new AbortController()
    const query = raw.trim()
    const timer = window.setTimeout(() => {
      searchIgdbGames(query, controller.signal).then(
        (answer) => {
          if (controller.signal.aborted) return
          setState(answer === null ? { kind: 'unavailable' } : { kind: 'results', input, query, hits: answer.results })
        },
        (error: unknown) => {
          if (controller.signal.aborted) return
          setState({ kind: 'failed', error })
        }
      )
    }, IGDB_SEARCH_DELAY_MS)
    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [raw, enabled, attempt])
  return state
}

interface IgdbSearchProps {
  query: string
  onQuery: (query: string) => void
  picked: IgdbSearchHitDto | null
  onPick: (hit: IgdbSearchHitDto) => void
  // "Anderes Spiel wählen" (and editing the text while a game is picked)
  onClearPick: () => void
  // "Nicht dabei? Ohne IGDB hinzufügen"
  onWithoutIgdb: () => void
  disabled?: boolean
  inputRef?: RefObject<HTMLInputElement | null>
}

// The names of the platforms IGDB lists for a hit as one line; our own platform name where it maps to one, else IGDB's
function hitLine(hit: IgdbSearchHitDto, t: TFunction<['dashboard', 'common']>): string {
  const platforms = hit.platforms.map((p) => (p.maps ? t(`common:platforms.${platformLabelKey(p.maps.platform, p.maps.variant)}`) : p.name))
  const unique = [...new Set(platforms)].join(', ')
  if (hit.releaseYear && unique) return t('dashboard:addGame.search.yearAndPlatforms', { year: hit.releaseYear, platforms: unique })
  if (unique) return t('dashboard:addGame.search.onlyPlatforms', { platforms: unique })
  if (hit.releaseYear) return t('dashboard:addGame.search.onlyYear', { year: hit.releaseYear })
  return t('dashboard:addGame.search.noInfo')
}

// A small cover, or the calm placeholder (the cover of a hit may be missing or fail to load)
function Thumb({ url, className }: { url: string | null; className: string }) {
  const [failed, setFailed] = useState(false)
  if (!url || failed) {
    return (
      <span className={`${className} pv-igdb-thumb--empty`} aria-hidden="true">
        <IconDeviceGamepad2 size={20} />
      </span>
    )
  }
  return <img src={url} alt="" loading="lazy" className={className} onError={() => setFailed(true)} />
}

/**
 * The IGDB search of the add dialog: one field for a name, an IGDB number or an igdb.com/games/<slug> link, a result list, the way out "Nicht dabei? Ohne
 * IGDB hinzufügen". The line under the field (hint, what was recognised, what is wrong) has its room reserved; the field turns red with aria-invalid
 * while the text cannot be searched.
 */
export function IgdbSearch({ query, onQuery, picked, onPick, onClearPick, onWithoutIgdb, disabled = false, inputRef }: IgdbSearchProps) {
  const { t } = useTranslation(['dashboard', 'common'])
  const id = useId()
  const [attempt, setAttempt] = useState(0)
  const state = useIgdbSearch(query, picked === null, attempt)

  // The line under the field
  const lineCandidates = [
    t('dashboard:addGame.search.hint'),
    t('dashboard:addGame.search.searching'),
    t('dashboard:addGame.search.tooLong', { length: MAX_SEARCH_LENGTH + 100, max: MAX_SEARCH_LENGTH }),
    t('dashboard:addGame.search.notIgdbLink'),
    t('dashboard:addGame.search.foundId', { id: 9999999999 }),
    t('dashboard:addGame.search.foundLink', { slug: 'the-legend-of-zelda-tears-of-the-kingdom' }),
  ]
  let line: { tone: 'muted' | 'error' | 'ok'; text: string; busy?: boolean } | null = { tone: 'muted', text: t('dashboard:addGame.search.hint') }
  const invalid = state.kind === 'invalid'
  if (picked) line = { tone: 'muted', text: t('dashboard:addGame.search.hint') }
  else if (state.kind === 'searching') {
    line = { tone: 'muted', text: t('dashboard:addGame.search.searching'), busy: true }
  } else if (state.kind === 'invalid') {
    line =
      state.input.kind === 'tooLong'
        ? { tone: 'error', text: t('dashboard:addGame.search.tooLong', { length: state.input.length, max: state.input.max }) }
        : { tone: 'error', text: t('dashboard:addGame.search.notIgdbLink') }
  } else if (state.kind === 'results' && state.input.kind === 'id' && state.hits.length > 0) {
    line = { tone: 'ok', text: t('dashboard:addGame.search.foundId', { id: state.input.id }) }
  } else if (state.kind === 'results' && state.input.kind === 'link' && state.hits.length > 0) {
    line = { tone: 'ok', text: t('dashboard:addGame.search.foundLink', { slug: state.input.slug }) }
  } else if (state.kind === 'results') {
    line = null
  }

  return (
    <div className="pv-igdb">
      <label htmlFor={`${id}-q`} className="pv-field-label">
        {t('dashboard:addGame.search.label')}
      </label>
      <div className="pv-igdb-field">
        <IconSearch size={18} aria-hidden="true" />
        <input
          id={`${id}-q`}
          ref={inputRef}
          type="text"
          value={query}
          disabled={disabled}
          onChange={(e) => {
            onQuery(e.target.value)
            if (picked) onClearPick()
          }}
          placeholder={t('dashboard:addGame.search.placeholder')}
          autoComplete="off"
          spellCheck={false}
          aria-invalid={invalid ? true : undefined}
          aria-describedby={`${id}-msg`}
          className="pv-input pv-input-icon"
          style={invalid ? { borderColor: 'var(--accent-crimson)' } : undefined}
        />
      </div>
      <ReservedText id={`${id}-msg`} className="pv-field-msg" candidates={lineCandidates} aria-live="polite">
        {line && (
          <span className="pv-field-msg-in" data-tone={line.tone}>
            {line.tone === 'error' ? (
              <IconAlertCircle size={16} aria-hidden="true" />
            ) : line.tone === 'ok' ? (
              <IconCircleCheck size={16} aria-hidden="true" />
            ) : line.busy ? (
              <IconLoader2 size={16} className="animate-spin" aria-hidden="true" />
            ) : null}
            <span>{line.text}</span>
          </span>
        )}
      </ReservedText>

      {!picked && (
        <div className="pv-igdb-area">
          {state.kind === 'results' && state.hits.length > 0 && (
            <div>
              <h3 className="pv-eyebrow pv-igdb-count">{t('dashboard:addGame.search.results', { count: state.hits.length })}</h3>
              <ul className="pv-igdb-list" aria-label={t('dashboard:addGame.search.resultsLabel')}>
                {state.hits.map((hit) => (
                  <li key={hit.igdbId}>
                    <button type="button" className="pv-igdb-hit" disabled={disabled} onClick={() => onPick(hit)} aria-label={t('dashboard:addGame.search.pick', { name: hit.name })}>
                      <Thumb url={hit.coverUrl} className="pv-igdb-thumb" />
                      <span className="pv-igdb-hit-text">
                        <span className="pv-igdb-hit-name">{hit.name}</span>
                        <span className="pv-igdb-hit-meta">{hitLine(hit, t)}</span>
                        {hit.existing.length > 0 && (
                          <span className="pv-igdb-hit-have">
                            {t('dashboard:addGame.search.alreadyAdded', {
                              platforms: hit.existing.map((e) => t(`common:platforms.${platformLabelKey(e.platform, e.variant)}`)).join(', '),
                            })}
                          </span>
                        )}
                      </span>
                      <IconChevronRight size={18} aria-hidden="true" />
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {state.kind === 'results' && state.hits.length === 0 && (
            <p className="pv-igdb-note" role="status">
              {t('dashboard:addGame.search.noResults', { query: state.query })}
            </p>
          )}
          {state.kind === 'unavailable' && (
            <p className="pv-igdb-note" role="status">
              {t('dashboard:addGame.search.unavailable')}
            </p>
          )}
          {state.kind === 'failed' && <SearchFailure error={state.error} onRetry={() => setAttempt((n) => n + 1)} />}
        </div>
      )}

      {!picked && (
        <button type="button" className="pv-btn-text pv-igdb-without" disabled={disabled} onClick={onWithoutIgdb}>
          <IconPencil size={16} aria-hidden="true" />
          <span>{t('dashboard:addGame.search.withoutIgdb')}</span>
        </button>
      )}
    </div>
  )
}

// A failed search in full: what failed, the server's reason (IGDB's own words behind the arrow), the copy button, and "try again"
function SearchFailure({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const { t } = useTranslation(['dashboard', 'common'])
  const { message, details } = describeError(error, t('common:unknownError'))
  return (
    <div className="pv-igdb-fail">
      <ErrorBlock title={t('dashboard:addGame.search.failedTitle')} message={message} details={details} defaultOpen />
      <button type="button" className="pv-btn" onClick={onRetry}>
        {t('common:retry')}
      </button>
    </div>
  )
}

// ---- the picked game: cover and the facts IGDB has ----------------------------------------------------------------------------

function Fact({ icon, label, children }: { icon: ReactNode; label: string; children: ReactNode }) {
  return (
    <div className="pv-igdb-fact">
      <span className="pv-igdb-fact-ic" aria-hidden="true">
        {icon}
      </span>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}

interface IgdbPickedProps {
  hit: IgdbSearchHitDto
  // The facts once they are loaded (the dialog loads them, it also needs the platforms); undefined = still loading, null = could not be loaded
  game: IgdbGameDto | null | undefined
  failure: unknown
  onChange: () => void
}

/**
 * The picked game: its cover and what IGDB knows (release, developer, genres, time to beat, the two ratings); what IGDB does not know is left out. While the
 * facts load, a calm line says so inside the card (the cover and the name are there from the search hit, so the card keeps its height).
 */
export function IgdbPicked({ hit, game, failure, onChange }: IgdbPickedProps) {
  const { t } = useTranslation(['dashboard', 'common'])
  const fmt = useFormat()
  const released = game?.firstReleaseAt ? fmt.date(game.firstReleaseAt) : hit.firstReleaseAt ? fmt.date(hit.firstReleaseAt) : hit.releaseYear ? String(hit.releaseYear) : ''
  const developers = game?.developers?.join(', ') ?? ''
  const genres = game?.genres.join(', ') ?? ''
  const beat = game?.timeToBeat
  const hours = beat?.completelyHours ?? null
  const time = hours != null && hours > 0 ? fmt.number(hours, hours < 10 ? 1 : 0) : ''
  const duration = time ? (beat?.count ? t('dashboard:addGame.picked.durationWithCount', { time, count: beat.count }) : t('dashboard:addGame.picked.durationValue', { time })) : ''
  const critics = game?.aggregatedRating != null ? Math.round(game.aggregatedRating) : null
  const players = game?.rating != null ? Math.round(game.rating) : null
  return (
    <section className="pv-igdb-picked" aria-label={t('dashboard:addGame.picked.region')}>
      <Thumb url={game?.coverUrl ?? hit.coverUrl} className="pv-igdb-cover" />
      <div className="pv-igdb-picked-body">
        <h3 className="pv-igdb-picked-name">{hit.name}</h3>
        <dl className="pv-igdb-facts">
          {released && (
            <Fact icon={<IconCalendar size={16} />} label={t('dashboard:addGame.picked.released')}>
              {released}
            </Fact>
          )}
          {developers && (
            <Fact icon={<IconUsers size={16} />} label={t('dashboard:addGame.picked.developer')}>
              {developers}
            </Fact>
          )}
          {genres && (
            <Fact icon={<IconTag size={16} />} label={t('dashboard:addGame.picked.genres')}>
              {genres}
            </Fact>
          )}
          {duration && (
            <Fact icon={<IconClock size={16} />} label={t('dashboard:addGame.picked.duration')}>
              {duration}
            </Fact>
          )}
        </dl>
        {game === undefined && !failure && (
          <p className="pv-igdb-loading" role="status">
            <IconLoader2 size={16} className="animate-spin" aria-hidden="true" />
            <span>{t('dashboard:addGame.picked.factsLoading')}</span>
          </p>
        )}
        {(critics != null || players != null) && (
          <p className="pv-igdb-ratings">
            {critics != null && (
              <span>
                <IconPencil size={14} aria-hidden="true" />
                {t('dashboard:addGame.picked.critics', { score: critics })}
              </span>
            )}
            {players != null && (
              <span>
                <IconUsers size={14} aria-hidden="true" />
                {t('dashboard:addGame.picked.players', { score: players })}
              </span>
            )}
          </p>
        )}
        <p className="pv-igdb-source">
          <span>{t('dashboard:addGame.picked.source')}</span>
          {game?.url && (
            <a href={game.url} target="_blank" rel="noreferrer" className="pv-igdb-link">
              <IconExternalLink size={14} aria-hidden="true" />
              {t('dashboard:addGame.picked.igdbPage')}
            </a>
          )}
          <button type="button" className="pv-btn-text pv-igdb-link" onClick={onChange}>
            {t('dashboard:addGame.picked.change')}
          </button>
        </p>
        {failure != null && <FactsFailure error={failure} />}
      </div>
    </section>
  )
}

function FactsFailure({ error }: { error: unknown }) {
  const { t } = useTranslation(['dashboard', 'common'])
  const { message, details } = describeError(error, t('common:unknownError'))
  return (
    <div className="pv-igdb-factsfail">
      <ErrorBlock title={t('dashboard:addGame.picked.factsFailedTitle')} message={`${t('dashboard:addGame.picked.factsFailed')} ${message}`.trim()} details={details} />
    </div>
  )
}

// The facts of the picked game: one query per IGDB number (the server caches them too)
export function useIgdbFacts(igdbId: number | null) {
  return useQuery({
    queryKey: ['igdb-game', igdbId],
    queryFn: () => fetchIgdbGame(igdbId!),
    enabled: igdbId !== null,
    staleTime: 10 * 60_000,
    retry: false,
  })
}
