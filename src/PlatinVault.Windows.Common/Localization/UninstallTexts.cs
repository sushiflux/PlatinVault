namespace PlatinVault.Windows.Localization;

/// <summary>Texts of the designed uninstall window.</summary>
public static class UninstallTexts
{
    public static readonly Msg WindowTitle = new("PlatinVault deinstallieren", "Uninstall PlatinVault");
    public static readonly Msg SideSubtitle = new("Deinstallation", "Uninstall");

    public static readonly Msg StepConfirm = new("Daten", "Data");
    public static readonly Msg StepRemove = new("Entfernen", "Remove");
    public static readonly Msg StepDone = new("Fertig", "Done");

    public static readonly Msg ConfirmKicker = new("Deinstallation", "Uninstall");
    public static readonly Msg ConfirmTitle = new("Daten behalten?", "Keep your data?");
    public static readonly Msg ConfirmSubtitle = new(
        "PlatinVault wird entfernt. Entscheide, was mit deinen Guides, Häkchen und Einstellungen passiert.",
        "PlatinVault will be removed. Decide what happens to your guides, checkmarks and settings.");

    public static readonly Msg KeepTitle = new("Daten behalten (empfohlen)", "Keep data (recommended)");
    public static readonly Msg KeepText = new(
        "Datenbank, Sicherungen und API-Keys bleiben erhalten. Eine spätere Installation macht genau dort weiter.",
        "Database, backups and API keys stay. A later installation carries on right where you left off.");
    public static readonly Msg DeleteTitle = new("Alles löschen", "Delete everything");
    public static readonly Msg DeleteText = new(
        "Löscht Datenbank, alle Sicherungen, gespeicherte API-Keys und Protokolle endgültig. Das lässt sich nicht rückgängig machen.",
        "Permanently deletes the database, all backups, stored API keys and logs. This cannot be undone.");
    public static Msg DataLocation(string folder) => new($"Datenordner: {folder}", $"Data folder: {folder}");

    public static readonly Msg ButtonUninstall = new("Deinstallieren", "Uninstall");
    public static readonly Msg ButtonDeleteAll = new("Alles löschen und deinstallieren", "Delete everything and uninstall");

    public static readonly Msg RemoveKicker = new("Bitte warten", "Please wait");
    public static readonly Msg RemoveTitle = new("PlatinVault wird entfernt", "Removing PlatinVault");
    public static readonly Msg RemoveSubtitle = new(
        "PlatinVault wird beendet (mit letzter Sicherung) und alles Gewählte entfernt.",
        "PlatinVault is closed (with a last backup) and everything you chose removed.");

    public static readonly Msg ProgressStopping = new("PlatinVault wird beendet (mit letzter Sicherung) …", "Closing PlatinVault (with a last backup) …");
    public static readonly Msg ProgressEntries = new("Autostart, Verknüpfungen und „Apps“-Eintrag werden entfernt …", "Removing autostart, shortcuts and the “Apps” entry …");
    public static readonly Msg ProgressKeepData = new("Daten bleiben erhalten …", "Keeping your data …");
    public static readonly Msg ProgressDeleteData = new("Daten, Datenbank und Sicherungen werden gelöscht …", "Deleting data, database and backups …");
    public static readonly Msg ProgressProgram = new("Programmordner wird entfernt …", "Removing the program folder …");
    public static readonly Msg ProgressDone = new("Fertig.", "Done.");

    public static readonly Msg DoneKicker = new("Erledigt", "Done");
    public static readonly Msg DoneTitle = new("PlatinVault wurde entfernt", "PlatinVault has been removed");
    public static readonly Msg DoneSubtitle = new("Danke, dass du PlatinVault benutzt hast.", "Thanks for using PlatinVault.");
    public static readonly Msg FailedTitle = new("Deinstallation unterbrochen", "Uninstall interrupted");
    public static readonly Msg FailedSubtitle = new(
        "Nicht alles konnte entfernt werden. Der Grund steht unten vollständig.",
        "Not everything could be removed. The full reason is shown below.");
}
