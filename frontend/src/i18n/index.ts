import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'
import { INTERNATIONAL_LANGUAGE, parseUiLanguage, SOURCE_LANGUAGE, UI_LANGUAGES } from './languages'
import type { UiLanguage } from './languages'
import { DEFAULT_NAMESPACE, NAMESPACES, resources } from './resources'

// The languages of the app come from languages.json (see languages.ts). ONE choice drives UI texts, date/number formats, Steam
// achievement texts and guides.
export { LANGUAGE_NAMES, SUPPORTED_LANGUAGES, UI_LANGUAGES, languageInfo, parseUiLanguage } from './languages'
export type { LanguageInfo, UiLanguage } from './languages'

/** Per-browser copy of the chosen language: read before React renders (public/theme-init.js, same key) so there is no flicker. The server value wins once loaded. */
export const LANGUAGE_STORAGE_KEY = 'pv_ui_language_v1'

/** The language cached in this browser by an earlier visit (null on the very first visit or when storage is blocked). */
export function readStoredLanguage(): UiLanguage | null {
  try {
    return parseUiLanguage(localStorage.getItem(LANGUAGE_STORAGE_KEY))
  } catch {
    return null
  }
}

function storeLanguage(language: UiLanguage): void {
  try {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language)
  } catch {
    // Storage unavailable (private mode): the server value still decides on the next visit
  }
}

/** First guess for a visitor without a stored choice: the first browser language the app speaks; none of them: English. */
export function detectBrowserLanguage(): UiLanguage {
  const candidates = typeof navigator === 'undefined' ? [] : [...(navigator.languages ?? []), navigator.language]
  for (const candidate of candidates) {
    const supported = parseUiLanguage(candidate?.toLowerCase().split('-')[0])
    if (supported) return supported
  }
  // No browser information at all keeps the app's original language
  return candidates.length === 0 ? SOURCE_LANGUAGE : INTERNATIONAL_LANGUAGE
}

/** The language the app speaks right now. */
export function currentLanguage(): UiLanguage {
  return parseUiLanguage(i18next.resolvedLanguage) ?? parseUiLanguage(i18next.language) ?? SOURCE_LANGUAGE
}

void i18next.use(initReactI18next).init({
  resources,
  lng: readStoredLanguage() ?? detectBrowserLanguage(),
  fallbackLng: SOURCE_LANGUAGE,
  supportedLngs: [...UI_LANGUAGES],
  nonExplicitSupportedLngs: false,
  ns: NAMESPACES,
  defaultNS: DEFAULT_NAMESPACE,
  // The bundled resources are in memory: initialise synchronously so the first render already has its texts
  initAsync: false,
  // React escapes what it renders; escaping twice would turn "&" into "&amp;"
  interpolation: { escapeValue: false },
  react: { useSuspense: false },
  // A missing key shows the key itself; in development it is also logged (the types should make it impossible)
  parseMissingKeyHandler: (key) => {
    if (import.meta.env.DEV) console.warn(`[i18n] missing translation: ${key}`)
    return key
  },
})

// Keeps <html lang> (hyphenation, screen readers, the browser's translate offer) and the per-browser copy in step with the language
i18next.on('languageChanged', (language) => {
  const parsed = parseUiLanguage(language)
  if (!parsed) return
  document.documentElement.lang = parsed
  storeLanguage(parsed)
})
// init() has already switched to the first language before the listener existed
document.documentElement.lang = currentLanguage()

/** Switches the whole app to a language instantly (no reload). Saving it on the server is up to the caller (see useUiLanguage). */
export async function applyLanguage(language: UiLanguage): Promise<void> {
  if (currentLanguage() !== language) await i18next.changeLanguage(language)
  else storeLanguage(language)
}

/** A t function fixed to one language, independent of the active one (used by the first-run screen to preview both). */
export function fixedT<N extends (typeof NAMESPACES)[number]>(language: UiLanguage, namespace: N) {
  return i18next.getFixedT(language, namespace)
}

export default i18next
