// Namespace "common": words every screen uses. German source of truth - en/common.ts is typed against it.
const common = {
  cancel: 'Abbrechen',
  close: 'Schließen',
  retry: 'Erneut versuchen',
  // Fallbacks inside messages like "... konnte nicht geladen werden: {{reason}}" when the failure carries no text of its own
  unknownError: 'unbekannter Fehler',
  noServerResponse: 'keine Antwort vom Server',
} as const

export default common
