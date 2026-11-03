namespace Companion.Api.Localization;

/// <summary>
/// The texts of "start a game on the gaming PC" (part of <see cref="ServerTexts"/>, kept in its own file so the area can change
/// without touching the large main catalogue). Same rules as there: parameterised text = method, fixed text = field, English line
/// directly behind the German one.
/// </summary>
public static partial class ServerTexts
{
    // ---- Starting a game (Steam: steam://run) ----------------------------------------------------------------------------

    public static class Launch
    {
        public static readonly Msg NoSteamAppTitle = M("Kein Steam-Spiel", "Not a Steam game");

        public static Msg NoSteamAppDetail(string title) => M(
            $"„{title}“ ist keiner Steam-App zugeordnet und kann deshalb nicht gestartet werden. Verknüpfe das Spiel zuerst mit seiner Steam-AppID.",
            $"“{title}” is not linked to a Steam app and cannot be started. Link the game to its Steam AppID first.");

        public static readonly Msg NotInLibraryTitle = M("Spiel nicht in deiner Sammlung", "Game not in your collection");

        public static Msg NotInLibraryDetail(int appId) => M(
            $"Die Steam-AppID {appId} gehört weder zu deiner Sammlung noch zu deiner Steam-Bibliothek. Gestartet werden nur eigene Spiele.",
            $"Steam AppID {appId} is neither in your collection nor in your Steam library. Only your own games can be started.");

        public static readonly Msg LibraryUnknownTitle = M("Steam-Bibliothek nicht prüfbar", "Steam library cannot be checked");

        public static Msg LibraryUnknownDetail(int appId, string reason) => M(
            $"Die AppID {appId} gehört nicht zu deiner Sammlung, und die Steam-Bibliothek ließ sich nicht laden, um sie dort zu suchen: {reason}",
            $"AppID {appId} is not in your collection, and the Steam library could not be loaded to look for it there: {reason}");

        public static readonly Msg TooManyOpenTitle = M("Zu viele Startbefehle", "Too many launch commands");

        public static Msg TooManyOpenDetail(int max) => M(
            $"Es warten schon {max} Startbefehle auf den Gaming-PC. Warte einen Moment und versuche es erneut.",
            $"{max} launch commands are already waiting for the gaming PC. Wait a moment and try again.");

        public static readonly Msg CommandNotFoundTitle = M("Startbefehl nicht gefunden", "Launch command not found");

        public static readonly Msg CommandNotFoundDetail = M(
            "Dieser Startbefehl ist unbekannt oder schon so lange her, dass er vergessen wurde.",
            "This launch command is unknown, or so long ago that it has been forgotten.");

        // What the app shows while and after a command is on its way (Message of GET /api/launch/{id})

        public static Msg Waiting(string title) => M(
            $"Warte auf den Gaming-PC … {title} wird gestartet.",
            $"Waiting for the gaming PC … {title} is being started.");

        public static Msg Done(string title) => M(
            $"Steam startet {title} auf dem Gaming-PC.",
            $"Steam is launching {title} on the gaming PC.");

        public static readonly Msg SteamNotFound = M(
            "Steam wurde auf dem Gaming-PC nicht gefunden.",
            "Steam was not found on the gaming PC.");

        public static readonly Msg StartFailed = M(
            "Der Gaming-PC konnte den Spielstart nicht auslösen.",
            "The gaming PC could not trigger the game launch.");

        // The Windows program starts the game itself on this PC (no bridge, no browser question)

        public static readonly Msg LocalFailedTitle = M("Spielstart fehlgeschlagen", "Game launch failed");

        public static Msg LocalSteamMissing(string title) => M(
            $"{title} konnte nicht gestartet werden: Steam ist auf diesem PC nicht installiert oder nicht für steam://-Links registriert.",
            $"{title} could not be started: Steam is not installed on this PC or not registered for steam:// links.");

        public static Msg LocalStartFailed(string title, string? detail) => M(
            $"{title} konnte nicht gestartet werden. Windows meldet: {detail ?? "(kein Grund angegeben)"}",
            $"{title} could not be started. Windows reports: {detail ?? "(no reason given)"}");

        public static readonly Msg NotPickedUp = M(
            "Der Gaming-PC hat den Startbefehl nicht abgeholt. Läuft die PlatinVault Bridge dort und ist sie mit dem Server verbunden?",
            "The gaming PC did not pick up the launch command. Is the PlatinVault Bridge running there and connected to the server?");

        public static readonly Msg NotConfirmed = M(
            "Der Gaming-PC hat den Start nicht bestätigt. Prüfe dort, ob das Spiel gestartet wurde.",
            "The gaming PC did not confirm the launch. Check there whether the game has started.");

        // Bridge side of the channel (POST /api/bridge/commands)

        public static readonly Msg PollNotJson = M(
            "Die Anfrage der Bridge ist kein gültiges JSON im erwarteten Format.",
            "The bridge's request is not valid JSON in the expected format.");

        public static Msg TooManyResults(int max) => M(
            $"Pro Anfrage sind höchstens {max} Ergebnisse erlaubt.",
            $"At most {max} results are allowed per request.");
    }
}
