// The choices of the trophy cabinet that are remembered per browser (localStorage, every access guarded: storage can be refused,
// e.g. in a private window, then the choice simply is not remembered).

// Five shelf sizes (the matching --cab-* values live in cabinet.css, data-size 0..4); left = smaller = more per shelf.
// The names are texts: size.step.<key> in the "cabinet" namespace.
export const SIZE_STEPS = ['veryS', 'small', 'medium', 'large', 'veryL'] as const
// The default is "small" (index 1)
export const DEFAULT_SIZE = 1
export const SIZE_KEY = 'pv_cabinet_size'

// The lighting of the cabinet. A new mode is one entry here plus one block of CSS under .pv-cab[data-lighting='<id>']
// (cabinet.css, section "Lighting modes"); nothing else in the code needs to know about it.
export const LIGHTING_KEY = 'pv_cabinet_lighting'
// The name and hint of a mode are texts: view.<id>.name / view.<id>.hint in the "cabinet" namespace.
export const LIGHTING_MODES = ['spots', 'led'] as const
export type Lighting = (typeof LIGHTING_MODES)[number]
export const DEFAULT_LIGHTING: Lighting = 'spots'

// Optional view: show the still empty places of the shelves as quiet placeholders (default off)
export const EMPTY_KEY = 'pv_cabinet_empty_slots'

export function loadSize(): number {
  try {
    const raw = localStorage.getItem(SIZE_KEY)
    const value = Number(raw)
    return raw !== null && raw.trim() !== '' && Number.isInteger(value) && value >= 0 && value <= SIZE_STEPS.length - 1 ? value : DEFAULT_SIZE
  } catch {
    return DEFAULT_SIZE
  }
}

export function saveSize(value: number) {
  try {
    localStorage.setItem(SIZE_KEY, String(value))
  } catch {
    // Storage unavailable (private mode): the size just is not remembered
  }
}

export function loadLighting(): Lighting {
  try {
    const value = localStorage.getItem(LIGHTING_KEY)
    return LIGHTING_MODES.find((mode) => mode === value) ?? DEFAULT_LIGHTING
  } catch {
    return DEFAULT_LIGHTING
  }
}

export function saveLighting(value: Lighting) {
  try {
    localStorage.setItem(LIGHTING_KEY, value)
  } catch {
    // Storage unavailable (private mode): the choice just is not remembered
  }
}

export function loadShowEmpty(): boolean {
  try {
    return localStorage.getItem(EMPTY_KEY) === '1'
  } catch {
    return false
  }
}

export function saveShowEmpty(value: boolean) {
  try {
    localStorage.setItem(EMPTY_KEY, value ? '1' : '0')
  } catch {
    // Storage unavailable (private mode): the choice just is not remembered
  }
}
