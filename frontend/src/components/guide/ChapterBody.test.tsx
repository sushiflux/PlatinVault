import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GuideSectionDto } from '../../api/types'
import { ChapterBody } from './ChapterBody'
import { buildOutline } from './outline'

const heading = (title: string, level?: number): GuideSectionDto => ({ type: 'heading', title, level, htmlContent: null })
const note = (text: string): GuideSectionDto => ({ type: 'story', paragraphs: [text] })

// Two parts, the first with two areas, the second with one; an overview before the first area, sources at the very end
const structured: GuideSectionDto[] = [
  note('Vorwort vor dem ersten Teil'),
  heading('Der Aufbruch', 1),
  { type: 'overview', counts: [{ label: 'Truhen', count: 4 }] },
  heading('Dorf Eichental', 2),
  note('Gehe zum Brunnen.'),
  heading('Nordwald', 2),
  note('Folge dem Pfad.'),
  heading('Die Ruinen', 1),
  heading('Eingangshalle', 2),
  { type: 'boss', bossId: 'wache', title: 'Steinwache' },
  { type: 'sources', sources: [{ title: 'Handbuch', url: 'https://example.test/', origin: 'cited' }] },
]

// An older walkthrough: headings without any level
const legacy: GuideSectionDto[] = [heading('Kapitel 1'), note('Text eins'), heading('Kapitel 2'), note('Text zwei'), heading('Kapitel 3')]

const render1 = (sections: GuideSectionDto[], tabId = 'tab-1') =>
  render(
    <ChapterBody
      tabId={tabId}
      sections={sections}
      renderSection={(section, index) => (
        <div data-testid="section" data-type={section.type} data-index={index}>
          {section.paragraphs?.join(' ') ?? section.type}
        </div>
      )}
    />
  )

let scrollIntoView: ReturnType<typeof vi.fn>

beforeEach(() => {
  scrollIntoView = vi.fn()
  Object.defineProperty(Element.prototype, 'scrollIntoView', { value: scrollIntoView, configurable: true })
})
afterEach(cleanup)

describe('buildOutline', () => {
  it('numbers parts and areas and groups the sections', () => {
    const outline = buildOutline(structured, 'p')
    expect(outline.structured).toBe(true)
    expect(outline.entries.map((e) => [e.level, e.number, e.title])).toEqual([
      [1, '1', 'Der Aufbruch'],
      [2, '1.1', 'Dorf Eichental'],
      [2, '1.2', 'Nordwald'],
      [1, '2', 'Die Ruinen'],
      [2, '2.1', 'Eingangshalle'],
    ])
    // Content before the first heading is a part of its own without a heading; the sources are pulled out of every frame
    expect(outline.parts[0]!.entry).toBeUndefined()
    expect(outline.parts[0]!.indices).toEqual([0])
    expect(outline.parts[1]!.indices).toEqual([2])
    expect(outline.parts[1]!.areas.map((a) => a.indices)).toEqual([[4], [6]])
    expect(outline.tail).toEqual([10])
  })

  it('treats headings without levels as a flat list (no frames, no numbers)', () => {
    const outline = buildOutline(legacy, 'p')
    expect(outline.structured).toBe(false)
    expect(outline.entries.map((e) => [e.level, e.number])).toEqual([[1, ''], [1, ''], [1, '']])
  })

  it('counts an area before any part as its own number', () => {
    const outline = buildOutline([heading('Nur Gebiet', 2), heading('Teil', 1), heading('Gebiet', 2)], 'p')
    expect(outline.entries.map((e) => e.number)).toEqual(['1', '1', '1.1'])
  })
})

describe('ChapterBody with parts and areas', () => {
  it('draws a numbered band per part and a framed, numbered section per area', () => {
    const { container } = render1(structured)
    const parts = container.querySelectorAll('section.pv-part')
    expect(parts).toHaveLength(2)
    expect(parts[0]!.querySelector('.pv-part-no')?.textContent).toBe('01')
    expect(within(parts[0] as HTMLElement).getByRole('heading', { level: 3, name: 'Der Aufbruch' })).toBeTruthy()
    const areas = container.querySelectorAll('section.pv-area')
    expect(areas).toHaveLength(3)
    expect(Array.from(areas).map((a) => a.querySelector('.pv-area-no')?.textContent)).toEqual(['1.1', '1.2', '2.1'])
    // The overview sits in the part before its first area; the boss inside the area where the fight is
    expect(parts[0]!.querySelector(':scope > [data-type="overview"]')).toBeTruthy()
    expect(areas[2]!.querySelector('[data-type="boss"]')).toBeTruthy()
    expect(container.querySelector('.pv-part-intro [data-testid="section"]')?.textContent).toContain('Vorwort')
  })

  it('puts the sources after all parts, outside every frame', () => {
    const { container } = render1(structured)
    const sources = container.querySelector('[data-type="sources"]')!
    expect(sources.closest('.pv-part')).toBeNull()
    expect(sources.closest('.pv-area')).toBeNull()
    expect(container.querySelector('.pv-wt-content')!.lastElementChild).toBe(sources)
  })

  it('lists parts and areas in a table of contents that jumps to the heading and marks the current one', () => {
    render1(structured)
    const nav = screen.getByRole('navigation', { name: 'Inhalt der Komplettlösung' })
    const links = within(nav).getAllByRole('link')
    expect(links.map((a) => a.textContent)).toEqual(['1Der Aufbruch', '1.1Dorf Eichental', '1.2Nordwald', '2Die Ruinen', '2.1Eingangshalle'])
    act(() => {
      fireEvent.click(links[2]!)
    })
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    const target = scrollIntoView.mock.contexts[0] as HTMLElement
    expect(target.id).toBe(links[2]!.getAttribute('href')!.slice(1))
    expect(target.querySelector('.pv-area-title')?.textContent).toBe('Nordwald')
    expect(links[2]!.getAttribute('aria-current')).toBe('location')
    expect(links.filter((a) => a.getAttribute('aria-current') === 'location')).toHaveLength(1)
  })

  it('follows the scroll position: the last heading above the sticky bars is the current one', () => {
    const { container } = render1(structured)
    const top = new Map<string, number>()
    Array.from(container.querySelectorAll<HTMLElement>('[id^="pv-wt-tab-1-h"]')).filter((el) => /-h\d+$/.test(el.id)).forEach((el, i) => top.set(el.id, i < 3 ? -100 + i * 10 : 900))
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      return { top: top.get(this.id) ?? 0, bottom: 0, left: 0, right: 0, width: 0, height: 0, x: 0, y: 0, toJSON: () => ({}) }
    })
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
      cb(0)
      return 1
    })
    act(() => {
      window.dispatchEvent(new Event('scroll'))
    })
    const current = screen.getByRole('navigation').querySelector('[aria-current="location"]')
    expect(current?.textContent).toContain('Nordwald')
    vi.restoreAllMocks()
  })

  it('collapses the table of contents on a narrow screen and opens it with its button', () => {
    render1(structured)
    const toggle = screen.getByRole('button', { name: /Inhalt anzeigen/ })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(toggle)
    expect(screen.getByRole('button', { name: /Inhalt ausblenden/ }).getAttribute('aria-expanded')).toBe('true')
    // Picking an entry closes it again
    fireEvent.click(within(screen.getByRole('navigation')).getAllByRole('link')[0]!)
    expect(screen.getByRole('button', { name: /Inhalt anzeigen/ }).getAttribute('aria-expanded')).toBe('false')
  })
})

describe('ChapterBody for older walkthroughs and plain chapters', () => {
  it('keeps the calm heading (no numbers, no frames) but still offers a table of contents', () => {
    const { container } = render1(legacy)
    expect(container.querySelectorAll('.pv-part-head, .pv-area-head, section.pv-area')).toHaveLength(0)
    expect(container.querySelectorAll('.pv-wt-heading')).toHaveLength(3)
    const links = within(screen.getByRole('navigation')).getAllByRole('link')
    expect(links.map((a) => a.textContent)).toEqual(['Kapitel 1', 'Kapitel 2', 'Kapitel 3'])
  })

  it('draws a chapter with one heading or none exactly as a plain list, without a table of contents', () => {
    const { container, rerender } = render1([heading('Einziges'), note('x')])
    expect(container.querySelector('.pv-wt')).toBeNull()
    expect(screen.queryByRole('navigation')).toBeNull()
    expect(container.querySelectorAll('.pv-wt-heading')).toHaveLength(1)
    rerender(<ChapterBody tabId="tab-1" sections={[note('a'), note('b')]} renderSection={(s) => <p data-testid="plain">{s.paragraphs?.[0]}</p>} />)
    expect(screen.getAllByTestId('plain')).toHaveLength(2)
    expect(screen.queryByRole('navigation')).toBeNull()
  })

  it('keeps the lead sentence of a heading', () => {
    render1([{ type: 'heading', title: 'Teil', level: 1, htmlContent: 'Eine Zeile zur Einstimmung.' }, { type: 'heading', title: 'Gebiet', level: 2 }])
    expect(screen.getByText('Eine Zeile zur Einstimmung.')).toBeTruthy()
  })
})
