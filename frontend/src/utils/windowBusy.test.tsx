import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, renderHook } from '@testing-library/react'
import { applyLanguage } from '../i18n'
import { AiProgressNotice } from '../components/ai/AiProgress'
import { FloatingNoticeStack } from '../components/ui/FloatingNotice'
import { isWindowBusy, useWindowBusy } from './windowBusy'

describe('windowBusy', () => {
  beforeEach(async () => {
    await applyLanguage('en')
  })

  afterEach(() => {
    cleanup()
  })

  it('is busy exactly while a component that asked for it is mounted', () => {
    expect(isWindowBusy()).toBe(false)
    const first = renderHook(() => useWindowBusy())
    expect(isWindowBusy()).toBe(true)
    const second = renderHook(() => useWindowBusy())
    first.unmount()
    // Two jobs at once: the window stays busy until the last one is over
    expect(isWindowBusy()).toBe(true)
    second.unmount()
    expect(isWindowBusy()).toBe(false)
  })

  it('does nothing while inactive', () => {
    const { rerender, unmount } = renderHook(({ active }) => useWindowBusy(active), { initialProps: { active: false } })
    expect(isWindowBusy()).toBe(false)
    rerender({ active: true })
    expect(isWindowBusy()).toBe(true)
    rerender({ active: false })
    expect(isWindowBusy()).toBe(false)
    unmount()
  })

  it('counts the progress notice of a running AI action as a long job of this window', () => {
    const { unmount } = render(
      <FloatingNoticeStack>
        <AiProgressNotice key="ai-progress" kind="complete" view={null} />
      </FloatingNoticeStack>
    )
    expect(isWindowBusy()).toBe(true)
    unmount()
    expect(isWindowBusy()).toBe(false)
  })
})
