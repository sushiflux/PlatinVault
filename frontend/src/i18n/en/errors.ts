import type { Translation } from '../types'
import type de from '../de/errors'

// Namespace "errors": English texts, typed against the German source file.
const errors = {
  http: {
    unreachable: 'PlatinVault server not reachable (HTTP {{status}}) – is the backend running? Please try again in a moment.',
    network:
      'PlatinVault server not reachable – the browser got no answer. Please check: is the backend or container running, and is the network connection to the server working? Then try again.',
    notFound: 'Not found (HTTP 404).',
    tooLarge: 'The file or request is too large (HTTP 413).',
    tooManyRequests: 'Too many requests – please wait a moment (HTTP 429).',
    unexpected: 'Unexpected response from the server (HTTP {{status}}).',
  },
  boundary: {
    title: 'An unexpected error occurred',
    text: 'Please reload the page to continue.',
    reload: 'Reload page',
    devReload: 'Reload',
  },
} satisfies Translation<typeof de>

export default errors
