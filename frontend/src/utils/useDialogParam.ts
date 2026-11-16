import { useCallback } from 'react'
import { useSearchParams } from 'react-router'

/** The app windows that live in the address (?dialog=...), so a reload (F5) keeps the window open and Back closes it. */
export type DialogParam = 'handbook' | 'licenses' | 'whatsnew'

/**
 * Open state of one window kept in the URL query. Opening adds `dialog=<name>` as a new history entry (Back closes the window), closing
 * removes it again; every other parameter (tab, steamLink, ...) stays as it is.
 */
export function useDialogParam(name: DialogParam): [boolean, (open: boolean) => void] {
  const [searchParams, setSearchParams] = useSearchParams()
  const open = searchParams.get('dialog') === name
  const setOpen = useCallback(
    (next: boolean) => {
      setSearchParams(
        (previous) => {
          const params = new URLSearchParams(previous)
          if (next) params.set('dialog', name)
          else if (params.get('dialog') === name) params.delete('dialog')
          return params
        },
        { replace: !next }
      )
    },
    [name, setSearchParams]
  )
  return [open, setOpen]
}
