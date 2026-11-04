import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { GuideSectionDto, GuideTabDto } from '../../api/types'
import { HintAlert, isGuideAlertKind } from './GuideBlocks'
import { findSideQuestChapter } from './sideQuestChapter'
import { SideQuestTargetContext } from './SideQuestsAlert'
import type { SideQuestTarget } from './SideQuestsAlert'

afterEach(cleanup)

const section: GuideSectionDto = {
  type: 'alert',
  kind: 'side_quests',
  quests: [
    { title: 'Der verlorene Ring', availableFrom: 'Teil 1, Dorf Eichental', missable: 'Ende von Teil 2', reward: 'Silberring', ref: 'quest-ring' },
    { title: 'Brems Lieferung', missable: true },
    { title: 'Pilze sammeln', reward: '200 Gold', ref: 'quest-unknown' },
    { title: '  ' },
  ],
}

const target = (over: Partial<SideQuestTarget> = {}): SideQuestTarget => ({
  slug: 'tab-7-nebenquests',
  title: 'Nebenquests & Aktivitäten',
  hasRow: (ref) => ref === 'quest-ring',
  open: vi.fn(),
  ...over,
})

const renderWith = (value: SideQuestTarget | null, s: GuideSectionDto = section) =>
  render(
    <SideQuestTargetContext.Provider value={value}>
      <HintAlert section={s} />
    </SideQuestTargetContext.Provider>
  )

describe('side quest callout', () => {
  it('is a known alert kind and draws a small box with the quest names and a meta line each', () => {
    expect(isGuideAlertKind('side_quests')).toBe(true)
    const { container } = renderWith(null)
    const box = container.querySelector('[data-hint-kind="side_quests"]') as HTMLElement
    expect(within(box).getByText('Nebenaufgaben hier verfügbar')).toBeTruthy()
    // the blank entry is left out
    expect(box.querySelectorAll('li.pv-quest')).toHaveLength(3)
    const ring = box.querySelector('[data-quest-ref="quest-ring"]') as HTMLElement
    expect(ring.textContent).toContain('ab Teil 1, Dorf Eichental')
    expect(ring.textContent).toContain('verpassbar bis Ende von Teil 2')
    expect(ring.textContent).toContain('Belohnung: Silberring')
    expect(box.textContent).toContain('verpassbar')
    // it is not one of the loud alert boxes
    expect(box.className).not.toContain('pv-alert')
  })

  it('shows the names without links when the guide has no side quest chapter', () => {
    renderWith(null)
    expect(screen.queryAllByRole('link')).toHaveLength(0)
    expect(screen.getByText('Brems Lieferung')).toBeTruthy()
  })

  it('links every quest and the box to the side quest chapter', () => {
    const t = target()
    renderWith(t)
    expect(screen.getByRole('link', { name: 'Alle im Kapitel „Nebenquests & Aktivitäten“' })).toBeTruthy()
    fireEvent.click(screen.getByRole('link', { name: 'Brems Lieferung im Kapitel „Nebenquests & Aktivitäten“ öffnen' }))
    expect(t.open).toHaveBeenLastCalledWith(undefined)
    fireEvent.click(screen.getByRole('link', { name: 'Alle im Kapitel „Nebenquests & Aktivitäten“' }))
    expect(t.open).toHaveBeenLastCalledWith()
  })

  it('jumps to the exact row only when the chapter has a row with that reference', () => {
    const t = target()
    renderWith(t)
    fireEvent.click(screen.getByRole('link', { name: /Der verlorene Ring/ }))
    expect(t.open).toHaveBeenLastCalledWith('quest-ring')
    // a reference the chapter does not know opens just the chapter
    fireEvent.click(screen.getByRole('link', { name: /Pilze sammeln/ }))
    expect(t.open).toHaveBeenLastCalledWith(undefined)
  })

  it('draws nothing without quests and without a text', () => {
    const { container } = renderWith(null, { type: 'alert', kind: 'side_quests', quests: [] })
    expect(container.firstChild).toBeNull()
  })
})

describe('findSideQuestChapter', () => {
  const tab = (id: string, slug: string, title: string, over: Partial<GuideTabDto> = {}): GuideTabDto => ({
    id,
    slug,
    title,
    icon: 'mdi:map',
    sortOrder: 0,
    totalCheckableItems: 0,
    checkedItemsCount: 0,
    sections: [],
    isAchievementList: false,
    groupKey: 'start',
    ...over,
  })

  it('prefers the chapter that carries the topic "quests"', () => {
    const tabs = [tab('1', 'tab-1-guide', 'Aufträge der Gilde'), tab('2', 'tab-2-x', 'Aufgaben', { topicId: 'quests' })]
    expect(findSideQuestChapter(tabs)?.id).toBe('2')
  })

  it('falls back to the title or slug, in German and English', () => {
    expect(findSideQuestChapter([tab('1', 'walkthrough', 'Komplettlösung'), tab('2', 'tab-3-nebenquests-aktivitaeten', 'Foo')])?.id).toBe('2')
    expect(findSideQuestChapter([tab('1', 'walkthrough', 'Komplettlösung'), tab('2', 'tab-3-x', 'Side quests & activities')])?.id).toBe('2')
    expect(findSideQuestChapter([tab('1', 'walkthrough', 'Komplettlösung'), tab('2', 'tab-3-x', 'Nebenaufgaben')])?.id).toBe('2')
  })

  it('finds none in a guide without one, and never the chapter that is being read or the achievement list', () => {
    expect(findSideQuestChapter([tab('1', 'walkthrough', 'Komplettlösung'), tab('2', 'tab-2-waffen', 'Beste Waffen')])).toBeNull()
    expect(findSideQuestChapter([tab('2', 'tab-2-nebenquests', 'Nebenquests')], '2')).toBeNull()
    expect(findSideQuestChapter([tab('2', 'tab-2-nebenquests', 'Nebenquests', { isAchievementList: true })])).toBeNull()
  })
})
