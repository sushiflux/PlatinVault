namespace PlatinVault.Bridge;

// "Start this game in Steam" - the command channel to the server (see CommandPoller). Same rules as the rest of BridgeTexts.
internal static partial class BridgeTexts
{
    public static class Launch
    {
        // Balloon on the gaming PC. The game name comes from the server (the title in the user's collection), already shortened.
        public static Msg Balloon(string name) => M($"Starte {name} …", $"Launching {name} …");

        public static Msg BalloonSteamMissing(string name) => M(
            $"{name} konnte nicht gestartet werden: Steam wurde auf diesem PC nicht gefunden.",
            $"{name} could not be launched: Steam was not found on this PC.");

        public static Msg BalloonFailed(string name) => M(
            $"{name} konnte nicht gestartet werden – Details stehen im Protokoll.",
            $"{name} could not be launched – see the log for details.");

        public static Msg Starting(string name, uint appId) => M(
            $"Spielstart auf Anforderung von PlatinVault: {name} (AppID {appId}).",
            $"Game launch requested by PlatinVault: {name} (AppID {appId}).");

        public static Msg StartedLog(string name, uint appId) => M(
            $"{name} (AppID {appId}): Der Start wurde an Steam übergeben.",
            $"{name} (AppID {appId}): The launch was handed to Steam.");

        public static Msg SteamMissingLog(string name, uint appId, string detail) => M(
            $"{name} (AppID {appId}) nicht gestartet: Steam wurde auf diesem PC nicht gefunden ({detail}).",
            $"{name} (AppID {appId}) not launched: Steam was not found on this PC ({detail}).");

        public static Msg FailedLog(string name, uint appId, string detail) => M(
            $"{name} (AppID {appId}) konnte nicht gestartet werden: {detail}",
            $"{name} (AppID {appId}) could not be launched: {detail}");

        public static Msg IgnoredCommand(string id, string type, string appId) => M(
            $"Befehl {id} ({type}, AppID {appId}) wird nicht ausgeführt: unbekannte Art oder ungültige AppID.",
            $"Command {id} ({type}, AppID {appId}) is not run: unknown kind or invalid AppID.");

        // Debug lines (--verbose): the channel is quiet in the normal log, the connection state is reported by the achievement loop
        public static Msg ChannelProblem(string reason) => M(
            $"Befehlskanal: {reason}. Die Bridge versucht es weiter.",
            $"Command channel: {reason}. The bridge keeps trying.");

        public static readonly Msg ChannelRestored = M("Befehlskanal wieder erreichbar.", "Command channel reachable again.");
    }
}
