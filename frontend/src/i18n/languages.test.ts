import flags from 'virtual:language-flags'
import { describe, expect, it } from 'vitest'
import registry from '../../../languages.json'
import { SUPPORTED_LANGUAGES, UI_LANGUAGES, languageInfo, parseUiLanguage } from './languages'
import { localeFor } from './format'
import { resources } from './resources'

// The registry (languages.json in the repository root) is read by the frontend AND the server: these tests keep every consumer
// of the list consistent, so adding a language is one entry plus its text files and nothing else has to be remembered.
describe('language registry (languages.json)', () => {
  it('has text resources for exactly the registered languages', () => {
    expect([...UI_LANGUAGES].sort()).toEqual(Object.keys(resources).sort())
  })

  it('describes every language completely', () => {
    for (const language of SUPPORTED_LANGUAGES) {
      expect(language.code).toMatch(/^[a-z]{2,3}$/)
      expect(language.endonym.trim()).not.toBe('')
      expect(language.steamLanguage.trim()).not.toBe('')
      // a locale the browser's Intl knows (an unknown one would silently fall back to the default locale)
      expect(Intl.DateTimeFormat.supportedLocalesOf(language.locale)).toEqual([language.locale])
      expect(localeFor(language.code)).toBe(language.locale)
    }
    expect(new Set(UI_LANGUAGES).size).toBe(UI_LANGUAGES.length)
  })

  it('bundles the flag of every language (nothing is fetched at runtime)', () => {
    for (const language of SUPPORTED_LANGUAGES) {
      expect(typeof flags[language.flag.toUpperCase()], `flag ${language.flag} of ${language.code}`).toBe('function')
    }
    // exactly the used flags are bundled, not the whole flag set
    expect(Object.keys(flags).sort()).toEqual([...new Set(SUPPORTED_LANGUAGES.map((language) => language.flag.toUpperCase()))].sort())
  })

  it('writes German as de-DE and English as en-GB with the UK flag', () => {
    expect(languageInfo('de')).toMatchObject({ endonym: 'Deutsch', locale: 'de-DE', flag: 'DE', steamLanguage: 'german' })
    expect(languageInfo('en')).toMatchObject({ endonym: 'English', locale: 'en-GB', flag: 'GB', steamLanguage: 'english' })
  })

  it('accepts only registered codes', () => {
    expect(parseUiLanguage('de')).toBe('de')
    expect(parseUiLanguage('xx')).toBeNull()
  })

  it('keeps the shape the server reads (LanguageRegistry.cs): a "languages" array with lower-case code and steamLanguage', () => {
    const file = registry as { languages: { code: string; steamLanguage: string }[] }
    expect(file.languages.map((language) => language.code)).toEqual([...UI_LANGUAGES])
    expect(file.languages.every((language) => language.code === language.code.toLowerCase() && language.steamLanguage === language.steamLanguage.toLowerCase())).toBe(true)
  })
})
