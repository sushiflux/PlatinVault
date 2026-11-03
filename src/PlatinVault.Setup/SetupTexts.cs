using PlatinVault.Windows.Localization;

namespace PlatinVault.Setup;

/// <summary>Every text of the installer, German and English side by side.</summary>
internal static class SetupTexts
{
    public static readonly Msg WindowTitle = new("PlatinVault installieren", "Install PlatinVault");
    public static readonly Msg SideInstall = new("Installation", "Setup");
    public static Msg SideUpdate(string from) => new($"Update von {from}", $"Update from {from}");

    public static readonly Msg StepLanguage = new("Sprache", "Language");
    public static readonly Msg StepLicence = new("Lizenz", "Licence");
    public static readonly Msg StepDatabase = new("Datenbank", "Database");
    public static readonly Msg StepLocation = new("Ort & Autostart", "Location & autostart");
    public static readonly Msg StepInstall = new("Installieren", "Installing");
    public static readonly Msg StepDone = new("Fertig", "Done");

    public static readonly Msg Install = new("Installieren", "Install");
    public static readonly Msg Update = new("Aktualisieren", "Update");
    public static readonly Msg Finish = new("Fertig", "Finish");

    // ---- language ------------------------------------------------------------------------------------------------------------
    public static readonly Msg LanguageKicker = new("Willkommen", "Welcome");
    public static readonly Msg LanguageTitle = new("Willkommen bei PlatinVault", "Welcome to PlatinVault");
    public static readonly Msg LanguageSubtitle = new(
        "Live-Trophäen über deinem Spiel und 100%-Guides – als Programm für Windows. Wähle deine Sprache.",
        "Live trophies over your game and 100% guides – as a program for Windows. Choose your language.");
    public static readonly Msg LanguageUpdateTitle = new("PlatinVault aktualisieren", "Update PlatinVault");
    public static Msg LanguageUpdateSubtitle(string from, string to) => new(
        $"Von {from} auf {to}. Deine Daten, Guides und Einstellungen bleiben erhalten; vorher wird die Datenbank gesichert.",
        $"From {from} to {to}. Your data, guides and settings stay; the database is backed up first.");
    public static readonly Msg GermanText = new("Die ganze App, das Handbuch und Steams Erfolgstexte auf Deutsch.", "The whole app, the handbook and Steam's achievement texts in German.");
    public static readonly Msg EnglishText = new("Die ganze App, das Handbuch und Steams Erfolgstexte auf Englisch.", "The whole app, the handbook and Steam's achievement texts in English.");
    public static readonly Msg FeatureLive = new("Trophäen-Popup in Echtzeit über dem Spiel", "Real-time trophy popup over your game");
    public static readonly Msg FeatureGuides = new("100%-Guides, von deiner KI recherchiert", "100% guides researched by your AI");
    public static readonly Msg FeatureLocal = new("Alles bleibt auf deinem PC", "Everything stays on your PC");

    // ---- licence -------------------------------------------------------------------------------------------------------------
    public static readonly Msg LicenceKicker = new("Lizenz", "Licence");
    public static readonly Msg LicenceTitle = new("Freie Software", "Free software");
    public static readonly Msg LicenceSubtitle = new(
        "PlatinVault steht unter der GNU AGPL v3: kostenlos nutzbar, ohne etwas zu akzeptieren. Die Lizenz regelt Weitergabe und Änderung.",
        "PlatinVault is licensed under the GNU AGPL v3: free to use, nothing to accept. The licence covers sharing and changing it.");
    public static readonly Msg LicenceSource = new(
        "Quellcode: https://github.com/sushiflux/PlatinVault – der Lizenztext ist das englische Original der Free Software Foundation.",
        "Source code: https://github.com/sushiflux/PlatinVault – the licence text is the original published by the Free Software Foundation.");
    // Shown on the licence page of every setup (new installation and update). Keep it true to the code: the connections PlatinVault makes without
    // the user's own setup are the GitHub update check (switchable), the WebView2 download of this setup, and - with a German interface and no
    // AI key - the Google translation of English Steam achievement texts (switchable). Everything else needs the user's own account or key.
    public static readonly Msg PrivacyTitle = new("Netzwerk & Datenschutz", "Network & privacy");
    public static readonly Msg PrivacyText = new(
        "PlatinVault sendet keine Nutzungsdaten und hat keine Konten. Von selbst fragt es nur bei GitHub (api.github.com) nach neuen Versionen; GitHub sieht dabei deine IP-Adresse. " +
        "Abschalten: Einstellungen → System → Updates → „Nach Updates suchen“ auf „Nie“. Fehlt Windows die WebView2-Laufzeit, lädt dieses Setup sie von Microsoft. " +
        "Mit deutscher Oberfläche und ohne KI-Schlüssel gehen englische Erfolgstexte von Steam an Google Übersetzer (abschaltbar unter Einstellungen → Live & Sound). " +
        "Steam, KI-Anbieter, Twitch und IGDB werden erst genutzt, wenn du dort selbst ein Konto oder einen Schlüssel einträgst.",
        "PlatinVault sends no usage data and has no accounts. By itself it only asks GitHub (api.github.com) for new versions; GitHub sees your IP address when it does. " +
        "To switch this off: Settings → System → Updates → “Check for updates” set to “Never”. If Windows lacks the WebView2 runtime, this setup downloads it from Microsoft. " +
        "With a German interface and no AI key, English Steam achievement texts are sent to Google Translate (can be switched off under Settings → Live & Sound). " +
        "Steam, AI providers, Twitch and IGDB are only used once you enter your own account or key there.");
    public static readonly Msg LicenceMissing = new(
        "Der Lizenztext fehlt im Installationspaket. Bitte die Installationsdatei erneut herunterladen.",
        "The licence text is missing from the setup package. Please download the setup file again.");

    // ---- database ------------------------------------------------------------------------------------------------------------
    public static readonly Msg DatabaseKicker = new("Datenbank", "Database");
    public static readonly Msg DatabaseTitle = new("Nichts einzurichten", "Nothing to set up");
    public static readonly Msg DatabaseSubtitle = new(
        "Du musst nichts installieren und nichts eintragen – PlatinVault speichert alles in einer Datei auf deinem PC.",
        "You don't install or type anything – PlatinVault keeps everything in one file on your PC.");
    public static readonly Msg DbLocalTitle = new("Eine Datei, nur auf diesem PC", "One file, only on this PC");
    public static readonly Msg DbLocalText = new(
        "Guides, Häkchen und Einstellungen in einer Datei im Datenordner – kein Datenbankserver, kein Dienst, keine Adminrechte.",
        "Guides, checkmarks and settings in one file in the data folder – no database server, no service, no admin rights.");
    public static readonly Msg DbSecretTitle = new("API-Keys verschlüsselt", "API keys encrypted");
    public static readonly Msg DbSecretText = new(
        "Deine API-Keys werden mit deinem Windows-Konto verschlüsselt und getrennt von der Datenbank gespeichert.",
        "Your API keys are encrypted with your Windows account and stored apart from the database.");
    public static readonly Msg DbBackupTitle = new("Automatische Sicherungen", "Automatic backups");
    public static readonly Msg DbBackupText = new(
        "Beim Start, alle 6 Stunden und vor jedem Update – nur wenn sich etwas geändert hat. 20 aktuelle und 60 Tage tägliche.",
        "On start, every 6 hours and before every update – only when something changed. 20 recent plus 60 days of dailies.");

    // ---- location ------------------------------------------------------------------------------------------------------------
    public static readonly Msg LocationKicker = new("Ort & Autostart", "Location & autostart");
    public static readonly Msg LocationTitle = new("Wohin kommt PlatinVault?", "Where does PlatinVault go?");
    public static readonly Msg LocationSubtitle = new(
        "Installiert nur für dich – ohne Administratorrechte. Daten und Sicherungen liegen getrennt vom Programm.",
        "Installed just for you – no administrator rights. Data and backups are kept apart from the program.");
    public static readonly Msg ProgramFolder = new("Programmordner", "Program folder");
    public static readonly Msg ChangeFolder = new("Ändern …", "Change …");
    public static Msg DataFolder(string path) => new($"Daten und Sicherungen: {path}", $"Data and backups: {path}");
    public static readonly Msg AutostartTitle = new("Mit Windows starten", "Start with Windows");
    public static readonly Msg AutostartText = new(
        "Startet unauffällig neben der Uhr – nötig, damit Trophäen auch ohne geöffnetes Fenster erkannt werden.",
        "Starts quietly next to the clock – needed so trophies are noticed without an open window.");
    public static readonly Msg StartMenuTitle = new("Eintrag im Startmenü", "Start menu entry");
    public static readonly Msg DesktopTitle = new("Verknüpfung auf dem Desktop", "Desktop shortcut");
    public static Msg FolderUnsafe(string path) => new(
        $"„{path}“ kann nicht der Programmordner sein (Laufwerk, Benutzer- oder Systemordner). Bitte einen eigenen Unterordner wählen, z. B. …\\PlatinVault.",
        $"“{path}” cannot be the program folder (a drive, user or system folder). Please choose a folder of its own, e.g. …\\PlatinVault.");
    public static Msg FolderNotEmpty(string path) => new(
        $"Im Ordner „{path}“ liegen schon andere Dateien. Bitte einen leeren Ordner oder einen neuen Unterordner wählen.",
        $"The folder “{path}” already contains other files. Please choose an empty folder or a new subfolder.");
    public static Msg FolderSpace(string needed, string free) => new(
        $"Benötigt etwa {needed}, frei sind {free}.",
        $"Needs about {needed}, {free} free.");

    // ---- installing ----------------------------------------------------------------------------------------------------------
    public static readonly Msg InstallKicker = new("Installation", "Installing");
    public static readonly Msg InstallTitle = new("PlatinVault wird installiert", "Installing PlatinVault");
    public static readonly Msg InstallSubtitle = new("Das dauert meist weniger als eine Minute.", "This usually takes less than a minute.");
    public static readonly Msg UpdateTitle = new("PlatinVault wird aktualisiert", "Updating PlatinVault");
    public static readonly Msg FailedTitle = new("Installation unterbrochen", "Setup interrupted");
    public static readonly Msg FailedSubtitle = new(
        "Der Grund steht unten vollständig. Deine Daten wurden nicht verändert.",
        "The full reason is shown below. Your data was not changed.");

    public static readonly Msg TaskPrepare = new("Voraussetzungen prüfen", "Checking requirements");
    public static readonly Msg TaskStop = new("Laufendes PlatinVault beenden und sichern", "Closing and backing up the running PlatinVault");
    public static readonly Msg TaskFiles = new("Programmdateien kopieren", "Copying program files");
    public static readonly Msg TaskWebView = new("Fenster-Laufzeit prüfen (WebView2)", "Checking the window runtime (WebView2)");
    public static readonly Msg TaskDatabase = new("Datenbank einrichten", "Setting up the database");
    public static readonly Msg TaskBackupExisting = new("Vorhandene Datenbank sichern", "Backing up the existing database");
    public static readonly Msg TaskShortcuts = new("Verknüpfungen und Autostart", "Shortcuts and autostart");

    public static readonly Msg Elevated = new(
        "Die Installation läuft mit Administratorrechten. PlatinVault wird nur für dich installiert – als Administrator landeten Programm und Daten womöglich im Konto des Administrators statt in deinem. Bitte die Installationsdatei normal per Doppelklick starten (nicht „Als Administrator ausführen“).",
        "Setup is running with administrator rights. PlatinVault is installed just for you – as administrator, program and data could end up in the administrator's account instead of yours. Please start the setup file normally with a double-click (not “Run as administrator”).");
    public static Msg NoSpace(string needed, string free) => new(
        $"Auf dem Laufwerk ist nicht genug Platz: benötigt etwa {needed}, frei sind {free}.",
        $"There is not enough space on the drive: needs about {needed}, {free} free.");
    public static readonly Msg PayloadBroken = new(
        "Die Installationsdatei ist beschädigt oder unvollständig heruntergeladen (Prüfsumme stimmt nicht). Bitte erneut herunterladen.",
        "The setup file is damaged or was not downloaded completely (checksum mismatch). Please download it again.");
    public static Msg CopyFailed(string folder, string reason) => new(
        $"Die Programmdateien konnten nicht nach „{folder}“ kopiert werden: {reason}",
        $"The program files could not be copied to “{folder}”: {reason}");
    public static Msg WebViewFailed(string reason) => new(
        $"Die „Microsoft Edge WebView2 Runtime“ fehlt und konnte nicht installiert werden: {reason} Installiere sie von https://developer.microsoft.com/microsoft-edge/webview2/ und starte die Installation erneut.",
        $"The “Microsoft Edge WebView2 Runtime” is missing and could not be installed: {reason} Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and run setup again.");
    public static Msg DatabaseFailed(string reason) => new(
        $"Die Datenbank konnte nicht eingerichtet werden: {reason}",
        $"The database could not be set up: {reason}");
    public static Msg BackupFailed(string reason) => new(
        $"Vor dem Update ließ sich keine Sicherung der Datenbank anlegen: {reason} Trotzdem aktualisieren? Die automatischen Sicherungen der letzten Tage bleiben erhalten (Ordner backups).",
        $"No backup of the database could be made before the update: {reason} Update anyway? The automatic backups of the last days are kept (backups folder).");
    public static Msg BackupFailedCancelled(string reason) => new(
        $"Update abgebrochen: Vor dem Update ließ sich keine Sicherung der Datenbank anlegen. Es wurde nichts verändert. Grund: {reason}",
        $"Update cancelled: no backup of the database could be made before the update. Nothing was changed. Reason: {reason}");
    public static readonly Msg UpdateAnyway = new("Trotzdem aktualisieren", "Update anyway");
    public static readonly Msg CancelUpdate = new("Update abbrechen", "Cancel update");
    public static Msg UpdateRolledBack(Msg reason) => new(
        $"Das Update ist fehlgeschlagen, die bisherige Version wurde wiederhergestellt und lässt sich wie gewohnt starten. Deine Daten wurden vor dem Update gesichert (Ordner backups\\recent). Grund: {reason.De}",
        $"The update failed; the previous version was restored and starts as usual. Your data was backed up before the update (backups\\recent folder). Reason: {reason.En}");
    public static Msg RollBackFailed(Msg reason, string backup, string error) => new(
        $"Das Update ist fehlgeschlagen und die bisherige Version ließ sich nicht zurückholen ({error}). Sie liegt unverändert in „{backup}“; bitte die Installation erneut ausführen. Grund: {reason.De}",
        $"The update failed and the previous version could not be put back ({error}). It is unchanged in “{backup}”; please run the setup again. Reason: {reason.En}");

    public static readonly Msg CancelQuestion = new(
        "Installation abbrechen? Es wurde noch nichts installiert.",
        "Cancel setup? Nothing has been installed yet.");
    public static readonly Msg CancelYes = new("Installation abbrechen", "Cancel setup");
    public static readonly Msg CancelNo = new("Weiter installieren", "Continue setup");

    // ---- done ----------------------------------------------------------------------------------------------------------------
    public static readonly Msg DoneKicker = new("Geschafft", "All set");
    public static readonly Msg DoneTitle = new("PlatinVault ist bereit", "PlatinVault is ready");
    public static readonly Msg DoneUpdatedTitle = new("PlatinVault ist aktuell", "PlatinVault is up to date");
    public static readonly Msg DoneSubtitle = new(
        "Verbinde in den Einstellungen dein Steam-Konto – danach erscheinen deine Spiele. Viel Spaß auf dem Weg zu Platin!",
        "Connect your Steam account in the settings – your games appear afterwards. Have fun on your way to platinum!");
    public static readonly Msg LaunchNow = new("PlatinVault jetzt starten", "Start PlatinVault now");
    public static readonly Msg TipTrayTitle = new("Neben der Uhr", "Next to the clock");
    public static readonly Msg TipTrayText = new("Das Fenster schließen beendet PlatinVault nicht – Beenden geht über das Symbol.", "Closing the window does not quit PlatinVault – quit from the icon.");
    public static readonly Msg TipHotkeyTitle = new("Strg+Alt+G im Spiel", "Ctrl+Alt+G in game");
    public static readonly Msg TipHotkeyText = new("Öffnet den Guide zum laufenden Spiel über dem Spiel.", "Opens the guide for the running game over the game.");
}
