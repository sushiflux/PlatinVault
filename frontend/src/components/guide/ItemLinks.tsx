import { createContext, useContext } from 'react'
import { IconArrowRight, IconMapPin } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { ItemLinkIndex } from './linkIndex'
import './guide.css'

/**
 * What cross-chapter linked checkboxes need from the page: the index of who points at whom, the shared check state (one `progress_checks` entry per item id, so
 * a row and the row it refers to are the same check) and the two jumps. The page provides it; without a provider every row simply uses its own item id.
 */
export interface ItemLinkApi {
  index: ItemLinkIndex
  isChecked: (key: string) => boolean
  toggle: (key: string, checked: boolean) => Promise<void>
  /** Opens the chapter that holds the row and scrolls to it. */
  openRow: (itemId: string) => void
  /** Opens a chapter and scrolls to a heading of it (null anchor = the chapter only). */
  openAnchor: (slug: string, anchorId: string | null) => void
}

export const ItemLinkContext = createContext<ItemLinkApi | null>(null)

export const useItemLinks = () => useContext(ItemLinkContext)

/**
 * The small line under a row's name: "-> chapter" when the row is the same check as a row of another chapter, and "-> where in the game" for every place that
 * points at this row. Compact and secondary; it sits in the name cell, so no column changes its width.
 */
export function RowLinks({ itemId, refId }: { itemId: string; refId?: string | null }) {
  const { t } = useTranslation('game')
  const links = useItemLinks()
  if (!links) return null
  const key = links.index.resolveKey(itemId, refId)
  const target = key !== itemId ? links.index.target(key) : null
  const backs = links.index.backRefs(itemId)
  if (!target && backs.length === 0) return null
  return (
    <span className="pv-rowlinks">
      {target && (
        <a
          href={`?tab=${encodeURIComponent(target.tabSlug)}`}
          className="pv-rowlink"
          data-link="target"
          aria-label={t('itemLinks.goToRow', { chapter: target.tabTitle })}
          onClick={(event) => {
            event.preventDefault()
            links.openRow(key)
          }}
        >
          <IconArrowRight size={12} aria-hidden="true" />
          <span>{target.tabTitle}</span>
        </a>
      )}
      {backs.map((back) => (
        <a
          key={`${back.tabId}-${back.anchorId ?? ''}`}
          href={`?tab=${encodeURIComponent(back.tabSlug)}`}
          className="pv-rowlink pv-rowlink-back"
          data-link="back"
          aria-label={t('itemLinks.goToPlace', { path: back.path })}
          onClick={(event) => {
            event.preventDefault()
            links.openAnchor(back.tabSlug, back.anchorId)
          }}
        >
          <IconMapPin size={12} aria-hidden="true" />
          <span>{back.path}</span>
        </a>
      ))}
    </span>
  )
}
