using Companion.Api.Localization;

namespace Companion.Api.Services;

public sealed partial class AiGuideGeneratorService
{
    // The platform rule: PlatinVault's games come from the player's Steam library, so every guide is about the Steam version on PC. Research sources
    // are often console-centric (PlayStation trophy guides, TrueAchievements), and without this rule the AI copied "PS4 trophy guide: 35 to 50+ hours to
    // the platinum" into a Steam guide. ONE place per language; it is part of ResearchRules, which every prompt that researches or writes guide content
    // includes (blueprint, chapter, walkthrough outline/parts/single, Story & Lore, achievement tips, the manual subscription prompts) - a test checks that.

    /// <summary>The mandatory rule "Steam version on PC only", in the language of the guide.</summary>
    internal static string PlatformRules(string language) => language == Lang.English
        ? """
          Platform (mandatory rule): the player plays the STEAM version on PC.
          - Achievements are Steam achievements. Never write about PlayStation trophies, a platinum trophy, Xbox achievements or Gamerscore;
            "100%" means all Steam achievements (plus the in-game completion they need).
          - Controls are keyboard and mouse (or a controller on PC). Never use console button names (Cross, Square, Triangle, R1, A/B/X/Y …) unless the
            PC version itself shows them.
          - Leave out console-only content: console exclusives, console patch states, PSN/Xbox-only DLC, bonuses or events, cross-save, console
            performance modes. If the PC version differs (patch state, DLC, content), the PC version counts.
          - Console sources (PSNProfiles, PlayStation trophy guides, TrueAchievements, Xbox guides …) may only be used for facts that are the same on PC,
            and are then not named as "PS4/PS5/PlayStation/Xbox guide" in the text – name the fact, not the console. Prefer PC sources (Steam community guides,
            Steam achievement stats, PC wikis).
          - Time estimates ("hours to 100%") are for all Steam achievements on PC – never "hours to the platinum trophy".
          """
        : """
          Plattform (verbindliche Regel): Der Spieler spielt die STEAM-Version auf dem PC.
          - Erfolge sind Steam-Erfolge. Schreibe nie über PlayStation-Trophäen, eine Platin-Trophäe, Xbox-Erfolge oder Gamerscore;
            „100 %“ heißt alle Steam-Erfolge (plus die Spiel-Fortschritte, die sie brauchen).
          - Steuerung ist Tastatur und Maus (oder ein Controller am PC). Nutze nie Konsolen-Tastennamen (Kreuz, Quadrat, Dreieck, R1, A/B/X/Y …), außer die
            PC-Version zeigt sie selbst an.
          - Lass reine Konsolen-Inhalte weg: Konsolen-Exklusives, Konsolen-Patchstände, PSN-/Xbox-exklusive DLCs, Boni oder Events, Cross-Save,
            Konsolen-Leistungsmodi. Unterscheidet sich die PC-Version (Patchstand, DLC, Inhalt), gilt die PC-Version.
          - Konsolen-Quellen (PSNProfiles, PlayStation-Trophäen-Guides, TrueAchievements, Xbox-Guides …) nur für Fakten nutzen, die am PC genauso gelten,
            und im Text dann nicht als „PS4-/PS5-/PlayStation-/Xbox-Guide“ nennen – nenne den Fakt, nicht die Konsole. Bevorzuge PC-Quellen
            (Steam-Community-Guides, Steam-Erfolgsstatistiken, PC-Wikis).
          - Zeitangaben („Stunden bis 100 %“) gelten für alle Steam-Erfolge am PC – nie „Stunden bis zur Platin-Trophäe“.
          """;
}
