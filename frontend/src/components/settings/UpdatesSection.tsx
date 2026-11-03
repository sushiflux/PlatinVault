import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconCloudDownload, IconDownload, IconExternalLink, IconLoader2, IconRefresh } from '@tabler/icons-react'
import { checkForUpdates, fetchUpdateStatus, installUpdate, updateSettings } from '../../api/client'
import type { UpdateCheckInterval, UpdateStatusDto } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { Switch } from '../ui/Switch'
import { ReservedText } from '../ui/ReservedText'
import { StackedLabel } from '../ui/StackedLabel'
import { useConfirm } from '../ui/ConfirmDialog'
import { UPDATE_CHECK_INTERVALS, UPDATE_STATUS_KEY } from '../updates/updateOffer'
import { errorText, RESULT_TONE_COLOR, updateResult, updateResultCandidates, type ResultTone } from '../updates/updateResult'

const SWITCH_ROW = 'flex w-full items-center gap-2.5 text-xs font-semibold text-[color:var(--text-secondary)]'

/**
 * Settings > System > Updates (user decision 2026-11-02, every platform): the switch "Beta-Versionen erhalten" (default off; saved at
 * once, it changes what the check offers), the running version, "Jetzt nach Updates suchen" with its result, and - depending on the
 * host - "Update installieren" (the Windows program installs itself) or how to switch the image tag (a container cannot replace its own
 * image: Unraid, NAS, docker compose). Every result line has its room reserved; errors are shown in full.
 */
export function UpdatesSection() {
  const { t } = useTranslation('settings')
  const format = useFormat()
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const [switchError, setSwitchError] = useState<string | null>(null)
  const [intervalError, setIntervalError] = useState<string | null>(null)
  const [installMessage, setInstallMessage] = useState<{ tone: ResultTone; text: string } | null>(null)

  const status = useQuery({ queryKey: UPDATE_STATUS_KEY, queryFn: fetchUpdateStatus, retry: false })
  const data = status.data

  const check = useMutation({
    mutationFn: checkForUpdates,
    onSuccess: (fresh) => queryClient.setQueryData(UPDATE_STATUS_KEY, fresh),
  })

  const toggle = useMutation({
    mutationFn: (enabled: boolean) => updateSettings({ betaUpdatesEnabled: enabled }),
    onMutate: (enabled) => {
      setSwitchError(null)
      // Show the new position at once; the status is read again afterwards (the offer depends on the switch)
      const previous = queryClient.getQueryData<UpdateStatusDto>(UPDATE_STATUS_KEY)
      if (previous) queryClient.setQueryData(UPDATE_STATUS_KEY, { ...previous, betaUpdatesEnabled: enabled })
      return { previous }
    },
    onError: (error, _enabled, context) => {
      if (context?.previous) queryClient.setQueryData(UPDATE_STATUS_KEY, context.previous)
      setSwitchError(t('updates.saveFailed', { reason: errorText(error) }))
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: UPDATE_STATUS_KEY }),
  })

  const interval = useMutation({
    mutationFn: (next: UpdateCheckInterval) => updateSettings({ updateCheckInterval: next }),
    onMutate: (next) => {
      setIntervalError(null)
      const previous = queryClient.getQueryData<UpdateStatusDto>(UPDATE_STATUS_KEY)
      if (previous) queryClient.setQueryData(UPDATE_STATUS_KEY, { ...previous, checkInterval: next })
      return { previous }
    },
    onError: (error, _next, context) => {
      if (context?.previous) queryClient.setQueryData(UPDATE_STATUS_KEY, context.previous)
      setIntervalError(t('updates.interval.saveFailed', { reason: errorText(error) }))
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: UPDATE_STATUS_KEY }),
  })

  const install = useMutation({
    mutationFn: (tag: string) => installUpdate(tag),
    onMutate: () => setInstallMessage({ tone: 'muted', text: t('updates.result.installing') }),
    onSuccess: () => setInstallMessage({ tone: 'ok', text: t('updates.result.installStarted') }),
    onError: (error) => setInstallMessage({ tone: 'error', text: t('updates.result.installFailed', { reason: errorText(error) }) }),
  })

  const betaOn = data?.betaUpdatesEnabled ?? false
  const available = data?.available ?? null
  const checking = check.isPending

  // The result line: what the last check found (or why there is none)
  const result = updateResult(t, { data, statusError: status.error, checkError: check.error, checking })

  const throttledText =
    data?.throttled && data.nextManualCheckAt ? t('updates.result.throttled', { time: format.date(data.nextManualCheckAt, 'timeSeconds') }) : ''

  // Every text the result line can show in normal operation, so its height never changes (a long technical error may still grow it)
  const resultCandidates = [...updateResultCandidates(t), result.text]

  // A dev build never takes part in the update check: switch and check are locked and the result line carries the note (same rows,
  // nothing moves); the container how-to is left out because it explains the :latest/:beta tags a dev build does not follow
  const devBuild = data?.state === 'devBuild'
  const desktop = data?.host === 'desktop'
  const image = data?.imageRepository ?? 'ghcr.io/sushiflux/platinvault'
  const latestTag = `${image}:latest`
  const betaTag = `${image}:beta`

  const onInstall = async () => {
    if (!data || !available) return
    const ok = await confirm({
      title: t('updates.confirm.title'),
      message: t('updates.confirm.message', { next: available.display, current: data.currentVersion }),
      confirmLabel: t('updates.confirm.confirm'),
    })
    if (ok) install.mutate(available.tag)
  }

  return (
    <div className="pv-card space-y-3" data-testid="updates-section">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
          <IconCloudDownload size={18} aria-hidden="true" />
          <span>{t('updates.title')}</span>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs" style={{ color: 'var(--text-secondary)' }}>
          <span>{t('updates.installed')}</span>
          <span className="font-mono font-semibold" style={{ color: 'var(--text-primary)' }}>
            {data?.currentVersion ?? '…'}
          </span>
          {data && (
            <span
              className="rounded-sm px-1.5 py-0.5 text-[0.68rem] font-extrabold uppercase tracking-wide"
              style={{
                color: data.currentChannel === 'beta' ? 'var(--accent-gold)' : 'var(--text-secondary)',
                border: `1px solid ${data.currentChannel === 'beta' ? 'var(--accent-gold-border)' : 'var(--border-subtle)'}`,
              }}
            >
              {t(`updates.channel.${data.currentChannel}`)}
            </span>
          )}
        </div>
      </div>

      <Switch checked={betaOn} onChange={(next) => toggle.mutate(next)} className={SWITCH_ROW} disabled={!data || devBuild || toggle.isPending}>
        {t('updates.betaSwitch')}
      </Switch>
      {/* Explanation of the current position; room for the longer of both, and for a failed save under it */}
      <ReservedText className="text-xs leading-relaxed" style={{ color: 'var(--text-muted)' }} candidates={[t('updates.betaOn'), t('updates.betaOff')]}>
        {betaOn ? t('updates.betaOn') : t('updates.betaOff')}
      </ReservedText>
      {switchError && (
        <p role="alert" className="text-xs font-semibold leading-relaxed" style={{ color: 'var(--accent-crimson)' }}>
          {switchError}
        </p>
      )}

      {/* How often PlatinVault looks by itself (the server decides when GitHub is asked; a check by hand always works) */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t pt-3" style={{ borderColor: 'var(--border-subtle)' }}>
        <label htmlFor="update-interval" className="text-xs font-semibold" style={{ color: 'var(--text-secondary)' }}>
          {t('updates.interval.label')}
        </label>
        <select
          id="update-interval"
          className="pv-input !w-auto font-semibold"
          value={data?.checkInterval ?? 'daily'}
          disabled={!data || devBuild || interval.isPending}
          onChange={(event) => interval.mutate(event.target.value as UpdateCheckInterval)}
        >
          {UPDATE_CHECK_INTERVALS.map((id) => (
            <option key={id} value={id}>
              {t(`updates.interval.options.${id}`)}
            </option>
          ))}
        </select>
      </div>
      <ReservedText
        className="text-xs leading-relaxed"
        style={{ color: 'var(--text-muted)' }}
        candidates={UPDATE_CHECK_INTERVALS.map((id) => t(`updates.interval.hints.${id}`))}
      >
        {t(`updates.interval.hints.${data?.checkInterval ?? 'daily'}`)}
      </ReservedText>
      {intervalError && (
        <p role="alert" className="text-xs font-semibold leading-relaxed" style={{ color: 'var(--accent-crimson)' }}>
          {intervalError}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-t pt-3" style={{ borderColor: 'var(--border-subtle)' }}>
        <button
          type="button"
          className="pv-btn !py-1 !px-3 !text-xs"
          onClick={() => check.mutate()}
          disabled={checking || !data || data.state === 'disabled' || devBuild}
        >
          {checking ? <IconLoader2 size={14} className="animate-spin" aria-hidden="true" /> : <IconRefresh size={14} aria-hidden="true" />}
          <StackedLabel active={checking ? 1 : 0} labels={[t('updates.check'), t('updates.checking')]} />
        </button>
        <span className="text-xs" style={{ color: 'var(--text-muted)' }}>
          {data?.checkedAt ? t('updates.checkedAt', { time: format.date(data.checkedAt, 'dateTimeSeconds') }) : t('updates.neverChecked')}
        </span>
      </div>

      <ReservedText role="status" className="text-sm font-semibold leading-relaxed" style={{ color: RESULT_TONE_COLOR[result.tone] }} candidates={resultCandidates}>
        {result.text}
      </ReservedText>
      {/* A second manual check within a minute is answered from memory; its note has its room from the start */}
      <ReservedText
        className="text-xs leading-relaxed"
        style={{ color: 'var(--text-muted)' }}
        candidates={[t('updates.result.throttled', { time: '00:00:00' })]}
      >
        {throttledText}
      </ReservedText>

      {/* Release notes and (Windows program) the install button; the row keeps its height when there is nothing to offer */}
      <div className={`flex flex-wrap items-center gap-2 ${available ? '' : 'invisible'}`} aria-hidden={available ? undefined : true}>
        {available?.notesUrl ? (
          <a className="pv-btn !py-1 !px-3 !text-xs" href={available.notesUrl} target="_blank" rel="noopener noreferrer">
            <IconExternalLink size={14} aria-hidden="true" />
            <span>{t('updates.notes')}</span>
          </a>
        ) : (
          // Holds the row's height while there is nothing to offer
          <span className="pv-btn invisible !py-1 !px-3 !text-xs" aria-hidden="true">
            <IconExternalLink size={14} />
            <span>{t('updates.notes')}</span>
          </span>
        )}
        {desktop && (
          <button
            type="button"
            className={`pv-btn pv-btn-primary !py-1 !px-3 !text-xs ${data?.canInstall ? '' : 'invisible'}`}
            onClick={() => void onInstall()}
            disabled={!data?.canInstall || install.isPending}
          >
            {install.isPending ? <IconLoader2 size={14} className="animate-spin" aria-hidden="true" /> : <IconDownload size={14} aria-hidden="true" />}
            <span>{t('updates.install')}</span>
          </button>
        )}
      </div>
      {desktop && (
        <ReservedText
          role="status"
          className="text-xs font-semibold leading-relaxed"
          style={{ color: RESULT_TONE_COLOR[installMessage?.tone ?? 'muted'] }}
          candidates={[t('updates.result.installing'), t('updates.result.installStarted'), installMessage?.text ?? '']}
        >
          {installMessage?.text ?? ''}
        </ReservedText>
      )}

      {data && !desktop && !devBuild && <DockerHowTo data={data} latestTag={latestTag} betaTag={betaTag} />}
    </div>
  )
}

/** How a container gets betas or releases: the two tags, what to do now, and the steps for Unraid, docker compose and other NAS. */
function DockerHowTo({ data, latestTag, betaTag }: { data: UpdateStatusDto; latestTag: string; betaTag: string }) {
  const { t } = useTranslation('settings')
  const advice = (() => {
    switch (data.dockerAdvice) {
      case 'useBeta':
        return data.available?.isBeta
          ? t('updates.docker.adviceUseBetaNew', { version: data.available.display, latest: latestTag, beta: betaTag })
          : t('updates.docker.adviceUseBeta', { latest: latestTag, beta: betaTag })
      case 'betaStays':
        return t('updates.docker.adviceBetaStays', { current: data.currentVersion, latest: latestTag })
      case 'switchToLatest':
        return t('updates.docker.adviceSwitchToLatest', { version: data.latestRelease?.display ?? '', current: data.currentVersion, latest: latestTag })
      default:
        return null
    }
  })()
  const wanted = data.betaUpdatesEnabled ? betaTag : latestTag
  const sample = 'v0000.00.000 (00.0000 β)'
  const adviceCandidates = [
    t('updates.docker.adviceUseBeta', { latest: latestTag, beta: betaTag }),
    t('updates.docker.adviceUseBetaNew', { version: sample, latest: latestTag, beta: betaTag }),
    t('updates.docker.adviceBetaStays', { current: sample, latest: latestTag }),
    t('updates.docker.adviceSwitchToLatest', { version: sample, current: sample, latest: latestTag }),
  ]

  return (
    <div className="space-y-3 border-t pt-3 text-xs leading-relaxed" style={{ borderColor: 'var(--border-subtle)', color: 'var(--text-secondary)' }}>
      <div className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
        {t('updates.docker.title')}
      </div>
      <p>{t('updates.docker.intro')}</p>
      <dl className="grid grid-cols-1 gap-x-3 gap-y-1 sm:grid-cols-[auto_1fr]">
        <dt>
          <code className="break-all">{latestTag}</code>
        </dt>
        <dd>{t('updates.docker.tagLatest')}</dd>
        <dt>
          <code className="break-all">{betaTag}</code>
        </dt>
        <dd>{t('updates.docker.tagBeta')}</dd>
      </dl>
      {/* What to do now. The box keeps the room of the longest advice, so switching or a new check result never moves the steps below */}
      <ReservedText
        role="note"
        className="rounded-sm px-3 py-2 font-semibold"
        style={{
          color: 'var(--text-primary)',
          background: advice ? 'var(--accent-gold-bg)' : 'transparent',
          border: `1px solid ${advice ? 'var(--accent-gold-border)' : 'transparent'}`,
        }}
        candidates={adviceCandidates}
      >
        {advice ?? ''}
      </ReservedText>
      <div className="space-y-1">
        <div className="font-bold" style={{ color: 'var(--text-primary)' }}>
          {t('updates.docker.unraidTitle')}
        </div>
        <p>{t('updates.docker.unraidSteps', { image: wanted })}</p>
      </div>
      <div className="space-y-1">
        <div className="font-bold" style={{ color: 'var(--text-primary)' }}>
          {t('updates.docker.composeTitle')}
        </div>
        <p>{t('updates.docker.composeSteps')}</p>
        <pre className="overflow-x-auto rounded-sm px-3 py-2 font-mono" style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)', color: 'var(--text-primary)' }}>
          {`image: ${wanted}\n\ndocker compose pull && docker compose up -d`}
        </pre>
      </div>
      <div className="space-y-1">
        <div className="font-bold" style={{ color: 'var(--text-primary)' }}>
          {t('updates.docker.otherTitle')}
        </div>
        <p>{t('updates.docker.otherSteps')}</p>
      </div>
    </div>
  )
}
