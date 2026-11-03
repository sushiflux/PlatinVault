import { useEffect, useId, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { IconCheck, IconDeviceDesktop, IconLoader2, IconRestore } from '@tabler/icons-react'
import { checkSteamRoot, fetchSettings, updateSettings } from '../../api/client'
import type { SettingsResponseDto, SteamLocalSettingsDto, SteamRootCandidateDto, SteamRootCheckResponse } from '../../api/types'
import { ReservedText } from '../ui/ReservedText'
import { errorText } from '../updates/updateResult'

// Wait this long after the last keystroke before the server checks the folder (no request per keystroke)
export const STEAM_ROOT_CHECK_DELAY_MS = 400

type CheckState =
  | { kind: 'idle' }
  | { kind: 'checking'; value: string }
  | { kind: 'done'; value: string; result: SteamRootCheckResponse }
  | { kind: 'failed'; value: string; reason: string }

/** "Gefunden über …" in words: the method plus its detail (variable name, native / Flatpak / Snap). */
export function steamVia(t: TFunction<'settings'>, method: SteamLocalSettingsDto['method'], detail: string | null): string {
  const variant = t(`steamLocal.variant.${detail === 'flatpak' || detail === 'snap' ? detail : 'native'}`)
  return t(`steamLocal.via.${method}`, { detail: detail ?? '', variant })
}

/**
 * Settings > Steam: "Steam-Ordner (optional)" - only where PlatinVault reads Steam on its own computer (the Windows program, the native
 * Linux package; mode "native"), or where a folder is set/found anyway. On Unraid/NAS (container without Steam) the section is not shown:
 * there the bridge brings the live trophies. The folder is checked by the server while it is typed (debounced, same rules as saving),
 * saved on its own with "Übernehmen" (like the update switch), and "Wieder selbst suchen" removes it. Every message line has its room
 * reserved, so nothing moves.
 */
export function SteamFolderSection() {
  const { t } = useTranslation('settings')
  const queryClient = useQueryClient()
  const id = useId()
  const settingsQuery = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  const settings = settingsQuery.data
  const stored = settings?.steamRoot ?? null
  const local = settings?.steamLocal ?? null

  const [value, setValue] = useState('')
  const [touched, setTouched] = useState(false)
  const [check, setCheck] = useState<CheckState>({ kind: 'idle' })
  const [saveMessage, setSaveMessage] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null)

  // The stored folder fills the field once it is known (until the user types)
  useEffect(() => {
    if (!touched && stored) setValue(stored)
  }, [stored, touched])

  // Live check: every change is checked by the server after a short pause; an answer for an older value is dropped
  const trimmed = value.trim()
  useEffect(() => {
    if (trimmed.length === 0) {
      setCheck({ kind: 'idle' })
      return
    }
    setCheck({ kind: 'checking', value: trimmed })
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      checkSteamRoot(trimmed, controller.signal).then(
        (result) => setCheck({ kind: 'done', value: trimmed, result }),
        (error: unknown) => {
          if (controller.signal.aborted) return
          setCheck({ kind: 'failed', value: trimmed, reason: errorText(error) })
        }
      )
    }, STEAM_ROOT_CHECK_DELAY_MS)
    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [trimmed])

  const save = useMutation({
    mutationFn: (steamRoot: string) => updateSettings({ steamRoot }),
    onMutate: () => setSaveMessage(null),
    onSuccess: (response: SettingsResponseDto, steamRoot) => {
      queryClient.setQueryData(['settings'], response)
      void queryClient.invalidateQueries({ queryKey: ['system-status'] })
      setTouched(false)
      setValue(response.steamRoot ?? '')
      setSaveMessage(
        steamRoot === ''
          ? { tone: 'ok', text: t('steamLocal.cleared') }
          : { tone: 'ok', text: t('steamLocal.saved', { path: response.steamRoot ?? steamRoot }) }
      )
    },
    onError: (error) => setSaveMessage({ tone: 'error', text: t('steamLocal.saveFailed', { reason: errorText(error) }) }),
  })

  if (!local || !(local.mode === 'native' || local.found || stored)) return null

  const current = check.kind !== 'idle' && check.value === trimmed ? check : null
  const result = current?.kind === 'done' ? current.result : null
  const invalid = Boolean(result && !result.valid) || current?.kind === 'failed'
  // Never send what the field shows as red: "Übernehmen" waits for a valid answer for exactly this value (or an emptied field)
  const canApply =
    !save.isPending &&
    (trimmed.length === 0 ? stored !== null : Boolean(result?.valid) && result?.path !== stored)
  const shownPath = result?.path ?? trimmed

  const fieldMessage =
    current?.kind === 'checking'
      ? { tone: 'muted' as const, text: t('steamLocal.checking') }
      : current?.kind === 'failed'
        ? { tone: 'error' as const, text: t('steamLocal.checkFailed', { reason: current.reason }) }
        : result
          ? result.valid
            ? { tone: 'ok' as const, text: t('steamLocal.valid', { path: result.resolvedPath ?? result.path }) }
            : { tone: 'error' as const, text: t(`steamLocal.problem.${result.problem === 'none' ? 'notFound' : result.problem}`, { path: result.path }) }
          : null

  const fieldCandidates = [
    t('steamLocal.hint'),
    t('steamLocal.checking'),
    t('steamLocal.valid', { path: result?.resolvedPath ?? shownPath }),
    t('steamLocal.problem.notAbsolute', { path: shownPath }),
    t('steamLocal.problem.notFound', { path: shownPath }),
    t('steamLocal.problem.noSteamApps', { path: shownPath }),
    t('steamLocal.problem.libraryOnly', { path: shownPath }),
  ]
  const toneColor = { ok: 'var(--accent-emerald)', error: 'var(--accent-crimson)', muted: 'var(--text-secondary)' }

  const foundLine = local.found && local.root
    ? t('steamLocal.found', { path: local.root, via: steamVia(t, local.method, local.methodDetail) })
    : t('steamLocal.notFound')
  const foundCandidates = [foundLine, t('steamLocal.notFound'), t('steamLocal.found', { path: shownPath, via: steamVia(t, 'manual', null) })]
  const saveCandidates = [t('steamLocal.cleared'), t('steamLocal.saved', { path: shownPath })]

  return (
    <div className="space-y-2 rounded-xl p-3" style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }} data-testid="steam-folder-section">
      <div className="flex items-center gap-2 text-xs font-extrabold" style={{ color: 'var(--text-primary)' }}>
        <IconDeviceDesktop size={16} aria-hidden="true" style={{ color: 'var(--accent-crystal)' }} />
        <span>{t('steamLocal.title')}</span>
      </div>

      <ReservedText className="text-xs" style={{ color: local.found ? 'var(--text-secondary)' : 'var(--text-muted)' }} candidates={foundCandidates} role="status">
        {foundLine}
      </ReservedText>
      {local.experimental && (
        <p className="text-xs font-semibold" style={{ color: 'var(--accent-gold)' }}>
          {t('steamLocal.experimental')}
        </p>
      )}
      {local.configuredRoot && (
        <p className="text-xs" style={{ color: 'var(--text-muted)' }}>
          {t('steamLocal.configured', { path: local.configuredRoot })}
        </p>
      )}

      <div>
        <label htmlFor={id} className="mb-1 block text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
          {t('steamLocal.label')}
        </label>
        <div className="flex flex-wrap gap-2">
          <input
            id={id}
            type="text"
            value={value}
            onChange={(e) => {
              setTouched(true)
              setSaveMessage(null)
              setValue(e.target.value)
            }}
            placeholder={local.platform === 'windows' ? t('steamLocal.placeholderWindows') : t('steamLocal.placeholderLinux')}
            autoComplete="off"
            spellCheck={false}
            aria-invalid={invalid ? true : undefined}
            aria-describedby={`${id}-msg`}
            className="pv-input min-w-[14rem] flex-1 font-mono"
            style={invalid ? { borderColor: 'var(--accent-crimson)' } : undefined}
          />
          <button type="button" className="pv-btn pv-btn-primary shrink-0" disabled={!canApply} onClick={() => save.mutate(trimmed.length === 0 ? '' : (result?.path ?? trimmed))}>
            {/* The spinner sits over the label, so the button keeps its size */}
            <span className="relative inline-flex items-center gap-1.5">
              <span className={`inline-flex items-center gap-1.5 ${save.isPending ? 'invisible' : ''}`}>
                <IconCheck size={15} aria-hidden="true" />
                <span>{t('steamLocal.save')}</span>
              </span>
              {save.isPending && <IconLoader2 size={15} className="absolute inset-0 m-auto animate-spin" aria-hidden="true" />}
            </span>
          </button>
          <button type="button" className="pv-btn shrink-0" disabled={stored === null || save.isPending} onClick={() => save.mutate('')}>
            <IconRestore size={15} aria-hidden="true" />
            <span>{t('steamLocal.reset')}</span>
          </button>
        </div>
        {/* Hint and every check result share one reserved line: nothing below moves while typing */}
        <ReservedText
          id={`${id}-msg`}
          className="mt-1 text-[11px] font-semibold leading-snug"
          style={{ color: fieldMessage ? toneColor[fieldMessage.tone] : 'var(--text-muted)' }}
          candidates={fieldCandidates}
          aria-live="polite"
        >
          {fieldMessage?.text ?? t('steamLocal.hint')}
        </ReservedText>
        <ReservedText
          className="mt-1 text-xs font-semibold"
          style={{ color: saveMessage?.tone === 'error' ? 'var(--accent-crimson)' : 'var(--accent-emerald)' }}
          candidates={saveCandidates}
          role="status"
        >
          {saveMessage?.text ?? ''}
        </ReservedText>
      </div>
    </div>
  )
}

/** The places where Steam was looked for (system check, row "Steam auf diesem Rechner"), the one in use marked. */
export function SteamCandidateList({ candidates }: { candidates: SteamRootCandidateDto[] }) {
  const { t } = useTranslation('settings')
  const [open, setOpen] = useState(false)
  const listId = useId()
  if (candidates.length === 0) return null
  return (
    <div className="mt-1">
      <button
        type="button"
        className="text-[11px] font-bold hover:underline"
        style={{ color: 'var(--accent-crystal)' }}
        aria-expanded={open}
        aria-controls={listId}
        onClick={() => setOpen(!open)}
      >
        {open ? t('steamLocal.candidates.hide') : t('steamLocal.candidates.show', { count: candidates.length })}
      </button>
      {open && (
        <ul id={listId} className="mt-1 space-y-0.5 text-[11px] leading-snug" style={{ color: 'var(--text-secondary)' }}>
          {candidates.map((candidate, index) => {
            const state = candidate.chosen
              ? t('steamLocal.candidates.chosen')
              : candidate.isDuplicate
                ? t('steamLocal.candidates.duplicate')
                : !candidate.exists
                  ? t('steamLocal.candidates.missing')
                  : !candidate.hasSteamApps
                    ? t('steamLocal.candidates.noSteamApps')
                    : !candidate.isSteamRoot
                      ? t('steamLocal.candidates.libraryOnly')
                      : t('steamLocal.candidates.usable')
            return (
              <li key={`${candidate.path}-${index}`} className="break-all">
                <span className="font-mono" style={{ color: candidate.chosen ? 'var(--text-primary)' : undefined }}>
                  {candidate.path}
                </span>
                {' · '}
                <span style={{ color: candidate.chosen ? 'var(--accent-emerald)' : 'var(--text-muted)' }}>{state}</span>
                {candidate.resolvedPath && candidate.resolvedPath !== candidate.path && (
                  <span style={{ color: 'var(--text-muted)' }}> · {t('steamLocal.candidates.resolved', { path: candidate.resolvedPath })}</span>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
