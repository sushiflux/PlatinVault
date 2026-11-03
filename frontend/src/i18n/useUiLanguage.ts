import { useCallback } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { updateSettings } from '../api/client'
import { applyLanguage, currentLanguage, parseUiLanguage } from './index'
import type { UiLanguage } from './index'

/**
 * The language of the app and the one way to change it: the texts switch instantly (no reload), then the choice is saved on the
 * server (setting "ui.language", which also sets the Steam achievement language). When saving fails the previous language comes
 * back and the error is thrown, so the caller can show what went wrong where the user is looking.
 */
export function useUiLanguage() {
  const { i18n } = useTranslation()
  const queryClient = useQueryClient()
  const language = parseUiLanguage(i18n.resolvedLanguage) ?? 'de'

  const changeLanguage = useCallback(
    async (next: UiLanguage): Promise<void> => {
      const previous = currentLanguage()
      await applyLanguage(next)
      try {
        const updated = await updateSettings({ uiLanguage: next })
        queryClient.setQueryData(['settings'], updated)
        // Whatever the server delivers in the app language (Steam achievement texts, ...) is asked for again in the new one
        void queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] !== 'settings' })
      } catch (error) {
        await applyLanguage(previous)
        throw error
      }
    },
    [queryClient]
  )

  return { language, changeLanguage }
}
