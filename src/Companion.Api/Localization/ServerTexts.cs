namespace Companion.Api.Localization;

/// <summary>
/// Every text the API sends to the user, in German and English (see <see cref="Msg"/> for why plain C#). Grouped by area.
/// Rules: a parameterised text is a method (the compiler checks the arguments), a fixed text is a field; the English line sits
/// directly behind the German one; user-facing names of screens match the UI labels in the frontend's i18n files
/// ("KI-Recherche" / "AI research", "PlatinVault Bridge", "Twitch & IGDB"). Texts for operators (log lines) are not here.
/// The AI prompts and the texts that end up INSIDE a guide are language-parameterised separately (<see cref="GuideTexts"/>).
/// </summary>
public static partial class ServerTexts
{
    private static Msg M(string de, string en) => new(de, en);

    // ---- Shared by many endpoints --------------------------------------------------------------------------------------

    public static readonly Msg InvalidRequestTitle = M("Ungültige Anfrage", "Invalid request");
    public static readonly Msg GameNotFoundTitle = M("Spiel nicht gefunden", "Game not found");

    public static Msg GameNotFound(string slug) => M($"Kein Spiel mit Slug '{slug}' gefunden.", $"No game with slug '{slug}' found.");

    public static Msg GuideNotFound(string slug) => M($"Kein Guide unter '{slug}' gefunden.", $"No guide found under '{slug}'.");

    public static readonly Msg SecurityHeaderTitle = M("Sicherheits-Header fehlt", "Security header missing");
    public static readonly Msg SecurityHeaderDetail = M("Anfrage abgelehnt: X-Requested-With Header ist erforderlich.", "Request rejected: the X-Requested-With header is required.");

    // DNS rebinding protection (HostGuard): plain text, both languages at once - the request may not even reach the language middleware's
    // idea of who is asking, and a reverse-proxy user must understand it in any case
    public static Msg HostRefused(string host) => Msg.Same(
        $"PlatinVault hat diese Anfrage abgelehnt: Der Name \"{host}\" ist nicht freigegeben (Schutz gegen DNS-Rebinding). Frei sind IP-Adressen, localhost, Namen ohne Punkt (z. B. tower) und Namen auf .local, .lan, .home.arpa, .internal und .fritz.box. Rufst du PlatinVault absichtlich unter \"{host}\" auf (z. B. über einen Reverse-Proxy mit eigener Domain), setze im Container die Variable PLATINVAULT_ALLOWED_HOSTS={host} (mehrere mit ; trennen, *.example.com für alle Unternamen) und starte ihn neu.\n\n" +
        $"PlatinVault refused this request: the name \"{host}\" is not allowed (protection against DNS rebinding). Allowed are IP addresses, localhost, names without a dot (e.g. tower) and names ending in .local, .lan, .home.arpa, .internal and .fritz.box. If you open PlatinVault under \"{host}\" on purpose (e.g. through a reverse proxy with your own domain), set the container variable PLATINVAULT_ALLOWED_HOSTS={host} (separate several with ;, *.example.com for every name below it) and restart it.\n");

    public static readonly Msg TooManyRequestsTitle = M("Zu viele Anfragen", "Too many requests");
    public static readonly Msg TooManyRequestsDetail = M("Zu viele Anfragen in kurzer Zeit. Bitte warte einen Moment und versuche es erneut.", "Too many requests in a short time. Please wait a moment and try again.");

    // ---- Realtime bridge -----------------------------------------------------------------------------------------------

    public static class Bridge
    {
        public static readonly Msg UnauthorizedTitle = M("Bridge nicht autorisiert", "Bridge not authorized");
        public static readonly Msg UnauthorizedDetail = M(
            "Der Bridge-Token fehlt oder ist ungültig. Erzeuge in den Einstellungen unter „PlatinVault Bridge“ einen neuen Token.",
            "The bridge token is missing or invalid. Create a new token in Settings under “PlatinVault Bridge”.");

        public static readonly Msg StateNotJson = M("Der Bridge-Status ist kein gültiges JSON im erwarteten Format.", "The bridge status is not valid JSON in the expected format.");
        public static readonly Msg RunningAppIdInvalid = M("runningAppId fehlt oder liegt außerhalb des gültigen Bereichs.", "runningAppId is missing or outside the valid range.");
        public static Msg TooManyAchievements(int max) => M($"Pro Spiel sind höchstens {max} Achievements erlaubt.", $"At most {max} achievements are allowed per game.");
        public static readonly Msg ApiNameMissing = M("Jedes Achievement braucht einen apiName (höchstens 200 Zeichen).", "Every achievement needs an apiName (at most 200 characters).");
        public static readonly Msg SteamId64Invalid = M("steamId64 muss 17 Ziffern haben.", "steamId64 must have 17 digits.");
        public static readonly Msg LibraryNotJson = M("Die Bibliotheksdaten der Bridge sind kein gültiges JSON im erwarteten Format.", "The bridge's library data is not valid JSON in the expected format.");
        public static Msg TooManyLibraryGames(int max) => M($"Die Bibliotheksdaten dürfen höchstens {max} Spiele enthalten.", $"The library data may hold at most {max} games.");

        public static readonly Msg OtherAccountTitle = M("Anderes Steam-Konto", "Different Steam account");

        public static Msg OtherAccountDetail(string reported, string stored) => M(
            $"Die Bridge meldet das Steam-Konto {reported}, in den Einstellungen ist {stored} hinterlegt. Melde dich am Spiele-PC mit dem hinterlegten Konto an oder ändere die SteamID in den Einstellungen.",
            $"The bridge reports Steam account {reported}, but {stored} is stored in the settings. Sign in on the gaming PC with the stored account or change the SteamID in the settings.");

        public static readonly Msg NoBridgeTitle = M("Keine Bridge vorhanden", "No bridge available");

        public static readonly Msg MissingHintDevelopment = M(
            "Auf diesem Server liegt keine Bridge bereit. Baue sie im Repository mit .\\scripts\\build-bridge.ps1 (Ergebnis: artifacts\\bridge).",
            "There is no bridge on this server. Build it in the repository with .\\scripts\\build-bridge.ps1 (result: artifacts\\bridge).");

        public static readonly Msg MissingHint = M(
            "Auf diesem Server liegt keine Bridge bereit. Dieses Image wurde ohne PlatinVault.Bridge.exe gebaut – bitte eine aktuelle Version verwenden.",
            "There is no bridge on this server. This image was built without PlatinVault.Bridge.exe – please use a current version.");

        public static readonly Msg MissingHintLinuxDevelopment = M(
            "Auf diesem Server liegt keine Linux-Bridge bereit. Baue sie im Repository unter Linux oder WSL mit scripts/build-bridge-linux.sh (Ergebnis: artifacts/bridge-linux).",
            "There is no Linux bridge on this server. Build it in the repository on Linux or WSL with scripts/build-bridge-linux.sh (result: artifacts/bridge-linux).");

        public static readonly Msg MissingHintLinux = M(
            "Auf diesem Server liegt keine Linux-Bridge bereit. Dieses Image oder Paket wurde ohne die Linux-Bridge gebaut – bitte eine aktuelle Version verwenden.",
            "There is no Linux bridge on this server. This image or package was built without the Linux bridge – please use a current version.");

        public static Msg PackageOsInvalid(string os) => M(
            $"Unbekanntes Betriebssystem '{os}'. Erlaubt sind 'windows' und 'linux'.",
            $"Unknown operating system '{os}'. Allowed are 'windows' and 'linux'.");

        public static readonly Msg PackageBodyInvalid = M(
            "Der Body muss JSON der Form {\"origin\":\"http://server:port\"} sein.",
            "The body must be JSON of the form {\"origin\":\"http://server:port\"}.");

        public static readonly Msg ServerAddressMissingTitle = M("Server-Adresse fehlt", "Server address missing");
        public static readonly Msg ServerAddressMissingDetail = M(
            "Bitte sende die Adresse, unter der du PlatinVault geöffnet hast (origin, z. B. http://192.168.1.20:8080).",
            "Please send the address you opened PlatinVault with (origin, e.g. http://192.168.1.20:8080).");

        public static readonly Msg ServerAddressInvalidTitle = M("Ungültige Server-Adresse", "Invalid server address");

        public static Msg ServerAddressInvalidDetail(string origin) => M(
            $"Die Adresse „{origin}“ passt nicht zu diesem Server. Öffne PlatinVault über die Adresse, die auch der Gaming-PC erreicht, und lade das Paket von dort herunter.",
            $"The address “{origin}” does not match this server. Open PlatinVault through the address the gaming PC can reach too, and download the package from there.");
    }

    // ---- Games, guides, library ----------------------------------------------------------------------------------------

    public static class Games
    {
        public static readonly Msg InvalidSteamAppIdTitle = M("Ungültige Steam-AppID", "Invalid Steam AppID");
        public static readonly Msg InvalidSteamAppIdDetail = M("Die Steam-AppID muss eine positive Zahl sein.", "The Steam AppID must be a positive number.");
        public static readonly Msg InvalidAppIdTitle = M("Ungültige AppID", "Invalid AppID");

        public static readonly Msg AlreadyLinkedTitle = M("Bereits verknüpft", "Already linked");

        public static Msg AlreadyLinkedDetail(int appId) => M(
            $"Die Steam-AppID {appId} gehört schon zu einem anderen Spiel in deiner Sammlung.",
            $"Steam AppID {appId} already belongs to another game in your collection.");

        public static readonly Msg SteamGameNotFoundTitle = M("Steam-Spiel nicht gefunden", "Steam game not found");

        public static Msg SteamGameNotFoundDetail(int appId) => M($"Steam kennt kein Spiel mit der AppID {appId}.", $"Steam does not know a game with AppID {appId}.");

        public static readonly Msg OrderMissingTitle = M("Reihenfolge fehlt", "Order missing");
        public static readonly Msg OrderMissingDetail = M(
            "Bitte die Spiele als Liste von Slugs senden: {\"slugs\": [\"spiel-a\", \"spiel-b\"]}.",
            "Please send the games as a list of slugs: {\"slugs\": [\"game-a\", \"game-b\"]}.");

        public static readonly Msg TooManyGamesTitle = M("Zu viele Spiele", "Too many games");

        public static Msg TooManyGamesDetail(int count, int max) => M(
            $"Die Liste enthält {count} Einträge, erlaubt sind höchstens {max}.",
            $"The list has {count} entries, at most {max} are allowed.");

        public static readonly Msg InvalidOrderTitle = M("Ungültige Reihenfolge", "Invalid order");
        public static readonly Msg OrderEmptyEntry = M("Die Liste enthält einen leeren Eintrag.", "The list contains an empty entry.");

        public static Msg OrderDuplicates(List<string> slugs)
        {
            var list = DescribeSlugs(slugs);
            return M($"Jedes Spiel darf nur einmal in der Liste stehen. Doppelt: {list.De}.", $"Every game may appear only once in the list. Duplicated: {list.En}.");
        }

        public static readonly Msg UnknownGamesTitle = M("Unbekannte Spiele", "Unknown games");

        public static Msg UnknownGamesDetail(List<string> slugs)
        {
            var list = DescribeSlugs(slugs);
            return M(
                $"Diese Spiele gibt es nicht (mehr): {list.De}. Bitte die Seite neu laden und die Reihenfolge noch einmal setzen.",
                $"These games do not exist (any more): {list.En}. Please reload the page and set the order again.");
        }

        public static readonly Msg TitleMissingTitle = M("Titel fehlt", "Title missing");
        public static readonly Msg TitleMissingDetail = M("Bitte gib einen gültigen Spieltitel ein.", "Please enter a valid game title.");
        public static readonly Msg GameTitleMissingTitle = M("Spieltitel fehlt", "Game title missing");
        public static readonly Msg GameTitleMissingDetail = M("Bitte gib den Namen des Spiels an.", "Please enter the name of the game.");
        public static readonly Msg InputIncompleteTitle = M("Eingabe unvollständig", "Input incomplete");
        public static readonly Msg InputIncompleteDetail = M("Bitte gib sowohl einen Tab-Titel als auch ein Thema für die KI-Recherche an.", "Please enter both a tab title and a topic for the AI research.");
        public static readonly Msg NoSteamAppIdTitle = M("Keine Steam-AppID", "No Steam AppID");
        public static readonly Msg NoSteamAppIdDetail = M("Statistiken gibt es nur für Spiele mit Steam-AppID.", "Statistics are only available for games with a Steam AppID.");
        public static readonly Msg NoteInvalidDetail = M("AppId und ApiName sind erforderlich.", "AppId and ApiName are required.");

        public static readonly Msg AiResearchFailedTitle = M("KI-Recherche fehlgeschlagen", "AI research failed");
        public static readonly Msg AiAnalysisFailedTitle = M("Spiel konnte nicht analysiert werden", "The game could not be analysed");
        public static readonly Msg AiCompleteFailedTitle = M("Guide konnte nicht vervollständigt werden", "The guide could not be completed");
        public static readonly Msg AiExpandFailedTitle = M("KI-Tab-Erstellung fehlgeschlagen", "Creating the tab with AI failed");
        public static readonly Msg AiLoreFailedTitle = M("Story-&-Lore-Recherche fehlgeschlagen", "Story & Lore research failed");

        public static readonly Msg LibraryUnavailableTitle = M("Steam-Bibliothek nicht verfügbar", "Steam library not available");
        public static readonly Msg LibraryLoadFailedTitle = M("Steam-Bibliothek konnte nicht geladen werden", "The Steam library could not be loaded");
        public static readonly Msg SettingsInvalidTitle = M("Einstellung ungültig", "Invalid setting");

        /// <summary>Name of a game created from a bare Steam AppID.</summary>
        public static Msg SteamAppName(int appId) => M($"Steam-App {appId}", $"Steam app {appId}");

        /// <summary>At most five slugs, so a huge bad list does not produce a huge answer.</summary>
        private static Msg DescribeSlugs(List<string> slugs)
        {
            string Shorten(string s) => s.Length > 80 ? s[..80] : s;
            var shown = slugs.Take(5).Select(Shorten).ToList();
            var de = string.Join(", ", shown.Select(s => $"„{s}“"));
            var en = string.Join(", ", shown.Select(s => $"“{s}”"));
            return slugs.Count > 5 ? M($"{de} und {slugs.Count - 5} weitere", $"{en} and {slugs.Count - 5} more") : M(de, en);
        }
    }

    // ---- Settings ------------------------------------------------------------------------------------------------------

    public static class Settings
    {
        public static Msg TwitchCredentialInvalid(string label) => M(
            $"Twitch {label} ist ungültig: erlaubt sind 8 bis 128 Buchstaben, Ziffern, Bindestriche und Unterstriche, ohne Leerzeichen. Bitte direkt aus der Twitch-Developer-Konsole kopieren.",
            $"Twitch {label} is invalid: 8 to 128 letters, digits, hyphens and underscores are allowed, without spaces. Please copy it straight from the Twitch developer console.");

        /// <summary>Names both languages in both: a wrong value is a client mistake, and the user may not read the language the rest is in.</summary>
        public static Msg LanguageUnsupported(string value) => Msg.Same(
            $"Sprache \"{value}\" wird nicht unterstützt – erlaubt sind \"de\" und \"en\". / Language \"{value}\" is not supported – allowed: \"de\" and \"en\".");

        public static Msg PopupPositionInvalid(string value) => Msg.Same(
            $"Popup-Position \"{value}\" ist unbekannt – erlaubt sind \"top-left\", \"top-center\", \"top-right\", \"center-left\", \"center\", \"center-right\", \"bottom-left\", \"bottom-center\", \"bottom-right\" und \"fullscreen\". / Popup position \"{value}\" is unknown – allowed: \"top-left\", \"top-center\", \"top-right\", \"center-left\", \"center\", \"center-right\", \"bottom-left\", \"bottom-center\", \"bottom-right\" and \"fullscreen\".");

        public static Msg PopupTargetInvalid(string value) => Msg.Same(
            $"Anzeige des Trophäen-Popups \"{value}\" ist unbekannt – erlaubt sind \"app\", \"overlay\" und \"both\". / Trophy popup display \"{value}\" is unknown – allowed: \"app\", \"overlay\" and \"both\".");

        public static Msg PopupSecondsInvalid(double value) => Msg.Same(
            $"Anzeigedauer der Trophäen-Popups \"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" ist ungültig – erlaubt sind ganze Sekunden von {Companion.Api.Settings.SettingsStore.MinPopupSeconds} bis {Companion.Api.Settings.SettingsStore.MaxPopupSeconds}. / Trophy pop-up duration \"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" is invalid – allowed: whole seconds from {Companion.Api.Settings.SettingsStore.MinPopupSeconds} to {Companion.Api.Settings.SettingsStore.MaxPopupSeconds}.");

        public static Msg UpdateIntervalInvalid(string value) => Msg.Same(
            $"Update-Intervall \"{value}\" ist unbekannt – erlaubt sind \"startup\", \"4h\", \"daily\", \"weekly\", \"monthly\" und \"never\". / Update interval \"{value}\" is unknown – allowed: \"startup\", \"4h\", \"daily\", \"weekly\", \"monthly\" and \"never\".");

        public static Msg DashboardSortInvalid(string value, bool favorites) => new(
            $"Sortierung \"{value}\" ist ungültig – erwartet wird \"<Reihenfolge>:asc\" oder \"<Reihenfolge>:desc\" mit einer der Reihenfolgen {DashboardSortOrders(favorites)}.",
            $"Sort order \"{value}\" is invalid – expected \"<order>:asc\" or \"<order>:desc\" with one of the orders {DashboardSortOrders(favorites)}.");

        private static string DashboardSortOrders(bool favorites) =>
            string.Join(", ", (favorites ? new[] { Companion.Api.Settings.DashboardSort.Custom } : []).Concat(Companion.Api.Settings.DashboardSort.SharedOrders).Select(o => $"\"{o}\""));

        public static Msg AiSelectionModeInvalid(string value) => Msg.Same(
            $"KI-Auswahl \"{value}\" ist unbekannt – erlaubt sind \"default\", \"mixed\" und \"ask\". / AI selection \"{value}\" is unknown – allowed: \"default\", \"mixed\" and \"ask\".");

        public static Msg AiProviderInvalid(string value) => Msg.Same(
            $"KI \"{value}\" ist unbekannt – erlaubt sind \"gemini\", \"claude\" und \"openai\". / AI \"{value}\" is unknown – allowed: \"gemini\", \"claude\" and \"openai\".");

        public static Msg ResearchBlockInvalid(string group, string id) => Msg.Same(
            $"Recherche-Baustein \"{id}\" gibt es in der Gruppe \"{group}\" nicht. / The research block \"{id}\" does not exist in the group \"{group}\".");

        public static Msg PinsAfterUnlockInvalid(string value) => Msg.Same(
            $"Verhalten angehefteter Erfolge nach dem Freischalten \"{value}\" ist unbekannt – erlaubt sind \"keep\" und \"remove\". / Behaviour of pinned achievements after unlocking \"{value}\" is unknown – allowed: \"keep\" and \"remove\".");
    }

    // ---- Pinned achievements ("Im Blick") -------------------------------------------------------------------------------

    public static class Pins
    {
        public static readonly Msg LimitTitle = M("Maximum erreicht", "Limit reached");
        public static readonly Msg ConflictTitle = M("Gleichzeitig geändert", "Changed at the same time");

        public static readonly Msg InvalidDetail = M(
            "AppId und ApiName sind erforderlich; der ApiName darf höchstens 200 Zeichen lang sein.",
            "AppId and ApiName are required; the ApiName may be at most 200 characters long.");

        public static readonly Msg InvalidListDetail = M(
            "AppId und eine Liste von ApiNames sind erforderlich; jeder ApiName darf höchstens 200 Zeichen lang sein.",
            "AppId and a list of ApiNames are required; each ApiName may be at most 200 characters long.");

        public static Msg TooManyNames(int max) => M(
            $"Es können höchstens {max} Erfolge auf einmal gelöst werden.",
            $"At most {max} achievements can be unpinned at once.");

        public static Msg LimitReached(int max) => M(
            $"Es sind bereits {max} Erfolge angeheftet – das ist das Maximum pro Spiel. Löse zuerst einen Erfolg, dann kannst du einen neuen anheften.",
            $"{max} achievements are already pinned – that is the maximum per game. Unpin one first, then you can pin a new one.");

        public static readonly Msg ConflictDetail = M(
            "Die angehefteten Erfolge wurden gleichzeitig von einem anderen Fenster geändert und konnten nicht gespeichert werden. Bitte versuche es noch einmal.",
            "The pinned achievements were changed by another window at the same time and could not be saved. Please try again.");
    }

    // ---- Guide via the user's own AI subscription (copy the prompt, paste the answer) ---------------------------------------

    public static class Manual
    {
        public static readonly Msg AnswerEmpty = M("Es ist noch nichts eingefügt.", "Nothing has been pasted yet.");

        public static Msg TooLarge(int kilobytes, int maxKilobytes) => new(
            $"Der eingefügte Text ist mit {kilobytes} KB zu groß (höchstens {maxKilobytes} KB). Eine echte Antwort ist viel kleiner – wurde vielleicht der ganze Chat-Verlauf kopiert? Kopiere nur die letzte Antwort der KI.",
            $"The pasted text is too large at {kilobytes} KB (at most {maxKilobytes} KB). A real answer is much smaller – did you copy the whole chat history? Copy only the last answer of the AI.");

        public static readonly Msg NoJson = M(
            "In dem eingefügten Text steht kein JSON-Objekt (es müsste mit { beginnen). Kopiere die komplette Antwort der KI – oder bitte sie, nur das JSON-Objekt in einem Codeblock auszugeben.",
            "The pasted text contains no JSON object (it should start with {). Copy the complete answer of the AI – or ask it to output only the JSON object in a code block.");

        public static Msg CutOff(int missing) => new(
            $"Die Antwort ist abgeschnitten: Das JSON-Objekt wird nicht geschlossen (es fehlen {missing} schließende Klammer(n)). Bitte die KI, ihre Antwort komplett noch einmal auszugeben – oder wähle in den Einstellungen den Baustein „Umfang → Kompakt“, damit sie kürzer wird.",
            $"The answer is cut off: the JSON object is never closed ({missing} closing bracket(s) are missing). Ask the AI to output its complete answer again – or choose the building block \"Scope → Compact\" in the settings so that it becomes shorter.");

        /// <summary>The answer says the topic does not exist in this game: saving creates no chapter (and removes the placeholder it was meant for).</summary>
        public static Msg NotApplicable(string game, string reason, bool replacesPlaceholder) => new(
            $"Laut dieser Antwort gibt es das Thema in „{game}“ nicht{(string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason.Trim())} " +
            (replacesPlaceholder ? "Beim Speichern wird das fehlgeschlagene Kapitel entfernt, statt es zu füllen." : "Beim Speichern wird deshalb kein Kapitel angelegt."),
            $"According to this answer the topic does not exist in “{game}”{(string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason.Trim())} " +
            (replacesPlaceholder ? "Saving removes the failed chapter instead of filling it." : "Saving therefore creates no chapter."));

        /// <summary>A "does not exist" answer for a chapter every guide has (roadmap, tips, walkthrough).</summary>
        public static Msg NotApplicableNotAllowed(string title) => new(
            $"Die Antwort sagt, das Thema gebe es im Spiel nicht – das Kapitel „{title}“ gehört aber zu jedem Guide und kann nicht entfallen. Bitte die KI, das Kapitel mit echtem Inhalt (\"sections\") zu schreiben.",
            $"The answer says the topic does not exist in the game – but the chapter “{title}” belongs to every guide and cannot be left out. Ask the AI to write the chapter with real content (\"sections\").");

        public static Msg InvalidJson(int line, int column, string excerpt, string hint) => new(
            $"Das JSON ist ungültig – Zeile {line}, Zeichen {column}: „{excerpt}“. {hint}".Trim(),
            $"The JSON is invalid – line {line}, character {column}: “{excerpt}”. {hint}".Trim());

        public static readonly Msg HintTypographicQuotes = M(
            "Die KI hat typografische Anführungszeichen (“ ” ‘ ’) verwendet. Bitte sie um gültiges JSON mit geraden Anführungszeichen (\").",
            "The AI used typographic quotation marks (“ ” ‘ ’). Ask it for valid JSON with straight quotation marks (\").");

        public static readonly Msg HintRawNewline = M(
            "Mitten in einem Text steht ein roher Zeilenumbruch – in JSON muss er als \\n geschrieben sein. Bitte die KI, die Antwort noch einmal als gültiges JSON auszugeben.",
            "A raw line break stands inside a text – in JSON it has to be written as \\n. Ask the AI to output the answer again as valid JSON.");

        public static readonly Msg HintGeneric = M(
            "Bitte die KI, die Antwort noch einmal als gültiges JSON auszugeben (ohne Kommentare, ohne Platzhalter wie „...“).",
            "Ask the AI to output the answer again as valid JSON (no comments, no placeholders like “...”).");

        public static readonly Msg WholeGuideFile = M(
            "Das ist ein ganzer Guide (Format „platinvault-guide“), kein einzelnes Kapitel. Ganze Guide-Dateien lädst du über „Guide importieren“.",
            "This is a whole guide (format “platinvault-guide”), not a single chapter. Load whole guide files with “Import guide”.");

        public static readonly Msg BlueprintForChapter = M(
            "Das ist die Antwort auf den Bauplan-Prompt (sie enthält „topics“), nicht auf einen Kapitel-Prompt. Füge sie im Schritt „Kapitelliste“ ein; hier gehört die Antwort auf den Kapitel-Prompt hin.",
            "This is the answer to the blueprint prompt (it contains “topics”), not to a chapter prompt. Paste it in the step “Chapter list”; the answer to the chapter prompt belongs here.");

        public static readonly Msg ChapterForBlueprint = M(
            "Das ist die Antwort auf einen Kapitel-Prompt (sie enthält „sections“), nicht auf den Bauplan-Prompt. Die Kapitel-Antworten fügst du im nächsten Schritt pro Kapitel ein; hier gehört die Antwort auf den Bauplan-Prompt hin.",
            "This is the answer to a chapter prompt (it contains “sections”), not to the blueprint prompt. You paste the chapter answers one by one in the next step; the answer to the blueprint prompt belongs here.");

        public static Msg WrongGame(int expected, int found, string title) => new(
            $"Die Antwort gehört zu einem anderen Spiel: Sie nennt die Steam-AppID {found}, dieser Guide ist aber „{title}“ (AppID {expected}). Prüfe, ob du den Prompt des richtigen Spiels benutzt hast.",
            $"The answer belongs to a different game: it names the Steam AppID {found}, but this guide is “{title}” (AppID {expected}). Check that you used the prompt of the right game.");

        public static Msg NoAppIdEcho(int expected) => new(
            $"Die Antwort nennt keine Steam-AppID (erwartet: {expected}) – ob sie zu diesem Spiel gehört, lässt sich nicht prüfen. Sieh dir die Vorschau an, bevor du speicherst.",
            $"The answer names no Steam AppID (expected: {expected}) – whether it belongs to this game cannot be checked. Look at the preview before you save.");

        public static Msg OtherGameName(string found, string title) => new(
            $"Die Antwort nennt das Spiel „{found}“, dieser Guide ist aber „{title}“. Sieh dir die Vorschau genau an, bevor du speicherst.",
            $"The answer names the game “{found}”, but this guide is “{title}”. Look at the preview closely before you save.");

        public static readonly Msg NoSections = M(
            "Das Kapitel enthält keine Abschnitte: „sections“ fehlt oder ist leer. Bitte die KI, das Kapitel nach dem Aufbau des Prompts noch einmal auszugeben.",
            "The chapter contains no sections: “sections” is missing or empty. Ask the AI to output the chapter again following the structure of the prompt.");

        public static readonly Msg SectionsNotList = M(
            "„sections“ muss eine Liste (ein Array in eckigen Klammern) sein.",
            "“sections” must be a list (an array in square brackets).");

        public static Msg NothingReadable(int count) => new(
            $"Von {count} Abschnitt(en) ist keiner lesbar (erlaubt sind die Typen alert, cards_grid, checklist_table, story, puzzle und heading). Die Hinweise unten sagen, was bei jedem Abschnitt fehlt.",
            $"None of the {count} section(s) is readable (allowed types are alert, cards_grid, checklist_table, story, puzzle and heading). The notes below say what is missing in each section.");

        public static Msg SectionSkipped(int number, string why) => new(
            $"Abschnitt {number} wird übersprungen: {why}",
            $"Section {number} is skipped: {why}");

        public static Msg SectionNotObject => M("er ist kein Objekt in geschweiften Klammern.", "it is not an object in curly brackets.");
        public static Msg SectionNoType => M("es fehlt „type“.", "“type” is missing.");
        public static Msg SectionUnknownType(string type) => new($"der Typ „{type}“ ist unbekannt (erlaubt: alert, cards_grid, checklist_table, story, puzzle, heading).", $"the type “{type}” is unknown (allowed: alert, cards_grid, checklist_table, story, puzzle, heading).");
        public static Msg SectionStoryNoText => M("die Story hat weder „paragraphs“ noch „htmlContent“.", "the story has neither “paragraphs” nor “htmlContent”.");
        public static Msg SectionHeadingNoTitle => M("die Überschrift hat keinen „title“.", "the heading has no “title”.");
        public static Msg SectionPuzzleEmpty => M("das Rätsel hat weder „title“, „htmlContent“ noch „puzzle.location“.", "the puzzle has neither “title”, “htmlContent” nor “puzzle.location”.");
        public static Msg SectionTableMissing => M("die Tabelle hat kein „table“-Objekt.", "the table has no “table” object.");

        public static Msg TableWithoutRows(int number) => new(
            $"Abschnitt {number}: Die Tabelle hat keine Zeilen („rows“ fehlt oder ist leer).",
            $"Section {number}: the table has no rows (“rows” is missing or empty).");

        public static Msg CardsWithoutCards(int number) => new(
            $"Abschnitt {number}: Das Karten-Raster hat keine Karten („cards“ fehlt oder ist leer).",
            $"Section {number}: the card grid has no cards (“cards” is missing or empty).");

        public static Msg PuzzleWithoutSteps(int number, string name) => new(
            $"Abschnitt {number}: Das Rätsel „{name}“ hat keine „puzzle.steps“ – es wird als „Lösung nicht dokumentiert“ angezeigt. Bitte die KI, die vollständige Lösung (jeder Schritt, genaue Reihenfolge, Codes) zu liefern, oder bestätigen, dass es keine belegte Lösung gibt.",
            $"Section {number}: the puzzle “{name}” has no “puzzle.steps” – it is shown as “solution not documented”. Ask the AI for the complete solution (every step, exact order, codes), or to confirm that no verified solution exists.");

        public static Msg PuzzleWithoutLocation(int number, string name) => new(
            $"Abschnitt {number}: Beim Rätsel „{name}“ fehlt „puzzle.location“ – der Spieler erfährt nicht, wo es zu finden ist.",
            $"Section {number}: the puzzle “{name}” has no “puzzle.location” – the player is not told where to find it.");

        public static Msg AlertKindUnknown(int number, string kind) => new(
            $"Abschnitt {number}: Die Hinweisart „{kind}“ ist unbekannt (erlaubt: must_have, missable, optional, tip, achievement, point_of_no_return) – der Hinweis wird als normaler Hinweis angezeigt.",
            $"Section {number}: the hint kind “{kind}” is unknown (allowed: must_have, missable, optional, tip, achievement, point_of_no_return) – the hint is shown as a normal alert.");

        public static Msg BossWithoutName(int number) => new(
            $"Abschnitt {number}: Die Boss-Karte hat keinen Namen („title“) und wird übersprungen.",
            $"Section {number}: the boss card has no name (“title”) and is skipped.");

        public static Msg BossWithoutLocation(int number, string name) => new(
            $"Abschnitt {number}: Bei der Boss-Karte „{name}“ fehlt „location“ – der Spieler erfährt nicht, wo der Kampf stattfindet.",
            $"Section {number}: the boss card “{name}” has no “location” – the player is not told where the fight takes place.");

        public static Msg BossHpDropped(int number, string name, int count) => new(
            $"Abschnitt {number}: {count} HP-Angabe(n) der Boss-Karte „{name}“ haben keinen Wert oder keine Quelle („source“) und werden weggelassen – Zahlen nur mit Beleg.",
            $"Section {number}: {count} HP entr(y/ies) of the boss card “{name}” have no value or no source (“source”) and are left out – numbers only with evidence.");

        public static Msg BossHpWithoutAppliesTo(int number, string name, int count) => new(
            $"Abschnitt {number}: {count} HP-Angabe(n) der Boss-Karte „{name}“ sagen nicht, wofür sie gelten („appliesTo“: Schwierigkeit, Phase, Version).",
            $"Section {number}: {count} HP entr(y/ies) of the boss card “{name}” do not say what they apply to (“appliesTo”: difficulty, phase, version).");

        public static Msg UnknownApiNames(int count, string examples) => new(
            $"{count} Steam-API-Name(n) gibt es in der Erfolgsliste dieses Spiels nicht und werden weggelassen: {examples}",
            $"{count} Steam API name(s) are not in this game's achievement list and are left out: {examples}");

        public static Msg InvalidSourceUrls(int count) => new(
            $"{count} Quelle(n) haben keine gültige http(s)-Adresse und werden weggelassen.",
            $"{count} source(s) have no valid http(s) address and are left out.");

        public static Msg ForeignSentences(string guideLanguage, int count, string examples) => new(
            $"{count} Satz/Sätze scheinen {(guideLanguage == Lang.English ? "auf Deutsch" : "auf Englisch")} zu sein, der Guide ist aber {(guideLanguage == Lang.English ? "englisch" : "deutsch")}, z. B.: {examples} – den fertigen Korrektur-Prompt unten in den Chat kopieren und die neue Antwort hier einfügen.",
            $"{count} sentence(s) seem to be in {(guideLanguage == Lang.English ? "German" : "English")}, but the guide is {(guideLanguage == Lang.English ? "English" : "German")}, e.g.: {examples} – copy the ready fix prompt below into the chat and paste the new answer here.");

        public static readonly Msg BatchReplaceNotApplicable = M(
            "Die Antwort sagt, dass es das Thema in diesem Spiel nicht gibt – ein Kapitel mit Inhalt wird dadurch nicht gelöscht. Entferne es bei Bedarf selbst über „Kapitel entfernen“.",
            "The answer says the topic does not exist in this game – a chapter with content is not deleted by that. Remove it yourself with “Remove chapter” if you want.");

        public static readonly Msg ChapterNotReplaceable = M(
            "Die Steam-Erfolgsliste und „Story & Lore“ lassen sich nicht durch ein Kapitel ersetzen.",
            "The Steam achievement list and “Story & Lore” cannot be replaced by a chapter.");

        public static Msg RowsWithoutItemId(int count) => new(
            $"{count} Tabellenzeile(n) haben keine „itemId“; PlatinVault vergibt sie beim Speichern selbst (dann lässt sich der Eintrag trotzdem abhaken).",
            $"{count} table row(s) have no “itemId”; PlatinVault assigns them when saving (the entry can still be ticked off).");

        public static Msg DuplicateItemIds(int count) => new(
            $"{count} itemId(s) kommen doppelt vor; PlatinVault macht sie beim Speichern eindeutig.",
            $"{count} itemId(s) occur twice; PlatinVault makes them unique when saving.");

        public static Msg CategoryNotListed(int rows) => new(
            $"{rows} Tabellenzeile(n) haben eine „filterCategory“, die nicht in „categories“ steht – der Kategorie-Filter zeigt sie unter „Alle“.",
            $"{rows} table row(s) have a “filterCategory” that is not listed in “categories” – the category filter shows them under “All”.");

        public static Msg OtherLanguage(string guideLanguage) => new(
            $"Die Antwort scheint {(guideLanguage == Lang.English ? "auf Deutsch" : "auf Englisch")} geschrieben zu sein, dieser Guide ist aber {(guideLanguage == Lang.English ? "englisch" : "deutsch")}. Bitte die KI, das Kapitel in der Sprache des Guides zu schreiben.",
            $"The answer seems to be written in {(guideLanguage == Lang.English ? "German" : "English")}, but this guide is {(guideLanguage == Lang.English ? "English" : "German")}. Ask the AI to write the chapter in the language of the guide.");

        public static Msg NoTitle(string expected) => new(
            $"Die Antwort hat keinen „title“; das Kapitel heißt „{expected}“.",
            $"The answer has no “title”; the chapter is called “{expected}”.");

        public static readonly Msg NoTopics = M(
            "Der Bauplan enthält keine Themenliste: „topics“ und „extraChapters“ fehlen. Bitte die KI, den Bauplan nach dem Aufbau des Prompts noch einmal auszugeben.",
            "The blueprint contains no topic list: “topics” and “extraChapters” are missing. Ask the AI to output the blueprint again following the structure of the prompt.");

        public static readonly Msg NoChaptersPlanned = M(
            "Aus dem Bauplan ergibt sich kein einziges Kapitel: Die KI hat jedes Thema als „nicht zutreffend“ markiert oder alle schon vorhandenen Kapitel als abgedeckt. Du kannst Themen unten trotzdem selbst ankreuzen.",
            "The blueprint yields no chapter at all: the AI marked every topic as “not applicable” or every topic as already covered. You can still tick topics yourself below.");

        public static readonly Msg ChapterNotAPlaceholder = M(
            "Dieses Kapitel hat schon Inhalt – ersetzt werden nur Kapitel, die als „nicht recherchiert“ markiert sind, oder ein Kapitel, das ausdrücklich neu geschrieben werden soll („replace“).",
            "This chapter already has content – only chapters marked “could not be researched” are replaced, or a chapter that is explicitly to be rewritten (“replace”).");

        public static readonly Msg ChapterNotFound = M("Dieses Kapitel gibt es in diesem Guide nicht (mehr).", "This chapter does not exist (any more) in this guide.");

        public static Msg NotSaved(string reasons) => new(
            $"Das Kapitel wurde nicht gespeichert, weil die Antwort noch Fehler hat: {reasons}",
            $"The chapter was not saved because the answer still has errors: {reasons}");

        public static readonly Msg NotSavedTitle = M("Antwort nicht gespeichert", "Answer not saved");
        public static readonly Msg PromptTitleMissing = M("Für den Kapitel-Prompt fehlen Titel und Rechercheauftrag.", "The chapter prompt needs a title and a research assignment.");

        // ---- Batch mode: one prompt for many chapters, one field for all collected answers -------------------------------

        public static Msg BatchChaptersInvalid(int max) => new(
            $"Die Kapitelliste ist ungültig: Sie braucht 1 bis {max} Kapitel, jedes mit einem Titel (höchstens {MaxTitleLength} Zeichen) und – für den Prompt – einem Rechercheauftrag.",
            $"The chapter list is invalid: it needs 1 to {max} chapters, each with a title (at most {MaxTitleLength} characters) and – for the prompt – a research assignment.");

        public static Msg BatchFieldTooLong(int number, string field, int max) => new(
            $"Kapitel {number}: Das Feld „{field}“ ist zu lang (höchstens {max} Zeichen).",
            $"Chapter {number}: the field “{field}” is too long (at most {max} characters).");

        public static Msg BatchTooLarge(int kilobytes, int maxKilobytes) => new(
            $"Der eingefügte Text ist mit {kilobytes} KB zu groß (höchstens {maxKilobytes} KB). Füge nur die Antworten der KI ein, nicht den ganzen Chat-Verlauf mit Bildern oder Anhängen.",
            $"The pasted text is too large at {kilobytes} KB (at most {maxKilobytes} KB). Paste only the answers of the AI, not the whole chat history with images or attachments.");

        public static Msg BatchTooManyObjects(int max) => new(
            $"Im eingefügten Text stehen mehr als {max} JSON-Blöcke. Das sind zu viele für eine Kapitelliste – wurde etwas anderes als die Kapitel-Antworten eingefügt?",
            $"The pasted text contains more than {max} JSON blocks. That is too many for a chapter list – did you paste something other than the chapter answers?");

        public static Msg BatchCutOff(int missing) => new(
            $"Dieses Kapitel ist abgeschnitten: Das JSON-Objekt wird nicht geschlossen (es fehlen {missing} schließende Klammer(n)). Bitte die KI, genau dieses Kapitel noch einmal vollständig auszugeben – am besten allein in einer Antwort – und füge es hinzu; die neuere Fassung ersetzt die abgeschnittene.",
            $"This chapter is cut off: the JSON object is never closed ({missing} closing bracket(s) are missing). Ask the AI to output exactly this chapter again in full – ideally alone in one answer – and add it; the newer version replaces the cut-off one.");

        public static Msg BatchUnmatched(string? key, string? title) => new(
            $"Dieser Block gehört zu keinem Kapitel der Liste: {(string.IsNullOrWhiteSpace(key) ? "Er hat kein Feld „chapter“" : $"Der Schlüssel „{key}“ ist unbekannt")}{(string.IsNullOrWhiteSpace(title) ? "" : $" und der Titel „{title}“ passt zu keinem Kapitel")}. Er wird nicht gespeichert.",
            $"This block does not belong to any chapter of the list: {(string.IsNullOrWhiteSpace(key) ? "it has no “chapter” field" : $"the key “{key}” is unknown")}{(string.IsNullOrWhiteSpace(title) ? "" : $" and the title “{title}” matches no chapter")}. It is not saved.");

        public static Msg BatchDuplicateInText(int count) => new(
            $"Dieses Kapitel steht {count}-mal im eingefügten Text; die letzte vollständige Fassung wird verwendet.",
            $"This chapter appears {count} times in the pasted text; the last complete version is used.");

        public static readonly Msg BatchLaterCopyBroken = M(
            "Eine spätere Fassung dieses Kapitels ist abgeschnitten oder ungültig und wird ignoriert; die vorherige vollständige Fassung wird verwendet.",
            "A later version of this chapter is cut off or invalid and is ignored; the earlier complete version is used.");

        public static Msg BatchSkipUnknownKey(string key) => new(
            $"Der Schlüssel „{key}“ gehört zu keinem Kapitel der Liste.",
            $"The key “{key}” does not belong to any chapter of the list.");

        public static readonly Msg BatchSkipMissing = M("Zu diesem Kapitel steht keine Antwort im eingefügten Text.", "There is no answer for this chapter in the pasted text.");

        public static Msg BatchSkipErrors(string reasons) => new(
            $"Die Antwort hat noch Fehler und wird nicht gespeichert: {reasons}",
            $"The answer still has errors and is not saved: {reasons}");

        public static readonly Msg BatchSkipTabUsedTwice = M(
            "Das fehlgeschlagene Kapitel, das ersetzt werden soll, ist in dieser Liste schon einem anderen Eintrag zugeordnet.",
            "The failed chapter that is to be replaced is already assigned to another entry of this list.");

        public static readonly Msg BatchSkipSecondWalkthrough = M(
            "Die Komplettlösung gibt es nur einmal je Guide; ein zweiter Eintrag dafür in derselben Liste wird nicht gespeichert.",
            "The walkthrough exists only once per guide; a second entry for it in the same list is not saved.");

        private const int MaxTitleLength = 240;
    }

    // ---- Steam ---------------------------------------------------------------------------------------------------------

    public static class Steam
    {
        public static readonly Msg NoApiKey = M(
            "Kein Steam Web API-Key hinterlegt. Bitte öffne oben rechts die Einstellungen und trage deinen Steam API-Key ein (steamcommunity.com/dev/apikey).",
            "No Steam Web API key stored. Please open the settings at the top right and enter your Steam API key (steamcommunity.com/dev/apikey).");

        public static readonly Msg NoSteamId = M(
            "Keine Steam-ID oder Steam-Profil-URL hinterlegt. Bitte trage deine 17-stellige Steam-ID oder deinen Steam-Profilnamen in den Einstellungen ein.",
            "No Steam ID or Steam profile URL stored. Please enter your 17-digit Steam ID or your Steam profile name in the settings.");

        public static Msg ApiStatus(int status) => M(
            $"Steam API antwortete mit Status {status}. Prüfe deinen Steam API-Key und ob dein Steam-Profil (Spieldetails) auf Öffentlich steht.",
            $"The Steam API answered with status {status}. Check your Steam API key and whether your Steam profile (game details) is set to public.");

        public static Msg ProfileUnresolved(string input) => M(
            $"Steam-Profil '{input}' konnte nicht in eine 17-stellige SteamID64 aufgelöst werden. Bitte gib deine 17-stellige SteamID64 (z.B. 76561198...) oder deinen korrekten Steam-Vanity-Namen ein.",
            $"Steam profile '{input}' could not be resolved to a 17-digit SteamID64. Please enter your 17-digit SteamID64 (e.g. 76561198...) or your correct Steam vanity name.");

        // Sign in with Steam (OpenID): these end up in the redirect back to the app
        public static readonly Msg OpenIdCancelled = M("Die Anmeldung bei Steam wurde abgebrochen.", "The sign-in at Steam was cancelled.");
        public static readonly Msg OpenIdMismatch = M("Die Antwort von Steam passt nicht zu dieser Anmeldung.", "Steam’s answer does not match this sign-in.");
        public static readonly Msg OpenIdNotSigned = M("Die Antwort von Steam ist nicht vollständig signiert.", "Steam’s answer is not fully signed.");
        public static readonly Msg OpenIdNoSteamId = M("Steam hat keine gültige SteamID geliefert.", "Steam did not deliver a valid SteamID.");
        public static readonly Msg OpenIdNotConfirmed = M("Steam hat die Anmeldung nicht bestätigt (is_valid:false).", "Steam did not confirm the sign-in (is_valid:false).");
        public static readonly Msg OpenIdUnreachable = M("Steam war für die Bestätigung nicht erreichbar.", "Steam could not be reached for the confirmation.");
    }

    // ---- Live trophies -------------------------------------------------------------------------------------------------

    public static class Live
    {
        public static readonly Msg NoGameDetected = M("Kein Spiel erkannt", "No game detected");
        public static readonly Msg Ready = M("Bereit", "Ready");
        public static readonly Msg PlayingNow = M("Heute (Gerade aktiv)", "Today (playing now)");

        // Worldwide unlock shares (they decide Gold/Silver/Bronze): what the UI says when Steam could not be asked
        public static readonly Msg RarityStale = M(
            "Steam konnte nicht erreicht werden, deshalb gelten die zuletzt gespeicherten Seltenheitswerte. Die Stufen Gold, Silber und Bronze können leicht abweichen. Prüfe die Internetverbindung sowie Firewall oder Virenschutz – PlatinVault versucht es automatisch erneut.",
            "Steam could not be reached, so the last saved rarity values are used. The tiers Gold, Silver and Bronze may be slightly off. Check your internet connection and the firewall or antivirus – PlatinVault retries automatically.");

        public static readonly Msg RarityUnavailable = M(
            "Die weltweiten Freischaltquoten konnten nicht von Steam geladen werden. Die Stufen Gold, Silber und Bronze sind deshalb nur vorläufig: alle Erfolge zählen vorerst als Silber. Prüfe die Internetverbindung sowie Firewall oder Virenschutz – PlatinVault versucht es automatisch erneut.",
            "The worldwide unlock rates could not be loaded from Steam. The tiers Gold, Silver and Bronze are therefore only provisional: all achievements count as Silver for now. Check your internet connection and the firewall or antivirus – PlatinVault retries automatically.");

        /// <summary>"45 Min." / "1,5 Std." — in the language given (the cached info is built per language, not per request).</summary>
        public static string Playtime(int minutes, string language)
        {
            if (minutes < 60)
            {
                return language == Lang.English ? $"{minutes} min" : $"{minutes} Min.";
            }
            var hours = (minutes / 60.0).ToString("0.0", Lang.Culture(language));
            return language == Lang.English ? $"{hours} h" : $"{hours} Std.";
        }

        // Rarity scale (one for the whole app): the neutral band key is what the UI translates, the label is a convenience
        public static string RarityLabel(string band, string language) => (band, language == Lang.English) switch
        {
            ("ultraRare", false) => "Extrem selten",
            ("veryRare", false) => "Sehr selten",
            ("rare", false) => "Selten",
            ("uncommon", false) => "Ungewöhnlich",
            ("ultraRare", true) => "Ultra rare",
            ("veryRare", true) => "Very rare",
            ("rare", true) => "Rare",
            ("uncommon", true) => "Uncommon",
            (_, false) => "Häufig",
            _ => "Common"
        };

        // Sample trophies of the popup test (Settings > Live & sound)
        public static string TestName(string tier, string language) => (tier, language == Lang.English) switch
        {
            ("Gold", false) => "Meister des Schicksals (Master of Fate)",
            ("Silver", false) => "Über-Soldat (Ultimate Weapon)",
            (_, false) => "Erster Auftrag (On the Way)",
            ("Gold", true) => "Master of Fate",
            ("Silver", true) => "Ultimate Weapon",
            _ => "On the Way"
        };

        public static string TestDescription(string tier, string language) => (tier, language == Lang.English) switch
        {
            ("Gold", false) => "Schließe alle Kapitel im Schwierigkeitsgrad SCHWER (Hard Mode) auf 100% ab.",
            (_, false) => "Meistere die Herausforderung im Kampfsimulator und sichere dir den Trophäen-Meilenstein.",
            ("Gold", true) => "Complete all chapters on HARD difficulty (Hard Mode) with 100%.",
            _ => "Master the challenge in the combat simulator and secure the trophy milestone."
        };
    }

    // ---- Documents -----------------------------------------------------------------------------------------------------

    public static class Docs
    {
        public static readonly Msg HandbookNotFoundTitle = M("Handbuch nicht gefunden", "Handbook not found");

        public static Msg HandbookNotFoundDetail(string handbookLanguage) => M(
            $"Das {(handbookLanguage == Lang.German ? "deutsche" : "englische")} Handbuch (PDF) liegt auf diesem Server nicht bereit. Es wird mit .\\scripts\\build-konzept.ps1 erzeugt und gehört ins Docker-Image.",
            $"The {(handbookLanguage == Lang.German ? "German" : "English")} handbook (PDF) is not available on this server. It is generated with .\\scripts\\build-konzept.ps1 and belongs in the Docker image.");

        public static readonly Msg LicenseUnknownTitle = M("Unbekannte Lizenzdatei", "Unknown licence file");

        public static Msg LicenseUnknownDetail(string value) => M(
            $"Der Wert „{(value.Length > 40 ? value[..40] : value)}“ für file ist nicht bekannt. Erlaubt sind license (Lizenz von PlatinVault, GNU AGPL v3) und notices (Drittanbieter-Hinweise).",
            $"The value “{(value.Length > 40 ? value[..40] : value)}” for file is not known. Allowed: license (the licence of PlatinVault, GNU AGPL v3) and notices (third-party notices).");

        public static readonly Msg LicenseNotFoundTitle = M("Lizenzdatei nicht gefunden", "Licence file not found");

        public static readonly Msg NoticesNotFoundDetail = M(
            "Die Drittanbieter-Hinweise (THIRD-PARTY-NOTICES.md) liegen auf diesem Server nicht bereit. Sie werden mit .\\scripts\\build-notices.ps1 erzeugt und gehören ins Docker-Image.",
            "The third-party notices (THIRD-PARTY-NOTICES.md) are not available on this server. They are generated with .\\scripts\\build-notices.ps1 and belong in the Docker image.");

        public static readonly Msg LicenseNotFoundDetail = M(
            "Der Lizenztext von PlatinVault (LICENSE, GNU AGPL v3) liegt auf diesem Server nicht bereit. Er gehört ins Docker-Image.",
            "The licence text of PlatinVault (LICENSE, GNU AGPL v3) is not available on this server. It belongs in the Docker image.");
    }

    // ---- Guide import / export -----------------------------------------------------------------------------------------

    public static class Import
    {
        public static readonly Msg NoDataTitle = M("Keine Daten", "No data");
        public static readonly Msg NoDataDetail = M("Die Anfrage ist leer – sende den Inhalt einer .platinvault.json-Datei als JSON.", "The request is empty – send the content of a .platinvault.json file as JSON.");
        public static readonly Msg InvalidFileTitle = M("Ungültige Datei", "Invalid file");

        public static Msg NotJson(long? line, long? position)
        {
            var where = line is { } l ? (De: $" (Zeile {l + 1}, Position {(position ?? 0) + 1})", En: $" (line {l + 1}, position {(position ?? 0) + 1})") : (De: string.Empty, En: string.Empty);
            return M(
                $"Die Datei ist kein gültiges JSON{where.De}. Prüfe, ob sie vollständig ist und unverändert aus PlatinVault exportiert wurde.",
                $"The file is not valid JSON{where.En}. Check that it is complete and was exported from PlatinVault unchanged.");
        }

        public static readonly Msg NotPossibleTitle = M("Import nicht möglich", "Import not possible");
        public static readonly Msg AlreadyExistsTitle = M("Spiel bereits vorhanden", "Game already exists");

        public static Msg AlreadyExistsDetail(string title, int appId) => M(
            $"„{title}“ ist bereits in deiner Sammlung (Steam-AppID {appId}). Entferne es zuerst, wenn du stattdessen den Guide aus dieser Datei übernehmen möchtest.",
            $"“{title}” is already in your collection (Steam AppID {appId}). Remove it first if you want to take over the guide from this file instead.");

        public static Msg ItemIdTooLong(int max, string start) => M(
            $"Eine Checklisten-Zeile hat eine zu lange itemId (mehr als {max} Zeichen, beginnt mit „{start}…“). Kürze die ID in der Datei.",
            $"A checklist row has an itemId that is too long (more than {max} characters, starts with “{start}…”). Shorten the ID in the file.");

        public static readonly Msg FileTooLargeTitle = M("Datei zu groß", "File too large");

        public static Msg FileTooLargeDetail(long megabytes) => M(
            $"Die Datei ist größer als {megabytes} MB und kann nicht importiert werden.",
            $"The file is larger than {megabytes} MB and cannot be imported.");

        // Validation of the file contents (GuideImport)
        public static readonly Msg RootNotObject = M("Die Datei muss ein JSON-Objekt mit den Feldern format, formatVersion, game und tabs sein.", "The file must be a JSON object with the fields format, formatVersion, game and tabs.");
        public static readonly Msg GameMissing = M("Das Feld „game“ fehlt oder ist kein Objekt (erwartet: title, subtitle, steamAppId, …).", "The field “game” is missing or is not an object (expected: title, subtitle, steamAppId, …).");
        public static readonly Msg GameTitleMissing = M("Im Feld „game.title“ fehlt der Spieltitel.", "The field “game.title” is missing the game title.");

        public static Msg FormatMissing(string expected) => M(
            $"Das Feld „format“ fehlt oder ist kein Text – erwartet wird „{expected}“. Das ist keine PlatinVault-Guide-Datei.",
            $"The field “format” is missing or is not text – “{expected}” is expected. This is not a PlatinVault guide file.");

        public static Msg FormatWrong(string actual, string expected) => M(
            $"Das Feld „format“ hat den Wert „{actual}“ – erwartet wird „{expected}“. Das ist keine PlatinVault-Guide-Datei.",
            $"The field “format” has the value “{actual}” – “{expected}” is expected. This is not a PlatinVault guide file.");

        public static readonly Msg FormatVersionMissing = M("Das Feld „formatVersion“ fehlt oder ist keine ganze Zahl.", "The field “formatVersion” is missing or is not a whole number.");

        public static Msg FormatVersionTooNew(int actual, int supported) => M(
            $"Die Datei hat die Format-Version {actual}, diese PlatinVault-Version versteht nur Version {supported}. Aktualisiere PlatinVault oder exportiere den Guide mit der passenden Version neu.",
            $"The file has format version {actual}, this PlatinVault version only understands version {supported}. Update PlatinVault or export the guide again with a matching version.");

        public static Msg FormatVersionInvalid(int actual, int supported) => M(
            $"Die Format-Version {actual} ist ungültig (erwartet: {supported}).",
            $"Format version {actual} is invalid (expected: {supported}).");

        public static readonly Msg TabsNotList = M("Das Feld „tabs“ muss eine Liste sein.", "The field “tabs” must be a list.");
        public static Msg TooManyTabs(int count, int max) => M($"Die Datei enthält {count} Tabs, erlaubt sind höchstens {max}.", $"The file contains {count} tabs, at most {max} are allowed.");
        public static Msg TabNotObject(string path) => M($"Der Eintrag „{path}“ muss ein Objekt mit title, icon, sortOrder und sections sein.", $"The entry “{path}” must be an object with title, icon, sortOrder and sections.");
        public static Msg TabTitleMissing(string path) => M($"Im Feld „{path}.title“ fehlt der Tab-Titel.", $"The field “{path}.title” is missing the tab title.");
        public static Msg SortOrderInvalid(string path, string tab) => M($"Das Feld „{path}.sortOrder“ muss eine ganze Zahl sein (Tab „{tab}“).", $"The field “{path}.sortOrder” must be a whole number (tab “{tab}”).");
        public static Msg LoreTabTwice(string slug, string path) => M($"Die Datei enthält mehr als einen Tab mit dem Slug „{slug}“ (zuletzt {path}) – erlaubt ist höchstens einer.", $"The file contains more than one tab with the slug “{slug}” (last {path}) – at most one is allowed.");
        public static Msg SectionsNotList(string path, string tab) => M($"Das Feld „{path}.sections“ muss eine Liste sein (Tab „{tab}“).", $"The field “{path}.sections” must be a list (tab “{tab}”).");

        public static Msg NoReadableSections(string tab, string path, int count) => M(
            $"Der Tab „{tab}“ ({path}) enthält {count} Abschnitt(e), aber keiner ist lesbar. Erlaubt sind die Typen „alert“, „cards_grid“ und „checklist_table“ mit den Feldern des PlatinVault-Exports (Tabellen brauchen „table“ mit „columns“ und „rows“).",
            $"The tab “{tab}” ({path}) contains {count} section(s), but none is readable. Allowed are the types “alert”, “cards_grid” and “checklist_table” with the fields of the PlatinVault export (tables need “table” with “columns” and “rows”).");

        public static readonly Msg SteamAppIdInvalid = M("Das Feld „game.steamAppId“ muss eine positive ganze Zahl oder null sein.", "The field “game.steamAppId” must be a positive whole number or null.");
        public static Msg FieldNotText(string path) => M($"Das Feld „{path}“ muss Text sein.", $"The field “{path}” must be text.");
        public static Msg FieldTooLong(string path, int length, int max) => M($"Das Feld „{path}“ ist zu lang ({length} Zeichen, erlaubt sind höchstens {max}).", $"The field “{path}” is too long ({length} characters, at most {max} are allowed).");
    }

    // ---- System check (Settings > System) ------------------------------------------------------------------------------

    public static class SystemCheck
    {
        public static readonly Msg BackendLabel = Msg.Same("Backend (API)");
        public static Msg BackendDetail(TimeSpan uptime, string environment) => M($"Läuft seit {Duration.De(uptime)} · {environment}", $"Running for {Duration.En(uptime)} · {environment}");

        public static readonly Msg DatabaseLabel = M("Datenbank (SQLite)", "Database (SQLite)");
        public static readonly Msg DatabaseUnreachable = M("Datenbankdatei nicht lesbar – gehört der Datenordner dem Benutzer, unter dem PlatinVault läuft?", "Database file not readable – does the data folder belong to the user PlatinVault runs as?");
        public static Msg DatabasePending(int pending) => M($"Erreichbar, aber {pending} Migration(en) offen – Backend neu starten", $"Reachable, but {pending} migration(s) pending – restart the backend");
        public static Msg DatabaseOk(int games, TimeSpan? lastBackupAgo) => M(
            $"Schema aktuell · {games} Spiele gespeichert · " + (lastBackupAgo is { } de ? $"letzte Sicherung vor {Duration.De(de)}" : "noch keine Sicherung"),
            $"Schema up to date · {games} games stored · " + (lastBackupAgo is { } en ? $"last backup {Duration.En(en)} ago" : "no backup yet"));
        public static Msg Error(string message) => M($"Fehler: {message}", $"Error: {message}");

        public static readonly Msg DataLabel = M("Datenordner", "Data folder");
        public static readonly Msg DataOk = M("Beschreibbar (verschlüsselte Keys, Caches)", "Writable (encrypted keys, caches)");
        public static Msg DataNotWritable(string message) => M($"Nicht beschreibbar: {message}", $"Not writable: {message}");

        public static readonly Msg SteamLocalLabel = M("Steam auf diesem Rechner", "Steam on this computer");
        public static readonly Msg SteamLocalOff = M("Kein lokales Steam (Server, NAS, Unraid) – Echtzeit kommt über die PlatinVault Bridge", "No local Steam (server, NAS, Unraid) – real time comes through the PlatinVault Bridge");

        public static readonly Msg BridgeLabel = Msg.Same("PlatinVault Bridge");
        public static readonly Msg BridgeNone = M("Noch keine Meldung einer Bridge", "No report from a bridge yet");
        public static Msg BridgeLast(TimeSpan age) => M($"Letzte Meldung vor {Duration.De(age)}", $"Last report {Duration.En(age)} ago");
        public static Msg BridgeLastWarn(TimeSpan age) => M($"Letzte Meldung vor {Duration.De(age)} – läuft die Bridge auf dem Gaming-PC?", $"Last report {Duration.En(age)} ago – is the bridge running on the gaming PC?");

        public static readonly Msg SteamKeyLabel = M("Steam Web API-Key", "Steam Web API key");
        public static readonly Msg SteamKeyOk = M("Hinterlegt (Bibliothek, Spielzeit, Erfolgstexte in deiner Sprache ohne lokales Steam)", "Stored (library, playtime, achievement texts in your language without a local Steam)");
        public static readonly Msg SteamKeyOff = M("Nicht hinterlegt – Bibliothek und Bibliotheks-Statistik fehlen", "Not stored – library and library statistics are missing");

        public static readonly Msg AiLabel = M("KI-Anbieter", "AI providers");
        public static Msg AiOk(string providers) => M($"Hinterlegt: {providers}", $"Stored: {providers}");
        public static readonly Msg AiOff = M("Kein KI-Key – Guides lassen sich nur importieren", "No AI key – guides can only be imported");

        public static readonly Msg TwitchLabel = Msg.Same("Twitch & IGDB");
        public static readonly Msg TwitchOff = M("Nicht hinterlegt – Live-Streams und IGDB-Fakten (Spielzeit bis 100 %, Wertungen, ähnliche Spiele) fehlen", "Not stored – live streams and IGDB facts (time to 100 %, ratings, similar games) are missing");
        public static readonly Msg TwitchNoToken = M("Twitch hat keinen Token ausgestellt", "Twitch did not issue a token");
        public static Msg TwitchOk(bool streamsActive) => M($"Zugangsdaten gültig (Twitch-Token vorhanden) · IGDB-Fakten an · Live-Streams {(streamsActive ? "an" : "aus")}", $"Credentials valid (Twitch token received) · IGDB facts on · Live streams {(streamsActive ? "on" : "off")}");
    }

    // ---- Twitch & IGDB -------------------------------------------------------------------------------------------------

    public static class Twitch
    {
        public static readonly Msg IgdbUnavailableTitle = M("IGDB nicht verfügbar", "IGDB not available");
        public static readonly Msg TwitchUnavailableTitle = M("Twitch nicht verfügbar", "Twitch not available");
        public static readonly Msg GameMissingTitle = M("Spiel fehlt", "Game missing");
        public static readonly Msg GameMissingDetail = M("Bitte appId (Steam-AppID) oder name (Spieltitel) angeben.", "Please provide appId (Steam AppID) or name (game title).");
        public static readonly Msg NameTooLongTitle = M("Name zu lang", "Name too long");
        public static Msg TooManyGames(int max) => M($"Pro Anfrage sind höchstens {max} Spiele erlaubt.", $"At most {max} games are allowed per request.");
        public static readonly Msg NameTooLongDetail = M("Der Spieltitel darf höchstens 200 Zeichen lang sein.", "The game title may be at most 200 characters long.");

        public static readonly Msg NoCredentials = M("Es sind keine Twitch-Zugangsdaten hinterlegt (Einstellungen > Twitch & IGDB).", "No Twitch credentials are stored (Settings > Twitch & IGDB).");
        public static Msg Unreachable(string reason) => M($"Twitch bzw. IGDB ist nicht erreichbar ({reason}).", $"Twitch or IGDB cannot be reached ({reason}).");
        public static Msg TokenUnreachable(string reason) => M($"Twitch (id.twitch.tv) ist nicht erreichbar ({reason}).", $"Twitch (id.twitch.tv) cannot be reached ({reason}).");
        public static readonly Msg NoAccessToken = M("Twitch hat keinen Zugriffs-Token geliefert (Antwort ohne access_token).", "Twitch did not deliver an access token (answer without access_token).");
        public static readonly Msg TokenAnswerUnreadable = M("Twitch hat eine unlesbare Antwort auf die Token-Anfrage geliefert.", "Twitch returned an unreadable answer to the token request.");
        public static readonly Msg AnswerUnreadable = M("Twitch hat eine unlesbare Antwort geliefert.", "Twitch returned an unreadable answer.");

        public static readonly Msg TestEnterCredentials = M("Bitte Client-ID und Client-Secret der Twitch-Anwendung eintragen.", "Please enter the Client ID and Client Secret of the Twitch application.");
        public static readonly Msg TestIllegalCharacters = M("Client-ID oder Client-Secret enthält unerlaubte Zeichen – bitte ohne Leerzeichen aus der Twitch-Developer-Konsole kopieren.", "The Client ID or Client Secret contains illegal characters – please copy it from the Twitch developer console without spaces.");
        public static readonly Msg TestOk = M("Verbindung steht: Die Zugangsdaten sind gültig, IGDB und die Twitch-Streams antworten.", "Connection works: the credentials are valid, IGDB and the Twitch streams answer.");
        public static Msg TestPartial(Msg problems) => M($"Die Zugangsdaten sind gültig (Token erhalten), aber: {problems.De}", $"The credentials are valid (token received), but: {problems.En}");

        public static Msg ProbeStatus(string label, int status, string message) => M($"{label} antwortet mit HTTP {status}{message}.", $"{label} answers with HTTP {status}{message}.");
        public static Msg ProbeUnreachable(string label, string reason) => M($"{label} ist nicht erreichbar ({reason}).", $"{label} cannot be reached ({reason}).");

        public static Msg SecretRejected(string detail) => M(
            $"Twitch lehnt das Client-Secret ab{detail}. Bitte in der Twitch-Developer-Konsole ein neues Secret erzeugen und hier eintragen.",
            $"Twitch rejects the Client Secret{detail}. Please create a new secret in the Twitch developer console and enter it here.");

        public static Msg ClientIdRejected(string detail) => M(
            $"Twitch lehnt die Client-ID ab{detail}. Bitte die Client-ID der Anwendung aus der Twitch-Developer-Konsole prüfen.",
            $"Twitch rejects the Client ID{detail}. Please check the application’s Client ID in the Twitch developer console.");

        public static readonly Msg TokenRateLimited = M("Twitch begrenzt gerade die Token-Anfragen (HTTP 429) – bitte in einer Minute noch einmal versuchen.", "Twitch is limiting token requests right now (HTTP 429) – please try again in a minute.");
        public static Msg TokenNotIssued(int status, string detail) => M($"Twitch hat keinen Token ausgestellt (HTTP {status}){detail}.", $"Twitch did not issue a token (HTTP {status}){detail}.");

        public static Msg StreamsUnauthorized(string suffix) => M(
            $"Twitch lehnt die Zugangsdaten ab (HTTP 401){suffix}. Bitte Client-ID und Client-Secret in den Einstellungen prüfen.",
            $"Twitch rejects the credentials (HTTP 401){suffix}. Please check the Client ID and Client Secret in the settings.");

        public static readonly Msg StreamsRateLimited = M("Twitch-Anfragelimit erreicht (HTTP 429) – bitte in einer Minute noch einmal versuchen.", "Twitch request limit reached (HTTP 429) – please try again in a minute.");
        public static Msg StreamsServerError(int status, string suffix) => M($"Twitch ist gerade gestört (HTTP {status}){suffix}. Bitte später noch einmal versuchen.", $"Twitch is having problems right now (HTTP {status}){suffix}. Please try again later.");
        public static Msg StreamsStatus(int status, string suffix) => M($"Twitch antwortet mit HTTP {status}{suffix}.", $"Twitch answers with HTTP {status}{suffix}.");

        public static readonly Msg IgdbAnswerUnreadable = M("IGDB hat eine unlesbare Antwort geliefert.", "IGDB returned an unreadable answer.");
        public static readonly Msg IgdbTooSlow = M("IGDB antwortet zu langsam – bitte später noch einmal versuchen.", "IGDB is answering too slowly – please try again later.");
        public static Msg IgdbUnauthorized(int status, string suffix) => M($"IGDB lehnt die Twitch-Zugangsdaten ab (HTTP {status}){suffix}. Bitte Client-ID und Client-Secret in den Einstellungen prüfen.", $"IGDB rejects the Twitch credentials (HTTP {status}){suffix}. Please check the Client ID and Client Secret in the settings.");
        public static readonly Msg IgdbRateLimited = M("IGDB-Anfragelimit erreicht (4 Anfragen pro Sekunde) – bitte gleich noch einmal versuchen.", "IGDB request limit reached (4 requests per second) – please try again in a moment.");
        public static Msg IgdbBadRequest(string suffix) => M($"IGDB hat die Anfrage abgelehnt (HTTP 400){suffix}.", $"IGDB rejected the request (HTTP 400){suffix}.");
        public static Msg IgdbServerError(int status, string suffix) => M($"IGDB ist gerade gestört (HTTP {status}){suffix}. Bitte später noch einmal versuchen.", $"IGDB is having problems right now (HTTP {status}){suffix}. Please try again later.");
        public static Msg IgdbStatus(int status, string suffix) => M($"IGDB antwortet mit HTTP {status}{suffix}.", $"IGDB answers with HTTP {status}{suffix}.");
    }

    // ---- Guide chapter groups (side navigation) --------------------------------------------------------------------------

    public static class Groups
    {
        public static Msg Label(string key) => key switch
        {
            "start" => M("Einstieg", "Getting started"),
            "story" => M("Story & Welt", "Story & world"),
            "characters" => M("Charaktere & Begleiter", "Characters & companions"),
            "combat" => M("Kampf & Taktik", "Combat & tactics"),
            "progress" => M("Fortschritt & Freischaltungen", "Progression & unlocks"),
            "explore" => M("Sammeln & Erkunden", "Collecting & exploring"),
            "economy" => M("Wirtschaft & Handwerk", "Economy & crafting"),
            _ => M("Extras", "Extras")
        };
    }

    // ---- Guide translation -----------------------------------------------------------------------------------------------

    public static class Translation
    {
        public static Msg StructureChanged(string chapter) => M(
            $"Die Übersetzung des Kapitels „{chapter}“ hat dessen Aufbau verändert und wurde verworfen.",
            $"The translation of the chapter “{chapter}” changed its structure and was discarded.");

        public static Msg Incomplete(string chapter, int missing) => M(
            $"Die KI hat im Kapitel „{chapter}“ {missing} Text(e) nicht übersetzt. Bitte später erneut versuchen.",
            $"The AI left {missing} text(s) of the chapter “{chapter}” untranslated. Please try again later.");

        public static readonly Msg GameGone = M("Das Spiel wurde während der Übersetzung gelöscht.", "The game was deleted during the translation.");
    }

    // ---- AI providers --------------------------------------------------------------------------------------------------

    public static class Ai
    {
        public static string ProviderName(string provider) => provider switch
        {
            "claude" => "Claude (Anthropic)",
            "openai" => "OpenAI (ChatGPT)",
            _ => "Gemini (Google)"
        };

        public static readonly Msg NoProviderKey = M(
            "Bitte hinterlege mindestens einen KI-API-Key (Google Gemini, Anthropic Claude oder OpenAI ChatGPT) in den Einstellungen unter „KI-Recherche“.",
            "Please store at least one AI API key (Google Gemini, Anthropic Claude or OpenAI ChatGPT) in Settings under “AI research”.");

        public static Msg NoKey(string provider) => M(
            $"Kein {ProviderName(provider)}-API-Key hinterlegt. Bitte in den Einstellungen unter „KI-Recherche“ den Key eintragen.",
            $"No {ProviderName(provider)} API key stored. Please enter the key in Settings under “AI research”.");

        public static Msg NoUsableAnswer(string provider) => M(
            $"{ProviderName(provider)} lieferte keine auswertbare Antwort.",
            $"{ProviderName(provider)} returned no usable answer.");

        public static Msg Unreachable(string provider) => M(
            $"{ProviderName(provider)} ist nicht erreichbar. Bitte die Internetverbindung prüfen und später erneut versuchen.",
            $"{ProviderName(provider)} cannot be reached. Please check the internet connection and try again later.");

        public static Msg TimedOut(string provider) => M(
            $"{ProviderName(provider)} hat nicht rechtzeitig geantwortet (sehr viel Last oder ein sehr langer Auftrag). Bitte später erneut versuchen.",
            $"{ProviderName(provider)} did not answer in time (heavy load or a very long request). Please try again later.");

        public static readonly Msg UnknownProvider = M("Unbekannter KI-Provider.", "Unknown AI provider.");

        public static Msg OverBudget(string scopeName, int max) => new(
            $"Passt zum Spiel, ist aber nicht im Vorschlag: Der Umfang „{scopeName}“ sieht höchstens {max} Kapitel vor.",
            $"Fits the game, but is not in the suggestion: the scope “{scopeName}” allows at most {max} chapters.");

        public static readonly Msg AnswerUnreadable = M(
            "Die Antwort der KI war unvollständig oder nicht lesbar (meist ist sie abgeschnitten oder enthält zusätzlichen Text neben den Daten). Ein erneuter Versuch hilft in der Regel.",
            "The AI's answer was incomplete or unreadable (usually it was cut off or contains extra text next to the data). Trying again usually helps.");
        public static readonly Msg ChapterFailedUnexpected = M(
            "Beim Recherchieren dieses Kapitels ist ein unerwarteter Fehler aufgetreten. Ein erneuter Versuch hilft oft; die technischen Details nennen den Grund.",
            "An unexpected error occurred while researching this chapter. Trying again often helps; the technical details name the reason.");

        /// <summary>The provider stopped at its output limit (the answer is cut off); the first of the two attempts.</summary>
        public static Msg AnswerTruncated(string provider) => M(
            $"Die Antwort von {ProviderName(provider)} war zu lang und wurde an der Ausgabegrenze des Modells abgeschnitten.",
            $"The answer of {ProviderName(provider)} was too long and was cut off at the output limit of the model.");

        /// <summary>Also the second, more compact attempt hit the output limit.</summary>
        public static Msg AnswerTooLong(string provider) => M(
            $"Die Antwort von {ProviderName(provider)} war auch im zweiten, kompakteren Versuch zu lang und wurde an der Ausgabegrenze des Modells abgeschnitten. Wähle in den Einstellungen unter „KI-Recherche“ den Umfang „Kompakt“ oder ein Modell mit größerer Ausgabegrenze und recherchiere das Kapitel erneut.",
            $"The answer of {ProviderName(provider)} was too long again in the second, more compact attempt and was cut off at the output limit of the model. Choose the scope “Compact” or a model with a larger output limit in Settings under “AI research” and research the chapter again.");

        /// <summary>A chapter the player asked for himself ("add chapter") does not exist in the game according to the research: nothing is created.</summary>
        public static Msg ChapterNotApplicable(string title, string reason) => new(
            $"Kein Kapitel „{title}“ angelegt: Laut Recherche gibt es dieses Thema in diesem Spiel nicht. {(string.IsNullOrWhiteSpace(reason) ? string.Empty : "Begründung: " + reason.Trim())}".Trim(),
            $"No chapter “{title}” was created: according to the research this topic does not exist in this game. {(string.IsNullOrWhiteSpace(reason) ? string.Empty : "Reason: " + reason.Trim())}".Trim());

        /// <summary>The roadmap, tips and walkthrough are never dropped: an answer that says "does not exist" for them is unusable.</summary>
        public static Msg ChapterNotApplicableNotAllowed(string title) => new(
            $"Die KI hat das Kapitel „{title}“ mit „gibt es in diesem Spiel nicht“ beantwortet. Dieses Kapitel gehört aber zu jedem Guide – bitte erneut recherchieren.",
            $"The AI answered the chapter “{title}” with “does not exist in this game”. This chapter belongs to every guide, though – please research it again.");

        public static readonly Msg ChapterHasContentTitle = M("Kapitel wird nicht entfernt", "Chapter is not removed");
        public static readonly Msg ChapterHasContent = M(
            "Dieses Kapitel hat Inhalt, es ist kein fehlgeschlagenes Kapitel. Nur Kapitel, die nur einen Fehlerhinweis enthalten, lassen sich so entfernen.",
            "This chapter has content, it is not a failed chapter. Only chapters that hold nothing but an error note can be removed this way.");

        public static readonly Msg ChapterNotFoundTitle = M("Kapitel nicht gefunden", "Chapter not found");
        public static readonly Msg ChapterNotFound = M(
            "Dieses Kapitel gibt es in diesem Guide nicht (mehr). Vielleicht wurde es schon entfernt – lade die Seite neu.",
            "This guide does not have this chapter (any more). It may have been removed already – reload the page.");

        public static readonly Msg ChapterNotRemovableTitle = M("Kapitel kann nicht entfernt werden", "Chapter cannot be removed");
        public static readonly Msg ChapterNotRedoableTitle = M("Kapitel kann nicht neu recherchiert werden", "Chapter cannot be researched again");
        public static readonly Msg ChapterRedoAchievementList = M(
            "Das ist die Steam-Erfolgsliste des Guides, kein Kapitel. Sie kommt von Steam und wird nicht neu recherchiert.",
            "This is the guide's Steam achievement list, not a chapter. It comes from Steam and is not researched again.");

        /// <summary>A repeated research of a chapter says the topic does not exist: the chapter stays as it was.</summary>
        public static Msg RedoNotApplicable(string title, string reason) => new(
            $"Das Kapitel „{title}“ bleibt unverändert: Laut der neuen Recherche gibt es dieses Thema in diesem Spiel nicht. {(string.IsNullOrWhiteSpace(reason) ? string.Empty : "Begründung: " + reason.Trim() + " ")}Entferne das Kapitel bei Bedarf über „Kapitel entfernen“.",
            $"The chapter “{title}” stays as it was: according to the new research this topic does not exist in this game. {(string.IsNullOrWhiteSpace(reason) ? string.Empty : "Reason: " + reason.Trim() + " ")}Remove the chapter with “Remove chapter” if you want.");

        /// <summary>While a chapter was researched again it was removed (or replaced by something that cannot be researched again) - nothing is saved.</summary>
        public static Msg RedoChapterGone(string title) => new(
            $"Die neue Recherche für „{title}“ wird nicht gespeichert: Das Kapitel wurde in der Zwischenzeit entfernt oder ersetzt (z. B. in einem zweiten Fenster). Lade die Seite neu und starte die Recherche bei Bedarf erneut.",
            $"The new research for “{title}” is not saved: the chapter was removed or replaced in the meantime (e.g. in a second window). Reload the page and start the research again if you want.");

        /// <summary>A repeated research of the walkthrough lost parts: the old walkthrough is kept whole.</summary>
        public static Msg RedoWalkthroughPartsFailed(int failed, int total, Msg first) => new(
            $"Die Komplettlösung bleibt unverändert: {failed} von {total} Teilen konnten nicht recherchiert werden. Erster Fehler: {first.De}",
            $"The walkthrough stays as it was: {failed} of {total} parts could not be researched. First error: {first.En}");
        public static readonly Msg ChapterIsAchievementList = M(
            "Das ist die Steam-Erfolgsliste des Guides, kein Kapitel. Sie bleibt, weil die Live-Trophäen ihre Tipps daraus lesen.",
            "This is the guide's Steam achievement list, not a chapter. It stays because the live trophies read their tips from it.");
        public static readonly Msg ChapterIsLore = M(
            "„Story & Lore“ ist ein eigener Bereich und kein Kapitel der Navigation. Über „Story & Lore“ lässt er sich neu recherchieren.",
            "“Story & Lore” is an area of its own, not a chapter of the navigation. It can be researched again from “Story & Lore”.");

        public static readonly Msg NoFailedChaptersTitle = M("Kein fehlgeschlagenes Kapitel", "No failed chapter");
        public static readonly Msg NoFailedChapters = M(
            "Dieser Guide hat kein Kapitel, das erneut recherchiert werden muss.",
            "This guide has no chapter that needs to be researched again.");

        public static readonly Msg JobGoneTitle = M("KI-Auftrag nicht gefunden", "AI job not found");
        public static readonly Msg JobGone = M(
            "Der Server wurde neu gestartet, die Recherche wurde unterbrochen – bereits fertige Kapitel sind gespeichert; erneut starten setzt fort.",
            "The server was restarted and the research was interrupted – chapters that were already finished are saved; starting again continues.");

        public static readonly Msg JobBusyTitle = M("Es läuft schon eine KI-Aktion", "An AI action is already running");

        /// <summary>Another AI action of this game is still running (only one at a time: they all change the same guide).</summary>
        public static Msg JobBusy(string runningKind)
        {
            var (de, en) = runningKind switch
            {
                "plan" => ("die Analyse für den Guide-Baukasten", "the analysis for the guide builder"),
                "complete" => ("„Guide vervollständigen“", "“Complete guide”"),
                "expand" => ("„Kapitel ergänzen“", "“Add chapter”"),
                "retry" => ("„Kapitel erneut recherchieren“", "“Research chapter again”"),
                "redo" => ("„Kapitel neu recherchieren“", "“Redo chapter”"),
                "lore" => ("die Recherche für „Story & Lore“", "the research for “Story & Lore”"),
                _ => ("eine andere KI-Aktion", "another AI action")
            };
            return M(
                $"Für dieses Spiel läuft gerade {de}. Warte, bis sie fertig ist, und starte die neue Aktion dann noch einmal – zwei KI-Aktionen gleichzeitig würden denselben Guide ändern.",
                $"{char.ToUpperInvariant(en[0])}{en[1..]} is running for this game right now. Wait until it has finished, then start the new action again – two AI actions at the same time would change the same guide.");
        }

        /// <summary>The same AI action is running for this game, but for something else (another chapter): the new one is not attached to it.</summary>
        public static Msg JobBusyOtherTarget(string runningKind, string? targetLabel)
        {
            var running = JobBusy(runningKind);
            var (de, en) = string.IsNullOrWhiteSpace(targetLabel)
                ? ("Sie arbeitet gerade an anderen Kapiteln als den gewählten.", "It is working on other chapters than the ones you chose.")
                : ($"Sie arbeitet gerade am Kapitel „{targetLabel}“, nicht an dem gewählten.", $"It is working on the chapter “{targetLabel}”, not on the one you chose.");
            return M($"{running.De} {de}", $"{running.En} {en}");
        }

        /// <summary>Short name for a sentence ("Gemini quota used up – continuing with Claude."), without the company.</summary>
        public static string ShortName(string provider) => provider switch
        {
            "claude" => "Claude",
            "openai" => "OpenAI",
            _ => "Gemini"
        };

        /// <summary>
        /// The notice shown whenever an action had to change provider - never silent. <paramref name="noWebSearch"/> adds that the new
        /// provider researches without web search (OpenAI in this app).
        /// </summary>
        public static Msg FallbackNotice(string from, string to, global::Companion.Api.Services.AiFailureKind reason, bool noWebSearch)
        {
            var a = ShortName(from);
            var b = ShortName(to);
            var text = reason switch
            {
                global::Companion.Api.Services.AiFailureKind.InvalidKey => M(
                    $"{a}: Der API-Key wird abgelehnt – weiter mit {b}.",
                    $"{a}: the API key is rejected – continuing with {b}."),
                global::Companion.Api.Services.AiFailureKind.Unavailable => M(
                    $"{a} ist gerade nicht erreichbar – weiter mit {b}.",
                    $"{a} is not available right now – continuing with {b}."),
                _ => M(
                    $"{a}-Kontingent aufgebraucht – weiter mit {b}.",
                    $"{a} quota used up – continuing with {b}.")
            };
            return noWebSearch
                ? M(text.De + $" {b} recherchiert hier ohne Websuche.", text.En + $" {b} researches without web search here.")
                : text;
        }

        /// <summary>
        /// What happened and what to do, short. The provider's own (English) error text is NOT part of it: it travels separately as
        /// <c>providerMessage</c> (see <see cref="AiProviderException"/>) and the UI shows it under "technical details".
        /// </summary>
        public static Msg ProviderError(string provider, int status, string providerMessage, string? model)
        {
            var name = ProviderName(provider);
            var modelText = string.IsNullOrWhiteSpace(model) ? "?" : model;

            // The same predicates decide whether a failure hands the request to another provider (AiFailure)
            if (global::Companion.Api.Services.AiFailure.IsKeyRejected(status, providerMessage))
            {
                return M(
                    $"{name} lehnt den API-Key ab. Bitte den Key in den Einstellungen unter „KI-Recherche“ prüfen oder neu eintragen.",
                    $"{name} rejected the API key. Please check or re-enter the key in Settings under “AI research”.");
            }

            if (status == 404)
            {
                // Google names the replacement model in its message ("use models/gemini-3.8-flash")
                var suggested = provider == "gemini"
                    ? global::System.Text.RegularExpressions.Regex.Match(providerMessage, @"use models/([A-Za-z0-9.\-]+)")
                    : global::System.Text.RegularExpressions.Match.Empty;
                var hint = suggested.Success ? M($" Google empfiehlt „{suggested.Groups[1].Value}“.", $" Google suggests “{suggested.Groups[1].Value}”.") : M(string.Empty, string.Empty);
                return M(
                    $"Das Modell „{modelText}“ ist für diesen Key nicht verfügbar.{hint.De} Bitte in den Einstellungen unter „KI-Recherche“ ein anderes Modell wählen.",
                    $"The model “{modelText}” is not available for this key.{hint.En} Please choose another model in Settings under “AI research”.");
            }

            if (global::Companion.Api.Services.AiFailure.IsCreditGone(status, providerMessage))
            {
                return provider == "gemini" && status == 429
                    ? M(
                        $"{name}: Kontingent oder Ausgabenlimit erschöpft. Für den Gratis-Tarif einen Key aus einem Google-Projekt ohne Abrechnung verwenden; „Verbindung echt testen“ in den Einstellungen zeigt, was der Key darf.",
                        $"{name}: quota or spending limit used up. For the free tier use a key from a Google project without billing; “Test connection for real” in the settings shows what the key may do.")
                    : M(
                        $"Das Guthaben bzw. Kontingent des {name}-Kontos ist aufgebraucht. Bitte Guthaben aufladen oder einen Key mit freiem Kontingent verwenden.",
                        $"The credit or quota of the {name} account is used up. Please top up the balance or use a key with free quota.");
            }

            if (status == 429)
            {
                return M(
                    $"{name}: Limit erreicht (zu viele Anfragen oder Tageslimit). Bitte später erneut versuchen.",
                    $"{name}: limit reached (too many requests or daily limit). Please try again later.");
            }

            if (status >= 500)
            {
                return M(
                    $"{name} ist gerade nicht verfügbar (HTTP {status}). Bitte später erneut versuchen.",
                    $"{name} is not available right now (HTTP {status}). Please try again later.");
            }

            if (status == 400)
            {
                return M($"{name} hat die Anfrage abgelehnt (HTTP 400).", $"{name} rejected the request (HTTP 400).");
            }

            return M($"{name} antwortete mit HTTP {status}.", $"{name} answered with HTTP {status}.");
        }

        public static readonly Msg BlueprintEmpty = M(
            "Die KI hat keinen Kapitel-Bauplan für dieses Spiel geliefert. Bitte erneut versuchen oder einen anderen KI-Provider wählen.",
            "The AI did not deliver a chapter blueprint for this game. Please try again or choose another AI provider.");

        public static readonly Msg AnswerNotJson = M("Die KI-Antwort enthielt kein gültiges JSON-Objekt.", "The AI answer did not contain a valid JSON object.");
        public static readonly Msg LoreNotJson = M("Die KI-Antwort war kein gültiges JSON.", "The AI answer was not valid JSON.");
        public static readonly Msg LoreNoSectionList = M("Die KI-Antwort enthielt keine Abschnittsliste („sections“).", "The AI answer contained no list of sections (“sections”).");
        public static readonly Msg LoreNoReadableSections = M("Die KI-Antwort enthielt keine lesbaren Abschnitte.", "The AI answer contained no readable sections.");

        public static readonly Msg WalkthroughOutlineNotJson = M(
            "Die Gliederung der Komplettlösung war kein gültiges JSON. Die Recherche kann erneut gestartet werden.",
            "The outline of the walkthrough was not valid JSON. The research can be started again.");

        public static readonly Msg WalkthroughPartNotJson = M("Die KI-Antwort zu diesem Teil der Komplettlösung war kein gültiges JSON.", "The AI answer for this part of the walkthrough was not valid JSON.");
        public static readonly Msg WalkthroughPartNoSections = M("Die KI-Antwort zu diesem Teil der Komplettlösung enthielt keine lesbaren Abschnitte.", "The AI answer for this part of the walkthrough contained no readable sections.");

        /// <summary>The research found no reliable, complete walkthrough source: the chapter is not written (nothing is invented).</summary>
        public static Msg WalkthroughNoSource(string reason) => string.IsNullOrWhiteSpace(reason)
            ? M(
                "Für dieses Spiel wurde keine belastbare, vollständige Quelle für eine Komplettlösung gefunden – das Kapitel wurde nicht erstellt (es wird nichts erfunden). Du kannst die Recherche später erneut versuchen, wenn es bessere Quellen gibt.",
                "No reliable, complete source for a walkthrough was found for this game – the chapter was not created (nothing is invented). You can try the research again later when better sources exist.")
            : new(
                $"Für dieses Spiel wurde keine belastbare, vollständige Quelle für eine Komplettlösung gefunden – das Kapitel wurde nicht erstellt (es wird nichts erfunden). Begründung der Recherche: {reason.Trim()}",
                $"No reliable, complete source for a walkthrough was found for this game – the chapter was not created (nothing is invented). Reason given by the research: {reason.Trim()}");

        public static Msg LoreAllPartsFailed(Msg firstError) => M(
            $"Die Recherche ist in allen vier Teilen fehlgeschlagen. Erster Fehler: {firstError.De}",
            $"The research failed in all four parts. First error: {firstError.En}");

        // Diagnosis (Settings > AI research > "Test connection")
        public static readonly Msg DiagnoseNoKey = M("Kein Gemini API-Key hinterlegt.", "No Gemini API key stored.");
        public static readonly Msg DiagnosePlainOk = M("Funktioniert", "Works");
        public static readonly Msg DiagnoseSearchOk = M("Funktioniert – mit Google-Suche", "Works – with Google Search");
        public static readonly Msg DiagnoseUnreachable = M("Google ist nicht erreichbar. Bitte die Internetverbindung prüfen.", "Google cannot be reached. Please check the internet connection.");
    }

    /// <summary>Short time spans for sentences: "wenigen Sekunden" / "12 Min." / "3 Std. 5 Min." / "2 Tag(en)" and the English counterparts.</summary>
    public static class Duration
    {
        public static string De(TimeSpan span) =>
            span.TotalMinutes < 1 ? "wenigen Sekunden"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} Min."
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} Std. {span.Minutes} Min."
            : $"{(int)span.TotalDays} Tag(en)";

        public static string En(TimeSpan span) =>
            span.TotalMinutes < 1 ? "a few seconds"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
            : $"{(int)span.TotalDays} day(s)";
    }
}
