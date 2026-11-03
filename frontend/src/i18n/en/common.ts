import type { Translation } from '../types'
import type de from '../de/common'

// Namespace "common": English texts, typed against the German source file.
const common = {
  cancel: 'Cancel',
  close: 'Close',
  retry: 'Try again',
  unknownError: 'unknown error',
  noServerResponse: 'no response from the server',
} satisfies Translation<typeof de>

export default common
