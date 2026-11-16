// The colour scheme is owned by App (state + storage). Screens that sit above the app (the first-run screen) change it through this event,
// so App's state and the page stay in step: nothing else may write the stored scheme behind App's back.
export const THEME_EVENT = 'pv-theme-set'

export type ThemeName = 'light' | 'dark'

export function readTheme(): ThemeName {
  return document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light'
}

export function requestTheme(theme: ThemeName): void {
  window.dispatchEvent(new CustomEvent<ThemeName>(THEME_EVENT, { detail: theme }))
}
