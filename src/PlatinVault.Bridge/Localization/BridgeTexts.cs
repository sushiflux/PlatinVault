namespace PlatinVault.Bridge;

/// <summary>
/// Every text the bridge shows or logs, in German and English (see <see cref="Msg"/> for why plain C#). Split by area over
/// several files: this one holds command line, configuration, language, start-up and the polling loop; BridgeTexts.Install.cs the
/// self-update, installer and uninstaller; BridgeTexts.Ui.cs the windows and the tray.
/// Rules: a parameterised text is a method (the compiler checks the arguments), a fixed text is a field; the English line sits
/// directly behind the German one; names of screens match the PlatinVault app's labels (Settings &gt; PlatinVault Bridge).
/// Not here: the lines only a test run writes (<c>Testlauf</c>), which stay German by design - nobody but a developer reads them.
/// <c>--test-texts</c> checks that every entry has both texts.
/// </summary>
internal static partial class BridgeTexts
{
    private static Msg M(string de, string en) => new(de, en);

    // ---- Command line --------------------------------------------------------------------------------------------------

    public static class Cli
    {
        public static Msg UnknownOption(string arg) => M(
            $"Unbekannte Option: '{arg}'. Mit --help siehst du alle Optionen.",
            $"Unknown option: '{arg}'. Use --help to see all options.");

        public static Msg MissingValue(string name) => M(
            $"Für {name} fehlt der Wert. Beispiel: {name} <Wert>",
            $"{name} needs a value. Example: {name} <value>");

        public static Msg InvalidInterval(string raw, int min, int max) => M(
            $"Ungültiger Wert für --interval: '{raw}'. Erlaubt sind ganze Millisekunden von {min} bis {max}.",
            $"Invalid value for --interval: '{raw}'. Allowed are whole milliseconds from {min} to {max}.");

        public static Msg InvalidApp(string raw) => M(
            $"Ungültiger Wert für --app: '{raw}'. Erwartet wird eine Steam-AppID (positive ganze Zahl).",
            $"Invalid value for --app: '{raw}'. A Steam AppID (positive whole number) is expected.");

        public static Msg InvalidLanguage(string raw) => M(
            $"Ungültiger Wert für --language: '{raw}'. Erlaubt sind 'de' und 'en'.",
            $"Invalid value for --language: '{raw}'. Allowed are 'de' and 'en'.");

        public static readonly Msg UsageHint = M("Hilfe: PlatinVault.Bridge.exe --help", "Help: PlatinVault.Bridge.exe --help");

        public static Msg Help(string version, BridgeEnvironment env) => M(
            $"""
            PlatinVault Bridge {version}
            Meldet die auf diesem PC freigeschalteten Steam-Erfolge in Echtzeit an deinen PlatinVault-Server
            (z. B. auf Unraid). Liest dafür die lokalen Steam-Statistikdateien, ganz ohne Steam-Web-API.
            Läuft mit Tray-Symbol im Hintergrund, ohne installiertes .NET, und aktualisiert sich selbst vom Server.

            Normaler Start: PlatinVault.Bridge.exe doppelklicken. Beim ersten Start öffnet sich das Einrichtungsfenster
            (Installieren, Autostart, Startmenü); danach läuft die Bridge als Tray-Symbol.

            Aufruf:
              PlatinVault.Bridge.exe [--server <url>] [--token <token>] [--save] [--interval <ms>] [--verbose]

            Optionen:
              --server <url>       Adresse des PlatinVault-Servers, z. B. http://192.168.178.10:5233
              --token <token>      Bridge-Token (in PlatinVault: Einstellungen > PlatinVault Bridge > Token erzeugen)
              --save               Server, Token und Sprache speichern ({env.UserConfigPath}
                                   bzw. eine vorhandene bridge.json neben der Exe); spätere Starts brauchen keine Argumente
              --interval <ms>      Abfrageintervall in Millisekunden, {CliOptions.MinIntervalMs}-{CliOptions.MaxIntervalMs} (Standard {CliOptions.DefaultIntervalMs})
              --language <de|en>   Sprache der Bridge erzwingen (sonst die Sprache des PlatinVault-Servers, vor der
                                   ersten Verbindung die Windows-Sprache)
              --no-setup           Ohne Einrichtungsfenster von hier aus laufen (kein Installieren)
              --no-update          Selbstupdate vom Server abschalten
              --uninstall          Bridge deinstallieren (so ruft Windows unter „Apps“ die Deinstallation auf)
              --verbose            Ausführliche Ausgabe (Heartbeats, Fehlerdetails)
              --app <appid>        Nur zum Testen: so tun, als liefe dieses Spiel (liest dessen lokale Erfolge)
              --version            Version anzeigen
              --help               Diese Hilfe

            Konfiguration, in dieser Reihenfolge:
              1. Argumente
              2. Umgebungsvariablen {BridgeSettings.ServerEnvVar} / {BridgeSettings.TokenEnvVar}
              3. bridge.json neben der Exe
              4. {env.UserConfigPath}

            Protokoll: {env.LogPath}
            Installationsordner: {env.InstallRoot}

            Beispiel:
              PlatinVault.Bridge.exe --no-setup --server http://192.168.178.10:5233 --token <token> --save

            Beenden: Tray-Symbol > Beenden (im Terminal auch Strg+C).
            """,
            $"""
            PlatinVault Bridge {version}
            Reports the Steam achievements unlocked on this PC to your PlatinVault server in real time
            (e.g. on Unraid). It reads the local Steam statistics files for this, without the Steam web API.
            Runs in the background with a tray icon, needs no installed .NET, and updates itself from the server.

            Normal start: double-click PlatinVault.Bridge.exe. On the first start the setup window opens
            (install, autostart, Start menu); after that the bridge runs as a tray icon.

            Usage:
              PlatinVault.Bridge.exe [--server <url>] [--token <token>] [--save] [--interval <ms>] [--verbose]

            Options:
              --server <url>       Address of the PlatinVault server, e.g. http://192.168.178.10:5233
              --token <token>      Bridge token (in PlatinVault: Settings > PlatinVault Bridge > Create token)
              --save               Save server, token and language ({env.UserConfigPath}
                                   or an existing bridge.json next to the exe); later starts need no arguments
              --interval <ms>      Polling interval in milliseconds, {CliOptions.MinIntervalMs}-{CliOptions.MaxIntervalMs} (default {CliOptions.DefaultIntervalMs})
              --language <de|en>   Force the language of the bridge (otherwise the language of the PlatinVault server,
                                   before the first connection the Windows language)
              --no-setup           Run from here without the setup window (no installing)
              --no-update          Switch off the self-update from the server
              --uninstall          Uninstall the bridge (this is how Windows calls the uninstall under "Apps")
              --verbose            Verbose output (heartbeats, error details)
              --app <appid>        For testing only: pretend this game is running (reads its local achievements)
              --version            Show the version
              --help               This help

            Configuration, in this order:
              1. Arguments
              2. Environment variables {BridgeSettings.ServerEnvVar} / {BridgeSettings.TokenEnvVar}
              3. bridge.json next to the exe
              4. {env.UserConfigPath}

            Log: {env.LogPath}
            Installation folder: {env.InstallRoot}

            Example:
              PlatinVault.Bridge.exe --no-setup --server http://192.168.178.10:5233 --token <token> --save

            Quit: tray icon > Exit (in a terminal also Ctrl+C).
            """);
    }

    // ---- Configuration -------------------------------------------------------------------------------------------------

    public static class Config
    {
        public static Msg NoServer(string envVar) => M(
            "Es ist keine Server-Adresse konfiguriert. Gib sie mit --server <url> an (z. B. --server http://192.168.178.10:5233), " +
            $"über die Umgebungsvariable {envVar}, in einer bridge.json neben der Exe oder speichere sie einmalig mit --save.",
            "No server address is configured. Pass it with --server <url> (e.g. --server http://192.168.178.10:5233), " +
            $"through the environment variable {envVar}, in a bridge.json next to the exe, or save it once with --save.");

        public static Msg NoToken(string envVar) => M(
            "Es ist kein Bridge-Token konfiguriert. Erzeuge ihn in PlatinVault unter Einstellungen > PlatinVault Bridge und gib ihn " +
            $"mit --token <token> an (oder über die Umgebungsvariable {envVar}, in einer bridge.json neben der Exe bzw. einmalig mit --save).",
            "No bridge token is configured. Create it in PlatinVault under Settings > PlatinVault Bridge and pass it with " +
            $"--token <token> (or through the environment variable {envVar}, in a bridge.json next to the exe, or once with --save).");

        public static readonly Msg TokenInvalidChars = M(
            "Der Bridge-Token enthält Leerzeichen oder Steuerzeichen und kann so nicht stimmen. Bitte den Token in PlatinVault neu kopieren.",
            "The bridge token contains spaces or control characters and cannot be right. Please copy the token from PlatinVault again.");

        public static Msg InvalidServer(string text) => M(
            $"Ungültige Server-Adresse '{text}'. Erwartet wird eine vollständige Adresse mit http:// oder https://, z. B. http://192.168.178.10:5233",
            $"Invalid server address '{text}'. A complete address with http:// or https:// is expected, e.g. http://192.168.178.10:5233");

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

    // ---- Start-up and shutdown -----------------------------------------------------------------------------------------

    public static class Program
    {
        public static Msg Started(string product, string version, string server, string tokenHint, int intervalMs, string logPath) => M(
            $"{product} {version} gestartet – Server {server}, Token {tokenHint}, Intervall {intervalMs} ms, Protokoll {logPath}",
            $"{product} {version} started – server {server}, token {tokenHint}, interval {intervalMs} ms, log {logPath}");

        public static Msg ConfigSaved(string path) => M(
            $"Konfiguration gespeichert: {path} (spätere Starts brauchen keine Argumente).",
            $"Configuration saved: {path} (later starts need no arguments).");

        public static Msg ConfigSaveFailed(string path, string reason) => M(
            $"Die Konfiguration konnte nicht gespeichert werden ({path}): {reason}",
            $"The configuration could not be saved ({path}): {reason}");

        public static Msg TestMode(uint appId) => M(
            $"Testmodus: AppID {appId} wird behandelt, als würde das Spiel laufen.",
            $"Test mode: AppID {appId} is treated as if the game were running.");

        public static readonly Msg SteamNotFound = M(
            "Steam wurde in der Registry nicht gefunden – die Bridge sendet nur Heartbeats, bis Steam läuft.",
            "Steam was not found in the registry – the bridge only sends heartbeats until Steam is running.");

        public static readonly Msg Stopped = M("Bridge beendet.", "Bridge stopped.");

        public static Msg NewBridgeStarted(string version, int? processId) => M(
            $"Neue Bridge {version} gestartet (Prozess {processId}), diese Instanz beendet sich.",
            $"New bridge {version} started (process {processId}), this instance is exiting.");

        public static Msg RestartFailed(string reason, string exeName) => M(
            $"Die aktualisierte Bridge konnte nicht gestartet werden: {reason}. Bitte {exeName} manuell starten.",
            $"The updated bridge could not be started: {reason}. Please start {exeName} manually.");

        public static readonly Msg AlreadyRunningOld = M(
            "Die PlatinVault Bridge läuft bereits (ältere Version ohne Fenster).",
            "The PlatinVault Bridge is already running (older version without a window).");

        public static readonly Msg AskReplaceOld = M(
            "Die PlatinVault Bridge läuft bereits im Hintergrund, aber in einer älteren Version ohne Fenster.\n\n" +
            "Soll sie beendet und diese neue Version gestartet werden?",
            "The PlatinVault Bridge is already running in the background, but as an older version without a window.\n\n" +
            "Should it be ended and this new version started?");

        public static Msg ProcessNotEnded(int processId, string reason) => M(
            $"Prozess {processId} konnte nicht beendet werden: {reason}",
            $"Process {processId} could not be ended: {reason}");

        public static readonly Msg OldWontQuit = M(
            "Die ältere Bridge ließ sich nicht beenden. Bitte im Task-Manager „PlatinVault.Bridge.exe“ beenden.",
            "The older bridge could not be ended. Please end “PlatinVault.Bridge.exe” in the Task Manager.");
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
}
