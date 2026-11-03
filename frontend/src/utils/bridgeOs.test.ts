import { describe, expect, it } from 'vitest'
import { defaultBridgeOs } from './bridgeOs'

describe('defaultBridgeOs', () => {
  it('preselects Linux for a Linux desktop and the Steam Deck browser', () => {
    expect(defaultBridgeOs('Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36')).toBe('linux')
    expect(defaultBridgeOs('Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0')).toBe('linux')
  })

  it('keeps Windows for Windows, macOS and phones (Android also says Linux)', () => {
    expect(defaultBridgeOs('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36')).toBe('windows')
    expect(defaultBridgeOs('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15')).toBe('windows')
    expect(defaultBridgeOs('Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36')).toBe('windows')
    expect(defaultBridgeOs('')).toBe('windows')
  })
})
