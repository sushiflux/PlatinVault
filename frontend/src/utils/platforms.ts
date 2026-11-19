import type { PlatformId, PlatformVariant, TrophyKind } from '../api/types'
import type common from '../i18n/de/common'

// The platforms a game can be played on - the same ids as the server (Services/GamePlatforms.cs). A game of the Steam library is always
// "steam"; every other platform is a game added by hand (no Steam AppID, trophies ticked by hand). PlayStation has the variants ps5 / ps4
// because their trophy lists are separate. Nintendo Switch has no trophies or achievements (guide only).

export const PLATFORM_IDS: readonly PlatformId[] = ['steam', 'playstation', 'xbox', 'epic', 'gog', 'nintendo', 'ubisoft', 'ea', 'battlenet']

// The platforms a game can be added by hand for (Steam games come from the Steam library)
export const MANUAL_PLATFORM_IDS: readonly PlatformId[] = PLATFORM_IDS.filter((id) => id !== 'steam')

const PLAYSTATION_VARIANTS: readonly PlatformVariant[] = ['ps5', 'ps4']

export function isPlatformId(value: unknown): value is PlatformId {
  return typeof value === 'string' && (PLATFORM_IDS as readonly string[]).includes(value)
}

// The variants of a platform (PlayStation: ps5, ps4); empty for every other one
export function variantsOf(platform: PlatformId): readonly PlatformVariant[] {
  return platform === 'playstation' ? PLAYSTATION_VARIANTS : []
}

// The platform of a game DTO; a missing or unknown value (older server) is a Steam game
export function platformOf(game: { platform?: string | null }): PlatformId {
  return isPlatformId(game.platform) ? game.platform : 'steam'
}

// A game added by hand: its trophy list comes from GET /api/games/{slug}/trophies and is ticked by hand
export function isManualGame(game: { platform?: string | null }): boolean {
  return platformOf(game) !== 'steam'
}

// Whether the platform has trophies / achievements at all (Nintendo Switch has none)
export function hasTrophies(platform: PlatformId): boolean {
  return platform !== 'nintendo'
}

// PlayStation calls them trophies, every other platform achievements
export function trophyKind(platform: PlatformId): TrophyKind {
  return platform === 'playstation' ? 'trophies' : 'achievements'
}

// PlayStation grades its trophies and has a platinum trophy; everywhere else "platinum" means 100 %
export function hasPlatinumGrade(platform: PlatformId): boolean {
  return platform === 'playstation'
}

export type PlatformLabelKey = keyof typeof common.platforms

// The key of the platform's name in the namespace "common" (useTranslation("common"), t(`platforms.${key}`)): PlayStation with a variant = its own name
export function platformLabelKey(platform: PlatformId, variant?: PlatformVariant | null): PlatformLabelKey {
  if (platform === 'playstation' && variant && PLAYSTATION_VARIANTS.includes(variant)) return variant
  return platform
}
