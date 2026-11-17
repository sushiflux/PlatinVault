// The ratings of a game as the tile's rating strip shows them: Steam reviews, Metacritic, IGDB critics, IGDB players.

export type RatingKey = 'steam' | 'metacritic' | 'igdbCritics' | 'igdbPlayers'

/** The fields of a favourite (GameSummaryDto) or of a library game (SteamOwnedGameDto) the strip reads; all optional, the library knows fewer. */
export interface RatingSource {
  reviewPercent?: number | null
  reviewScoreDesc?: string | null
  sortReviewPercent?: number | null
  metacriticScore?: number | null
  sortMetacritic?: number | null
  igdbRating?: number | null
  igdbUserRating?: number | null
}

export interface RatingSlot {
  key: RatingKey
  /** Whole number 0-100: Steam = share of positive reviews in %, the others = score out of 100. */
  value: number
  /** Steam's own verdict ("Sehr positiv"), Steam slot only. */
  verdict: string | null
}

const whole = (value: number | null | undefined): number | null => (value == null || !Number.isFinite(value) ? null : Math.round(value))

/**
 * The slots a game has a value for, in the fixed order Steam, Metacritic, IGDB critics, IGDB players. A rating that is missing is not in
 * the list at all (the strip then gives the width to the others). Without IGDB credentials the two IGDB slots never appear.
 */
export function ratingSlots(game: RatingSource, igdbConfigured: boolean): RatingSlot[] {
  const slots: RatingSlot[] = []
  const steam = whole(game.reviewPercent ?? game.sortReviewPercent)
  if (steam != null) slots.push({ key: 'steam', value: steam, verdict: game.reviewScoreDesc?.trim() || null })
  const metacritic = whole(game.metacriticScore ?? game.sortMetacritic)
  if (metacritic != null) slots.push({ key: 'metacritic', value: metacritic, verdict: null })
  if (igdbConfigured) {
    const critics = whole(game.igdbRating)
    if (critics != null) slots.push({ key: 'igdbCritics', value: critics, verdict: null })
    const players = whole(game.igdbUserRating)
    if (players != null) slots.push({ key: 'igdbPlayers', value: players, verdict: null })
  }
  return slots
}
