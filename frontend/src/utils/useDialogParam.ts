import { useCallback } from 'react'
import { useSearchParams } from 'react-router'

/** The app windows that live in the address (?dialog=...), so a reload (F5) keeps the window open and Back closes it. */
export type DialogParam = 'handbook' | 'licenses' | 'whatsnew' | 'addgame'

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

/** The full-screen windows that live in the address as a plain flag (?cabinet=1) instead of ?dialog=..., so they can sit on top of any page. */
export type FlagParam = 'cabinet'

/**
 * Open state of a full-screen window kept in the URL as `<name>=1` on the current page: the same history behaviour as useDialogParam
 * (opening adds an entry so Back closes it, closing removes the flag) and every other parameter of the page stays as it is.
 */
export function useFlagParam(name: FlagParam): [boolean, (open: boolean) => void] {
  const [searchParams, setSearchParams] = useSearchParams()
  const open = searchParams.get(name) === '1'
  const setOpen = useCallback(
    (next: boolean) => {
      setSearchParams(
        (previous) => {
          const params = new URLSearchParams(previous)
          if (next) params.set(name, '1')
          else params.delete(name)
          return params
        },
        { replace: !next }
      )
    },
    [name, setSearchParams]
  )
  return [open, setOpen]
}
