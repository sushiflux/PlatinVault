import { createContext, useContext, useMemo } from 'react'
import type { ReactNode } from 'react'
import { IconTrophy } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { LiveAchievementItemDto } from '../../api/types'

/** What the guide shows for a Steam achievement it refers to by API name. */
export interface AchievementInfo {
  apiName: string
  title: string
  description?: string
  iconUrl?: string
  achieved?: boolean
}

type Lookup = (apiName: string) => AchievementInfo | undefined

const AchievementLookupContext = createContext<Lookup>(() => undefined)

/** Makes the game's Steam achievement list available to the blocks of the guide (boss card, overview, achievement hints). */
export function AchievementLookupProvider({ achievements, children }: { achievements: LiveAchievementItemDto[] | undefined; children: ReactNode }) {
  const lookup = useMemo<Lookup>(() => {
    const byName = new Map<string, AchievementInfo>()
    for (const item of achievements ?? []) {
      byName.set(item.apiName.toLowerCase(), {
        apiName: item.apiName,
        title: item.displayName,
        description: item.description || undefined,
        iconUrl: item.achieved ? item.iconUrl : item.iconGrayUrl || item.iconUrl,
        achieved: item.achieved,
      })
    }
    return (apiName) => byName.get(apiName.toLowerCase())
  }, [achievements])
  return <AchievementLookupContext.Provider value={lookup}>{children}</AchievementLookupContext.Provider>
}

export const useAchievementLookup = () => useContext(AchievementLookupContext)

/** One achievement as a quiet row: its icon and title from the game's Steam list; the API name when the list does not know it. */
export function AchievementRef({ apiName, withDescription = false }: { apiName: string; withDescription?: boolean }) {
  const { t } = useTranslation('game')
  const info = useAchievementLookup()(apiName)
  return (
    <span className="pv-ach-ref" data-achievement={apiName} data-known={info ? 'true' : 'false'}>
      {info?.iconUrl ? <img src={info.iconUrl} alt="" width={24} height={24} loading="lazy" className="pv-ach-icon" /> : <IconTrophy size={18} className="pv-ach-glyph" aria-hidden="true" />}
      <span className="pv-ach-text">
        <span className="pv-ach-title">{info ? info.title : t('achievementRef.unknown', { name: apiName })}</span>
        {withDescription && info?.description && <span className="pv-ach-desc">{info.description}</span>}
      </span>
      {info?.achieved && <span className="pv-ach-done">{t('achievementRef.done')}</span>}
    </span>
  )
}

/** A list of achievements; renders nothing for an empty list. */
export function AchievementRefList({ apiNames, withDescription = false }: { apiNames: string[] | null | undefined; withDescription?: boolean }) {
  const names = (apiNames ?? []).filter((name) => name.trim().length > 0)
  if (names.length === 0) return null
  return (
    <ul className="pv-ach-list">
      {names.map((name) => (
        <li key={name}>
          <AchievementRef apiName={name} withDescription={withDescription} />
        </li>
      ))}
    </ul>
  )
}
