// Namespace "errors": error screens and the messages for failed API calls. German source of truth.
const errors = {
  // Shown when the server sent no problem details (e.g. the proxy answers while the backend is down)
  http: {
    unreachable: 'PlatinVault-Server nicht erreichbar (HTTP {{status}}) – läuft das Backend? Bitte gleich noch einmal versuchen.',
    network:
      'PlatinVault-Server nicht erreichbar – der Browser hat keine Antwort bekommen. Bitte prüfen: Läuft das Backend bzw. der Container, und ist die Netzwerkverbindung zum Server in Ordnung? Danach erneut versuchen.',
    notFound: 'Nicht gefunden (HTTP 404).',
    tooLarge: 'Die Datei bzw. Anfrage ist zu groß (HTTP 413).',
    tooManyRequests: 'Zu viele Anfragen – bitte kurz warten (HTTP 429).',
    unexpected: 'Unerwartete Antwort vom Server (HTTP {{status}}).',
  },
  // The "Copy" button of every error message (components/ui/CopyErrorButton.tsx) and the text it copies (utils/errorReport.ts)
  copy: {
    button: 'Kopieren',
    done: 'Kopiert',
    failed: 'Kopieren nicht möglich',
    // Accessible name and tooltip of the button
    label: 'Fehlermeldung kopieren',
    // Announced to screen readers
    doneAnnouncement: 'Die Fehlermeldung wurde in die Zwischenablage kopiert.',
    failedAnnouncement: 'Die Fehlermeldung konnte nicht in die Zwischenablage kopiert werden. Du kannst den Text markieren und mit Strg+C kopieren.',
  },
  report: {
    details: 'Technische Details',
    code: 'Fehlercode',
  },
  boundary: {
    title: 'Ein unerwarteter Fehler ist aufgetreten',
    text: 'Bitte lade die Seite neu, um fortzufahren.',
    reload: 'Seite neu laden',
    // The development console keeps its English technical headings (React Render Exception, Stack Trace); only the button is translated
    devReload: 'Neu laden',
  },
} as const

export default errors
