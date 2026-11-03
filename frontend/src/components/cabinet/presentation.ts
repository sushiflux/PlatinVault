import type { CabinetPresentedDto, ShowcaseGameDto } from '../../api/types'

// Which completed games the cabinet still has to present (animate into their slot), and how the presentation is paced.
// Pure functions - the component only wires them to the server state and to timers.

/**
 * The games that were never presented, in shelf order (newest platinum first, the order of `games`).
 *
 * The rule for the very first use: without a stored state on the server (`initialized` false) NOTHING is pending - the
 * caller records the whole current collection as the baseline instead, so a fresh install (or the first run of this feature
 * with an existing collection) never plays the animation for every old game. Only games that reach 100 % AFTER the baseline
 * are presented. While the state is unknown (still loading, or the request failed) nothing is pending either: never animate
 * on a guess.
 */
export function pendingGames(games: readonly ShowcaseGameDto[], state: CabinetPresentedDto | undefined): ShowcaseGameDto[] {
  if (!state?.initialized) return []
  const presented = new Set(state.appIds)
  return games.filter((game) => !presented.has(game.appId))
}

/** True when the cabinet has no stored state yet but a state could be read: the current collection becomes the baseline. */
export function needsBaseline(state: CabinetPresentedDto | undefined): boolean {
  return state !== undefined && !state.initialized
}

export interface LandingSchedule {
  // Pause after the lights are on before the first game moves
  firstDelayMs: number
  // Time from the start of one game to the start of the next (shorter than `landMs` = the landings overlap)
  stepMs: number
  // How long one landing takes (spring, glow, marker); the game counts as presented after this
  landMs: number
}

/**
 * The pace: up to five games land one after another with a clear beat; more are overlapped and sped up so the whole
 * presentation stays around five seconds however many games there are (at least 100 ms between two starts).
 * Reduced motion: nothing flies, the places just fade in one after the other, quickly.
 */
export function landingSchedule(count: number, reducedMotion: boolean): LandingSchedule {
  if (reducedMotion) return { firstDelayMs: 250, stepMs: 90, landMs: 700 }
  const stepMs = count <= 5 ? 850 : Math.max(100, Math.floor(4300 / count))
  return { firstDelayMs: 500, stepMs, landMs: 1100 }
}

/** Seconds the whole presentation takes (for tests and for choosing a sensible limit). */
export function presentationDurationMs(count: number, reducedMotion: boolean): number {
  if (count <= 0) return 0
  const { firstDelayMs, stepMs, landMs } = landingSchedule(count, reducedMotion)
  return firstDelayMs + (count - 1) * stepMs + landMs
}

/** The names that go into the one polite announcement: the first few, in order. */
export function announcedNames(names: readonly string[], limit = 3): string[] {
  return names.slice(0, limit)
}
