import type { UpdateReleaseDto } from './types'

// The release notes of one version arrive with the update status (`notes`, filled by the server from the GitHub release body).
// The app itself makes no request to GitHub: it only shows what the server sent.

/** True when the release carries notes text, so the notes dialog has something to show. */
export function hasReleaseNotes(release: Pick<UpdateReleaseDto, 'notes'>): boolean {
  return typeof release.notes === 'string' && release.notes.trim() !== ''
}

/** The address of the release page for a plain external link; only https://github.com addresses are followed (the value comes from the server). */
export function releasePageUrl(release: Pick<UpdateReleaseDto, 'notesUrl'>): string | null {
  if (!release.notesUrl) return null
  try {
    const url = new URL(release.notesUrl)
    return url.protocol === 'https:' && url.hostname === 'github.com' ? url.href : null
  } catch {
    return null
  }
}
