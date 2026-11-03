namespace PlatinVault.Bridge;

// Self-update, installer, uninstaller, handbook. See BridgeTexts.cs for the rules.
internal static partial class BridgeTexts
{
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

        public static Msg OnlyForPublishedExe(Version latest, string exeName) => M(
            $"Neue Bridge-Version {latest} verfügbar, aber das Selbstupdate gibt es nur für die veröffentlichte {exeName}.",
            $"New bridge version {latest} available, but the self-update only exists for the published {exeName}.");

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

        public static Msg DownloadVerified(double megabytes) => M(
            $"Download geprüft ({megabytes.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} MB, SHA-256 stimmt) – tausche die Datei aus ...",
            $"Download verified ({megabytes.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB, SHA-256 matches) – swapping the file ...");

        public static Msg FolderNotWritable(string? folder, string reason, Version latest) => M(
            $"Der Ordner {folder} ist nicht beschreibbar ({reason}). Das Selbstupdate wird übersprungen; " +
            $"Version {latest} ist verfügbar. Verschiebe die Bridge in einen Ordner mit Schreibrechten, um Updates zu erhalten.",
            $"The folder {folder} is not writable ({reason}). The self-update is skipped; " +
            $"version {latest} is available. Move the bridge to a folder you can write to in order to receive updates.");

        public static Msg UpdateFailed(Version latest, string reason, double minutes) => M(
            $"Das Update auf Version {latest} ist fehlgeschlagen: {reason}. Neuer Versuch in {minutes:0} Minuten.",
            $"The update to version {latest} failed: {reason}. Next attempt in {minutes:0} minutes.");

        public static Msg Installed(Version latest) => M(
            $"Bridge {latest} installiert – starte neu ...",
            $"Bridge {latest} installed – restarting ...");

        public static Msg FileStillInUse(string name) => M($"{name} ist noch in Benutzung.", $"{name} is still in use.");
    }

    // ---- Installer / uninstaller ---------------------------------------------------------------------------------------

    public static class Installer
    {
        public static Msg AutostartFailed(bool enabled, string reason) => M(
            $"Der Autostart-Eintrag konnte nicht {(enabled ? "angelegt" : "entfernt")} werden: {reason}",
            $"The autostart entry could not be {(enabled ? "created" : "removed")}: {reason}");

        public static Msg CopyFailed(string root, string reason) => M(
            $"Die Dateien konnten nicht nach {root} kopiert werden: {reason} " +
            "Läuft die Bridge noch (Tray-Icon)? Dann beende sie und versuche es erneut.",
            $"The files could not be copied to {root}: {reason} " +
            "Is the bridge still running (tray icon)? Then end it and try again.");

        public static Msg AppsVersionUpdated(string version) => M(
            $"Eintrag unter „Apps“ auf Version {version} aktualisiert.",
            $"Entry under “Apps” updated to version {version}.");

        public static Msg AppsVersionNotUpdatedDebug(string reason) => M(
            $"Version im „Apps“-Eintrag nicht aktualisiert: {reason}",
            $"Version in the “Apps” entry not updated: {reason}");

        public static readonly Msg ShortcutDescription = M(
            "Meldet freigeschaltete Steam-Erfolge in Echtzeit an PlatinVault",
            "Reports unlocked Steam achievements to PlatinVault in real time");

        public static Msg StartMenuFailed(bool enabled, string reason) => M(
            $"Der Startmenü-Eintrag konnte nicht {(enabled ? "angelegt" : "entfernt")} werden: {reason}",
            $"The Start menu entry could not be {(enabled ? "created" : "removed")}: {reason}");

        public static Msg AppsEntryFailed(string reason) => M(
            $"Der Eintrag unter „Apps“ konnte nicht angelegt werden: {reason}",
            $"The entry under “Apps” could not be created: {reason}");

        public static Msg LegacyTaskDebug(string reason) => M(
            $"Alte Aufgabe konnte nicht geprüft werden: {reason}",
            $"The old task could not be checked: {reason}");

        public static readonly Msg NoScriptHost = M(
            "Windows Script Host ist auf diesem PC nicht verfügbar.",
            "Windows Script Host is not available on this PC.");

        // What the uninstaller reports back (listed in the final message window, so these are short noun phrases)
        public static readonly Msg AutostartRemoved = M("Autostart-Eintrag entfernt", "Autostart entry removed");
        public static readonly Msg StartMenuRemoved = M("Startmenü-Eintrag entfernt", "Start menu entry removed");
        public static readonly Msg AppsEntryRemoved = M("Eintrag unter „Apps“ entfernt", "Entry under “Apps” removed");
        public static readonly Msg SettingsDeleted = M("gespeicherte Einstellungen gelöscht", "saved settings deleted");
        public static readonly Msg LogDeleted = M("Protokoll gelöscht", "log deleted");

        public static Msg FolderNotDeleted(string root) => M(
            $"Ordner {root} wurde NICHT gelöscht (kein typischer Installationsordner)",
            $"Folder {root} was NOT deleted (not a typical installation folder)");

        public static readonly Msg FolderDeleteScheduled = M("Programmordner wird nach dem Beenden gelöscht", "Program folder will be deleted after exit");
        public static readonly Msg FolderDeleted = M("Programmordner gelöscht", "Program folder deleted");

        public static Msg UninstallInterrupted(string reason) => M(
            $"Die Deinstallation wurde unterbrochen: {reason}",
            $"The uninstall was interrupted: {reason}");

        public static Msg FolderDeleteFailed(string root, string reason) => M(
            $"Der Ordner {root} konnte nicht gelöscht werden: {reason}",
            $"The folder {root} could not be deleted: {reason}");
    }

    // ---- Handbook ------------------------------------------------------------------------------------------------------

    public static class Handbook
    {
        public static Msg CopyFailed(string name, string reason) => M(
            $"Die Datei „{name}“ konnte nicht in den Programmordner kopiert werden: {reason}",
            $"The file “{name}” could not be copied to the program folder: {reason}");
    }

    // ---- Uninstall flow ------------------------------------------------------------------------------------------------

    public static class Uninstall
    {
        public static readonly Msg WindowTitle = M("PlatinVault Bridge – Deinstallieren", "PlatinVault Bridge – Uninstall");
        public static readonly Msg Subtitle = M("Deinstallieren", "Uninstall");
        public static readonly Msg Question = M("PlatinVault Bridge von diesem PC entfernen?", "Remove PlatinVault Bridge from this PC?");

        public static readonly Msg Details = M(
            "Das passiert:\n" +
            "•  Die laufende Bridge wird beendet.\n" +
            "•  Autostart- und Startmenü-Eintrag werden entfernt.\n" +
            "•  Der Eintrag unter „Apps“ wird entfernt.\n" +
            "•  Die gespeicherten Einstellungen (Server, Token) werden gelöscht.\n" +
            "•  Der Programmordner wird gelöscht:",
            "This will happen:\n" +
            "•  The running bridge is ended.\n" +
            "•  The autostart and Start menu entries are removed.\n" +
            "•  The entry under “Apps” is removed.\n" +
            "•  The saved settings (server, token) are deleted.\n" +
            "•  The program folder is deleted:");

        public static readonly Msg DeleteLog = M("Auch das Protokoll (bridge.log) löschen", "Also delete the log (bridge.log)");
        public static readonly Msg Confirm = M("Deinstallieren", "Uninstall");

        public static Msg DoneLog(IEnumerable<Msg> steps) => new(
            "Deinstalliert: " + string.Join(", ", steps.Select(s => s.De)) + ".",
            "Uninstalled: " + string.Join(", ", steps.Select(s => s.En)) + ".");

        public static Msg DoneDialog(IEnumerable<Msg> steps) => new(
            "PlatinVault Bridge wurde entfernt:\n\n•  " + string.Join("\n•  ", steps.Select(s => s.De)),
            "PlatinVault Bridge was removed:\n\n•  " + string.Join("\n•  ", steps.Select(s => s.En)));

        public static readonly Msg CannotQuit = M(
            "Die laufende Bridge lässt sich nicht beenden. Bitte im Task-Manager „PlatinVault.Bridge.exe“ beenden und die Deinstallation wiederholen.",
            "The running bridge cannot be ended. Please end “PlatinVault.Bridge.exe” in the Task Manager and repeat the uninstall.");
    }
}
