using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>Fixed games added by hand for the prompt tests (the golden file and the platform-rule coverage).</summary>
internal static class ManualPromptFixtures
{
    public static readonly DateTime Today = new(2026, 11, 19);

    public static GameEntity Ps5(string language) => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Title = "Astral Run",
        Slug = "astral-run",
        Platform = GamePlatforms.PlayStation,
        PlatformVariant = GamePlatforms.Ps5,
        IgdbId = 123456,
        FactsSource = "igdb",
        ReleaseDate = language == Lang.English ? "12 Oct 2023" : "12. Okt. 2023",
        Developer = "Nebula Works",
        Publisher = "Orbit Games",
        Summary = "A space racer across a dying galaxy.",
        CompletionHours = 42,
        SteamGenres = ["Racing", "Role-playing (RPG)"],
        ContentLanguage = language,
        TrophyListResearchedAt = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero),
        Tabs = [],
    };

    public static List<GameTrophyEntity> Ps5Trophies(Guid gameId) =>
    [
        new() { GameId = gameId, TrophyKey = "astral-champion", SortOrder = 0, Name = "Astral Champion", Description = "Unlock all other trophies.", Grade = TrophyTiers.Platinum },
        new() { GameId = gameId, TrophyKey = "first-lap", SortOrder = 1, Name = "First Lap", Description = "Finish the first race.", Grade = TrophyTiers.Bronze },
        new() { GameId = gameId, TrophyKey = "dark-star", SortOrder = 2, Name = "Dark Star", Description = "Defeat the Dark Star.", Grade = TrophyTiers.Gold, IsHidden = true },
    ];

    /// <summary>The same game as PS4 version, without a researched list yet.</summary>
    public static GameEntity Ps4(string language)
    {
        var game = Ps5(language);
        game.PlatformVariant = GamePlatforms.Ps4;
        game.TrophyListResearchedAt = null;
        return game;
    }

    public static GameEntity Xbox(string language) => new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Title = "Harbor Lights",
        Slug = "harbor-lights",
        Platform = GamePlatforms.Xbox,
        FactsSource = "ai",
        ReleaseDate = "2021",
        Developer = "Lantern Studio",
        SteamGenres = ["Adventure"],
        ContentLanguage = language,
        TrophyListResearchedAt = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero),
        Tabs = [],
    };

    public static List<GameTrophyEntity> XboxTrophies(Guid gameId) =>
    [
        new() { GameId = gameId, TrophyKey = "lighthouse-keeper", SortOrder = 0, Name = "Lighthouse Keeper", Description = "Light every lighthouse.", Points = 50 },
        new() { GameId = gameId, TrophyKey = "night-swim", SortOrder = 1, Name = "Night Swim", Description = "Swim to the island at night.", Points = 15, IsMissable = true },
    ];

    public static GameEntity Epic(string language) => new()
    {
        Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Title = "Rune Forge",
        Slug = "rune-forge",
        Platform = GamePlatforms.Epic,
        ContentLanguage = language,
        Tabs = [],
    };

    public static GameEntity Nintendo(string language) => new()
    {
        Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Title = "Meadow Quest",
        Slug = "meadow-quest",
        Platform = GamePlatforms.Nintendo,
        IgdbId = 98765,
        FactsSource = "igdb",
        ReleaseDate = language == Lang.English ? "3 Mar 2017" : "3. März 2017",
        Developer = "Clover Co.",
        SteamGenres = ["Platform", "Adventure"],
        ContentLanguage = language,
        Tabs = [],
    };

    public static AiGuideGeneratorService.GameContext Context(GameEntity game, IReadOnlyList<GameTrophyEntity> trophies, string language, ResearchBlocks.Selection? blocks = null) =>
        AiGuideGeneratorService.BuildManualContext(game, trophies, blocks ?? ResearchBlocks.Selection.Empty, language, Today);
}
