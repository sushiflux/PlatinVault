import { useEffect } from 'react'
import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchSettings } from '../../api/client'
import { applyLanguage, parseUiLanguage, readStoredLanguage } from '../../i18n'
import { LanguageScreen, LanguageWait } from './LanguageScreen'

/**
 * Keeps the app on the language chosen on the server and shows the first-run language screen while there is none.
 * Sits above everything else (router, intro tour, hints), so the language screen really is the first thing a visitor sees.
 * - Server value known: the app speaks it (the server wins over this browser's cached copy).
 * - Server has none yet: the language screen, once; the intro tour only starts after it.
 * - Nothing cached in this browser and the server not answered yet: a short brand splash instead of a flash of the wrong language.
 * - Server unreachable: the app starts in the browser's language and shows its own error messages.
 */
export function LanguageGate({ children }: { children: ReactNode }) {
  const { data, isPending } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  const serverLanguage = parseUiLanguage(data?.uiLanguage)

  useEffect(() => {
    if (serverLanguage) void applyLanguage(serverLanguage)
  }, [serverLanguage])

  if (data && !serverLanguage) return <LanguageScreen />
  if (isPending && !readStoredLanguage()) return <LanguageWait />
  return <>{children}</>
}
