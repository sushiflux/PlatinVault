import type { LiveTrophyTierSummaryDto, ManualTrophyItemDto, ManualTrophyListDto, PlatformId, RarityTier } from '../../api/types'

// Pure helpers of the trophy list of a game added by hand (no React): the optimistic tick, the facts of a game that miss, which tiers a list has.

/** The list with one trophy ticked or un-ticked, the counts, tier figures and progress recomputed - what the server answers a moment later. */
export function applyTick(list: ManualTrophyListDto, apiName: string, unlocked: boolean, at: string): ManualTrophyListDto {
  if (!list.achievements.some((a) => a.apiName === apiName)) return list
  const achievements = list.achievements.map((a) => (a.apiName === apiName ? { ...a, achieved: unlocked, unlockTime: unlocked ? at : null } : a))
  const achievedCount = achievements.filter((a) => a.achieved).length
  const totalCount = achievements.length
  const tier = (name: RarityTier): LiveTrophyTierSummaryDto => {
    const ofTier = achievements.filter((a) => a.rarityTier === name)
    return { achieved: ofTier.filter((a) => a.achieved).length, total: ofTier.length }
  }
  const latest = achievements
    .filter((a): a is ManualTrophyItemDto & { unlockTime: string } => a.achieved && Boolean(a.unlockTime))
    .reduce<ManualTrophyItemDto | null>((best, a) => (best === null || new Date(a.unlockTime).getTime() >= new Date(best.unlockTime ?? 0).getTime() ? a : best), null)
  return {
    ...list,
    achievements,
    achievedCount,
    remainingCount: totalCount - achievedCount,
    progressPercentage: totalCount > 0 ? (achievedCount / totalCount) * 100 : 0,
    is100Percent: totalCount > 0 && achievedCount === totalCount,
    platinum: tier('Platinum'),
    gold: tier('Gold'),
    silver: tier('Silver'),
    bronze: tier('Bronze'),
    latestAchievement: latest,
    announced: null,
  }
}

/** The tiers the tier filter offers for a list: the Steam three, plus Platinum when the list has a platinum trophy (PlayStation). */
export function tierFiltersOf(list: Pick<ManualTrophyListDto, 'platinum'> | undefined): RarityTier[] {
  return (list?.platinum.total ?? 0) > 0 ? ['Platinum', 'Gold', 'Silver', 'Bronze'] : ['Gold', 'Silver', 'Bronze']
}

/** Whether any trophy of the list states how rare it is (only then the rarity sorts make sense). */
export function hasRarityShares(list: Pick<ManualTrophyListDto, 'achievements'> | undefined): boolean {
  return list?.achievements.some((a) => a.hasGlobalPercent) ?? false
}

/** The same test as the server's AiGuideGeneratorService.FactsMissing: nothing researched or entered yet, and not from IGDB. */
export function factsMissing(game: { factsSource?: string | null; releaseDate?: string | null; developer?: string | null; publisher?: string | null; summary?: string | null }): boolean {
  if (game.factsSource === 'igdb') return false
  return !game.factsSource || ![game.releaseDate, game.developer, game.publisher, game.summary].some((value) => Boolean(value?.trim()))
}

/** The trophy tab of a game added by hand exists for every platform that has trophies or achievements (everything but Nintendo). */
export function hasTrophyArea(platform: PlatformId): boolean {
  return platform !== 'steam' && platform !== 'nintendo'
}

/** The how-to per trophy for the detail dialogs: the researched tip of each trophy (the key of a trophy = its apiName). */
export function trophyTipsOf(list: Pick<ManualTrophyListDto, 'achievements'> | undefined): Record<string, string> {
  const tips: Record<string, string> = {}
  for (const item of list?.achievements ?? []) {
    if (item.tip.trim()) tips[item.apiName] = item.tip.trim()
  }
  return tips
}
