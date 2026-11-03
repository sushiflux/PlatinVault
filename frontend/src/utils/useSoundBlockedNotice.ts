import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useLauncher } from '../components/launch/LaunchProvider'
import { soundEngine } from './audio'

const NOTICE_ID = 'sound-blocked'

/**
 * Says so when the browser refused an achievement sound (autoplay policy: no click or key press on the page since it was loaded,
 * typical right after a reload while the user is in a game). The popup itself still showed; without this notice the missing sound
 * looks like a bug. The notice offers to play the missed sound - that click is what allows audio again. Any other click or key
 * press on the page allows it too and removes the notice. Needs the LaunchProvider (the app-wide notice stack) above it.
 */
export function useSoundBlockedNotice() {
  const { t } = useTranslation('live')
  const { notify, dismissNotice } = useLauncher()

  useEffect(() => {
    const show = () =>
      notify({
        id: NOTICE_ID,
        tone: 'error',
        title: t('soundBlocked.title'),
        message: t('soundBlocked.message'),
        actions: [{ label: t('soundBlocked.action'), onClick: () => soundEngine.replayBlocked() }],
      })
    const unsubscribe = soundEngine.onBlockedChange((blocked) => (blocked ? show() : dismissNotice(NOTICE_ID)))
    if (soundEngine.isBlocked()) show()

    // Bubble phase on window: the notice's own button has played the missed sound (its onClick ran first) before this clears the state
    const allow = () => soundEngine.clearBlocked()
    window.addEventListener('click', allow)
    window.addEventListener('keydown', allow)
    return () => {
      unsubscribe()
      window.removeEventListener('click', allow)
      window.removeEventListener('keydown', allow)
    }
  }, [t, notify, dismissNotice])
}
