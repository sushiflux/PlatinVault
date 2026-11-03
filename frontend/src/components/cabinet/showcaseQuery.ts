import { queryOptions } from '@tanstack/react-query'
import { fetchShowcase } from '../../api/client'
import type { ShowcaseGameDto } from '../../api/types'

// The backend caches /api/showcase for ten minutes, so a few minutes of freshness here are safe: a second opening is instant
export const SHOWCASE_STALE_MS = 5 * 60_000

// One definition for the cabinet itself and for the prefetch (button hover / focus, idle after the page has loaded)
export const showcaseQueryOptions = queryOptions({
  queryKey: ['showcase'],
  queryFn: fetchShowcase,
  staleTime: SHOWCASE_STALE_MS,
  // A failing server should be reported quickly, not after three silent retries
  retry: 1,
})

/**
 * Loads and decodes the first covers so they are on screen the moment the doors open. A slow or broken cover never blocks
 * the opening: after `timeoutMs` it carries on (a missing cover falls back to the placeholder like everywhere else).
 */
export function preloadCovers(games: ShowcaseGameDto[], timeoutMs = 1500, limit = 48): Promise<void> {
  const urls = games
    .slice(0, limit)
    .map((game) => game.coverUrl)
    .filter((url): url is string => Boolean(url))
  if (urls.length === 0) return Promise.resolve()
  const decode = (url: string) =>
    new Promise<void>((resolve) => {
      const image = new Image()
      image.src = url
      image.decode().then(
        () => resolve(),
        () => resolve()
      )
    })
  const timeout = new Promise<void>((resolve) => window.setTimeout(resolve, timeoutMs))
  return Promise.race([Promise.all(urls.map(decode)).then(() => undefined), timeout])
}
