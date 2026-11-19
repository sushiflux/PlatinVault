import type { Translation } from '../types'
import type de from '../de/common'

// Namespace "common": English texts, typed against the German source file.
const common = {
  cancel: 'Cancel',
  close: 'Close',
  retry: 'Try again',
  unknownError: 'unknown error',
  noServerResponse: 'no response from the server',
  platforms: {
    steam: 'Steam',
    playstation: 'PlayStation',
    ps5: 'PlayStation 5',
    ps4: 'PlayStation 4',
    xbox: 'Xbox',
    epic: 'Epic Games',
    gog: 'GOG',
    nintendo: 'Nintendo Switch',
    ubisoft: 'Ubisoft Connect',
    ea: 'EA app',
    battlenet: 'Battle.net',
  },
  platformChips: {
    steam: 'Steam',
    playstation: 'PlayStation',
    ps5: 'PS5',
    ps4: 'PS4',
    xbox: 'Xbox',
    epic: 'Epic',
    gog: 'GOG',
    nintendo: 'Switch',
    ubisoft: 'Ubisoft',
    ea: 'EA',
    battlenet: 'Battle.net',
  },
} satisfies Translation<typeof de>

export default common
