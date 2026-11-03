import type { ReactNode } from 'react'
import { UI_LANGUAGES } from '../../i18n'
import type { UiLanguage } from '../../i18n'
import { useFormat } from '../../i18n/format'

interface LangStackProps {
  // Draws the text in the given language (use `fixedT(language, '<namespace>')` and `formatNumber(…, { locale: localeFor(language) })`)
  render: (language: UiLanguage) => ReactNode
  // Texts the control may show LATER (e.g. the count that appears once a list has loaded): drawn invisibly like the others, so
  // their width is there from the start
  reserve?: (language: UiLanguage) => ReactNode
  // Alignment of the shorter text inside the width of the longer one
  align?: 'start' | 'center' | 'end'
  className?: string
}

const ALIGN = { start: 'justify-items-start', center: 'justify-items-center', end: 'justify-items-end' } as const

/**
 * A text of a control whose width comes from its content (a switch button, a caption next to a slider). The text is drawn in every
 * language of the app on top of each other in one grid cell and only the active language is visible, so the control is always as
 * wide as its longest version and switching the language never moves or resizes anything around it (project rule "Rigid layout").
 * The hidden versions are invisible and removed from the accessibility tree.
 */
export function LangStack({ render, reserve, align = 'start', className = '' }: LangStackProps) {
  const { language: active } = useFormat()
  return (
    <span className={`grid ${ALIGN[align]} ${className}`.trim()}>
      {UI_LANGUAGES.map((language) => (
        <span
          key={language}
          lang={language}
          className={`col-start-1 row-start-1 ${language === active ? '' : 'invisible'}`}
          aria-hidden={language === active ? undefined : true}
        >
          {render(language)}
        </span>
      ))}
      {reserve &&
        UI_LANGUAGES.map((language) => (
          <span key={`reserve-${language}`} lang={language} className="invisible col-start-1 row-start-1" aria-hidden="true">
            {reserve(language)}
          </span>
        ))}
    </span>
  )
}
