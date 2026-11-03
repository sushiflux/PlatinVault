namespace PlatinVault.Windows.Localization;

/// <summary>Texts of the install and uninstall engine (messages that end up in the windows as they are).</summary>
public static class InstallTexts
{
    public static Msg AutostartFailed(string reason) => new(
        $"Der Autostart konnte nicht eingetragen werden. Windows meldet: {reason}",
        $"Autostart could not be registered. Windows reports: {reason}");

    public static Msg AppsEntryFailed(string reason) => new(
        $"Der Eintrag unter „Apps“ (zum Deinstallieren) konnte nicht geschrieben werden. Windows meldet: {reason}",
        $"The entry under “Apps” (for uninstalling) could not be written. Windows reports: {reason}");

    public static Msg ShortcutFailed(string path, string reason) => new(
        $"Die Verknüpfung „{path}“ konnte nicht angelegt werden. Windows meldet: {reason}",
        $"The shortcut “{path}” could not be created. Windows reports: {reason}");

    public static readonly Msg ShortcutDescription = new(
        "PlatinVault – Live-Trophäen und 100%-Guides für Steam",
        "PlatinVault – live trophies and 100% guides for Steam");

    public static readonly Msg StillRunning = new(
        "PlatinVault läuft noch und hat sich nicht beendet. Beende es über das Symbol neben der Uhr (Rechtsklick → Beenden) und versuche es dann erneut.",
        "PlatinVault is still running and did not close. Quit it from the icon next to the clock (right-click → Quit) and try again.");

    public static Msg DataNotDeleted(string folder, string reason) => new(
        $"Der Datenordner „{folder}“ konnte nicht vollständig gelöscht werden: {reason}",
        $"The data folder “{folder}” could not be deleted completely: {reason}");

    public static Msg ProgramNotDeleted(string folder, string reason) => new(
        $"Der Programmordner „{folder}“ konnte nicht gelöscht werden: {reason}",
        $"The program folder “{folder}” could not be deleted: {reason}");

    public static Msg UnsafeFolder(string folder) => new(
        $"Der Ordner „{folder}“ wird nicht gelöscht, weil er nicht eindeutig der Programmordner von PlatinVault ist. Bitte prüfe ihn und lösche ihn bei Bedarf selbst.",
        $"The folder “{folder}” is not deleted because it is not clearly PlatinVault's program folder. Please check it and delete it yourself if needed.");

    // ---- what the uninstaller did (list on the last page) ----------------------------------------------------------------------

    public static readonly Msg DoneStopped = new("PlatinVault beendet", "PlatinVault closed");
    public static readonly Msg DoneEntries = new("Autostart, Verknüpfungen und Eintrag unter „Apps“ entfernt", "Autostart, shortcuts and the “Apps” entry removed");
    public static readonly Msg DoneDataDeleted = new("Daten, Datenbank und Sicherungen gelöscht", "Data, database and backups deleted");
    public static Msg DoneDataKept(string folder) => new($"Daten behalten in {folder}", $"Data kept in {folder}");
    public static readonly Msg DoneProgramDeleted = new("Programmordner gelöscht", "Program folder deleted");
    public static readonly Msg DoneProgramScheduled = new("Programmordner wird nach dem Schließen gelöscht", "Program folder is deleted after closing");
}
