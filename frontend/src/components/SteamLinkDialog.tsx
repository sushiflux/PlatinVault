import { useEffect, useState } from 'react'
import { ErrorBlock } from './ui/ErrorBlock'
import { IconButton } from './ui/IconButton'
import '../styles/dialogs.css'
import { IconLoader2, IconSearch, IconX } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { linkSteamApp, searchSteamGames } from '../api/client'
import type { GameDetailDto, SteamSearchItemDto } from '../api/types'

interface SteamLinkDialogProps {
  slug: string
  initialQuery: string
  onClose: () => void
  onLinked: (game: GameDetailDto) => void
}

// Searches the Steam store and links the chosen app to a game that has no AppID yet (cover, achievements, stats).
export function SteamLinkDialog({ slug, initialQuery, onClose, onLinked }: SteamLinkDialogProps) {
  const { t } = useTranslation(['game', 'common'])
  const [query, setQuery] = useState(initialQuery)
  const [results, setResults] = useState<SteamSearchItemDto[]>([])
  const [searching, setSearching] = useState(false)
  const [linkingId, setLinkingId] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const q = query.trim()
    if (q.length < 2) {
      setResults([])
      return
    }
    const timer = setTimeout(() => {
      setSearching(true)
      searchSteamGames(q)
        .then(setResults)
        .catch((err: unknown) =>
          setError(t('game:steamLink.searchFailed', { reason: err instanceof Error ? err.message : t('common:unknownError') }))
        )
        .finally(() => setSearching(false))
    }, 280)
    return () => clearTimeout(timer)
  }, [query])

  const link = async (item: SteamSearchItemDto) => {
    setLinkingId(item.appId)
    setError(null)
    try {
      onLinked(await linkSteamApp(slug, item.appId))
    } catch (err: unknown) {
      setError(t('game:steamLink.linkFailed', { name: item.name, reason: err instanceof Error ? err.message : t('common:unknownError') }))
    } finally {
      setLinkingId(null)
    }
  }

  return (
    <div
      className="pv-modal-backdrop pv-modal-backdrop--top"
      role="dialog"
      aria-modal="true"
      aria-labelledby="steam-link-title"
      onKeyDown={(e) => {
        if (e.key === 'Escape' && linkingId === null) {
          e.stopPropagation()
          onClose()
        }
      }}
    >
      <div className="pv-modal w-full max-w-lg p-6">
        <div className="flex items-start justify-between gap-3">
          <h2 id="steam-link-title" className="pv-dialog-title">
            {t('game:steamLink.title')}
          </h2>
          <IconButton label={t('common:close')} onClick={onClose} disabled={linkingId !== null} className="shrink-0">
            <IconX size={20} aria-hidden="true" />
          </IconButton>
        </div>

        <p className="pv-dialog-text">{t('game:steamLink.text')}</p>

        <div className="relative mt-4">
          <IconSearch size={17} className="absolute left-3 top-1/2 -translate-y-1/2" style={{ color: 'var(--text-muted)' }} aria-hidden="true" />
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={t('game:steamLink.placeholder')}
            aria-label={t('game:steamLink.searchLabel')}
            className="pv-input pv-input-icon w-full"
            autoFocus
          />
        </div>

        {/* Hairline list: cover, name and AppID per row; a row is one frameless button */}
        <ul className="m-0 mt-2 max-h-72 min-h-40 list-none overflow-y-auto p-0">
          {results.map((item) => (
            <li key={item.appId} style={{ borderBlockEnd: '1px solid var(--border-subtle)' }}>
              <button
                type="button"
                disabled={linkingId !== null}
                onClick={() => void link(item)}
                className="relative flex min-h-12 w-full cursor-pointer items-center gap-3 px-1 py-2 text-left hover:bg-[var(--bg-elevated)]"
              >
                <img src={item.headerImageUrl} alt="" className="h-10 w-20 shrink-0 object-cover" style={{ borderRadius: 'var(--radius-sm)' }} />
                <span className="min-w-0 flex-1">
                  <span className="block break-words font-medium" style={{ color: 'var(--text-primary)' }}>
                    {item.name}
                  </span>
                  <span className="block text-sm" style={{ color: 'var(--text-muted)' }}>
                    {t('game:steamLink.appId', { id: item.appId })}
                  </span>
                </span>
                {linkingId === item.appId && <IconLoader2 size={17} className="shrink-0 animate-spin" aria-hidden="true" />}
              </button>
            </li>
          ))}
          {!searching && results.length === 0 && query.trim().length >= 2 && (
            <li className="py-6 text-center" style={{ color: 'var(--text-muted)' }}>
              {t('game:steamLink.none')}
            </li>
          )}
        </ul>

        {/* Room for a problem, so it moves nothing */}
        <div className="mt-2 min-h-12">{error && <ErrorBlock title={t('game:steamLink.errorTitle')} message={error} defaultOpen />}</div>
      </div>
    </div>
  )
}
