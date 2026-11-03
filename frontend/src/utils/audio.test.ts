import { afterEach, describe, expect, it, vi } from 'vitest'
import { soundEngine } from './audio'

const flush = () => new Promise((resolve) => setTimeout(resolve, 0))

afterEach(() => {
  vi.restoreAllMocks()
  soundEngine.clearBlocked()
  soundEngine.setEnabled(true)
})

describe('soundEngine and the browser autoplay policy', () => {
  it('remembers a sound the browser refused, tells listeners once, and plays it on replay', async () => {
    const play = vi
      .spyOn(HTMLMediaElement.prototype, 'play')
      .mockRejectedValue(new DOMException('play() can only be initiated by a user gesture.', 'NotAllowedError'))
    const changes: boolean[] = []
    const unsubscribe = soundEngine.onBlockedChange((blocked) => changes.push(blocked))

    soundEngine.playTrophyUnlock('Gold')
    soundEngine.playTrophyUnlock('Bronze')
    await flush()
    expect(soundEngine.isBlocked()).toBe(true)
    expect(changes).toEqual([true])

    // The click on "play the missed sound" allows audio: the sound plays and the state clears
    play.mockResolvedValue(undefined)
    soundEngine.replayBlocked()
    await flush()
    expect(play).toHaveBeenCalledTimes(3)
    expect(soundEngine.isBlocked()).toBe(false)
    expect(changes).toEqual([true, false])
    unsubscribe()
  })

  it('stays quiet about other playback failures and about a disabled sound', async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play').mockRejectedValue(new DOMException('no source', 'NotSupportedError'))
    soundEngine.playTrophyUnlock('Silver')
    await flush()
    expect(soundEngine.isBlocked()).toBe(false)

    soundEngine.setEnabled(false)
    play.mockClear()
    soundEngine.playTrophyUnlock('Silver')
    expect(play).not.toHaveBeenCalled()
  })

  it('clears the blocked state when a sound plays again (the page got a click meanwhile)', async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play').mockRejectedValue(new DOMException('blocked', 'NotAllowedError'))
    soundEngine.playPlatinumFanfare()
    await flush()
    expect(soundEngine.isBlocked()).toBe(true)

    play.mockResolvedValue(undefined)
    soundEngine.playTrophyUnlock('Gold')
    await flush()
    expect(soundEngine.isBlocked()).toBe(false)
  })
})
