namespace Companion.Api.Localization;

/// <summary>
/// The texts of the endpoints that only the Windows program offers (opening its folders; part of <see cref="ServerTexts"/>, kept in its
/// own file). Same rules as there: parameterised text = method, fixed text = field, English line directly behind the German one.
/// </summary>
public static partial class ServerTexts
{
    public static class Desktop
    {
        public static readonly Msg NotDesktopTitle = M("Nur im Windows-Programm", "Windows program only");

        public static readonly Msg NotDesktop = M(
            "Diese Funktion gibt es nur im PlatinVault-Windows-Programm. Ein Container (Unraid, NAS, Docker) oder der Browser-Server hat keinen Zugriff auf die Ordner deines PCs.",
            "This function exists only in the PlatinVault Windows program. A container (Unraid, NAS, Docker) or the browser server has no access to the folders of your PC.");

        public static readonly Msg OpenFolderTitle = M("Ordner nicht geöffnet", "Folder not opened");

        public static Msg UnknownFolder(string kind) => M(
            $"„{kind}“ ist kein bekannter Ordner. Möglich sind: program, data, backups, logs.",
            $"“{kind}” is not a known folder. Possible values: program, data, backups, logs.");

        public static Msg FolderNotAvailable(string kind) => M(
            $"Der Ordner „{kind}“ wird von dieser Ausführung des Programms nicht angeboten.",
            $"The folder “{kind}” is not offered by this run of the program.");

        public static Msg OpenFailed(string kind, string reason) => M(
            $"Der Ordner „{kind}“ konnte nicht im Explorer geöffnet werden: {reason}",
            $"The folder “{kind}” could not be opened in Explorer: {reason}");
    }
}
