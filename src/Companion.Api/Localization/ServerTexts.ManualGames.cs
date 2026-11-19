using Companion.Api.Services;

namespace Companion.Api.Localization;

/// <summary>
/// Texts of the games added by hand: the IGDB search of the add dialog, adding a game, its uploaded cover, its trophy list and ticks, the pins
/// and notes by game id, and the guide import of such a game (part of <see cref="ServerTexts"/>, kept in its own file). Same rules as there:
/// parameterised text = method, fixed text = field, English line directly behind the German one. Platform names are proper names
/// (<see cref="GamePlatforms.DisplayName"/>); the texts for a refused platform choice are in <c>ServerTexts.Platforms</c>.
/// </summary>
public static partial class ServerTexts
{
    public static class ManualGames
    {
        // ---- IGDB search and facts ------------------------------------------------------------------------------------

        public static readonly Msg SearchInvalidTitle = M("IGDB-Suche ungültig", "Invalid IGDB search");

        public static Msg SearchInvalidDetail(int max) => M(
            $"Bitte gib 1 bis {max} Zeichen ein: den Namen des Spiels, die IGDB-Nummer oder einen Link von igdb.com/games/…. Die Zeichen \" \\ und ; werden nicht gesucht.",
            $"Please enter 1 to {max} characters: the name of the game, the IGDB number or a link from igdb.com/games/…. The characters \" \\ and ; are not searched for.");

        public static readonly Msg IgdbGameNotFoundTitle = M("IGDB kennt das Spiel nicht", "IGDB does not know the game");

        public static Msg IgdbGameNotFound(int igdbId) => M(
            $"IGDB hat kein Spiel mit der Nummer {igdbId}. Prüfe die Nummer oder suche über den Namen.",
            $"IGDB has no game with the number {igdbId}. Check the number or search by name.");

        public static readonly Msg IgdbNeedsCredentialsTitle = M("IGDB nicht eingerichtet", "IGDB is not set up");

        public static readonly Msg IgdbNeedsCredentialsDetail = M(
            "Ohne Twitch-Zugangsdaten kann PlatinVault keine Spielfakten und kein Cover von IGDB holen. Trage unter Einstellungen → Twitch & IGDB Client-ID und Client-Secret ein, oder füge das Spiel nur mit Titel und Plattform hinzu (das Cover lädst du dann selbst hoch).",
            "Without Twitch credentials PlatinVault cannot fetch game facts or a cover from IGDB. Enter the Client ID and Client Secret under Settings → Twitch & IGDB, or add the game with just a title and platform (you then upload the cover yourself).");

        // ---- Adding a game --------------------------------------------------------------------------------------------

        public static readonly Msg TitleMissingTitle = M("Titel fehlt", "Title missing");

        public static readonly Msg TitleMissingDetail = M(
            "Bitte gib den Titel des Spiels ein (oder wähle ein Spiel aus der IGDB-Suche).",
            "Please enter the title of the game (or pick a game from the IGDB search).");

        public static Msg TitleTooLong(int max) => M(
            $"Der Titel darf höchstens {max} Zeichen lang sein.",
            $"The title may have at most {max} characters.");

        public static readonly Msg InvalidIgdbIdDetail = M(
            "Die IGDB-Nummer muss eine positive ganze Zahl sein.",
            "The IGDB number must be a positive whole number.");

        public static readonly Msg AlreadyAddedTitle = M("Spiel bereits hinzugefügt", "Game already added");

        public static Msg AlreadyAdded(string title, string platformName, string slug) => M(
            $"„{title}“ ist für {platformName} schon in deiner Sammlung (Adresse: {slug}). Dasselbe Spiel auf einer anderen Plattform kannst du zusätzlich hinzufügen.",
            $"“{title}” is already in your collection for {platformName} (address: {slug}). You can add the same game for another platform as well.");

        // ---- Uploaded cover -------------------------------------------------------------------------------------------

        public static readonly Msg CoverTitle = M("Cover nicht gespeichert", "Cover not saved");

        public static readonly Msg CoverOnlyManual = M(
            "Ein eigenes Cover gibt es nur bei Spielen, die du von Hand hinzugefügt hast. Steam-Spiele bekommen ihr Bild von Steam.",
            "An own cover exists only for games you added by hand. Steam games get their picture from Steam.");

        public static readonly Msg CoverEmpty = M(
            "Die Anfrage enthält kein Bild. Wähle eine PNG-, JPEG-, WebP- oder GIF-Datei.",
            "The request contains no image. Choose a PNG, JPEG, WebP or GIF file.");

        public static Msg CoverTypeUnsupported(string? given) => M(
            $"Der Dateityp „{given ?? "unbekannt"}“ wird nicht angenommen. Erlaubt sind PNG, JPEG, WebP und GIF (kein SVG).",
            $"The file type “{given ?? "unknown"}” is not accepted. PNG, JPEG, WebP and GIF are allowed (no SVG).");

        public static Msg CoverContentMismatch(string declared) => M(
            $"Die Datei ist kein gültiges Bild vom Typ {declared}: ihr Anfang passt nicht zu diesem Typ. Wähle eine unbeschädigte PNG-, JPEG-, WebP- oder GIF-Datei.",
            $"The file is not a valid {declared} image: its beginning does not match that type. Choose an undamaged PNG, JPEG, WebP or GIF file.");

        public static Msg CoverTooLarge(int megabytes) => M(
            $"Das Bild ist größer als {megabytes} MB. Verkleinere es und lade es erneut hoch.",
            $"The image is larger than {megabytes} MB. Make it smaller and upload it again.");

        // ---- Trophy list and ticks ------------------------------------------------------------------------------------

        public static readonly Msg TrophiesOnlyManualTitle = M("Keine eigene Trophäenliste", "No trophy list of its own");

        public static readonly Msg TrophiesOnlyManualDetail = M(
            "Die Erfolge eines Steam-Spiels kommen live von Steam. Eine abhakbare Trophäenliste gibt es nur bei Spielen, die du von Hand hinzugefügt hast.",
            "The achievements of a Steam game come live from Steam. A list you can tick off exists only for games you added by hand.");

        public static readonly Msg TrophyNotFoundTitle = M("Trophäe nicht gefunden", "Trophy not found");

        public static Msg TrophyNotFound(string key) => M(
            $"Dieses Spiel hat keine Trophäe „{key}“. Vielleicht wurde die Liste inzwischen neu recherchiert: lade die Seite neu.",
            $"This game has no trophy “{key}”. The list may have been researched again in the meantime: reload the page.");

        public static readonly Msg TrophyKeyInvalid = M(
            "Der Schlüssel der Trophäe fehlt oder ist zu lang (höchstens 80 Zeichen).",
            "The key of the trophy is missing or too long (at most 80 characters).");

        // ---- Steam link, pins and notes -------------------------------------------------------------------------------

        public static readonly Msg NoSteamLinkTitle = M("Keine Steam-Verknüpfung möglich", "Cannot link to Steam");

        public static Msg NoSteamLink(string platformName) => M(
            $"Dieses Spiel läuft auf {platformName}. Nur ein Spiel der Steam-Bibliothek lässt sich mit einer Steam-App verknüpfen.",
            $"This game is played on {platformName}. Only a game of the Steam library can be linked to a Steam app.");

        public static readonly Msg ScopeInvalidDetail = M(
            "Bitte gib das Spiel an: entweder die Steam-AppID (appId) oder bei einem von Hand hinzugefügten Spiel die Spiel-ID (gameId), nicht beides.",
            "Please name the game: either the Steam AppID (appId) or, for a game added by hand, its game id (gameId), not both.");

        public static readonly Msg ScopeGameNotFoundTitle = M("Spiel nicht gefunden", "Game not found");

        public static Msg ScopeGameNotFound(Guid gameId) => M(
            $"Ein von Hand hinzugefügtes Spiel mit der ID {gameId:D} gibt es nicht (mehr).",
            $"There is no game added by hand with the id {gameId:D} (any more).");

        public static readonly Msg ScopeNotManualTitle = M("Kein von Hand hinzugefügtes Spiel", "Not a game added by hand");

        public static Msg ScopeNotManual(Guid gameId) => M(
            $"Das Spiel mit der ID {gameId:D} gehört zur Steam-Bibliothek. Pins und Notizen eines Steam-Spiels werden über seine Steam-AppID (appId) angesprochen, die Spiel-ID (gameId) gilt nur für von Hand hinzugefügte Spiele.",
            $"The game with the id {gameId:D} belongs to the Steam library. Pins and notes of a Steam game are addressed by its Steam AppID (appId); the game id (gameId) is only for games added by hand.");

        public static readonly Msg NoteKeyUnknownTitle = M("Trophäe nicht gefunden", "Trophy not found");

        public static Msg NoteKeyUnknown(string key) => M(
            $"Dieses Spiel hat keine Trophäe „{key}“, an die sich eine Notiz hängen ließe. Vielleicht wurde die Liste inzwischen neu recherchiert: lade die Seite neu.",
            $"This game has no trophy “{key}” a note could be attached to. The list may have been researched again in the meantime: reload the page.");

        // ---- Guide file of a manual game --------------------------------------------------------------------------------

        public static Msg ImportPlatformInvalid(Msg reason) => M(
            $"„game.platform“ der Datei ist ungültig: {reason.De}",
            $"“game.platform” of the file is invalid: {reason.En}");

        public static readonly Msg ImportSteamAppIdOnManual = M(
            "Ein Spiel einer anderen Plattform als Steam darf keine „game.steamAppId“ haben. Lass das Feld weg oder entferne „game.platform“.",
            "A game of a platform other than Steam must not have a “game.steamAppId”. Leave the field out or remove “game.platform”.");

        public static readonly Msg ImportIgdbIdInvalid = M(
            "„game.igdbId“ muss eine positive ganze Zahl sein.",
            "“game.igdbId” must be a positive whole number.");

        public static readonly Msg ImportTrophiesNotList = M(
            "„trophies“ muss eine Liste sein.",
            "“trophies” must be a list.");

        public static readonly Msg ImportTrophiesOnlyManual = M(
            "Eine Trophäenliste („trophies“) gehört nur zu Spielen von Hand hinzugefügter Plattformen, die Trophäen oder Erfolge haben (nicht Steam, nicht Nintendo Switch).",
            "A trophy list (“trophies”) belongs only to games of platforms that were added by hand and have trophies or achievements (not Steam, not Nintendo Switch).");

        public static Msg ImportTooManyTrophies(int count, int max) => M(
            $"Die Datei enthält {count} Trophäen, erlaubt sind höchstens {max}.",
            $"The file contains {count} trophies, at most {max} are allowed.");

        public static Msg ImportTrophyNotObject(string path) => M(
            $"{path} muss ein Objekt sein.",
            $"{path} must be an object.");

        public static Msg ImportTrophyFieldMissing(string path, string field) => M(
            $"{path}: Das Feld „{field}“ fehlt oder ist leer.",
            $"{path}: the field “{field}” is missing or empty.");

        public static Msg ImportTrophyFieldInvalid(string path, string field, int max) => M(
            $"{path}: Das Feld „{field}“ muss ein Text mit höchstens {max} Zeichen sein.",
            $"{path}: the field “{field}” must be a text of at most {max} characters.");

        public static Msg ImportTrophyNumberInvalid(string path, string field, string range) => M(
            $"{path}: Das Feld „{field}“ muss eine Zahl von {range} sein.",
            $"{path}: the field “{field}” must be a number from {range}.");

        public static Msg ImportTrophyGradeInvalid(string path, string value) => M(
            $"{path}: „{value}“ ist keine Stufe. Möglich sind: platinum, gold, silver, bronze.",
            $"{path}: “{value}” is not a grade. Possible values: platinum, gold, silver, bronze.");

        public static Msg ImportTrophyKeyTwice(string path, string key) => M(
            $"{path}: Der Schlüssel „{key}“ kommt in der Liste zweimal vor.",
            $"{path}: the key “{key}” occurs twice in the list.");

        public static Msg ImportDateInvalid(string path) => M(
            $"„{path}“ muss ein Datum sein, z. B. 2026-11-19T10:00:00Z.",
            $"“{path}” must be a date, e.g. 2026-11-19T10:00:00Z.");

        public static Msg ImportSourcesInvalid(string path) => M(
            $"{path} muss eine Liste von Objekten mit „title“ und „url“ sein.",
            $"{path} must be a list of objects with “title” and “url”.");
    }
}
