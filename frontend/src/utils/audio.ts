import type { RarityTier, SoundTheme } from '../api/types'

// Trophy sound engine. Every theme ships one recording per trophy tier in /public/sounds/<theme>/<tier>.mp3
// (bronze, silver, gold, platinum). All recordings are built from CC0 instrument samples (Versilian Community Sample Library
// https://github.com/sgossner/VCSL and VSCO 2 Community Edition https://github.com/sgossner/VSCO-2-CE; credits in THIRD-PARTY-NOTICES.md).

export const DEFAULT_SOUND_THEME: SoundTheme = 'kristall'

/** Themes offered in the settings, in display order. Their names and descriptions are texts: settings:live.soundThemes.<id>. */
export const SOUND_THEMES: { id: SoundTheme }[] = [{ id: 'kristall' }, { id: 'orchester' }, { id: 'spieluhr' }]

type SoundTier = 'bronze' | 'silver' | 'gold' | 'platinum'
const TIERS: SoundTier[] = ['bronze', 'silver', 'gold', 'platinum']

/**
 * Maps any stored value to a valid theme. Older versions stored 'ps5' / 'steam' (and the backend may hand back
 * anything a client once wrote), so unknown values fall back to the default instead of breaking playback.
 */
export function normalizeSoundTheme(value: string | null | undefined): SoundTheme {
  const v = (value ?? '').trim().toLowerCase()
  return SOUND_THEMES.some((t) => t.id === v) ? (v as SoundTheme) : DEFAULT_SOUND_THEME
}

function tierFor(rarity: RarityTier | string): SoundTier {
  switch (String(rarity).toLowerCase()) {
    case 'silver':
      return 'silver'
    case 'gold':
    case 'platinum': // the platinum hero sound belongs to the full-screen celebration (playPlatinumFanfare), not to a toast
      return 'gold'
    default:
      return 'bronze'
  }
}

type BlockedListener = (blocked: boolean) => void

class SoundEngine {
  private enabled = true
  private volume = 0.15
  private theme: SoundTheme = DEFAULT_SOUND_THEME
  private readonly cache = new Map<string, HTMLAudioElement>()
  // Browsers refuse to play audio until the page got a click or key press (autoplay policy) - e.g. right after a reload while the
  // user is in a game. The popup still shows; the sound that was refused is remembered so the user can hear it once they click.
  private blocked: { theme: SoundTheme; tier: SoundTier } | null = null
  private readonly blockedListeners = new Set<BlockedListener>()

  constructor() {
    this.preload(this.theme)
  }

  public setEnabled(enabled: boolean) {
    this.enabled = enabled
  }

  public setVolume(vol: number) {
    this.volume = Math.max(0, Math.min(1, Number.isFinite(vol) ? vol : 0.15))
  }

  /** Accepts any string on purpose: legacy values ('ps5', 'steam') and unknown ones select the default theme. */
  public setTheme(theme: string) {
    this.theme = normalizeSoundTheme(theme)
    this.preload(this.theme)
  }

  public getTheme(): SoundTheme {
    return this.theme
  }

  /** Plays the sound of a trophy tier (bronze / silver / gold) in the current theme. */
  public playTrophyUnlock(rarity: RarityTier = 'Bronze') {
    if (!this.enabled) return
    this.play(this.theme, tierFor(rarity))
  }

  /** Plays the platinum hero sound (about 5 s) in the current theme. */
  public playPlatinumFanfare() {
    if (!this.enabled) return
    this.play(this.theme, 'platinum')
  }

  /**
   * Plays one sound of any theme for the settings preview, independent of the enabled flag and without changing
   * the active theme. Uses the current volume.
   */
  public preview(theme: string, tier: RarityTier = 'Gold') {
    this.play(normalizeSoundTheme(theme), String(tier).toLowerCase() === 'platinum' ? 'platinum' : tierFor(tier))
  }

  private audioFor(theme: SoundTheme, tier: SoundTier): HTMLAudioElement | null {
    if (typeof window === 'undefined') return null
    const key = `${theme}/${tier}`
    let audio = this.cache.get(key)
    if (!audio) {
      audio = new Audio(`/sounds/${theme}/${tier}.mp3`)
      audio.preload = 'auto'
      this.cache.set(key, audio)
    }
    return audio
  }

  private preload(theme: SoundTheme) {
    for (const tier of TIERS) this.audioFor(theme, tier)
  }

  /** True while the browser refused the last sound because the page has not been clicked yet. */
  public isBlocked(): boolean {
    return this.blocked !== null
  }

  /** Calls `listener` whenever the blocked state changes; returns the unsubscribe function. */
  public onBlockedChange(listener: BlockedListener): () => void {
    this.blockedListeners.add(listener)
    return () => {
      this.blockedListeners.delete(listener)
    }
  }

  /** Plays the sound the browser refused (call it from a click: that click is what allows audio) and clears the blocked state. */
  public replayBlocked() {
    const missed = this.blocked
    this.clearBlocked()
    if (missed) this.play(missed.theme, missed.tier)
  }

  /** The page got a click or key press: audio is allowed from now on. */
  public clearBlocked() {
    if (!this.blocked) return
    this.blocked = null
    for (const listener of this.blockedListeners) listener(false)
  }

  private markBlocked(theme: SoundTheme, tier: SoundTier) {
    const wasBlocked = this.blocked !== null
    this.blocked = { theme, tier }
    if (!wasBlocked) for (const listener of this.blockedListeners) listener(true)
  }

  private play(theme: SoundTheme, tier: SoundTier) {
    try {
      const base = this.audioFor(theme, tier)
      if (!base) return
      const clone = base.cloneNode() as HTMLAudioElement
      clone.volume = this.volume
      void clone.play().then(
        () => this.clearBlocked(),
        (error: unknown) => {
          // NotAllowedError = autoplay policy (no click on the page yet); other failures (a missing file) stay silent
          if (error instanceof DOMException && error.name === 'NotAllowedError') this.markBlocked(theme, tier)
        }
      )
    } catch {
      // Ignore audio errors
    }
  }
}

export const soundEngine = new SoundEngine()
