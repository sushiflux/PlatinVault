namespace PlatinVault.Bridge;

/// <summary>
/// Every text the bridge shows or logs, in German and English (see <see cref="Msg"/> for why plain C#). Split by area over
/// several files: this one holds the command line and start-up; BridgeTexts.Install.cs the self-update, installer and uninstaller;
/// BridgeTexts.Ui.cs the windows and the tray. The texts of the code both bridges share (configuration, language, polling loops, talking to
/// the server, starting a game) are in <see cref="CoreTexts"/> in PlatinVault.Bridge.Core.
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
              --server <url>       Adresse des PlatinVault-Servers, z. B. http://192.168.1.20:5233
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
              PlatinVault.Bridge.exe --no-setup --server http://192.168.1.20:5233 --token <token> --save

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
              --server <url>       Address of the PlatinVault server, e.g. http://192.168.1.20:5233
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
              PlatinVault.Bridge.exe --no-setup --server http://192.168.1.20:5233 --token <token> --save

            Quit: tray icon > Exit (in a terminal also Ctrl+C).
            """);
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
}
