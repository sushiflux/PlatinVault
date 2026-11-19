// Namespace "common": words every screen uses. German source of truth - en/common.ts is typed against it.
const common = {
  cancel: 'Abbrechen',
  close: 'Schließen',
  retry: 'Erneut versuchen',
  // Fallbacks inside messages like "... konnte nicht geladen werden: {{reason}}" when the failure carries no text of its own
  unknownError: 'unbekannter Fehler',
  noServerResponse: 'keine Antwort vom Server',
  // Names of the platforms a game is played on (utils/platforms.ts: platformLabelKey). Proper names, the same in every language;
  // "playstation" = the platform without a known variant
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
  // The short text of the platform chip on a game tile (components/platform/PlatformBadge.tsx); the long name above is its tooltip and screen reader text
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
} as const

export default common
