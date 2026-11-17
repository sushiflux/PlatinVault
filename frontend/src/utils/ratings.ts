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

/**
 * The overall rating of a game: the plain arithmetic mean of the ratings it HAS among Steam reviews (% positive), Metacritic, IGDB critics
 * and IGDB players, all on 0-100 (user decision 2026-11-17: no weighting by the number of sources). It reads exactly the slots the tile's
 * rating strip shows (`ratingSlots`, so the strip and the sort agree on which values exist and on their rounding); one source gives that
 * value, none gives null. Rounded to one decimal, so the order keeps its resolution and no floating-point noise decides a tie.
 * `igdbConfigured` must be the same value the strip gets (Twitch credentials present): the server sends stored IGDB ratings whether or not
 * credentials exist, so without the flag old values would count here that no tile shows.
 */
export function overallRating(game: RatingSource, igdbConfigured: boolean): number | null {
  const slots = ratingSlots(game, igdbConfigured)
  if (slots.length === 0) return null
  const mean = slots.reduce((sum, slot) => sum + slot.value, 0) / slots.length
  return Math.round(mean * 10) / 10
}
