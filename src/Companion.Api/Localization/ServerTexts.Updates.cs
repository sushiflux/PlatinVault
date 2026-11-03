namespace Companion.Api.Localization;

/// <summary>
/// The texts of the update check ("Beta-Versionen erhalten", Settings > System > Updates) and of the start guard against a database
/// of a newer PlatinVault (part of <see cref="ServerTexts"/>, kept in its own file). Same rules as there: parameterised text = method,
/// fixed text = field, English line directly behind the German one.
/// </summary>
public static partial class ServerTexts
{
    // ---- Update check (GitHub releases of the public repository) ---------------------------------------------------------

    public static class Updates
    {
        public static readonly Msg CheckFailedTitle = M("Update-Prüfung fehlgeschlagen", "Update check failed");

        public static Msg Unreachable(string reason) => M(
            $"GitHub ist von diesem PlatinVault aus nicht erreichbar, deshalb ließ sich nicht prüfen, ob es eine neue Version gibt. Prüfe die Internetverbindung des Rechners bzw. Servers, auf dem PlatinVault läuft (Firewall, DNS, Proxy). Meldung des Systems: {reason}",
            $"GitHub cannot be reached from this PlatinVault, so it could not check for a new version. Check the internet connection of the computer or server PlatinVault runs on (firewall, DNS, proxy). System message: {reason}");

        public static Msg Timeout(int seconds) => M(
            $"GitHub hat nicht innerhalb von {seconds} Sekunden geantwortet. Die Update-Prüfung wurde abgebrochen; versuche es in ein paar Minuten erneut.",
            $"GitHub did not answer within {seconds} seconds. The update check was stopped; please try again in a few minutes.");

        public static Msg RateLimited(int minutes, string resetUtc) => M(
            $"GitHub lässt ohne Anmeldung nur 60 Abfragen pro Stunde je Internetanschluss zu, und dieses Kontingent ist gerade aufgebraucht (auch andere Programme in deinem Netz zählen mit). Die nächste Prüfung ist in etwa {minutes} Minuten möglich (ab {resetUtc} UTC).",
            $"Without signing in, GitHub allows only 60 requests per hour per internet connection, and that quota is used up right now (other programs in your network count too). The next check is possible in about {minutes} minutes (from {resetUtc} UTC).");

        public static Msg NotFound(string repository) => M(
            $"Die Release-Liste von „{repository}“ wurde auf GitHub nicht gefunden (HTTP 404). Das Repository ist umgezogen oder nicht öffentlich; bitte melde das dem Support.",
            $"The release list of “{repository}” was not found on GitHub (HTTP 404). The repository has moved or is not public; please report this to support.");

        public static Msg HttpStatus(int status, string reason) => M(
            $"GitHub hat die Update-Prüfung mit HTTP {status} ({reason}) abgelehnt. Versuche es später erneut.",
            $"GitHub refused the update check with HTTP {status} ({reason}). Please try again later.");

        public static Msg InvalidAnswer(string reason) => M(
            $"Die Antwort von GitHub war keine lesbare Release-Liste, deshalb wurde nichts angeboten. Technische Meldung: {reason}",
            $"The answer from GitHub was not a readable release list, so nothing is offered. Technical message: {reason}");

        public static readonly Msg VersionUnknown = M(
            "Die laufende Version ist unbekannt (die Datei version.txt fehlt oder ist unlesbar), deshalb kann PlatinVault nicht vergleichen, ob eine neuere Version existiert.",
            "The running version is unknown (the file version.txt is missing or unreadable), so PlatinVault cannot compare whether a newer version exists.");

        // ---- Installing from the settings (Windows program only) ------------------------------------------------------

        public static readonly Msg InstallTitle = M("Update nicht gestartet", "Update not started");

        public static readonly Msg InstallOnlyDesktop = M(
            "Updates installiert nur das Windows-Programm selbst. Ein Container (Unraid, NAS, Docker) wird über seinen Image-Tag aktualisiert, das Linux-Paket durch erneutes Ausführen seiner install.sh – die Anleitung steht unter Einstellungen → System → Updates.",
            "Only the Windows program installs updates itself. A container (Unraid, NAS, Docker) is updated through its image tag, the Linux package by running its install.sh again – see Settings → System → Updates.");

        public static readonly Msg InstallNotAllowed = M(
            "In dieser Umgebung (Entwicklungs- oder Testlauf) installiert PlatinVault keine Updates.",
            "PlatinVault does not install updates in this environment (development or test run).");

        public static Msg InstallNotOffered(string tag) => M(
            $"Die Version {tag} wird gerade nicht als Update angeboten (die Prüfung hat inzwischen etwas anderes ergeben oder die Version hat keinen Windows-Installer). Suche erneut nach Updates.",
            $"Version {tag} is not offered as an update right now (the check has found something else since, or the version has no Windows installer). Check for updates again.");

        public static readonly Msg InstallChecksumMismatch = M(
            "Der heruntergeladene Installer stimmt nicht mit der veröffentlichten Prüfsumme überein und wurde gelöscht. Es wurde nichts installiert. Versuche es später erneut.",
            "The downloaded installer does not match the published checksum and was deleted. Nothing was installed. Please try again later.");

        public static Msg InstallSignatureRejected(string reason) => M(
            $"Die digitale Signatur des heruntergeladenen Installers ist nicht in Ordnung: Sie fehlt, ist ungültig oder stammt nicht vom selben Herausgeber wie dein installiertes PlatinVault oder gehört nicht zu PlatinVault. Der Installer wurde gelöscht und nicht gestartet, es wurde nichts installiert. Versuche es später erneut oder lade den Installer von Hand von der offiziellen Release-Seite. Technische Einzelheiten: {reason}",
            $"The digital signature of the downloaded installer is not acceptable: it is missing, invalid, from a different publisher than your installed PlatinVault or not a PlatinVault installer. The installer was deleted and not started, nothing was installed. Try again later or download the installer by hand from the official release page. Technical details: {reason}");

        public static Msg InstallProgramSignatureInvalid(string reason) => M(
            $"Die digitale Signatur deines installierten PlatinVault ist nicht mehr gültig (Dateien verändert oder beschädigt?), deshalb lässt sich ein Update nicht sicher prüfen. Es wurde nichts installiert. Installiere PlatinVault bitte von Hand neu: Lade den Installer von der offiziellen Release-Seite herunter und führe ihn aus, deine Daten bleiben dabei erhalten. Technische Einzelheiten: {reason}",
            $"The digital signature of your installed PlatinVault is no longer valid (files changed or damaged?), so an update cannot be checked safely. Nothing was installed. Please reinstall PlatinVault by hand: download the installer from the official release page and run it, your data is kept. Technical details: {reason}");

        public static Msg InstallFailed(string reason) => M(
            $"Der Installer konnte nicht geladen oder gestartet werden: {reason}",
            $"The installer could not be downloaded or started: {reason}");
    }

    // ---- Start guard: the database belongs to a newer PlatinVault --------------------------------------------------------

    public static class DatabaseGuard
    {
        public static readonly Msg NewerTitle = M("Datenbank einer neueren PlatinVault-Version", "Database of a newer PlatinVault version");

        /// <param name="needed">The readable version that last changed the database (null when it is not recorded).</param>
        /// <param name="running">The readable running version.</param>
        /// <param name="since">Date of the newest unknown schema change (yyyy-MM-dd), the fallback when no version is recorded.</param>
        /// <param name="unknown">The ids of the schema changes this version does not know.</param>
        public static Msg Newer(string? needed, string running, string since, string unknown) => M(
            (needed is null
                ? $"Diese Datenbank wurde bereits von einer neueren PlatinVault-Version benutzt (Stand {since} oder später). "
                : $"Diese Datenbank wurde bereits von PlatinVault {needed} benutzt. ")
            + $"Du startest gerade PlatinVault {running}, das ihren Aufbau nicht kennt. Damit nichts beschädigt wird, startet PlatinVault nicht und ändert nichts – deine Daten sind unverändert. "
            + (needed is null
                ? "Installiere wieder die neuere Version oder warte auf das nächste Release und installiere es. "
                : $"Installiere wieder PlatinVault {needed} (oder neuer) oder warte auf das nächste Release und installiere es. ")
            + $"Unbekannte Änderungen: {unknown}",
            (needed is null
                ? $"This database has already been used by a newer PlatinVault version (from {since} or later). "
                : $"This database has already been used by PlatinVault {needed}. ")
            + $"You are starting PlatinVault {running}, which does not know its structure. So that nothing gets damaged, PlatinVault does not start and changes nothing – your data is untouched. "
            + (needed is null
                ? "Install the newer version again, or wait for the next release and install it. "
                : $"Install PlatinVault {needed} (or newer) again, or wait for the next release and install it. ")
            + $"Unknown changes: {unknown}");

        /// <param name="file">The database file.</param>
        /// <param name="technical">What SQLite reported (for example "SQLite Error 14: 'unable to open database file'.", not translated).</param>
        public static Msg CannotOpen(string file, string technical) => M(
            $"PlatinVault konnte die Datenbankdatei {file} nicht öffnen oder ihre Hilfsdateien ({Path.GetFileName(file)}-wal und {Path.GetFileName(file)}-shm) im selben Ordner nicht anlegen. "
            + "Häufige Ursache, besonders unter Windows: Ein Virenschutz oder ein anderes Sicherheitsprogramm hat Dateien im Datenordner blockiert oder in Quarantäne verschoben. "
            + "Weitere Ursachen: Der Ordner ist nicht beschreibbar, existiert nicht mehr oder die Platte ist voll. "
            + "So geht es weiter: Prüfe die Quarantäne bzw. das Schutzprotokoll deines Virenschutzes und stelle blockierte Dateien aus dem Datenordner wieder her (oder trage den Ordner dort als Ausnahme ein), "
            + "starte den Rechner neu und wiederhole dann die Installation bzw. den Start. "
            + "Deine Daten bleiben erhalten: PlatinVault hat nichts gelöscht oder verändert. Wurde sogar die Datenbankdatei selbst in die Quarantäne verschoben, lässt sie sich von dort zurückholen; "
            + $"die automatischen Sicherungen liegen im Ordner „backups“. Technische Meldung: {technical}",
            $"PlatinVault could not open the database file {file} or create its helper files ({Path.GetFileName(file)}-wal and {Path.GetFileName(file)}-shm) in the same folder. "
            + "Common cause, especially on Windows: a virus scanner or another security program has blocked files in the data folder or moved them to quarantine. "
            + "Other causes: the folder is not writable, no longer exists, or the disk is full. "
            + "What to do: check the quarantine or protection log of your virus scanner and restore blocked files from the data folder (or add the folder there as an exception), "
            + "restart the computer, then repeat the installation or the start. "
            + "Your data is kept: PlatinVault has not deleted or changed anything. Even if the database file itself was moved to quarantine, it can be restored from there; "
            + $"the automatic backups are in the “backups” folder. Technical message: {technical}");

        /// <param name="file">The database file.</param>
        /// <param name="detail">What SQLite reported (quick_check result or error message, not translated).</param>
        public static Msg Damaged(string file, string detail) => M(
            $"Die Datenbankdatei {file} ist beschädigt (SQLite-Prüfung: {detail}). Damit nichts verloren geht, startet PlatinVault nicht und "
            + "ändert nichts an der Datei. Stelle die letzte Sicherung wieder her (Ordner „backups“ neben der Datenbank bzw. im Datenordner; "
            + "Anleitung im Handbuch, Kapitel „Sicherungen“). Liegt die Datei auf einem Netzlaufwerk oder unter /mnt/user (Unraid), verschiebe "
            + "den Datenordner auf eine lokale Platte bzw. den Cache-Pool.",
            $"The database file {file} is damaged (SQLite check: {detail}). So that nothing is lost, PlatinVault does not start and does not "
            + "change the file. Restore the latest backup (the “backups” folder next to the database or in the data folder; see the handbook, "
            + "chapter “Backups”). If the file lies on a network drive or under /mnt/user (Unraid), move the data folder to a local disk or the "
            + "cache pool.");
    }

    // ---- Backups and restore (server commands --backup-now / --restore, the Windows program) -------------------------------

    public static class Backup
    {
        public static Msg ManualDone(string folder) => M($"Sicherung angelegt in {folder}.", $"Backup created in {folder}.");

        public static Msg ManualFailed(string folder) => M(
            $"Die Sicherung ist fehlgeschlagen (Ordner {folder}). Die Gründe stehen in den Zeilen darüber.",
            $"The backup failed (folder {folder}). The reasons are in the lines above.");

        public static readonly Msg RestoreNoFile = M(
            "Bitte die Sicherung angeben: --restore <Datei> (z. B. backups/recent/platinvault-20261102-180000.db).",
            "Please name the backup: --restore <file> (e.g. backups/recent/platinvault-20261102-180000.db).");

        public static Msg RestoreStarting(string file, string database) => M(
            $"Stelle {file} wieder her – ersetzt {database}. Der aktuelle Stand wird vorher gesichert.",
            $"Restoring {file} – replaces {database}. The current state is backed up first.");

        public static readonly Msg RestoreDone = M(
            "Wiederhergestellt. Beim nächsten Start bringt PlatinVault die Datenbank selbst auf den aktuellen Stand. Zeilen je Tabelle:",
            "Restored. On its next start PlatinVault brings the database up to date by itself. Rows per table:");

        public static Msg RestoreMissing(string file) => M($"Die Sicherung {file} gibt es nicht.", $"The backup {file} does not exist.");

        public static Msg RestoreUnreadable(string file) => M(
            $"Die Sicherung {file} ist nicht lesbar oder beschädigt. Es wurde nichts verändert.",
            $"The backup {file} cannot be read or is damaged. Nothing was changed.");

        public static Msg RestoreNotPlatinVault(string file) => M(
            $"{file} ist keine Sicherung von PlatinVault. Es wurde nichts verändert.",
            $"{file} is not a PlatinVault backup. Nothing was changed.");

        public static Msg RestoreNewer(string file) => M(
            $"Die Sicherung {file} stammt von einer neueren PlatinVault-Version, die diese Version nicht lesen kann. Installiere zuerst die neuere Version. Es wurde nichts verändert.",
            $"The backup {file} comes from a newer PlatinVault version that this version cannot read. Install the newer version first. Nothing was changed.");

        public static Msg RestoreInUse(string database) => M(
            $"Die Datenbank {database} wird gerade benutzt. Beende PlatinVault (bzw. stoppe den Container) und versuche es erneut. Es wurde nichts ersetzt.",
            $"The database {database} is in use right now. Quit PlatinVault (or stop the container) and try again. Nothing was replaced.");

        public static readonly Msg RestoreNoSafetyBackup = M(
            "Vom aktuellen Stand konnte keine Sicherheitskopie angelegt werden – die Wiederherstellung wurde abgebrochen, es wurde nichts verändert.",
            "No safety copy of the current state could be made – the restore was cancelled, nothing was changed.");
    }
}
