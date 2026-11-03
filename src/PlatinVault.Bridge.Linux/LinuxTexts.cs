namespace PlatinVault.Bridge;

/// <summary>
/// Every text the Linux bridge prints or logs on top of the shared ones in <see cref="CoreTexts"/>, in German and English (see
/// <see cref="Msg"/> for why plain C#). Rules: a parameterised text is a method (the compiler checks the arguments), a fixed text is a
/// field; the English line sits directly behind the German one; names of screens match the PlatinVault app's labels
/// (Settings &gt; PlatinVault Bridge). <c>--test-texts</c> checks that every entry has both texts.
/// Not here: the lines only a test run writes, which stay English by design - nobody but a developer reads them.
/// </summary>
internal static class LinuxTexts
{
    private static Msg M(string de, string en) => new(de, en);

    // ---- Command line --------------------------------------------------------------------------------------------------

    public static class Cli
    {
        public static Msg UnknownCommand(string word) => M(
            $"Unbekannter Befehl: '{word}'. Mit --help siehst du alle Befehle.",
            $"Unknown command: '{word}'. Use --help to see all commands.");

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

        public static readonly Msg UsageHint = M("Hilfe: platinvault-bridge --help", "Help: platinvault-bridge --help");

        public static Msg Help(string version, string configPath, string logPath, string installDirectory) => M(
            $"""
            PlatinVault Bridge für Linux {version}
            Meldet die auf diesem Rechner freigeschalteten Steam-Erfolge in Echtzeit an deinen PlatinVault-Server
            (z. B. auf Unraid oder im Docker-Container) und startet Spiele auf Wunsch von PlatinVault. Liest dafür die
            lokalen Steam-Dateien, ganz ohne Steam-Web-API. Läuft ohne Fenster im Hintergrund (systemd-Benutzerdienst).

            Aufruf:
              platinvault-bridge setup --server <url> --token <token>   einmalig einrichten
              platinvault-bridge run                                    im Vordergrund laufen (so startet der Dienst die Bridge)
              platinvault-bridge status                                 zeigt, was die Bridge gerade tut
              platinvault-bridge --version | --help

            Optionen:
              --server <url>       Adresse des PlatinVault-Servers, z. B. http://192.168.1.20:5233
              --token <token>      Bridge-Token (in PlatinVault: Einstellungen > PlatinVault Bridge)
              --token-stdin        Token von der Standardeingabe lesen (steht dann nicht in der Prozessliste)
              --from <datei>       setup: Server, Token und Sprache aus einer bridge.json lesen (das Paket vom Server bringt eine mit)
              --no-check           setup: Verbindung nicht testen
              --steam-dir <ordner> Steam-Ordner von Hand angeben, falls die automatische Suche ihn nicht findet
                                   (auch über die Umgebungsvariable PLATINVAULT_STEAM_DIR oder STEAM_DIR)
              --language <de|en>   Sprache erzwingen (sonst die Sprache des PlatinVault-Servers, vor der ersten Verbindung die des Systems)
              --interval <ms>      Abfrageintervall in Millisekunden, {LinuxCliOptions.MinIntervalMs}-{LinuxCliOptions.MaxIntervalMs} (Standard {LinuxCliOptions.DefaultIntervalMs})
              --no-update          Selbstupdate vom Server abschalten
              --verbose            Ausführliche Ausgabe (Heartbeats, Fehlerdetails)

            Konfiguration, in dieser Reihenfolge:
              1. Argumente  2. Umgebungsvariablen {BridgeSettings.ServerEnvVar} / {BridgeSettings.TokenEnvVar}
              3. bridge.json neben dem Programm  4. {configPath}

            Protokoll: {logPath}   (im Dienst auch: journalctl --user -u {LinuxEnvironment.UnitName})
            Installationsordner: {installDirectory}
            """,
            $"""
            PlatinVault Bridge for Linux {version}
            Reports the Steam achievements unlocked on this machine to your PlatinVault server in real time
            (e.g. on Unraid or in a Docker container) and starts games when PlatinVault asks for it. It reads the
            local Steam files for this, without the Steam web API. Runs in the background without a window (systemd user service).

            Usage:
              platinvault-bridge setup --server <url> --token <token>   set up once
              platinvault-bridge run                                    run in the foreground (this is how the service starts the bridge)
              platinvault-bridge status                                 shows what the bridge is doing right now
              platinvault-bridge --version | --help

            Options:
              --server <url>       Address of the PlatinVault server, e.g. http://192.168.1.20:5233
              --token <token>      Bridge token (in PlatinVault: Settings > PlatinVault Bridge)
              --token-stdin        Read the token from standard input (so it does not show in the process list)
              --from <file>        setup: read server, token and language from a bridge.json (the package from the server contains one)
              --no-check           setup: do not test the connection
              --steam-dir <folder> Give the Steam folder by hand if the automatic search does not find it
                                   (also through the environment variable PLATINVAULT_STEAM_DIR or STEAM_DIR)
              --language <de|en>   Force the language (otherwise the language of the PlatinVault server, before the first connection the system's)
              --interval <ms>      Polling interval in milliseconds, {LinuxCliOptions.MinIntervalMs}-{LinuxCliOptions.MaxIntervalMs} (default {LinuxCliOptions.DefaultIntervalMs})
              --no-update          Switch off the self-update from the server
              --verbose            Verbose output (heartbeats, error details)

            Configuration, in this order:
              1. Arguments  2. Environment variables {BridgeSettings.ServerEnvVar} / {BridgeSettings.TokenEnvVar}
              3. bridge.json next to the program  4. {configPath}

            Log: {logPath}   (in the service also: journalctl --user -u {LinuxEnvironment.UnitName})
            Installation folder: {installDirectory}
            """);
    }

    // ---- Run -----------------------------------------------------------------------------------------------------------

    public static class Run
    {
        public static Msg Started(string version, string server, string tokenHint, int intervalMs, string logPath) => M(
            $"PlatinVault Bridge für Linux {version} gestartet – Server {server}, Token {tokenHint}, Intervall {intervalMs} ms, Protokoll {logPath}",
            $"PlatinVault Bridge for Linux {version} started – server {server}, token {tokenHint}, interval {intervalMs} ms, log {logPath}");

        public static Msg SteamFolder(string root, string how) => M(
            $"Steam-Ordner: {root} (gefunden über: {how}).",
            $"Steam folder: {root} (found through: {how}).");

        public static Msg RunningGameMethod(string method) => M(
            $"Spielerkennung über: {method}.",
            $"Game detection through: {method}.");

        public static readonly Msg SteamNotFound = M(
            "Kein Steam-Ordner gefunden – die Bridge sendet nur Heartbeats, bis Steam installiert ist. Einen Ordner von Hand geben: platinvault-bridge setup --steam-dir <ordner> (oder PLATINVAULT_STEAM_DIR).",
            "No Steam folder found – the bridge only sends heartbeats until Steam is installed. Give a folder by hand: platinvault-bridge setup --steam-dir <folder> (or PLATINVAULT_STEAM_DIR).");

        public static readonly Msg ExperimentalHint = M(
            "Hinweis: Die Erfolgserkennung unter Linux ist noch nicht an echten Geräten geprüft. Meldet sich nichts, hilft 'platinvault-bridge status --verbose' und das Protokoll.",
            "Note: achievement detection on Linux has not been verified on real devices yet. If nothing is reported, 'platinvault-bridge status --verbose' and the log help.");

        public static Msg TestMode(uint appId) => M(
            $"Testmodus: AppID {appId} wird behandelt, als würde das Spiel laufen.",
            $"Test mode: AppID {appId} is treated as if the game were running.");

        public static readonly Msg Stopped = M("Bridge beendet.", "Bridge stopped.");

        public static Msg NewBridgeStarted(string version, int? processId) => M(
            $"Neue Bridge {version} gestartet (Prozess {processId}), diese Instanz beendet sich.",
            $"New bridge {version} started (process {processId}), this instance is exiting.");

        public static Msg RestartFailed(string reason) => M(
            $"Die aktualisierte Bridge konnte nicht gestartet werden: {reason}. Bitte platinvault-bridge run manuell starten.",
            $"The updated bridge could not be started: {reason}. Please start platinvault-bridge run manually.");

        public static readonly Msg RestartedBySystemd = M(
            "Die Bridge beendet sich für den Neustart durch systemd.",
            "The bridge is exiting so that systemd restarts it.");

        public static readonly Msg AlreadyRunning = M(
            "Eine andere PlatinVault Bridge läuft für diesen Benutzer bereits (der Dienst oder ein anderes 'run'). Diese Instanz beendet sich. Den Dienst stoppen: systemctl --user stop platinvault-bridge",
            "Another PlatinVault Bridge is already running for this user (the service or another 'run'). This instance is exiting. Stop the service with: systemctl --user stop platinvault-bridge");

        public static Msg ConfigError(string message) => M($"Konfiguration: {message}", $"Configuration: {message}");
    }

    // ---- Setup ---------------------------------------------------------------------------------------------------------

    public static class Setup
    {
        public static readonly Msg StdinEmpty = M(
            "Mit --token-stdin muss der Token auf der Standardeingabe stehen (z. B. echo '<token>' | platinvault-bridge setup --server <url> --token-stdin).",
            "With --token-stdin the token must come on standard input (e.g. echo '<token>' | platinvault-bridge setup --server <url> --token-stdin).");

        public static Msg Saved(string path) => M(
            $"Gespeichert: {path}. Der Dienst (oder 'platinvault-bridge run') braucht keine Argumente mehr.",
            $"Saved: {path}. The service (or 'platinvault-bridge run') needs no arguments any more.");

        public static Msg SaveFailed(string path, string reason) => M(
            $"Die Konfiguration konnte nicht gespeichert werden ({path}): {reason}",
            $"The configuration could not be saved ({path}): {reason}");

        public static Msg FromFileUnreadable(string path) => M(
            $"Die Datei {path} fehlt oder enthält keine lesbare Konfiguration (Server und Token).",
            $"The file {path} is missing or holds no readable configuration (server and token).");

        public static readonly Msg Checking = M("Teste die Verbindung zum Server ...", "Testing the connection to the server ...");

        public static Msg CheckOk(string server, string account) => M(
            $"Verbunden mit {server} – Steam-Konto: {account}.",
            $"Connected to {server} – Steam account: {account}.");

        public static Msg CheckOkNoAccount(string server) => M(
            $"Verbunden mit {server}. Es wurde noch kein Steam-Konto erkannt (läuft Steam und bist du angemeldet?).",
            $"Connected to {server}. No Steam account was detected yet (is Steam running and are you signed in?).");

        public static Msg CheckFailed(string reason) => M(
            $"Die Verbindung hat nicht geklappt: {reason}. Die Konfiguration ist trotzdem gespeichert; die Bridge versucht es im Betrieb automatisch weiter.",
            $"The connection did not work: {reason}. The configuration is saved anyway; the bridge keeps trying automatically while it runs.");

        public static readonly Msg CheckTokenRejected = M(
            "Der Server lehnt den Token ab. Erzeuge in PlatinVault unter Einstellungen > PlatinVault Bridge einen neuen und richte die Bridge damit ein.",
            "The server rejects the token. Create a new one in PlatinVault under Settings > PlatinVault Bridge and set the bridge up with it.");

        public static Msg SteamDirInvalid(string path, string problem) => M(
            $"Der Steam-Ordner {path} wird nicht akzeptiert: {problem}",
            $"The Steam folder {path} is not accepted: {problem}");

        public static readonly Msg SteamDirNotAbsolute = M("er ist kein absoluter Pfad", "it is not an absolute path");
        public static readonly Msg SteamDirNotFound = M("der Ordner existiert nicht", "the folder does not exist");
        public static readonly Msg SteamDirNoSteamApps = M("er enthält keinen Ordner steamapps (eine Ebene zu hoch oder zu tief?)", "it contains no steamapps folder (one level too high or too low?)");
        public static readonly Msg SteamDirLibraryOnly = M("das ist eine Spielebibliothek, nicht der Steam-Ordner: steamapps ist da, aber weder appcache noch config (gemeint ist der Ordner, in dem Steam selbst installiert ist)", "this is a game library, not the Steam folder: steamapps is there, but neither appcache nor config (enter the folder Steam itself is installed in)");

        public static readonly Msg ServiceHint = M(
            "Weiter: ./install.sh (aus dem Paket) richtet den Hintergrunddienst ein, oder zum Ausprobieren: platinvault-bridge run",
            "Next: ./install.sh (from the package) sets up the background service, or to try it out: platinvault-bridge run");
    }

    // ---- Status --------------------------------------------------------------------------------------------------------

    public static class Status
    {
        public static Msg Title(string version) => M($"PlatinVault Bridge für Linux {version}", $"PlatinVault Bridge for Linux {version}");

        public static Msg Config(string path, bool present) => present
            ? M($"Konfiguration: {path}", $"Configuration: {path}")
            : M($"Konfiguration: fehlt ({path}) – richte die Bridge ein: platinvault-bridge setup --server <url> --token <token>",
                $"Configuration: missing ({path}) – set the bridge up: platinvault-bridge setup --server <url> --token <token>");

        public static Msg Server(string server, string tokenHint) => M($"Server: {server} (Token {tokenHint})", $"Server: {server} (token {tokenHint})");

        public static Msg Service(string state) => M($"Dienst (systemd --user): {state}", $"Service (systemd --user): {state}");

        public static readonly Msg ServiceUnknown = M("unbekannt (systemctl nicht erreichbar)", "unknown (systemctl not reachable)");

        public static Msg Running(int pid, int seconds) => M(
            $"Bridge läuft (Prozess {pid}), letzte Meldung vor {seconds} s.",
            $"Bridge is running (process {pid}), last update {seconds} s ago.");

        public static readonly Msg NotRunning = M(
            "Bridge läuft gerade nicht (Starten: systemctl --user start platinvault-bridge, oder platinvault-bridge run).",
            "The bridge is not running right now (start it: systemctl --user start platinvault-bridge, or platinvault-bridge run).");

        public static Msg Connection(string state, string? detail) => detail is null
            ? M($"Verbindung: {state}", $"Connection: {state}")
            : M($"Verbindung: {state} – {detail}", $"Connection: {state} – {detail}");

        public static readonly Msg ConnOnline = M("verbunden", "connected");
        public static readonly Msg ConnOffline = M("Server nicht erreichbar", "server not reachable");
        public static readonly Msg ConnUnauthorized = M("Token ungültig", "token invalid");
        public static readonly Msg ConnRejected = M("Server lehnt die Daten ab", "server rejects the data");
        public static readonly Msg ConnStarting = M("startet", "starting");

        public static Msg Game(string name, int achieved, int total) => M(
            $"Spiel: {name} ({achieved}/{total} Erfolge)",
            $"Game: {name} ({achieved}/{total} achievements)");

        public static readonly Msg NoGame = M("Spiel: keins läuft", "Game: none is running");

        public static Msg LastUnlock(string name, string at) => M($"Letzter Erfolg: {name} ({at})", $"Last achievement: {name} ({at})");

        public static Msg SteamAccount(string steamId) => M($"Steam-Konto: {steamId}", $"Steam account: {steamId}");

        public static Msg SteamFolder(string root, string how) => M($"Steam-Ordner: {root} ({how})", $"Steam folder: {root} ({how})");

        public static readonly Msg SteamMissing = M(
            "Steam-Ordner: nicht gefunden (platinvault-bridge setup --steam-dir <ordner>, oder PLATINVAULT_STEAM_DIR)",
            "Steam folder: not found (platinvault-bridge setup --steam-dir <folder>, or PLATINVAULT_STEAM_DIR)");

        public static Msg Candidate(string path, Msg detail) => M($"  geprüft: {path} – {detail.De}", $"  checked: {path} – {detail.En}");

        public static readonly Msg CandidateMissing = M("existiert nicht", "does not exist");
        public static readonly Msg CandidateNoSteamApps = M("kein Ordner steamapps darin", "no steamapps folder inside");
        public static readonly Msg CandidateLibraryOnly = M("nur eine Spielebibliothek (weder appcache noch config)", "only a game library (neither appcache nor config)");
        public static readonly Msg CandidateDuplicate = M("derselbe Ordner wie ein früherer Treffer", "same folder as an earlier candidate");
        public static readonly Msg CandidateUsable = M("brauchbar", "usable");

        public static Msg Log(string path) => M($"Protokoll: {path}", $"Log: {path}");
    }

    // ---- Self-update ---------------------------------------------------------------------------------------------------

    public static class Updater
    {
        public static Msg LeftoverNotDeletedDebug(string name, string reason) => M(
            $"{name} konnte noch nicht gelöscht werden: {reason}",
            $"{name} could not be deleted yet: {reason}");

        public static Msg VersionNotParsableDebug(string text) => M(
            $"Die vom Server genannte Bridge-Version '{text}' ist keine Versionsnummer, ignoriert.",
            $"The bridge version '{text}' named by the server is not a version number, ignored.");

        public static Msg ServerOffersOlder(Version latest, Version? current) => M(
            $"Der Server bietet Version {latest} an, die gerade installierte Datei meldet aber Version {current}. " +
            "Kein erneutes Update (der Server liefert vermutlich eine veraltete Datei).",
            $"The server offers version {latest}, but the file that was just installed reports version {current}. " +
            "No further update (the server probably serves an outdated file).");

        public static Msg OnlyForPublishedBinary(Version latest, string name) => M(
            $"Neue Bridge-Version {latest} verfügbar, aber das Selbstupdate gibt es nur für die veröffentlichte Datei {name}.",
            $"New bridge version {latest} available, but the self-update only exists for the published file {name}.");

        public static Msg Updating(Version latest) => M(
            $"Neue Bridge-Version {latest} gefunden – aktualisiere ...",
            $"New bridge version {latest} found – updating ...");

        public static readonly Msg NoChecksum = M(
            "Der Server nennt keine Prüfsumme für die neue Bridge – ohne Prüfung wird nicht aktualisiert.",
            "The server gives no checksum for the new bridge – it is not updated without a check.");

        public static readonly Msg Downloading = M("Lade die neue Bridge vom Server ...", "Downloading the new bridge from the server ...");

        public static Msg DownloadFailed(string? error, double minutes) => M(
            $"Download der neuen Bridge fehlgeschlagen: {error}. Neuer Versuch in {minutes:0} Minuten.",
            $"Downloading the new bridge failed: {error}. Next attempt in {minutes:0} minutes.");

        public static Msg ChecksumMismatch(string expected, string received, Version latest, double hours) => M(
            $"Prüfsumme der heruntergeladenen Bridge stimmt nicht (erwartet {expected}, erhalten {received}). " +
            $"Die Datei wird verworfen, Version {latest} wird {hours:0} Std. lang nicht erneut versucht.",
            $"The checksum of the downloaded bridge does not match (expected {expected}, received {received}). " +
            $"The file is discarded, version {latest} is not tried again for {hours:0} h.");

        public static Msg DownloadVerified(string megabytes) => M(
            $"Download geprüft ({megabytes} MB, SHA-256 stimmt) – tausche die Datei aus ...",
            $"Download verified ({megabytes} MB, SHA-256 matches) – swapping the file ...");

        public static Msg FolderNotWritable(string? folder, string reason, Version latest) => M(
            $"Der Ordner {folder} ist nicht beschreibbar ({reason}). Das Selbstupdate wird übersprungen; " +
            $"Version {latest} ist verfügbar. Installiere die Bridge mit install.sh neu, um Updates zu erhalten.",
            $"The folder {folder} is not writable ({reason}). The self-update is skipped; " +
            $"version {latest} is available. Install the bridge again with install.sh to receive updates.");

        public static Msg UpdateFailed(Version latest, string reason, double minutes) => M(
            $"Das Update auf Version {latest} ist fehlgeschlagen: {reason}. Neuer Versuch in {minutes:0} Minuten.",
            $"The update to version {latest} failed: {reason}. Next attempt in {minutes:0} minutes.");

        public static Msg Installed(Version latest) => M(
            $"Bridge {latest} installiert – starte neu ...",
            $"Bridge {latest} installed – restarting ...");
    }

    // ---- Launching a game ----------------------------------------------------------------------------------------------

    public static class Launcher
    {
        public static readonly Msg NoSteamCommand = M(
            "Weder der Befehl 'steam' noch Flatpak-Steam noch 'xdg-open' wurde gefunden.",
            "Neither the 'steam' command nor Flatpak Steam nor 'xdg-open' was found.");

        public static Msg StartedWith(string tool) => M($"Start übergeben an: {tool}", $"Launch handed to: {tool}");

        public static Msg AllFailed(string details) => M($"Der Start ist fehlgeschlagen: {details}", $"The launch failed: {details}");
    }
}
