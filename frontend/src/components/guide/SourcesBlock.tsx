import { IconExternalLink, IconLink } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideSectionDto, GuideSourceDto } from '../../api/types'
import './guide.css'

/** The parsed address of a source when it is a plain web link (http or https, no embedded credentials); null for everything else (javascript:, data:, relative, broken). */
export function safeWebUrl(raw: string | null | undefined): URL | null {
  if (!raw) return null
  try {
    const url = new URL(raw.trim())
    if (url.protocol !== 'https:' && url.protocol !== 'http:') return null
    if (url.username || url.password) return null
    return url
  } catch {
    return null
  }
}

function originKey(origin: GuideSourceDto['origin']): 'cited' | 'ai' | 'search' | 'unknown' {
  return origin === 'cited' ? 'cited' : origin === 'ai' ? 'ai' : origin === 'search' ? 'search' : 'unknown'
}

/**
 * The sources of a chapter: a calm list at its end. Only real web links (http/https) are links and they open outside the app; anything else is shown as plain
 * text. How reliable the entry is stays visible but quiet: "cited" (the page was quoted) or "named by the AI" (from memory, not checked).
 */
export function SourcesBlock({ section }: { section: GuideSectionDto }) {
  const { t } = useTranslation('game')
  const sources = (section.sources ?? []).filter((source) => source && (source.url?.trim() || source.title?.trim()))
  if (sources.length === 0) return null
  return (
    <section className="pv-card pv-sources" aria-label={t('sources.title')} data-sources="true">
      <h4 className="pv-sources-title">
        <IconLink size={15} aria-hidden="true" />
        {t('sources.title')}
      </h4>
      <ul className="pv-sources-list">
        {sources.map((source, i) => {
          const url = safeWebUrl(source.url)
          const label = source.title?.trim() || url?.hostname || source.url
          return (
            <li key={`${source.url}-${i}`} className="pv-sources-item">
              {url ? (
                <a href={url.href} target="_blank" rel="noopener noreferrer nofollow" referrerPolicy="no-referrer" className="pv-sources-link">
                  <span>{label}</span>
                  <IconExternalLink size={13} aria-hidden="true" />
                  <span className="sr-only"> ({t('sources.external')})</span>
                </a>
              ) : (
                <span className="pv-sources-plain">{label}</span>
              )}
              {url && <span className="pv-sources-host">{url.hostname}</span>}
              <span className="pv-sources-origin" data-origin={originKey(source.origin)}>
                {t(`sources.${originKey(source.origin)}`)}
              </span>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
