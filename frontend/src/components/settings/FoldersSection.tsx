import { useState } from 'react'
import { CopyErrorButton } from '../ui/CopyErrorButton'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconCheck, IconCopy, IconFolder, IconFolderOpen, IconLoader2 } from '@tabler/icons-react'
import { fetchDesktopFolders, openDesktopFolder } from '../../api/client'
import type { DesktopFolderKind } from '../../api/types'
import { copyText } from '../../utils/clipboard'
import { useIsDesktopHost } from '../FooterParts'
import { IconButton } from '../ui/IconButton'
import { ReservedText } from '../ui/ReservedText'

// Always listed in this order, also while the paths are still being read (the rows never appear or move afterwards)
const KINDS: DesktopFolderKind[] = ['program', 'data', 'backups', 'logs']

type Message = { tone: 'ok' | 'error'; text: string }
const TONE_COLOR: Record<Message['tone'], string> = { ok: 'var(--accent-emerald)', error: 'var(--accent-crimson)' }

function reasonOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

/**
 * Settings > System > Folders - only in the Windows program (host "desktop"). Where PlatinVault keeps its files on this PC, each folder
 * with "Öffnen" (shows it in Explorer) and a copy button; the full path is always readable (it wraps instead of being cut off).
 * The message line under the list has its room from the start, so a result never moves anything.
 */
export function FoldersSection() {
  const desktop = useIsDesktopHost()
  return desktop ? <FoldersCard /> : null
}

function FoldersCard() {
  const { t } = useTranslation('settings')
  const [message, setMessage] = useState<Message | null>(null)
  const [opening, setOpening] = useState<DesktopFolderKind | null>(null)
  const [copied, setCopied] = useState<DesktopFolderKind | null>(null)

  const folders = useQuery({ queryKey: ['desktop-folders'], queryFn: fetchDesktopFolders, retry: false, staleTime: 5 * 60_000 })
  const pathOf = (kind: DesktopFolderKind) => folders.data?.folders.find((folder) => folder.kind === kind)?.path ?? null
  const label = (kind: DesktopFolderKind) => t(`folders.kinds.${kind}.label`)

  const open = useMutation({
    mutationFn: (kind: DesktopFolderKind) => openDesktopFolder(kind),
    onMutate: (kind) => {
      setMessage(null)
      setOpening(kind)
    },
    onError: (error, kind) => setMessage({ tone: 'error', text: t('folders.openFailed', { folder: label(kind), reason: reasonOf(error) }) }),
    onSettled: () => setOpening(null),
  })

  const copy = async (kind: DesktopFolderKind, path: string) => {
    setMessage(null)
    const ok = await copyText(path)
    setCopied(ok ? kind : null)
    if (ok) window.setTimeout(() => setCopied((current) => (current === kind ? null : current)), 1800)
    setMessage(ok ? { tone: 'ok', text: t('folders.copied', { folder: label(kind) }) } : { tone: 'error', text: t('folders.copyFailed', { folder: label(kind) }) })
  }

  const loadFailed = folders.error ? t('folders.loadFailed', { reason: reasonOf(folders.error) }) : null
  const shown: Message | null = loadFailed ? { tone: 'error', text: loadFailed } : message
  // Every message this block can show in normal operation. A failed open usually names the path in its reason (a Windows error text
  // plus the path), so its room is sized for the longest real path twice; only an unusually long technical reason may grow the line.
  const longestPath = (folders.data?.folders ?? []).reduce((longest, folder) => (folder.path.length > longest.length ? folder.path : longest), '')
  const candidates = KINDS.flatMap((kind) => [
    t('folders.openFailed', { folder: label(kind), reason: `${longestPath} ${longestPath}`.trim() || '…' }),
    t('folders.copied', { folder: label(kind) }),
    t('folders.copyFailed', { folder: label(kind) }),
  ])

  return (
    <div className="pv-card space-y-3" data-testid="folders-section">
      <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
        <IconFolder size={18} aria-hidden="true" />
        <span>{t('folders.title')}</span>
      </div>
      <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
        {t('folders.intro')}
      </p>

      <ul className="m-0 list-none p-0">
        {KINDS.map((kind) => {
          const path = pathOf(kind)
          const busy = opening === kind
          return (
            <li
              key={kind}
              data-testid={`folder-${kind}`}
              className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 border-t py-2.5 first:border-t-0"
              style={{ borderColor: 'var(--border-subtle)' }}
            >
              <div className="min-w-0 flex-1 basis-64">
                <div className="flex flex-wrap items-baseline gap-x-2">
                  <span className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
                    {label(kind)}
                  </span>
                  <span className="text-xs" style={{ color: 'var(--text-muted)' }}>
                    {t(`folders.kinds.${kind}.hint`)}
                  </span>
                </div>
                <code className="mt-0.5 block break-all text-xs leading-relaxed" style={{ color: path ? 'var(--text-primary)' : 'var(--text-muted)' }} data-testid={`folder-path-${kind}`}>
                  {path ?? (folders.error ? '–' : t('folders.pathLoading'))}
                </code>
              </div>
              <div className="flex shrink-0 items-center gap-1.5">
                <button
                  type="button"
                  className="pv-btn !px-3 !py-1 !text-xs"
                  aria-label={t('folders.openLabel', { folder: label(kind) })}
                  disabled={!path || open.isPending}
                  onClick={() => open.mutate(kind)}
                >
                  {busy ? <IconLoader2 size={14} className="animate-spin" aria-hidden="true" /> : <IconFolderOpen size={14} aria-hidden="true" />}
                  <span>{t('folders.open')}</span>
                </button>
                <IconButton label={t('folders.copy')} description={t('folders.copyHint')} disabled={!path} onClick={() => path && void copy(kind, path)}>
                  {copied === kind ? <IconCheck size={16} aria-hidden="true" style={{ color: 'var(--accent-emerald)' }} /> : <IconCopy size={16} aria-hidden="true" />}
                </IconButton>
              </div>
            </li>
          )
        })}
      </ul>

      <ReservedText
        role={shown?.tone === 'error' ? 'alert' : 'status'}
        className="text-xs font-semibold leading-relaxed"
        style={{ color: shown ? TONE_COLOR[shown.tone] : undefined }}
        candidates={[...candidates, shown?.text ?? '']}
      >
        {shown?.text ?? ''}
        {shown?.tone === 'error' && <CopyErrorButton className="ms-2 align-middle" report={{ message: shown.text }} />}
      </ReservedText>

      <p className="border-t pt-3 text-xs leading-relaxed" style={{ borderColor: 'var(--border-subtle)', color: 'var(--text-muted)' }}>
        {t('folders.programHint')}
      </p>
    </div>
  )
}
