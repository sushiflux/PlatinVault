import type { ManualTrophyItemDto, ManualTrophyListDto, PlatformId, PlatformVariant } from '../../api/types'

// Shared by the tests of the trophy list of a game added by hand: the answer of GET /api/games/{slug}/trophies as the server builds it.

export function manualItem(key: string, patch: Partial<ManualTrophyItemDto> = {}): ManualTrophyItemDto {
  return {
    apiName: key,
    displayName: `Trophäe ${key}`,
    description: `Beschreibung ${key}`,
    originalDescription: null,
    isTranslated: false,
    iconUrl: '',
    iconGrayUrl: '',
    achieved: false,
    unlockTime: null,
    globalPercent: 0,
    hasGlobalPercent: false,
    rarityTier: 'Bronze',
    rarityLabel: '',
    rarityBand: '',
    isHidden: false,
    userNote: '',
    grade: 'bronze',
    points: null,
    missable: false,
    group: null,
    category: 'Story',
    tip: '',
    sortOrder: 0,
    originalName: null,
    ...patch,
  }
}

const tierOf = (items: ManualTrophyItemDto[], tier: string) => {
  const ofTier = items.filter((item) => item.rarityTier === tier)
  return { achieved: ofTier.filter((item) => item.achieved).length, total: ofTier.length }
}

export function manualList(items: ManualTrophyItemDto[], patch: Partial<ManualTrophyListDto> = {}, platform: PlatformId = 'playstation', variant: PlatformVariant | null = 'ps5'): ManualTrophyListDto {
  const achievedCount = items.filter((item) => item.achieved).length
  return {
    gameId: 'g-manual-1',
    slug: 'stellar-blade',
    platform,
    platformVariant: variant,
    supported: true,
    kind: platform === 'playstation' ? 'trophies' : 'achievements',
    researchedAt: '2026-11-19T10:00:00Z',
    sources: [{ title: 'PSNProfiles', url: 'https://psnprofiles.com/trophies/stellar-blade' }],
    language: 'de',
    translation: { language: 'de', sourceLanguage: 'de', state: 'original', running: false, error: null, providerMessage: null },
    achievements: items,
    totalCount: items.length,
    achievedCount,
    remainingCount: items.length - achievedCount,
    progressPercentage: items.length ? (achievedCount / items.length) * 100 : 0,
    is100Percent: items.length > 0 && achievedCount === items.length,
    platinum: tierOf(items, 'Platinum'),
    gold: tierOf(items, 'Gold'),
    silver: tierOf(items, 'Silver'),
    bronze: tierOf(items, 'Bronze'),
    platinumAt: null,
    latestAchievement: null,
    ...patch,
  }
}

/** Four trophies in the order of a PlayStation list: the platinum trophy first, then a gold, a silver and a bronze one. */
export function ps5Items(): ManualTrophyItemDto[] {
  return [
    manualItem('platin', { displayName: 'Stellar Legende', grade: 'platinum', rarityTier: 'Platinum', sortOrder: 0 }),
    manualItem('gold-1', { displayName: 'Letzter Boss', grade: 'gold', rarityTier: 'Gold', sortOrder: 1 }),
    manualItem('silber-1', { displayName: 'Erstes Blut', grade: 'silver', rarityTier: 'Silver', sortOrder: 2, achieved: true, unlockTime: '2026-11-19T11:00:00Z' }),
    manualItem('bronze-1', { displayName: 'Kapitel 1 abgeschlossen', grade: 'bronze', rarityTier: 'Bronze', sortOrder: 3, missable: true }),
  ]
}
