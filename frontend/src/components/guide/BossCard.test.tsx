import { cleanup, render } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { GuideSectionDto } from '../../api/types'
import '../../i18n'
import { BossCard } from './BossCard'

afterEach(() => cleanup())

describe('BossCard ids', () => {
  it('gives the same boss in two walkthrough parts different heading ids', () => {
    const section = { type: 'boss', title: 'Ifrit', bossId: 'ifrit' } as GuideSectionDto
    const { container } = render(
      <>
        <BossCard section={section} />
        <BossCard section={section} />
      </>
    )
    const ids = [...container.querySelectorAll('h4[id]')].map((el) => el.id)
    expect(ids).toHaveLength(2)
    expect(new Set(ids).size).toBe(2)
    for (const article of container.querySelectorAll('article')) {
      expect(container.querySelector(`#${CSS.escape(article.getAttribute('aria-labelledby')!)}`)).toBeTruthy()
    }
  })
})
