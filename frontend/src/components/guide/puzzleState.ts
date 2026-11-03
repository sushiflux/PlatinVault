import { createContext, useCallback, useContext, useState } from 'react'

// What a reader has revealed of a puzzle: how many hints and whether the solution is open. Kept per viewer in the browser only
// (a convenience, never state that matters): every access is wrapped, the page works without storage.
export interface PuzzleReveal {
  hints: number
  solution: boolean
}

const STORAGE_KEY = 'pv_puzzles_v1'
const MAX_ENTRIES = 400

// The game the guide belongs to, so the same puzzle id in two guides does not share its revealed state
export const PuzzleScopeContext = createContext<string>('')

function readAll(): Record<string, PuzzleReveal> {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : {}
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, PuzzleReveal>) : {}
  } catch {
    return {}
  }
}

function writeEntry(key: string, value: PuzzleReveal) {
  try {
    const all = readAll()
    // Re-insert so the entry counts as the newest one
    delete all[key]
    if (value.hints > 0 || value.solution) {
      all[key] = value
    }
    const keys = Object.keys(all)
    // Keep the file small: the oldest entries go first (insertion order)
    for (const old of keys.slice(0, Math.max(0, keys.length - MAX_ENTRIES))) {
      delete all[old]
    }
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(all))
  } catch {
    // Storage blocked or full: the reveal state just is not remembered
  }
}

function clampReveal(value: PuzzleReveal | undefined, hintCount: number, hasSolution: boolean): PuzzleReveal {
  return {
    hints: Math.min(Math.max(0, Math.trunc(Number(value?.hints) || 0)), hintCount),
    solution: hasSolution && value?.solution === true,
  }
}

/** The revealed state of one puzzle, remembered in the browser. `id` is the puzzle's itemId (or its title when it has none). */
export function usePuzzleReveal(id: string, hintCount: number, hasSolution: boolean) {
  const scope = useContext(PuzzleScopeContext)
  const key = `${scope}/${id}`
  const [state, setState] = useState<PuzzleReveal>(() => clampReveal(readAll()[key], hintCount, hasSolution))

  const update = useCallback(
    (change: (current: PuzzleReveal) => PuzzleReveal) => {
      setState((current) => {
        const next = clampReveal(change(current), hintCount, hasSolution)
        writeEntry(key, next)
        return next
      })
    },
    [key, hintCount, hasSolution]
  )

  return {
    ...state,
    revealNextHint: () => update((c) => ({ ...c, hints: c.hints + 1 })),
    hideHints: () => update((c) => ({ ...c, hints: 0 })),
    toggleSolution: () => update((c) => ({ ...c, solution: !c.solution })),
  }
}
