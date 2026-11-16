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
  copy: {
    button: 'Copy',
    done: 'Copied',
    failed: 'Copy not possible',
    label: 'Copy error message',
    doneAnnouncement: 'The error message was copied to the clipboard.',
    failedAnnouncement: 'The error message could not be copied to the clipboard. You can select the text and copy it with Ctrl+C.',
  },
  report: {
    details: 'Technical details',
    code: 'Error code',
  },
  notFound: {
    title: 'Page not found',
    text: 'The page you are looking for does not exist or has moved. Nothing was changed.',
    home: 'Go to the overview',
  },
  // Full-page status screens (components/errors/StatusPage.tsx). They are always shown in English; the German texts keep the keys in step.
  status: {
    copy: 'Copy details',
    home: 'Go to the overview',
    retry: 'Try again',
    errorLine: 'Error {{code}}',
    401: { title: 'Sign-in required', text: 'You are not allowed to open this page. Nothing was changed.' },
    403: { title: 'Access denied', text: 'This access is not allowed. Nothing was changed.' },
    404: { title: 'Page not found', text: 'The page you are looking for does not exist or has moved. Nothing was changed.' },
    429: { title: 'Too many requests', text: 'Too many requests came in a short time. Please wait a moment and try again.' },
    500: { title: 'Server error', text: 'Something went wrong on the server. Your data was not changed. Please try again in a moment.' },
    503: { title: 'Server unavailable', text: 'The server cannot be reached right now or is still starting. Please try again in a moment.' },
    offline: { title: 'No connection', text: 'The browser got no answer from the PlatinVault server. Please check that PlatinVault is running and the network is fine.' },
  },
  boundary: {
    title: 'An unexpected error occurred',
    text: 'Please reload the page to continue.',
    reload: 'Reload page',
    details: 'Technical details',
    copy: 'Copy details',
    devReload: 'Reload',
  },
} satisfies Translation<typeof de>

export default errors
