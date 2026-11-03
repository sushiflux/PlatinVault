import flags from 'virtual:language-flags'
import { languageInfo } from '../../i18n'
import type { UiLanguage } from '../../i18n'

/**
 * The flag of a language (from languages.json), drawn as an inline SVG that is bundled into the app: no request, no CDN, works offline
 * and on Windows, where emoji flags do not exist. It is decoration next to the language's name, so it is hidden from screen readers.
 * A language whose flag is not bundled shows its code instead.
 */
export function LanguageFlag({ language, className = '' }: { language: UiLanguage; className?: string }) {
  const info = languageInfo(language)
  const Flag = flags[info.flag.toUpperCase()]
  return (
    <span className={`pv-flag ${className}`.trim()} aria-hidden="true">
      {Flag ? <Flag focusable="false" /> : <span className="pv-flag-code">{info.code.toUpperCase()}</span>}
    </span>
  )
}
