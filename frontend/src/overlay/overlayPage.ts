/** The route of the in-game overlay page (also checked by public/theme-init.js, which cannot import this). */
export const OVERLAY_PATH = '/overlay'

export function isOverlayPage(pathname: string): boolean {
  return pathname.replace(/\/+$/, '') === OVERLAY_PATH
}
