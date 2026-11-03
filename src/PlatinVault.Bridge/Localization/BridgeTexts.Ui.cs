namespace PlatinVault.Bridge;

// Windows, tray menu and message boxes. See BridgeTexts.cs for the rules.
internal static partial class BridgeTexts
{
    // ---- Message window (our own, never Windows' MessageBox) -----------------------------------------------------------

    public static class Dialog
    {
        public static readonly Msg Ok = Msg.Same("OK");
        public static readonly Msg Cancel = M("Abbrechen", "Cancel");
        public static readonly Msg Yes = M("Ja", "Yes");
        public static readonly Msg No = M("Nein", "No");

        // Second line of the window header, by kind of message
        public static readonly Msg SubtitleError = M("Fehler", "Error");
        public static readonly Msg SubtitleWarning = M("Achtung", "Warning");
        public static readonly Msg SubtitleQuestion = M("Frage", "Question");
        public static readonly Msg SubtitleInfo = M("Hinweis", "Notice");
    }

    // ---- Setup window ---------------------------------------------------------------------------------------------------

    public static class Setup
    {
        public static readonly Msg WindowTitle = M("PlatinVault Bridge – Einrichtung", "PlatinVault Bridge – Setup");

        public static Msg SubtitleInstall(string version) => M($"Einrichtung · Version {version}", $"Setup · Version {version}");
        public static Msg SubtitleUpdate(string version) => M($"Aktualisierung · Version {version}", $"Update · Version {version}");

        public static Msg SubtitleUpdateFrom(string installed, string version) => M(
            $"Aktualisierung von Version {installed} auf {version}",
            $"Update from version {installed} to {version}");

        public static Msg SubtitleSave(string version) => M($"Verbindung einrichten · Version {version}", $"Set up connection · Version {version}");

        public static readonly Msg Intro = M(
            "Die Bridge läuft unauffällig im Hintergrund (Symbol unten rechts neben der Uhr) und meldet freigeschaltete " +
            "Steam-Erfolge in Echtzeit an deinen PlatinVault-Server.",
            "The bridge runs quietly in the background (icon at the bottom right next to the clock) and reports unlocked " +
            "Steam achievements to your PlatinVault server in real time.");

        public static readonly Msg ServerLabel = M("Server-Adresse", "Server address");
        public static readonly Msg TokenLabel = M("Bridge-Token", "Bridge token");
        public static readonly Msg ShowToken = M("Token anzeigen", "Show token");

        public static readonly Msg ServerHint = M("z. B. http://192.168.178.10:5233", "e.g. http://192.168.178.10:5233");

        public static readonly Msg TokenHint = M(
            "In PlatinVault unter Einstellungen > PlatinVault Bridge > Token erzeugen.",
            "In PlatinVault under Settings > PlatinVault Bridge > Create token.");

        public static readonly Msg TokenBadChars = M(
            "Der Token enthält Leerzeichen oder Sonderzeichen – bitte neu aus PlatinVault kopieren.",
            "The token contains spaces or special characters – please copy it from PlatinVault again.");

        // The status card below the fields
        public static readonly Msg CardIdle = M(
            "Sobald Adresse und Token stimmen, wird die Verbindung geprüft.",
            "As soon as address and token are right, the connection is tested.");

        public static readonly Msg CardWaiting = M("Verbindung wird geprüft …", "The connection will be tested …");
        public static readonly Msg CardTesting = M("Prüfe Verbindung …", "Testing the connection …");

        public static Msg CardConnected(string server, Msg steam) => new(
            $"✓ Verbunden mit {server} – {steam.De}",
            $"✓ Connected to {server} – {steam.En}");

        public static Msg SteamAccount(string steamId) => M($"Steam-Konto {steamId}", $"Steam account {steamId}");

        public static readonly Msg NoSteamAccount = M(
            "kein Steam-Konto aktiv (Steam starten und anmelden)",
            "no Steam account active (start Steam and sign in)");

        public static readonly Msg CardTokenInvalid = M(
            "✗ Der Token ist ungültig – in PlatinVault unter Einstellungen > PlatinVault Bridge einen neuen Token erzeugen.",
            "✗ The token is invalid – create a new token in PlatinVault under Settings > PlatinVault Bridge.");

        public static Msg CardUnreachable(string? reason) => M(
            $"✗ Server nicht erreichbar ({reason}).",
            $"✗ Server not reachable ({reason}).");

        public static Msg CardRejected(string? reason) => M(
            $"✗ Der Server lehnt die Anfrage ab ({reason}).",
            $"✗ The server rejects the request ({reason}).");

        /// <summary>Marks a message as a failure in the status card (the cross is part of the text, not colour alone).</summary>
        public static Msg Failure(Msg message) => new("✗ " + message.De, "✗ " + message.En);

        public static readonly Msg Autostart = M("Mit Windows starten", "Start with Windows");
        public static readonly Msg StartMenu = M("Eintrag im Startmenü", "Start menu entry");

        public static Msg LocationSave(string path) => M($"Die Einstellungen werden gespeichert in: {path}", $"The settings are saved in: {path}");
        public static Msg LocationInstall(string path) => M($"Installationsordner: {path}", $"Installation folder: {path}");

        public static readonly Msg PrimaryInstall = M("Installieren & starten", "Install & start");
        public static readonly Msg PrimaryUpdate = M("Aktualisieren & starten", "Update & start");
        public static readonly Msg PrimarySave = M("Speichern & starten", "Save & start");
        public static readonly Msg OpenHandbook = M("Handbuch öffnen", "Open handbook");

        public static readonly Msg HandbookMissing = M(
            "Das Handbuch liegt nicht neben der Bridge. Sobald oben eine gültige Server-Adresse und der Token stehen, öffnet dieser Knopf das Handbuch deines PlatinVault-Servers.",
            "The handbook is not next to the bridge. As soon as a valid server address and the token are entered above, this button opens the handbook of your PlatinVault server.");

        public static Msg HandbookFailed(string reason) => M(
            $"Das Handbuch konnte nicht geöffnet werden: {reason}",
            $"The handbook could not be opened: {reason}");

        public static readonly Msg AskTokenRejected = M(
            "Der Server hat den Token abgelehnt. Die Bridge würde sich damit nicht verbinden können.\n\nTrotzdem fortfahren?",
            "The server rejected the token. The bridge would not be able to connect with it.\n\nContinue anyway?");

        public static readonly Msg Saving = M("Speichere …", "Saving …");
        public static readonly Msg Installing = M("Installiere …", "Installing …");

        public static Msg SaveFailed(string reason) => M(
            $"Die Einstellungen konnten nicht gespeichert werden: {reason}",
            $"The settings could not be saved: {reason}");

        public static Msg SaveFallback(string primary, string reason, string userPath) => M(
            $"{primary} ist nicht beschreibbar ({reason}); speichere stattdessen in {userPath}.",
            $"{primary} is not writable ({reason}); saving to {userPath} instead.");

        public static readonly Msg StoppingRunning = M("Beende die laufende Bridge …", "Ending the running bridge …");

        public static readonly Msg RunningWontQuitTest = M(
            "Die laufende Bridge hat sich nicht beenden lassen (Testlauf: wird nicht erzwungen).",
            "The running bridge could not be ended (test run: not forced).");

        public static readonly Msg AskKillOld = M(
            "Eine ältere Bridge läuft noch im Hintergrund (ohne Fenster) und lässt sich nicht freundlich beenden.\n\n" +
            "Soll sie jetzt beendet werden? Die neue Bridge übernimmt danach.",
            "An older bridge is still running in the background (without a window) and cannot be ended politely.\n\n" +
            "Should it be ended now? The new bridge takes over afterwards.");

        public static readonly Msg MustEndFirst = M(
            "Die laufende Bridge muss zuerst beendet werden (Task-Manager: PlatinVault.Bridge.exe).",
            "The running bridge must be ended first (Task Manager: PlatinVault.Bridge.exe).");

        public static Msg StartFailed(string reason) => M(
            $"Die installierte Bridge konnte nicht gestartet werden: {reason}",
            $"The installed bridge could not be started: {reason}");
    }

    // ---- Status window --------------------------------------------------------------------------------------------------

    public static class Status
    {
        public static readonly Msg WindowTitle = Msg.Same("PlatinVault Bridge – Status");
        public static readonly Msg Subtitle = Msg.Same("Status");

        public static readonly Msg RowServer = Msg.Same("Server");
        public static readonly Msg RowSteam = M("Steam-Konto", "Steam account");
        public static readonly Msg RowGame = M("Aktuelles Spiel", "Current game");
        public static readonly Msg RowUnlock = M("Letzter Erfolg", "Last achievement");
        public static readonly Msg RowDelivered = M("Zuletzt gemeldet", "Last reported");
        public static readonly Msg RowVersion = Msg.Same("Version");

        public static readonly Msg StateOnline = M("● Verbunden", "● Connected");
        public static readonly Msg DetailOnline = M("Erfolge werden in Echtzeit an den Server gemeldet.", "Achievements are reported to the server in real time.");

        public static readonly Msg StateStarting = M("● Verbinde …", "● Connecting …");
        public static readonly Msg DetailStarting = M("Die erste Meldung an den Server läuft.", "The first report to the server is in progress.");

        public static readonly Msg StateOffline = M("● Nicht verbunden", "● Not connected");

        public static readonly Msg DetailUnauthorized = M(
            "Der Token wurde abgelehnt – in PlatinVault unter Einstellungen > PlatinVault Bridge einen neuen erzeugen.",
            "The token was rejected – create a new one in PlatinVault under Settings > PlatinVault Bridge.");

        public static readonly Msg DetailOfflineRetry = M("Die Bridge versucht es automatisch weiter.", "The bridge keeps trying automatically.");

        public static Msg DetailOffline(string reason) => M(
            $"{reason}. Die Bridge versucht es automatisch weiter.",
            $"{reason}. The bridge keeps trying automatically.");

        public static readonly Msg SteamNotSignedIn = M("nicht angemeldet (Steam starten)", "not signed in (start Steam)");
        public static readonly Msg NoGame = M("kein Spiel", "no game");

        public static Msg GameProgress(string name, int achieved, int total) => M(
            $"{name} – {achieved} von {total} Erfolgen",
            $"{name} – {achieved} of {total} achievements");

        public static Msg GameNoAchievements(string name) => M($"{name} – keine Erfolge gefunden", $"{name} – no achievements found");

        public static readonly Msg NoUnlockYet = M("noch keiner in dieser Sitzung", "none yet in this session");

        /// <summary>German writes "14:03:22 Uhr", English just "14:03:22"; other days add the date.</summary>
        public static string Time(DateTime time) => time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + (BridgeLang.IsEnglish ? string.Empty : " Uhr");

        public static string DateAndTime(DateTime time) => time.ToString(BridgeLang.DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture) + (BridgeLang.IsEnglish ? string.Empty : " Uhr");

        public static readonly Msg OpenServer = M("PlatinVault öffnen", "Open PlatinVault");
        public static readonly Msg OpenLog = M("Protokoll öffnen", "Open log");
        public static readonly Msg Close = M("Schließen", "Close");
    }

    // ---- Tray icon -------------------------------------------------------------------------------------------------------

    public static class Tray
    {
        public static readonly Msg MenuConnecting = M("Verbinde …", "Connecting …");
        public static readonly Msg MenuOpenServer = M("PlatinVault öffnen", "Open PlatinVault");
        public static readonly Msg MenuOpenHandbook = M("Handbuch öffnen", "Open handbook");
        public static readonly Msg MenuOpenLog = M("Protokoll öffnen", "Open log");
        public static readonly Msg MenuAutostart = M("Mit Windows starten", "Start with Windows");
        public static readonly Msg MenuExit = M("Beenden", "Exit");
        public static readonly Msg MenuUninstall = M("Deinstallieren …", "Uninstall …");

        // Tooltip (Windows limits it to 127 characters)
        public static Msg TooltipGame(string name, int achieved, int total) => M(
            $"Spiel: {name}" + (total > 0 ? $" ({achieved}/{total})" : string.Empty),
            $"Game: {name}" + (total > 0 ? $" ({achieved}/{total})" : string.Empty));

        public static readonly Msg TooltipOnline = M("verbunden", "connected");
        public static readonly Msg TooltipStarting = M("verbinde …", "connecting …");
        public static readonly Msg TooltipOffline = M("nicht verbunden", "not connected");

        public static Msg MenuStatusGame(string name, int achieved, int total) => M(
            $"Verbunden – Spiel: {name}" + (total > 0 ? $" ({achieved}/{total})" : string.Empty),
            $"Connected – game: {name}" + (total > 0 ? $" ({achieved}/{total})" : string.Empty));

        public static readonly Msg MenuStatusOnline = M("Verbunden – kein Spiel", "Connected – no game");
        public static readonly Msg MenuStatusStarting = M("Verbinde …", "Connecting …");
        public static readonly Msg MenuStatusUnauthorized = M("Nicht verbunden – Token ungültig", "Not connected – token invalid");
        public static readonly Msg MenuStatusOffline = M("Nicht verbunden", "Not connected");

        public static Msg MenuStatusOfflineDetail(string detail) => M($"Nicht verbunden – {detail}", $"Not connected – {detail}");

        public static Msg BalloonUpdated(string version) => M(
            $"Auf Version {version} aktualisiert – läuft im Hintergrund weiter.",
            $"Updated to version {version} – keeps running in the background.");

        public static readonly Msg BalloonFirstRun = M(
            "Läuft im Hintergrund – Erfolge werden in Echtzeit gemeldet.",
            "Running in the background – achievements are reported in real time.");

        public static Msg BrowserFailed(string reason) => M(
            $"Der Browser konnte nicht geöffnet werden: {reason}",
            $"The browser could not be opened: {reason}");

        public static readonly Msg HandbookNotFound = M("Das Handbuch wurde nicht gefunden.", "The handbook was not found.");

        public static Msg NoLogYet(string path) => M($"Es gibt noch kein Protokoll ({path}).", $"There is no log yet ({path}).");

        public static Msg LogOpenFailed(string reason) => M(
            $"Das Protokoll konnte nicht geöffnet werden: {reason}",
            $"The log could not be opened: {reason}");

        public static readonly Msg AutostartOn = M("Autostart eingeschaltet.", "Autostart switched on.");
        public static readonly Msg AutostartOff = M("Autostart ausgeschaltet.", "Autostart switched off.");
    }
}
