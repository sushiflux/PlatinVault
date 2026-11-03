using PlatinVault.Windows.Localization;

namespace PlatinVault.Desktop;

/// <summary>Every text of the Windows program that a user can see, German and English side by side.</summary>
internal static class DesktopTexts
{
    public static class Database
    {
        public static readonly Msg StartFailed = new(
            "Die Datenbank ließ sich nicht öffnen.",
            "The database could not be opened.");
    }

    public static class Window
    {
        public static readonly Msg Starting = new("PlatinVault startet …", "PlatinVault is starting …");
        public static readonly Msg StartingDatabase = new("Datenbank wird geöffnet …", "Opening the database …");
        public static readonly Msg StartingServer = new("Oberfläche wird geladen …", "Loading the interface …");
        public static readonly Msg FailedTitle = new("PlatinVault konnte nicht starten", "PlatinVault could not start");
        public static Msg FailedLogHint(string folder) => new(
            $"Protokolle: {folder}",
            $"Logs: {folder}");
        public static readonly Msg OpenLogs = new("Protokolle öffnen", "Open logs");
        public static readonly Msg Quit = new("Beenden", "Quit");

        public static readonly Msg WebViewMissing = new(
            "Für das Programmfenster fehlt die „Microsoft Edge WebView2 Runtime“. Installiere sie von https://developer.microsoft.com/microsoft-edge/webview2/ (Evergreen) oder führe die PlatinVault-Installation erneut aus.",
            "The program window needs the “Microsoft Edge WebView2 Runtime”, which is missing. Install it from https://developer.microsoft.com/microsoft-edge/webview2/ (Evergreen) or run the PlatinVault setup again.");
    }

    public static class Prepare
    {
        public static readonly Msg Cluster = new("Datenbank wird vorbereitet …", "Preparing the database …");
        public static readonly Msg Schema = new("Tabellen werden angelegt …", "Creating the tables …");
        public static readonly Msg Backup = new("Erste Sicherung wird angelegt …", "Making the first backup …");
        public static readonly Msg Done = new("Datenbank ist bereit.", "The database is ready.");
    }

    public static class Tray
    {
        public static readonly Msg Tooltip = new("PlatinVault – läuft", "PlatinVault – running");
        public static readonly Msg Open = new("PlatinVault öffnen", "Open PlatinVault");
        public static readonly Msg OpenGuide = new("Guide zum laufenden Spiel", "Guide for the running game");
        public static readonly Msg Overlay = new("Trophäen-Overlay über dem Spiel", "Trophy overlay over the game");
        public static readonly Msg OverlayTest = new("Overlay testen", "Test the overlay");
        public static readonly Msg Notifications = new("Windows-Benachrichtigungen", "Windows notifications");
        public static readonly Msg Autostart = new("Mit Windows starten", "Start with Windows");
        public static readonly Msg Handbook = new("Handbuch öffnen", "Open handbook");
        public static Msg FolderNotOpened(string path, string reason) => new(
            $"Der Ordner „{path}“ konnte nicht geöffnet werden: {reason}",
            $"The folder “{path}” could not be opened: {reason}");
        public static readonly Msg ProgramFolder = new("Programmordner öffnen", "Open program folder");
        public static readonly Msg DataFolder = new("Datenordner öffnen", "Open data folder");
        public static readonly Msg Backups = new("Sicherungsordner öffnen", "Open backup folder");
        public static readonly Msg Logs = new("Protokolle öffnen", "Open logs");
        public static Msg Update(string version) => new($"Update auf {version} installieren …", $"Install update {version} …");
        public static readonly Msg CheckUpdates = new("Nach Updates suchen", "Check for updates");
        public static readonly Msg Quit = new("Beenden", "Quit");

        public static readonly Msg StillRunningTitle = new("PlatinVault läuft weiter", "PlatinVault keeps running");
        public static readonly Msg StillRunningText = new(
            "Live-Trophäen bleiben aktiv. Öffnen und Beenden über das Symbol neben der Uhr.",
            "Live trophies stay active. Open and quit from the icon next to the clock.");

        public static Msg HotkeyBusy(string keys) => new(
            $"Die Tastenkombination {keys} ist schon von einem anderen Programm belegt. Den Guide öffnest du über das Symbol neben der Uhr.",
            $"The shortcut {keys} is already used by another program. Open the guide from the icon next to the clock.");

        public static Msg HotkeyReady(string keys) => new(
            $"Mit {keys} öffnest du den Guide zum laufenden Spiel über dem Spiel.",
            $"Press {keys} to open the guide for the running game over the game.");
    }

    /// <summary>Texts around the in-game overlay; the popup itself is the app's own web page (/overlay) and speaks the app's language.</summary>
    public static class Overlay
    {
        public static readonly Msg Platinum = new("Platin!", "Platinum!");
        public static Msg PlatinumText(string game, int total) => new(
            $"Alle {total} Erfolge in {game} – 100 %!",
            $"All {total} achievements in {game} – 100%!");
        public static readonly Msg TestName = new("Test-Trophäe", "Test trophy");
        public static readonly Msg TestDescription = new(
            "So erscheint eine freigeschaltete Trophäe über deinem Spiel.",
            "This is how an unlocked trophy appears over your game.");
        public static readonly Msg TestGame = new("PlatinVault", "PlatinVault");
        public static readonly Msg Unavailable = new(
            "Das Overlay über dem Spiel ist gerade nicht verfügbar (die Overlay-Seite konnte nicht geladen werden). Trophäen-Popups erscheinen bis dahin in der App. Einzelheiten stehen im Protokoll (Menü \"Protokolle öffnen\").",
            "The overlay over the game is not available right now (its page could not be loaded). Until then trophy popups appear in the app. Details are in the log (menu \"Open logs\").");
        public static Msg SwitchFailed(string detail) => new(
            $"Die Einstellung \"Trophäen-Popup anzeigen\" konnte nicht gespeichert werden: {detail}",
            $"The setting \"Show trophy popup\" could not be saved: {detail}");
    }

    /// <summary>Development run (start-windows.ps1): the ports are fixed by the script, never replaced silently.</summary>
    public static class Dev
    {
        public static Msg PortBusy(Msg what, int port) => new(
            $"Der feste Port {port} ({what.De}) ist belegt - ein anderes Programm nutzt ihn. PlatinVault weicht in der Entwicklung nicht auf einen anderen Port aus: das belegende Programm beenden (netstat -ano | findstr :{port}) und neu starten.",
            $"The fixed port {port} ({what.En}) is in use by another program. In development PlatinVault does not switch to another port: stop the program using it (netstat -ano | findstr :{port}) and start again.");

        public static readonly Msg SharedNoBackupHere = new(
            "Im gemeinsamen Modus gehört die Datenbank dem Browser-Stack (.data\\dev\\platinvault.db); beide Server sichern sie selbst nach .backups\\auto, wiederherstellen mit .\\scripts\\restore-db.ps1. Dieser Befehl gilt nur für die eigene Datenbank (-OwnDb).",
            "In shared mode the database belongs to the browser stack (.data\\dev\\platinvault.db); both servers back it up themselves to .backups\\auto, restore it with .\\scripts\\restore-db.ps1. This command only applies to the program's own database (-OwnDb).");

        public static readonly Msg ServerPort = new("Server", "server");
        public const string WindowTitle = "PlatinVault (Entwicklung)";
        public const string TrayTooltip = "PlatinVault (Entwicklung) – läuft";

        /// <summary>The small gold badge next to "PlatinVault" in the title bar of a development run.</summary>
        public static readonly Msg Badge = new("Entwicklung", "Development");
    }

    public static class Update
    {
        public static Msg Available(string version) => new(
            $"PlatinVault {version} ist verfügbar.",
            $"PlatinVault {version} is available.");

        public static Msg Confirm(string current, string next) => new(
            $"PlatinVault {next} herunterladen und installieren? Du hast {current}. PlatinVault wird dafür beendet (mit Sicherung der Datenbank) und danach neu gestartet.",
            $"Download and install PlatinVault {next}? You have {current}. PlatinVault closes for this (after backing up the database) and starts again afterwards.");

        public static readonly Msg Install = new("Update installieren", "Install update");
        public static readonly Msg Later = new("Später", "Later");

        public static Msg Failed(string reason) => new(
            $"Das Update konnte nicht geladen werden: {reason}",
            $"The update could not be downloaded: {reason}");

        public static Msg UpToDate(string version, bool betas) => new(
            betas
                ? $"PlatinVault {version} ist aktuell – es gibt keine neuere Beta und kein neueres Release."
                : $"PlatinVault {version} ist aktuell – es gibt kein neueres Release. (Betas bekommst du, wenn du in den Einstellungen unter System → Updates „Beta-Versionen erhalten“ einschaltest.)",
            betas
                ? $"PlatinVault {version} is up to date – there is no newer beta and no newer release."
                : $"PlatinVault {version} is up to date – there is no newer release. (To get betas, switch on “Receive beta versions” in Settings under System → Updates.)");

        public static Msg CheckFailed(string reason) => new(
            $"Die Update-Prüfung hat nicht geklappt: {reason}",
            $"The update check did not work: {reason}");

        public static readonly Msg NotChecked = new(
            "Die Update-Prüfung hat noch kein Ergebnis. Versuche es in einer Minute erneut.",
            "The update check has no result yet. Please try again in a minute.");

        public static Msg SignatureRejected(string reason) => new(
            $"Die digitale Signatur des heruntergeladenen Installers ist nicht in Ordnung: Sie fehlt, ist ungültig oder stammt nicht vom selben Herausgeber wie dein installiertes PlatinVault oder gehört nicht zu PlatinVault. Der Installer wurde gelöscht und nicht gestartet, es wurde nichts installiert. Versuche es später erneut oder lade den Installer von Hand von der offiziellen Release-Seite. Technische Einzelheiten: {reason}",
            $"The digital signature of the downloaded installer is not acceptable: it is missing, invalid, from a different publisher than your installed PlatinVault or not a PlatinVault installer. The installer was deleted and not started, nothing was installed. Try again later or download the installer by hand from the official release page. Technical details: {reason}");

        public static Msg ProgramSignatureInvalid(string reason) => new(
            $"Die digitale Signatur deines installierten PlatinVault ist nicht mehr gültig (Dateien verändert oder beschädigt?), deshalb lässt sich ein Update nicht sicher prüfen. Es wurde nichts installiert. Installiere PlatinVault bitte von Hand neu: Lade den Installer von der offiziellen Release-Seite herunter und führe ihn aus, deine Daten bleiben dabei erhalten. Technische Einzelheiten: {reason}",
            $"The digital signature of your installed PlatinVault is no longer valid (files changed or damaged?), so an update cannot be checked safely. Nothing was installed. Please reinstall PlatinVault by hand: download the installer from the official release page and run it, your data is kept. Technical details: {reason}");

        public static readonly Msg ChecksumMismatch = new(
            "Die heruntergeladene Datei stimmt nicht mit der veröffentlichten Prüfsumme überein und wurde gelöscht. Bitte später erneut versuchen.",
            "The downloaded file does not match the published checksum and was deleted. Please try again later.");
    }
}
