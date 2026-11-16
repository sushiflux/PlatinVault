import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { SettingsModal, type SettingsTab } from '../components/SettingsModal'

const TABS: readonly SettingsTab[] = ['live', 'steam', 'ki', 'twitch', 'bridge', 'system']

function asTab(value: string | null): SettingsTab | undefined {
  return TABS.includes(value as SettingsTab) ? (value as SettingsTab) : undefined
}

/**
 * The settings as a page (route /settings?tab=live|steam|ki|twitch|bridge|system). Every "open the settings" of the app lands here; the
 * tab lives in the URL, so a reload stays on it. Saving keeps the page. The result of "Sign in with Steam" arrives as
 * ?steamLink=ok|error&reason=... (the shell forwards it here) and is shown on the Steam tab.
 */
export function SettingsPage() {
  const { t } = useTranslation('dashboard')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const tab = asTab(searchParams.get('tab'))
  const [notice, setNotice] = useState<{ type: 'ok' | 'err'; text: string } | null>(null)

  const steamLink = searchParams.get('steamLink')
  useEffect(() => {
    if (!steamLink) return
    setNotice(
      steamLink === 'ok'
        ? { type: 'ok', text: t('notice.steamLinked') }
        : { type: 'err', text: t('notice.steamLinkFailed', { reason: searchParams.get('reason') ?? t('notice.unknownReason') }) }
    )
    setSearchParams({ tab: 'steam' }, { replace: true })
    void queryClient.invalidateQueries({ queryKey: ['settings'] })
    void queryClient.invalidateQueries({ queryKey: ['steam-profile'] })
  }, [steamLink, searchParams, setSearchParams, queryClient, t])

  return (
    <SettingsModal
      page
      open
      initialTab={tab ?? (notice ? 'steam' : undefined)}
      notice={notice}
      onClose={() => void navigate('/')}
      onTabChange={(next) => setSearchParams({ tab: next }, { replace: true })}
      onSaved={() => {
        void queryClient.invalidateQueries({ queryKey: ['settings'] })
        void queryClient.invalidateQueries({ queryKey: ['live-status'] })
        void queryClient.invalidateQueries({ queryKey: ['live-achievements'] })
        // A new Steam key / account makes the library readable: the overview, "Alle Spiele" and the account row pick it up at once
        void queryClient.invalidateQueries({ queryKey: ['steam-profile'] })
        void queryClient.invalidateQueries({ queryKey: ['library-summary'] })
        void queryClient.invalidateQueries({ queryKey: ['steam-library'] })
      }}
    />
  )
}
