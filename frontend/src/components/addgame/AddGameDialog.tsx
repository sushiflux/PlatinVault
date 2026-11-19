import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { Link, useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconLoader2, IconX } from '@tabler/icons-react'
import { ApiError, createManualGame, fetchSettings, uploadGameCover } from '../../api/client'
import type { IgdbSearchHitDto, PlatformId, PlatformVariant } from '../../api/types'
import { describeError } from '../../utils/apiErrorText'
import { platformLabelKey } from '../../utils/platforms'
import { ErrorBlock } from '../ui/ErrorBlock'
import { ReservedText } from '../ui/ReservedText'
import '../../styles/dialogs.css'
import '../../styles/addgame.css'
import { IgdbPicked, IgdbSearch, useIgdbFacts } from './IgdbSearch'
import { ManualEntry } from './ManualEntry'
import { PlatformPicker } from './PlatformPicker'
import { igdbMarks, startingChoice, titleProblem, type CoverProblem } from './addGameRules'

interface AddGameDialogProps {
  onClose: () => void
}

type Phase = 'form' | 'adding' | 'uploading' | 'coverFailed'

const FOCUSABLE = 'button:not(:disabled), a[href], input:not(:disabled), [tabindex]:not([tabindex="-1"])'

/**
 * "+ Spiel hinzufügen": adds a game of another platform by hand. With Twitch credentials (IGDB) one search field finds the game by name, IGDB number or
 * link; its cover and facts come from IGDB and the platform is chosen (marked where IGDB lists it). Without credentials - or when the game is not on IGDB -
 * the player types a title, chooses the platform and uploads a cover. Duplicates (409) point to the existing game. No AI runs here: the trophy list and the
 * guide are separate steps on the game's page. Everything fixed in size: the lines under the fields have their room reserved.
 */
export function AddGameDialog({ onClose }: AddGameDialogProps) {
  const { t } = useTranslation(['dashboard', 'common'])
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const reduce = useReducedMotion() ?? false
  const panelRef = useRef<HTMLDivElement>(null)

  const settings = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  const igdbAvailable = Boolean(settings.data?.hasTwitchKey)
  const settingsReady = !settings.isPending

  // false = the IGDB search; true = by hand (also chosen by the player with "Ohne IGDB hinzufügen")
  const [byHand, setByHand] = useState(false)
  const mode: 'igdb' | 'manual' = igdbAvailable && !byHand ? 'igdb' : 'manual'

  const [query, setQuery] = useState('')
  const [picked, setPicked] = useState<IgdbSearchHitDto | null>(null)
  const [title, setTitle] = useState('')
  const [platform, setPlatform] = useState<PlatformId | ''>('')
  const [variant, setVariant] = useState<PlatformVariant | ''>('')
  const [cover, setCover] = useState<File | null>(null)
  const [coverProblemState, setCoverProblem] = useState<CoverProblem | null>(null)
  const [attempted, setAttempted] = useState(false)

  const [phase, setPhase] = useState<Phase>('form')
  const [failure, setFailure] = useState<{ title: string; message: string; details: string | null } | null>(null)
  const [duplicate, setDuplicate] = useState<{ message: string; slug: string | null } | null>(null)
  const [createdSlug, setCreatedSlug] = useState<string | null>(null)
  const busy = phase !== 'form' && phase !== 'coverFailed'
  const locked = phase !== 'form'

  const facts = useIgdbFacts(picked?.igdbId ?? null)
  // What IGDB lists for the picked game; the facts (when loaded) are the fuller answer, the hit is there at once
  const marks = useMemo(() => (picked ? igdbMarks(facts.data?.platforms ?? picked.platforms) : null), [picked, facts.data])

  // Focus the first field when the dialog opens (and when it switches between the search and the hand-made form)
  useEffect(() => {
    if (!settingsReady) return
    const first = panelRef.current?.querySelector<HTMLElement>(mode === 'igdb' ? '.pv-igdb input' : '.pv-manual input[type="text"]')
    first?.focus()
  }, [settingsReady, mode])
  // The element that had the focus before the dialog gets it back
  useEffect(() => {
    const before = document.activeElement as HTMLElement | null
    return () => before?.focus?.()
  }, [])

  const clearAnswers = () => {
    setFailure(null)
    setDuplicate(null)
  }

  const pick = (hit: IgdbSearchHitDto) => {
    clearAnswers()
    setPicked(hit)
    // IGDB's list leads to our platform when it names exactly one of ours (PS5 -> PlayStation + PS5); otherwise the player chooses
    const start = startingChoice(igdbMarks(hit.platforms))
    setPlatform(start.platform)
    setVariant(start.variant)
  }
  const clearPick = () => {
    clearAnswers()
    setPicked(null)
    setPlatform('')
    setVariant('')
  }
  const choosePlatform = (nextPlatform: PlatformId | '', nextVariant: PlatformVariant | '') => {
    clearAnswers()
    setPlatform(nextPlatform)
    setVariant(nextVariant)
  }

  // ---- what is missing, in one sentence (the footer says it, the button waits) ----
  const needsVariant = platform === 'playstation' && variant === ''
  const trimmedTitle = title.trim()
  const titleIssue = mode === 'manual' ? titleProblem(title) : null
  let reason: string | null = null
  if (mode === 'igdb' && !picked) reason = t('dashboard:addGame.footer.pickGame')
  else if (mode === 'manual' && titleIssue) reason = titleIssue.kind === 'required' ? t('dashboard:addGame.footer.enterTitle') : t('dashboard:addGame.footer.fixTitle')
  else if (platform === '') reason = t('dashboard:addGame.footer.pickPlatform')
  else if (platform === 'steam') reason = t('dashboard:addGame.footer.steam')
  else if (needsVariant) reason = t('dashboard:addGame.footer.pickVariant')
  else if (mode === 'manual' && coverProblemState) reason = t('dashboard:addGame.footer.fixCover')
  const valid = reason === null && platform !== ''
  const platformName = platform ? t(`common:platforms.${platformLabelKey(platform, variant === '' ? null : variant)}`) : ''
  const shownTitle = mode === 'igdb' ? (picked?.name ?? '') : trimmedTitle

  const footerText =
    phase === 'adding'
      ? t('dashboard:addGame.footer.adding')
      : phase === 'uploading'
        ? t('dashboard:addGame.footer.uploading')
        : phase === 'coverFailed'
          ? t('dashboard:addGame.footer.coverFailed')
          : (reason ?? t('dashboard:addGame.footer.summary', { title: shownTitle, platform: platformName }))
  const footerCandidates = [
    t('dashboard:addGame.footer.pickGame'),
    t('dashboard:addGame.footer.pickPlatform'),
    t('dashboard:addGame.footer.pickVariant'),
    t('dashboard:addGame.footer.enterTitle'),
    t('dashboard:addGame.footer.fixTitle'),
    t('dashboard:addGame.footer.fixCover'),
    t('dashboard:addGame.footer.steam'),
    t('dashboard:addGame.footer.adding'),
    t('dashboard:addGame.footer.uploading'),
    t('dashboard:addGame.footer.coverFailed'),
    t('dashboard:addGame.footer.summary', { title: 'x'.repeat(48), platform: t('common:platforms.ps5') }),
  ]

  const uploadCover = async (slug: string) => {
    if (!cover) return true
    setPhase('uploading')
    try {
      await uploadGameCover(slug, cover)
      return true
    } catch (err: unknown) {
      const { message, details } = describeError(err, t('common:unknownError'))
      setFailure({ title: t('dashboard:addGame.errors.coverFailedTitle'), message, details })
      setCreatedSlug(slug)
      setPhase('coverFailed')
      return false
    }
  }

  const submit = async () => {
    setAttempted(true)
    if (!valid || busy || phase === 'coverFailed') return
    clearAnswers()
    setPhase('adding')
    try {
      const created = await createManualGame({
        title: mode === 'manual' ? trimmedTitle : null,
        platform: platform as PlatformId,
        platformVariant: platform === 'playstation' && variant !== '' ? variant : null,
        igdbId: mode === 'igdb' ? picked!.igdbId : null,
      })
      // The cover goes up only for a game created by hand (an IGDB game brings its own)
      const coverOk = mode === 'manual' ? await uploadCover(created.slug) : true
      await queryClient.invalidateQueries({ queryKey: ['games'] })
      if (coverOk) void navigate(`/games/${created.slug}`)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 409) {
        setDuplicate({ message: err.message, slug: err.existingSlug ?? null })
      } else {
        const { message, details } = describeError(err, t('common:unknownError'))
        setFailure({ title: t('dashboard:addGame.errors.failedTitle'), message, details })
      }
      setPhase('form')
    }
  }

  const retryCover = async () => {
    if (!createdSlug) return
    setFailure(null)
    if (await uploadCover(createdSlug)) {
      await queryClient.invalidateQueries({ queryKey: ['games'] })
      void navigate(`/games/${createdSlug}`)
    }
  }

  const pristine = query === '' && picked === null && title === '' && platform === '' && cover === null
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.stopPropagation()
      if (!busy) onClose()
      return
    }
    if (event.key !== 'Tab' || !panelRef.current) return
    const nodes = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((node) => node.offsetParent !== null || node === document.activeElement)
    const first = nodes[0]
    const last = nodes[nodes.length - 1]
    if (!first || !last) return
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  const steamNote = (
    <>
      <span>{t('dashboard:addGame.platform.steamNote')}</span>{' '}
      <Link to="/library" className="pv-btn-text pv-pf-steamlink">
        {t('dashboard:addGame.platform.openLibrary')}
      </Link>
    </>
  )

  const dialog = (
    <motion.div
      className="pv-modal-backdrop pv-modal-backdrop--top"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: reduce ? 0 : 0.16 }}
      onMouseDown={(e) => {
        // A form with something typed in it is never thrown away by a stray click beside it
        if (e.target === e.currentTarget && pristine && !busy) onClose()
      }}
    >
      <motion.div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="addgame-title"
        className="pv-modal pv-addgame"
        onKeyDown={onKeyDown}
        initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.97, y: 10 }}
        animate={reduce ? { opacity: 1 } : { opacity: 1, scale: 1, y: 0 }}
        transition={reduce ? { duration: 0 } : { type: 'spring', stiffness: 420, damping: 32 }}
      >
        <div className="pv-addgame-head">
          <div className="min-w-0">
            <h2 id="addgame-title" className="pv-dialog-title">
              {t('dashboard:addGame.title')}
            </h2>
            <p className="pv-addgame-sub">{mode === 'igdb' ? t('dashboard:addGame.subtitleIgdb') : t('dashboard:addGame.subtitleManual')}</p>
          </div>
          <button type="button" className="pv-btn pv-icon-btn pv-icon-btn--ghost shrink-0" onClick={onClose} disabled={busy} aria-label={t('common:close')}>
            <IconX size={20} aria-hidden="true" />
          </button>
        </div>

        <div className="pv-addgame-body">
          {!settingsReady ? (
            <p className="pv-igdb-loading" role="status">
              <IconLoader2 size={16} className="animate-spin" aria-hidden="true" />
              <span>{t('dashboard:addGame.loadingSettings')}</span>
            </p>
          ) : (
            <>
              {settings.isError && (
                <ErrorBlock
                  title={t('dashboard:addGame.settingsFailed')}
                  message={describeError(settings.error, t('common:unknownError')).message}
                  details={describeError(settings.error, t('common:unknownError')).details}
                  defaultOpen
                />
              )}
              {mode === 'igdb' ? (
                <>
                  <IgdbSearch
                    query={query}
                    onQuery={(next) => {
                      clearAnswers()
                      setQuery(next)
                    }}
                    picked={picked}
                    onPick={pick}
                    onClearPick={clearPick}
                    onWithoutIgdb={() => {
                      clearAnswers()
                      setByHand(true)
                    }}
                    disabled={locked}
                  />
                  {picked && (
                    <>
                      <IgdbPicked hit={picked} game={facts.isPending ? undefined : (facts.data ?? null)} failure={facts.error} onChange={clearPick} />
                      <PlatformPicker platform={platform} variant={variant} onChange={choosePlatform} marks={marks} disabled={locked} steamNote={steamNote} />
                    </>
                  )}
                </>
              ) : (
                <ManualEntry
                  title={title}
                  onTitle={(next) => {
                    clearAnswers()
                    setTitle(next)
                  }}
                  attempted={attempted}
                  platform={platform}
                  variant={variant}
                  onPlatform={choosePlatform}
                  cover={cover}
                  onCover={setCover}
                  onCoverProblem={setCoverProblem}
                  igdbAvailable={igdbAvailable}
                  onBackToIgdb={() => {
                    clearAnswers()
                    setByHand(false)
                  }}
                  disabled={locked}
                  steamNote={steamNote}
                  onLeave={onClose}
                />
              )}

              {duplicate && (
                <div className="pv-addgame-answer" data-testid="add-duplicate">
                  <ErrorBlock title={t('dashboard:addGame.errors.duplicateTitle')} message={duplicate.message} defaultOpen />
                  {duplicate.slug && (
                    <Link to={`/games/${duplicate.slug}`} className="pv-btn">
                      {t('dashboard:addGame.errors.openExisting')}
                    </Link>
                  )}
                </div>
              )}
              {failure && (
                <div className="pv-addgame-answer">
                  <ErrorBlock title={failure.title} message={failure.message} details={failure.details} defaultOpen />
                </div>
              )}
            </>
          )}
        </div>

        <div className="pv-addgame-foot">
          <ReservedText className="pv-addgame-reason" candidates={footerCandidates} aria-live="polite">
            {footerText}
          </ReservedText>
          <div className="pv-addgame-actions">
            {phase === 'coverFailed' ? (
              <>
                <button type="button" className="pv-btn" onClick={() => createdSlug && void navigate(`/games/${createdSlug}`)}>
                  {t('dashboard:addGame.actions.toGame')}
                </button>
                <button type="button" className="pv-btn pv-btn-primary" onClick={() => void retryCover()}>
                  {t('dashboard:addGame.actions.retryCover')}
                </button>
              </>
            ) : (
              <>
                <button type="button" className="pv-btn" onClick={onClose} disabled={busy}>
                  {t('common:cancel')}
                </button>
                {/* The spinner lies over the label, so the button keeps its size */}
                <button type="button" className="pv-btn pv-btn-primary" disabled={!valid || busy} onClick={() => void submit()}>
                  <span className="relative inline-flex items-center justify-center">
                    <span className={busy ? 'invisible' : undefined}>{t('dashboard:addGame.actions.add')}</span>
                    {busy && <IconLoader2 size={16} className="absolute animate-spin" aria-hidden="true" />}
                  </span>
                </button>
              </>
            )}
          </div>
        </div>
      </motion.div>
    </motion.div>
  )

  // Into <body>, like the other own dialogs: nothing of the page (a transform, a stacking context) changes how it covers the app
  return createPortal(dialog, document.body)
}
