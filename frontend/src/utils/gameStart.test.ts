import { afterEach, describe, expect, it } from 'vitest'
import {
  MAX_OBSERVATION_GAP_MS,
  RESTART_GRACE_MS,
  gameLivePath,
  isDialogOpen,
  isGamePagePath,
  isTypingInTextField,
  newStartTracker,
  observeRunningGame,
} from './gameStart'

const SECOND = 1000

describe('observeRunningGame', () => {
  it('never reports the game that is already running when the page opens', () => {
    const tracker = newStartTracker()
    expect(observeRunningGame(tracker, 42, 0)).toBeNull()
    expect(observeRunningGame(tracker, 42, 5 * SECOND)).toBeNull()
  })

  it('reports a game that starts after "no game" was seen', () => {
    const tracker = newStartTracker()
    expect(observeRunningGame(tracker, 0, 0)).toBeNull()
    expect(observeRunningGame(tracker, 0, 5 * SECOND)).toBeNull()
    expect(observeRunningGame(tracker, 42, 10 * SECOND)).toBe(42)
  })

  it('reports a start only once while the game keeps running', () => {
    const tracker = newStartTracker()
    observeRunningGame(tracker, 0, 0)
    expect(observeRunningGame(tracker, 42, 5 * SECOND)).toBe(42)
    expect(observeRunningGame(tracker, 42, 10 * SECOND)).toBeNull()
    expect(observeRunningGame(tracker, 42, 15 * SECOND)).toBeNull()
  })

  it('does not report the game stopping', () => {
    const tracker = newStartTracker()
    observeRunningGame(tracker, 42, 0)
    expect(observeRunningGame(tracker, 0, 5 * SECOND)).toBeNull()
  })

  it('reports another game that replaces the running one', () => {
    const tracker = newStartTracker()
    observeRunningGame(tracker, 42, 0)
    expect(observeRunningGame(tracker, 7, 5 * SECOND)).toBe(7)
  })

  it('does not take a silent bridge or a restarted server for a new start of the same game', () => {
    const tracker = newStartTracker()
    observeRunningGame(tracker, 42, 0)
    observeRunningGame(tracker, 42, 5 * SECOND)
    // The answer shows "no game" for a while (bridge silent, server restarting) and then the same game again
    observeRunningGame(tracker, 0, 10 * SECOND)
    observeRunningGame(tracker, 0, 40 * SECOND)
    expect(observeRunningGame(tracker, 42, 60 * SECOND)).toBeNull()
  })

  it('reports the same game again once it was gone for longer than the grace time', () => {
    const tracker = newStartTracker()
    observeRunningGame(tracker, 42, 0)
    observeRunningGame(tracker, 0, 5 * SECOND)
    // Stay "watching" with observations in between, so only the grace rule decides
    let now = 5 * SECOND
    while (now < RESTART_GRACE_MS + 10 * SECOND) {
      now += 30 * SECOND
      observeRunningGame(tracker, 0, now)
    }
    expect(observeRunningGame(tracker, 42, now + 5 * SECOND)).toBe(42)
  })

  it('takes a new baseline after a long silence instead of reporting a start (sleeping computer, frozen tab)', () => {
    const tracker = newStartTracker()
    observeRunningGame(tracker, 0, 0)
    expect(observeRunningGame(tracker, 42, MAX_OBSERVATION_GAP_MS + 1)).toBeNull()
    // ... and from there on it watches normally again
    observeRunningGame(tracker, 0, MAX_OBSERVATION_GAP_MS + 10 * SECOND)
    expect(observeRunningGame(tracker, 7, MAX_OBSERVATION_GAP_MS + 15 * SECOND)).toBe(7)
  })
})

describe('isGamePagePath', () => {
  it('matches the page of exactly this game, with or without a trailing slash', () => {
    expect(isGamePagePath('/games/final-fantasy', 'final-fantasy')).toBe(true)
    expect(isGamePagePath('/games/final-fantasy/', 'final-fantasy')).toBe(true)
    expect(isGamePagePath('/games/final-fantasy-2', 'final-fantasy')).toBe(false)
    expect(isGamePagePath('/', 'final-fantasy')).toBe(false)
    expect(isGamePagePath('/games', 'final-fantasy')).toBe(false)
  })

  it('compares the decoded slug', () => {
    expect(isGamePagePath('/games/a%20b', 'a b')).toBe(true)
    expect(isGamePagePath('/games/%E0%A4%A', '%E0%A4%A')).toBe(true)
  })
})

describe('gameLivePath', () => {
  it('points at the Live trophies section of the game', () => {
    expect(gameLivePath('hades')).toBe('/games/hades?tab=live')
    expect(gameLivePath('a b')).toBe('/games/a%20b?tab=live')
  })
})

describe('page state checks', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('sees an open dialog by role or by the modal backdrop', () => {
    expect(isDialogOpen()).toBe(false)
    document.body.innerHTML = '<div role="dialog"></div>'
    expect(isDialogOpen()).toBe(true)
    document.body.innerHTML = '<div class="pv-modal-backdrop"></div>'
    expect(isDialogOpen()).toBe(true)
    document.body.innerHTML = '<div role="alertdialog"></div>'
    expect(isDialogOpen()).toBe(true)
  })

  it('counts a focused text field, search box and textarea as typing, but not buttons or checkboxes', () => {
    document.body.innerHTML = '<input id="a" type="text"><input id="b" type="search"><textarea id="c"></textarea><input id="d" type="checkbox"><button id="e">x</button><input id="f" type="text" readonly>'
    const focus = (id: string) => (document.getElementById(id) as HTMLElement).focus()
    focus('a')
    expect(isTypingInTextField()).toBe(true)
    focus('b')
    expect(isTypingInTextField()).toBe(true)
    focus('c')
    expect(isTypingInTextField()).toBe(true)
    focus('d')
    expect(isTypingInTextField()).toBe(false)
    focus('e')
    expect(isTypingInTextField()).toBe(false)
    focus('f')
    expect(isTypingInTextField()).toBe(false)
    ;(document.activeElement as HTMLElement).blur()
    expect(isTypingInTextField()).toBe(false)
  })
})
