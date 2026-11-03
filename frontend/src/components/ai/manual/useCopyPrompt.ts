import { useCallback, useEffect, useRef, useState } from 'react'
import { copyText } from '../../../utils/clipboard'

export type CopyState = 'idle' | 'copied' | 'selected' | 'exported'

/** Saves text as a file in the browser (no server involved). */
export function downloadText(text: string, fileName: string): void {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/markdown;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

/** What a prompt box says after the player copied or exported: "copied", "selected - press Ctrl+C" (clipboard blocked) or "exported". */
export function useCopyPrompt() {
  const [state, setState] = useState<CopyState>('idle')
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const show = useCallback((next: CopyState, ms = 12000) => {
    window.clearTimeout(timer.current)
    setState(next)
    // "selected" stays until the player acts; the other messages fade after a while
    if (next !== 'selected') timer.current = window.setTimeout(() => setState('idle'), ms)
  }, [])

  const copy = useCallback(
    async (text: string) => {
      const ok = await copyText(text)
      show(ok ? 'copied' : 'selected')
      return ok
    },
    [show]
  )

  return { state, copy, show, reset: () => show('idle') }
}
