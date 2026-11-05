import { useCallback, useEffect, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchWhatsNew, markWhatsNewSeen } from '../../api/client'
import type { UpdateReleaseDto, WhatsNewDto } from '../../api/types'
import { useOnboardingDone } from '../onboarding/useOnboardingTour'
import { ReleaseNotesViewer } from './ReleaseNotesDialog'
import { hasNotesContent, parseReleaseNotes } from './releaseNotes'
import { setWhatsNewOpen } from './whatsNewState'
import { useFormat } from '../../i18n/format'

// One query for the whole app: the gate and the version popover read the same entry.
export const WHATS_NEW_KEY = ['whats-new'] as const

/** The notes of the running version as a release object for the notes dialog (no GitHub page, no date: the text is shipped with the app). */
export function whatsNewRelease(data: WhatsNewDto): UpdateReleaseDto {
  return {
    tag: data.version,
    display: data.version,
    isBeta: data.version.includes('β'),
    notesUrl: null,
    notes: data.notes,
    publishedAt: null,
    hasInstaller: false,
  }
}

/** The running version's notes (also for the version popover). Asked once per page load; the language is part of the key so a switch reloads them. */
export function useWhatsNew(enabled = true) {
  const { language } = useFormat()
  return useQuery({
    queryKey: [...WHATS_NEW_KEY, language],
    queryFn: fetchWhatsNew,
    enabled,
    retry: false,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  })
}

/**
 * Opens the "What's new" window by itself, once, on the first start after an update. The server decides (`show`: an update, an entry
 * exists, not a dev build, not seen yet) and remembers the answer, so the browser and the Windows program share it. Waits until the
 * first-start introduction is done (the language screen sits above this component already, see LanguageGate), and never opens
 * while it is waiting. Closing it by any way (button, Esc, backdrop) marks the version as seen.
 */
export function WhatsNewGate() {
  const queryClient = useQueryClient()
  const introDone = useOnboardingDone()
  const { data } = useWhatsNew(introDone)
  const [closed, setClosed] = useState(false)

  const release = useMemo(() => (data ? whatsNewRelease(data) : null), [data])
  const { language } = useFormat()
  // Nothing to show in the entry (nothing the dialog can render): do not open an empty window
  const hasContent = useMemo(() => (data?.notes ? hasNotesContent(parseReleaseNotes(data.notes, language)) : false), [data, language])
  const open = introDone && !closed && data?.show === true && hasContent

  useEffect(() => {
    setWhatsNewOpen(open)
    return () => setWhatsNewOpen(false)
  }, [open])

  const close = useCallback(() => {
    setClosed(true)
    // Best effort, like any silent bookkeeping: when the mark cannot be saved the window simply opens once more at the next start
    void markWhatsNewSeen()
      .then(() => queryClient.invalidateQueries({ queryKey: WHATS_NEW_KEY }))
      .catch(() => undefined)
  }, [queryClient])

  return <ReleaseNotesViewer release={open ? release : null} onClose={close} intro />
}
