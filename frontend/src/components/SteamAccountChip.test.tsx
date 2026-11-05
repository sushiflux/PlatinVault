/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { SteamProfileDto } from '../api/types'
import { SteamAccountChip } from './SteamAccountChip'

afterEach(cleanup)

// Read from disk: the test setup does not load stylesheets, so a ?raw import would be empty. Line endings differ between checkouts.
const css = readFileSync(resolve(fileURLToPath(import.meta.url), '../../index.css'), 'utf-8').replaceAll('\r\n', '\n')

// The text of one top-level rule of the stylesheet, e.g. ".pv-hchip {"
function rule(selector: string): string {
  const start = css.indexOf(`\n${selector} {`)
  expect(start, `rule ${selector}`).toBeGreaterThan(-1)
  return css.slice(start, css.indexOf('}', start))
}

const profile: SteamProfileDto = {
  steamId: '76561198000000000',
  personaName: 'Test Account',
  avatarUrl: null,
  profileUrl: 'https://steamcommunity.com/profiles/76561198000000000',
  libraryGameCount: 1234,
}

describe('SteamAccountChip: the border stays whole on hover', () => {
  it('renders the link inside the bordered box', () => {
    const { container } = render(
      <QueryClientProvider client={new QueryClient()}>
        <SteamAccountChip profile={profile} />
      </QueryClientProvider>
    )
    const box = container.querySelector('.pv-hchip')!
    expect(box.querySelector('a.pv-chip-link')).not.toBeNull()
  })

  // jsdom does not load the stylesheet, so the rules themselves are read. Cause of the bug: the box is 2.5rem including its 1px
  // border, the link inside had a fixed 2.5rem too - 2px taller than the inside - and its hover background covered the bottom border.
  it('the box has the border, the link has no fixed height of its own and stretches inside it', () => {
    const box = rule('.pv-hchip')
    expect(box).toMatch(/block-size:\s*2\.5rem/)
    expect(box).toMatch(/border:\s*1px solid/)

    const link = rule('.pv-chip-link')
    expect(link).not.toMatch(/block-size/)
    expect(link).not.toMatch(/(^|[^-])height\s*:/)
    expect(link).toMatch(/align-self:\s*stretch/)
    // The inner curve of the border: the chip's radius minus the border width
    expect(link).toMatch(/border-radius:\s*calc\(var\(--radius-md\) - 1px\)/)
  })

  it('hover only changes the background; it never removes or covers the border', () => {
    const hover = rule('a.pv-chip-link:hover')
    expect(hover).toMatch(/background:/)
    expect(hover).not.toMatch(/border|outline|margin|transform/)
  })
})
