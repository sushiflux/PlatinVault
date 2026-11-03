namespace Companion.Api.Localization;

/// <summary>
/// The texts of "Steam on this computer": the setting "Steam-Ordner", its check and the system check line (part of
/// <see cref="ServerTexts"/>, same rules as there).
/// </summary>
public static partial class ServerTexts
{
    // ---- Steam on this computer (local Steam folder) -----------------------------------------------------------------------

    public static class SteamLocal
    {
        public static readonly Msg CheckTitle = M("Steam-Ordner ungültig", "Steam folder invalid");

        /// <summary>Why a folder given by hand cannot be used, with what to do instead.</summary>
        public static Msg Problem(SteamRootProblem problem, string path, SteamPlatform platform) => problem switch
        {
            SteamRootProblem.NotAbsolute => M(
                $"„{path}“ ist kein vollständiger Pfad. Bitte den ganzen Pfad angeben, z. B. {Example(platform)}.",
                $"“{path}” is not a full path. Please enter the whole path, e.g. {Example(platform)}."),
            SteamRootProblem.NotFound => M(
                $"Den Ordner „{path}“ gibt es auf dem Rechner, auf dem PlatinVault läuft, nicht – oder PlatinVault darf ihn nicht lesen.",
                $"The folder “{path}” does not exist on the computer PlatinVault runs on – or PlatinVault is not allowed to read it."),
            SteamRootProblem.NoSteamApps => M(
                $"„{path}“ ist kein Steam-Ordner: darin fehlt der Unterordner „steamapps“. Gemeint ist der Ordner, in dem „steamapps“ liegt.",
                $"“{path}” is not a Steam folder: the subfolder “steamapps” is missing. Enter the folder that contains “steamapps”."),
            SteamRootProblem.LibraryOnly => M(
                $"Das ist eine Spielebibliothek, nicht der Steam-Ordner: „{path}“ enthält zwar „steamapps“, aber weder „appcache“ noch „config“. Gemeint ist der Ordner, in dem Steam selbst installiert ist (z. B. {Example(platform)}) – nur dort liegen die Erfolgsdaten für die Live-Erkennung.",
                $"This is a game library, not the Steam folder: “{path}” contains “steamapps”, but neither “appcache” nor “config”. Enter the folder Steam itself is installed in (e.g. {Example(platform)}) – only there are the achievement files the live detection reads."),
            _ => M(string.Empty, string.Empty),
        };

        private static string Example(SteamPlatform platform) => platform == SteamPlatform.Windows
            ? @"C:\Program Files (x86)\Steam"
            : "/home/<name>/.local/share/Steam";

        public static Msg Via(SteamLocationMethod method, string? detail) => method switch
        {
            SteamLocationMethod.Manual => M("Einstellung „Steam-Ordner“", "setting “Steam folder”"),
            SteamLocationMethod.Configuration => M("Konfiguration Companion:Steam:Root", "configuration Companion:Steam:Root"),
            SteamLocationMethod.Environment => M($"Umgebungsvariable {detail}", $"environment variable {detail}"),
            SteamLocationMethod.Registry => M("Windows-Registry (SteamPath)", "Windows registry (SteamPath)"),
            SteamLocationMethod.Symlink => M($"Verknüpfung ({Variant(detail)})", $"symbolic link ({Variant(detail)})"),
            SteamLocationMethod.KnownPath => M($"Standardpfad ({Variant(detail)})", $"standard path ({Variant(detail)})"),
            _ => M("–", "–"),
        };

        private static string Variant(string? detail) => detail switch
        {
            "flatpak" => "Flatpak",
            "snap" => "Snap",
            _ => "native",
        };

        public static Msg Found(string root, SteamLocationMethod method, string? detail, bool experimental, bool inContainer)
        {
            var line = Msg.Join(" · ",
                M("Live-Erkennung direkt aus den lokalen Steam-Dateien", "Live detection straight from the local Steam files"),
                Msg.Join("", M($"Ordner {root} – gefunden über: ", $"Folder {root} – found via: "), Via(method, detail)));
            if (experimental)
            {
                line = Msg.Join(" · ", line, M(
                    "Linux: experimentell (noch nicht auf einem echten Linux-Steam geprüft)",
                    "Linux: experimental (not yet checked on a real Linux Steam)"));
            }
            if (inContainer)
            {
                line = Msg.Join(" · ", line, M(
                    "im Container erkennt PlatinVault das laufende Spiel nur mit der Prozessliste des Hosts (Docker: pid: host)",
                    "inside a container PlatinVault only detects the running game with the host's process list (Docker: pid: host)"));
            }
            return line;
        }

        /// <summary>No Steam on a computer where one could be (Windows / native Linux): say where to set it.</summary>
        public static readonly Msg NotFoundNative = M(
            "Kein Steam auf diesem Rechner gefunden. Ist Steam installiert, aber an einem anderen Ort, trage den Steam-Ordner unter Einstellungen > Steam ein. Läuft Steam auf einem anderen PC, kommt die Echtzeit über die PlatinVault Bridge.",
            "No Steam found on this computer. If Steam is installed somewhere else, enter the Steam folder under Settings > Steam. If Steam runs on another PC, real time comes through the PlatinVault Bridge.");

        public static Msg ManualUnusable(string path, Msg reason) => Msg.Join(" ",
            M($"Der eingestellte Steam-Ordner „{path}“ ist nicht nutzbar:", $"The Steam folder that is set, “{path}”, cannot be used:"), reason);
    }
}
