import { useEffect, useState } from 'react'
import { IconLink, IconLoader2, IconSearch, IconX } from '@tabler/icons-react'
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
    <div className="pv-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="steam-link-title">
      <div className="pv-modal w-full max-w-lg p-6 space-y-4">
        <div className="flex items-center justify-between pb-3" style={{ borderBottom: '1px solid var(--border-subtle)' }}>
          <div id="steam-link-title" className="flex items-center gap-2 font-bold text-sm" style={{ color: 'var(--accent-crystal)' }}>
            <IconLink size={20} />
            <span>{t('game:steamLink.title')}</span>
          </div>
          <button type="button" onClick={onClose} disabled={linkingId !== null} className="pv-btn !p-1.5" aria-label={t('common:close')}>
            <IconX size={18} />
          </button>
        </div>

        <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
          {t('game:steamLink.text')}
        </p>

        <div className="relative">
          <IconSearch size={16} className="absolute left-3 top-1/2 -translate-y-1/2" style={{ color: 'var(--text-muted)' }} />
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

        <div className="max-h-72 min-h-40 overflow-y-auto space-y-1.5">
          {results.map((item) => (
            <button
              key={item.appId}
              type="button"
              disabled={linkingId !== null}
              onClick={() => void link(item)}
              className="relative flex w-full items-center gap-3 rounded-lg p-2 text-left cursor-pointer"
              style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }}
            >
              <img src={item.headerImageUrl} alt="" className="h-10 w-20 rounded object-cover shrink-0" />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-xs font-bold" style={{ color: 'var(--text-primary)' }}>{item.name}</span>
                <span className="block text-[10px]" style={{ color: 'var(--text-muted)' }}>{t('game:steamLink.appId', { id: item.appId })}</span>
              </span>
              {linkingId === item.appId && <IconLoader2 size={16} className="animate-spin shrink-0" />}
            </button>
          ))}
          {!searching && results.length === 0 && query.trim().length >= 2 && (
            <p className="py-6 text-center text-xs" style={{ color: 'var(--text-muted)' }}>{t('game:steamLink.none')}</p>
          )}
        </div>

        <div className="min-h-[1.25rem] text-xs font-bold" role="alert" style={{ color: 'var(--accent-crimson)' }}>
          {error}
        </div>
      </div>
    </div>
  )
}
