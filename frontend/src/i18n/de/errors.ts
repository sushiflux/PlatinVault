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
  notFound: {
    title: 'Seite nicht gefunden',
    text: 'Die Seite, die du suchst, gibt es nicht oder sie ist umgezogen. Es wurde nichts verändert.',
    home: 'Zur Übersicht',
  },
  // Full-page status screens (components/errors/StatusPage.tsx). They are always shown in English; the German texts keep the keys in step.
  status: {
    copy: 'Details kopieren',
    home: 'Zur Übersicht',
    retry: 'Erneut versuchen',
    errorLine: 'Fehler {{code}}',
    401: { title: 'Anmeldung nötig', text: 'Für diese Seite fehlt die Berechtigung. Es wurde nichts verändert.' },
    403: { title: 'Zugriff verweigert', text: 'Dieser Zugriff ist nicht erlaubt. Es wurde nichts verändert.' },
    404: { title: 'Seite nicht gefunden', text: 'Die Seite, die du suchst, gibt es nicht oder sie ist umgezogen. Es wurde nichts verändert.' },
    429: { title: 'Zu viele Anfragen', text: 'Es kamen zu viele Anfragen in kurzer Zeit. Bitte warte einen Moment und versuche es dann noch einmal.' },
    500: { title: 'Serverfehler', text: 'Auf dem Server ist etwas schiefgelaufen. Deine Daten wurden dabei nicht verändert. Bitte versuche es gleich noch einmal.' },
    503: { title: 'Server nicht verfügbar', text: 'Der Server ist gerade nicht erreichbar oder startet noch. Bitte versuche es gleich noch einmal.' },
    offline: { title: 'Keine Verbindung', text: 'Der Browser hat keine Antwort vom PlatinVault-Server bekommen. Bitte prüfe, ob PlatinVault läuft und das Netzwerk in Ordnung ist.' },
  },
  boundary: {
    title: 'Ein unerwarteter Fehler ist aufgetreten',
    text: 'Bitte lade die Seite neu, um fortzufahren.',
    reload: 'Seite neu laden',
    details: 'Technische Details',
    copy: 'Details kopieren',
    // The development console keeps its English technical headings (React Render Exception, Stack Trace); only the button is translated
    devReload: 'Neu laden',
  },
} as const

export default errors
