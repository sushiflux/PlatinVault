import { useState } from 'react'
import type { ReactNode } from 'react'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { GuideSectionDto, GuideTabDto } from '../../api/types'
import { ChecklistTableSection } from '../../pages/GameGuidePage'
import { HintAlert } from './GuideBlocks'
import { ItemLinkContext } from './ItemLinks'
import type { ItemLinkApi } from './ItemLinks'
import { buildItemLinkIndex } from './linkIndex'

afterEach(cleanup)

const table = (tableId: string, rows: { itemId: string; name: string; ref?: string }[]): GuideSectionDto => ({
  type: 'checklist_table',
  table: {
    tableId,
    caption: tableId,
    checkable: true,
    columns: [{ key: 'name', label: 'Name', isRowHeader: true }],
    rows: rows.map((row) => ({ itemId: row.itemId, ref: row.ref, cells: { name: row.name } })),
  } as GuideSectionDto['table'],
})

const tab = (over: Partial<GuideTabDto> & { id: string; slug: string; title: string; sections: GuideSectionDto[] }): GuideTabDto =>
  ({ icon: '', sortOrder: 0, totalCheckableItems: 0, checkedItemsCount: 0, isAchievementList: false, groupKey: 'start', ...over }) as GuideTabDto

const walkthrough = tab({
  id: 'wt',
  slug: 'tab-1-komplettloesung',
  title: '1. Komplettlösung',
  sections: [
    { type: 'heading', title: 'Hafenstadt', level: 1 },
    { type: 'heading', title: 'Hafen', level: 2 },
    table('area-1', [
      { itemId: 'wt-ring', name: 'Ring aufnehmen', ref: 'quest-ring' },
      { itemId: 'wt-gone', name: 'Verschwunden', ref: 'quest-deleted' },
      { itemId: 'wt-own', name: 'Eigener Eintrag' },
    ]),
    { type: 'alert', kind: 'side_quests', quests: [{ title: 'Der Ring', ref: 'quest-ring' }] },
  ],
})
const quests = tab({
  id: 'qu',
  slug: 'tab-2-nebenquests',
  title: 'Nebenquests',
  sections: [table('quests', [{ itemId: 'quest-ring', name: 'Der verlorene Ring' }])],
})
const tabs = [walkthrough, quests]

function Harness({ children, spies = {} }: { children?: ReactNode; spies?: Partial<ItemLinkApi> }) {
  const [checked, setChecked] = useState(new Set<string>())
  const api: ItemLinkApi = {
    index: buildItemLinkIndex(tabs),
    isChecked: (key) => checked.has(key),
    toggle: async (key, on) => setChecked((prev) => new Set(on ? [...prev, key] : [...prev].filter((k) => k !== key))),
    openRow: vi.fn(),
    openAnchor: vi.fn(),
    ...spies,
  }
  return (
    <ItemLinkContext.Provider value={api}>
      {children}
      <ChecklistTableSection table={walkthrough.sections[2]!.table!} checkedSet={checked} onToggleCheck={api.toggle} />
      <ChecklistTableSection table={quests.sections[0]!.table!} checkedSet={checked} onToggleCheck={api.toggle} />
    </ItemLinkContext.Provider>
  )
}

const boxes = () => screen.getAllByRole('checkbox') as HTMLInputElement[]

describe('linked checkboxes across chapters', () => {
  it('resolves the key to the ref when its row exists, else to the own item id', () => {
    const index = buildItemLinkIndex(tabs)
    expect(index.resolveKey('wt-ring', 'quest-ring')).toBe('quest-ring')
    expect(index.resolveKey('wt-gone', 'quest-deleted')).toBe('wt-gone')
    expect(index.resolveKey('wt-own', undefined)).toBe('wt-own')
    expect(index.target('quest-ring')?.tabSlug).toBe('tab-2-nebenquests')
    expect(index.target('quest-deleted')).toBeNull()
  })

  it('ticking the walkthrough row ticks the quest row and the other way round', () => {
    render(<Harness />)
    // order: wt-ring, wt-gone, wt-own, quest-ring
    fireEvent.click(boxes()[0]!)
    expect(boxes()[0]!.checked).toBe(true)
    expect(boxes()[3]!.checked).toBe(true)
    expect(boxes()[1]!.checked).toBe(false)
    fireEvent.click(boxes()[3]!)
    expect(boxes()[0]!.checked).toBe(false)
    expect(boxes()[3]!.checked).toBe(false)
  })

  it('a ref without a target keeps its own check and shows no jump link', () => {
    render(<Harness />)
    fireEvent.click(boxes()[1]!)
    expect(boxes()[1]!.checked).toBe(true)
    expect(boxes().filter((box) => box.checked)).toHaveLength(1)
    expect(screen.getByText('Verschwunden').closest('th')!.querySelector('[data-link="target"]')).toBeNull()
  })

  it('draws the jump link on the ref row and the back-reference on the defining row', () => {
    render(<Harness />)
    const jump = screen.getByText('Ring aufnehmen').closest('th')!.querySelector('[data-link="target"]') as HTMLElement
    expect(jump.textContent).toBe('Nebenquests')
    expect(jump.getAttribute('aria-label')).toContain('Nebenquests')
    const back = screen.getByText('Der verlorene Ring').closest('th')!.querySelectorAll('[data-link="back"]')
    // the table row and the side quest callout sit in the same area, so it is one place
    expect(back).toHaveLength(1)
    expect(back[0]!.textContent).toBe('Komplettlösung · Hafenstadt · Hafen')
    expect(back[0]!.getAttribute('aria-label')).toContain('Komplettlösung · Hafenstadt · Hafen')
  })

  it('the jump links open the row and the heading of the area', () => {
    const openRow = vi.fn()
    const openAnchor = vi.fn()
    render(<Harness spies={{ openRow, openAnchor }} />)
    fireEvent.click(screen.getByText('Ring aufnehmen').closest('th')!.querySelector('[data-link="target"]')!)
    expect(openRow).toHaveBeenCalledWith('quest-ring')
    fireEvent.click(screen.getByText('Der verlorene Ring').closest('th')!.querySelector('[data-link="back"]')!)
    const [slug, anchor] = openAnchor.mock.calls[0]!
    expect(slug).toBe('tab-1-komplettloesung')
    expect(anchor).toMatch(/^pv-wt-wt/)
  })

  it('falls back to the server map when the page finds no referring place', () => {
    const index = buildItemLinkIndex([quests, { ...walkthrough, sections: [] }], {
      itemLinks: { 'quest-ring': [{ tabId: 'wt', tabSlug: 'tab-1-komplettloesung', tabTitle: 'Komplettlösung', sectionIndex: 99, path: 'Teil 2' }] },
    })
    expect(index.backRefs('quest-ring')).toEqual([expect.objectContaining({ tabSlug: 'tab-1-komplettloesung', path: 'Komplettlösung · Teil 2', anchorId: null })])
  })

  it('uses the server maps: forward target and the heading found from the section index', () => {
    const index = buildItemLinkIndex(tabs, {
      refTargets: { 'quest-ring': { tabId: 'qu', tabSlug: 'tab-2-nebenquests', tabTitle: '2. Nebenquests' } },
      itemLinks: { 'quest-ring': [{ tabId: 'wt', tabSlug: 'tab-1-komplettloesung', tabTitle: 'Komplettlösung', sectionIndex: 2, path: 'Hafenstadt · Hafen' }] },
    })
    expect(index.target('quest-ring')?.tabTitle).toBe('Nebenquests')
    expect(index.backRefs('quest-ring')[0]).toEqual(expect.objectContaining({ path: 'Komplettlösung · Hafenstadt · Hafen', anchorId: expect.stringMatching(/^pv-wt-wt/) }))
  })

  it('a side quest with a ref gets a tick box bound to the quest row', () => {
    const { container } = render(
      <Harness>
        <HintAlert section={walkthrough.sections[3]!} />
      </Harness>
    )
    const quest = container.querySelector('[data-quest-ref="quest-ring"]') as HTMLElement
    const box = within(quest).getByRole('checkbox') as HTMLInputElement
    fireEvent.click(box)
    expect(box.checked).toBe(true)
    // the same check as the row in the quest chapter and the walkthrough row that refers to it (callout box + walkthrough row + quest row)
    expect(boxes().filter((b) => b.checked)).toHaveLength(3)
  })
})
