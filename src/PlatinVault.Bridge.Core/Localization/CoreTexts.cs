namespace PlatinVault.Bridge;

/// <summary>
/// The texts of the shared bridge code (configuration, language, the polling loops, talking to the server, starting a game), in German
/// and English (see <see cref="Msg"/> for why plain C#). Each host has its own catalogue next to this one for what only it knows (command
/// line help, installer, windows, systemd service). Rules: a parameterised text is a method (the compiler checks the arguments), a
/// fixed text is a field; the English line sits directly behind the German one; names of screens match the PlatinVault app's labels
/// (Settings &gt; PlatinVault Bridge). <c>--test-texts</c> of each host checks that every entry has both texts.
/// </summary>
internal static class CoreTexts
{
    private static Msg M(string de, string en) => new(de, en);

    // ---- Configuration -------------------------------------------------------------------------------------------------

    public static class Config
    {
        // linux: the Linux bridge has no --save and no exe-side file; it is configured once with "platinvault-bridge setup"
        public static Msg NoServer(string envVar, bool linux = false) => linux
            ? M(
                "Es ist keine Server-Adresse konfiguriert. Richte die Bridge einmalig ein: platinvault-bridge setup --server http://192.168.1.20:5233 --token <token> " +
                $"(oder setze die Umgebungsvariable {envVar}).",
                "No server address is configured. Set the bridge up once: platinvault-bridge setup --server http://192.168.1.20:5233 --token <token> " +
                $"(or set the environment variable {envVar}).")
            : M(
                "Es ist keine Server-Adresse konfiguriert. Gib sie mit --server <url> an (z. B. --server http://192.168.1.20:5233), " +
                $"über die Umgebungsvariable {envVar}, in einer bridge.json neben der Exe oder speichere sie einmalig mit --save.",
                "No server address is configured. Pass it with --server <url> (e.g. --server http://192.168.1.20:5233), " +
                $"through the environment variable {envVar}, in a bridge.json next to the exe, or save it once with --save.");

        public static Msg NoToken(string envVar, bool linux = false) => linux
            ? M(
                "Es ist kein Bridge-Token konfiguriert. Erzeuge ihn in PlatinVault unter Einstellungen > PlatinVault Bridge und richte die Bridge damit ein: " +
                $"platinvault-bridge setup --server <url> --token <token> (oder setze die Umgebungsvariable {envVar}).",
                "No bridge token is configured. Create it in PlatinVault under Settings > PlatinVault Bridge and set the bridge up with it: " +
                $"platinvault-bridge setup --server <url> --token <token> (or set the environment variable {envVar}).")
            : M(
                "Es ist kein Bridge-Token konfiguriert. Erzeuge ihn in PlatinVault unter Einstellungen > PlatinVault Bridge und gib ihn " +
                $"mit --token <token> an (oder über die Umgebungsvariable {envVar}, in einer bridge.json neben der Exe bzw. einmalig mit --save).",
                "No bridge token is configured. Create it in PlatinVault under Settings > PlatinVault Bridge and pass it with " +
                $"--token <token> (or through the environment variable {envVar}, in a bridge.json next to the exe, or once with --save).");

        public static readonly Msg TokenInvalidChars = M(
            "Der Bridge-Token enthält Leerzeichen oder Steuerzeichen und kann so nicht stimmen. Bitte den Token in PlatinVault neu kopieren.",
            "The bridge token contains spaces or control characters and cannot be right. Please copy the token from PlatinVault again.");

        public static Msg InvalidServer(string text) => M(
            $"Ungültige Server-Adresse '{text}'. Erwartet wird eine vollständige Adresse mit http:// oder https://, z. B. http://192.168.1.20:5233",
            $"Invalid server address '{text}'. A complete address with http:// or https:// is expected, e.g. http://192.168.1.20:5233");

        public static readonly Msg ServerHasCredentials = M(
            "Die Server-Adresse darf keine Zugangsdaten (Benutzer:Passwort@) enthalten. Der Bridge-Token wird separat mit --token übergeben.",
            "The server address must not contain credentials (user:password@). The bridge token is passed separately with --token.");

        public static readonly Msg ServerHasQuery = M(
            "Die Server-Adresse darf keine Parameter (?...) oder Anker (#...) enthalten.",
            "The server address must not contain parameters (?...) or anchors (#...).");

        public static Msg Unreadable(string path, string reason) => M(
            $"Die Konfiguration {path} konnte nicht gelesen werden und wird ignoriert: {reason}",
            $"The configuration {path} could not be read and is ignored: {reason}");

        public static Msg NotWritable(string path, string reason) => M(
            $"{path} ist nicht beschreibbar ({reason}); die Sprache wird stattdessen in der Benutzer-Konfiguration gemerkt.",
            $"{path} is not writable ({reason}); the language is remembered in the per-user configuration instead.");
    }

    // ---- Language ------------------------------------------------------------------------------------------------------

    public static class Language
    {
        public static Msg AdoptedFromServer(string language) => M(
            $"Sprache vom PlatinVault-Server übernommen: {(language == BridgeLang.English ? "Englisch" : "Deutsch")}.",
            $"Language taken from the PlatinVault server: {(language == BridgeLang.English ? "English" : "German")}.");

        public static Msg NotRemembered(string reason) => M(
            $"Die Sprache konnte nicht in der Konfiguration gemerkt werden: {reason}",
            $"The language could not be remembered in the configuration: {reason}");
    }

    // ---- Polling loop (what the log and the status window report) -------------------------------------------------------

    public static class Loop
    {
        public static Msg UnexpectedError(string text) => M(
            $"Unerwarteter Fehler in der Bridge-Schleife, mache weiter: {text}",
            $"Unexpected error in the bridge loop, carrying on: {text}");

        public static readonly Msg TokenInvalidLog = M(
            "Der Token ist ungültig – in PlatinVault unter Einstellungen > PlatinVault Bridge neu erzeugen. Die Bridge versucht es alle 30 Sekunden erneut.",
            "The token is invalid – create a new one in PlatinVault under Settings > PlatinVault Bridge. The bridge retries every 30 seconds.");

        public static readonly Msg TokenInvalidShort = M("Token ungültig", "Token invalid");

        public static Msg LibrarySent(int games) => M(
            $"Bibliotheksdaten an den Server gesendet: {games} Spiele (Veröffentlichungsdatum, Bewertungen, Größe auf der Platte).",
            $"Library data sent to the server: {games} games (release date, ratings, size on disk).");

        public static Msg LibraryNotAccepted(string? reason) => M(
            $"Der Server hat die Bibliotheksdaten nicht angenommen ({reason}) – neuer Versuch in 30 Minuten. Die Trophäen sind davon nicht betroffen.",
            $"The server did not accept the library data ({reason}) – trying again in 30 minutes. Trophies are not affected.");

        public static Msg ServerRejects(string? reason) => M(
            $"Der Server lehnt die Daten ab ({reason}). Die Bridge versucht es alle 30 Sekunden erneut.",
            $"The server rejects the data ({reason}). The bridge retries every 30 seconds.");

        public static Msg ServerUnreachable(string server, string? reason) => M(
            $"Server {server} nicht erreichbar ({reason}). Die Bridge versucht es automatisch weiter und meldet sich, sobald die Verbindung steht.",
            $"Server {server} is not reachable ({reason}). The bridge keeps trying automatically and will report as soon as the connection is up.");

        public static Msg Connected(string server, string tokenHint) => M(
            $"Verbunden mit {server} (Token {tokenHint}).",
            $"Connected to {server} (token {tokenHint}).");

        public static readonly Msg ConnectionRestored = M("Verbindung zum Server wiederhergestellt.", "Connection to the server restored.");
        public static readonly Msg TokenAccepted = M("Der Token wird jetzt akzeptiert.", "The token is accepted now.");
        public static readonly Msg ServerAcceptsAgain = M("Der Server nimmt die Daten wieder an.", "The server accepts the data again.");

        public static readonly Msg NoSteamAccount = M(
            "Kein aktives Steam-Konto gefunden – läuft Steam und bist du angemeldet?",
            "No active Steam account found – is Steam running and are you signed in?");

        public static Msg ActiveSteamAccount(string steamId) => M($"Aktives Steam-Konto: {steamId}", $"Active Steam account: {steamId}");

        public static readonly Msg WaitingForGame = M(
            "Kein Spiel läuft – die Bridge wartet auf einen Spielstart.",
            "No game is running – the bridge is waiting for a game to start.");

        public static readonly Msg GameEnded = M("Das Spiel wurde beendet.", "The game has ended.");

        public static Msg GameDetectedNoData(uint appId) => M(
            $"Spiel erkannt: AppID {appId} – keine lokalen Erfolgsdaten gefunden (Spiel ohne Erfolge oder Steam hat sie noch nicht geladen).",
            $"Game detected: AppID {appId} – no local achievement data found (game without achievements, or Steam has not loaded them yet).");

        public static Msg GameDetected(string name, uint appId, int done, int total) => M(
            $"Spiel erkannt: {name} (AppID {appId}) – {done} von {total} Erfolgen freigeschaltet.",
            $"Game detected: {name} (AppID {appId}) – {done} of {total} achievements unlocked.");

        public static readonly Msg HeartbeatSent = M("Heartbeat gesendet.", "Heartbeat sent.");

        public static readonly Msg StatsChangedNoNew = M(
            "Lokale Statistiken geändert, keine neuen Erfolge.",
            "Local statistics changed, no new achievements.");

        public static Msg AchievementUnlocked(string title, int count, int total) => M(
            $"Erfolg freigeschaltet: {title} ({count}/{total}) – an den Server gesendet.",
            $"Achievement unlocked: {title} ({count}/{total}) – sent to the server.");

        public static Msg MoreAchievements(int count) => M($"… und {count} weitere Erfolge.", $"… and {count} more achievements.");
    }

    // ---- Talking to the server -----------------------------------------------------------------------------------------

    public static class Network
    {
        public static Msg Redirect(string? target) => M(
            $"Der Server leitet um{(target is null ? string.Empty : " auf " + target)}. Bitte die endgültige Adresse (z. B. mit https://) als --server verwenden.",
            $"The server redirects{(target is null ? string.Empty : " to " + target)}. Please use the final address (e.g. with https://) as --server.");

        public static Msg Timeout(double seconds) => M($"Zeitüberschreitung nach {seconds:0} s", $"Timed out after {seconds:0} s");

        public static readonly Msg NameResolution = M("Servername nicht auflösbar", "Server name cannot be resolved");

        public static readonly Msg ConnectionError = M(
            "Verbindung nicht möglich (Server aus oder Adresse/Port falsch)",
            "Cannot connect (server off, or address/port wrong)");

        public static readonly Msg SecureConnectionError = M("TLS-/Zertifikatsfehler bei der Verbindung", "TLS/certificate error on the connection");
        public static readonly Msg ProxyError = M("Proxy-Fehler", "Proxy error");
        public static readonly Msg DownloadTooLarge = M("Die Datei ist unerwartet groß, Abbruch.", "The file is unexpectedly large, aborting.");

        public static Msg NetworkErrorDebug(string details) => M($"Netzwerkfehler: {details}", $"Network error: {details}");
        public static Msg DownloadNetworkErrorDebug(string details) => M($"Netzwerkfehler beim Download: {details}", $"Network error during the download: {details}");

        public static Msg ReplyUnreadableDebug(string reason) => M(
            $"Antwort des Servers nicht lesbar: {reason}",
            $"The server's reply is not readable: {reason}");
    }

    // ---- Starting a game for the server ---------------------------------------------------------------------------------

    // "Start this game in Steam" - the command channel to the server (see CommandPoller).
    public static class Launch
    {
        // Balloon on the gaming PC. The game name comes from the server (the title in the user's collection), already shortened.
        public static Msg Balloon(string name) => M($"Starte {name} …", $"Launching {name} …");

        public static Msg BalloonSteamMissing(string name) => M(
            $"{name} konnte nicht gestartet werden: Steam wurde auf diesem PC nicht gefunden.",
            $"{name} could not be launched: Steam was not found on this PC.");

        public static Msg BalloonFailed(string name) => M(
            $"{name} konnte nicht gestartet werden – Details stehen im Protokoll.",
            $"{name} could not be launched – see the log for details.");

        public static Msg Starting(string name, uint appId) => M(
            $"Spielstart auf Anforderung von PlatinVault: {name} (AppID {appId}).",
            $"Game launch requested by PlatinVault: {name} (AppID {appId}).");

        public static Msg StartedLog(string name, uint appId) => M(
            $"{name} (AppID {appId}): Der Start wurde an Steam übergeben.",
            $"{name} (AppID {appId}): The launch was handed to Steam.");

        public static Msg SteamMissingLog(string name, uint appId, string detail) => M(
            $"{name} (AppID {appId}) nicht gestartet: Steam wurde auf diesem PC nicht gefunden ({detail}).",
            $"{name} (AppID {appId}) not launched: Steam was not found on this PC ({detail}).");

        public static Msg FailedLog(string name, uint appId, string detail) => M(
            $"{name} (AppID {appId}) konnte nicht gestartet werden: {detail}",
            $"{name} (AppID {appId}) could not be launched: {detail}");

        public static Msg IgnoredCommand(string id, string type, string appId) => M(
            $"Befehl {id} ({type}, AppID {appId}) wird nicht ausgeführt: unbekannte Art oder ungültige AppID.",
            $"Command {id} ({type}, AppID {appId}) is not run: unknown kind or invalid AppID.");

        // Debug lines (--verbose): the channel is quiet in the normal log, the connection state is reported by the achievement loop
        public static Msg ChannelProblem(string reason) => M(
            $"Befehlskanal: {reason}. Die Bridge versucht es weiter.",
            $"Command channel: {reason}. The bridge keeps trying.");

        public static readonly Msg ChannelRestored = M("Befehlskanal wieder erreichbar.", "Command channel reachable again.");
    }
}
