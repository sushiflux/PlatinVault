import registry from '../../../languages.json'
import type { resources } from './resources'

// The languages of the app come from ONE list: languages.json in the repository root (the server reads the same file). This module
// is its typed view for the frontend; the menu, the first-run screen, the formats and the flags all derive from it.
// Adding a language: one entry in languages.json + its text files (src/i18n/<code>/*.ts, registered in resources.ts).

/** A language the app speaks: the keys of the registered text resources (resources.ts). */
export type UiLanguage = keyof typeof resources

export interface LanguageInfo {
  /** ISO 639-1 code: the i18next language, <html lang>, the value of the setting ui.language */
  code: UiLanguage
  /** The language's name in itself ("Deutsch", "English"): never translated, so a visitor can always find theirs */
  endonym: string
  /** Intl locale for dates and numbers ("de-DE", "en-GB") */
  locale: string
  /** ISO 3166-1 alpha-2 code of the flag shown next to the name (bundled SVG, see virtual:language-flags) */
  flag: string
  /** Steam Web API language name of the achievement texts ("german", "english") */
  steamLanguage: string
}

export const SUPPORTED_LANGUAGES: readonly LanguageInfo[] = (registry as { languages: LanguageInfo[] }).languages

export const UI_LANGUAGES: readonly UiLanguage[] = SUPPORTED_LANGUAGES.map((language) => language.code)

/** The language the texts are written in first (the source of truth of every namespace); shown when nothing else is known. */
export const SOURCE_LANGUAGE: UiLanguage = 'de'

/** The language a visitor gets whose browser language the app does not speak. */
export const INTERNATIONAL_LANGUAGE: UiLanguage = 'en'

export function languageInfo(code: UiLanguage): LanguageInfo {
  return SUPPORTED_LANGUAGES.find((language) => language.code === code) ?? SUPPORTED_LANGUAGES[0]!
}

export function parseUiLanguage(value: unknown): UiLanguage | null {
  return typeof value === 'string' ? (UI_LANGUAGES.find((code) => code === value) ?? null) : null
}

/** Each language written in its own language. */
export const LANGUAGE_NAMES = Object.fromEntries(SUPPORTED_LANGUAGES.map((language) => [language.code, language.endonym])) as Record<UiLanguage, string>
