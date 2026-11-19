using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Tests;

/// <summary>Migration AddManualGames on a real SQLite file: existing games become "steam", the new tables and their rules.</summary>
public sealed class ManualGamesMigrationTests
{
    private const string MigrationBefore = "20261116234222_AddIgdbUserRating";

    [Fact]
    public async Task Existing_games_become_steam_games_and_keep_their_data()
    {
        using var scratch = new SqliteScratch();
        await SqliteDatabase.PrepareAsync(scratch.DatabaseFile, NullLogger.Instance);
        await using (var before = scratch.Context())
        {
            await before.GetService<IMigrator>().MigrateAsync(MigrationBefore, cancellationToken: CancellationToken.None);
            // One Steam tile and one imported guide without an AppID, written with the columns of the schema before the migration
            await before.Database.ExecuteSqlRawAsync("""
                INSERT INTO games (Id, Slug, Title, Subtitle, SteamAppId, CoverUrl, HeroBannerUrl, AccentColor, EstimatedHours, MissablesInfo,
                    DifficultyInfo, GenreTags, TrophiesUnlocked, TrophiesTotal, PlatinumAt, IsFavorite, SteamGenres, SortOrder, CreatedAt, UpdatedAt)
                VALUES
                    ('11111111-1111-1111-1111-111111111111', 'old-steam', 'Old Steam', '', 620, '', '', 'emerald', '', '', '', '', 12, 12,
                     '2026-11-01T10:00:00.0000000+00:00', 1, '[]', 0, '2026-11-01T10:00:00.0000000+00:00', '2026-11-01T10:00:00.0000000+00:00'),
                    ('22222222-2222-2222-2222-222222222222', 'imported', 'Imported', '', NULL, '', '', 'emerald', '', '', '', '', 0, 0,
                     NULL, 1, '[]', 1, '2026-11-01T10:00:00.0000000+00:00', '2026-11-01T10:00:00.0000000+00:00');
                """);
        }

        await using var db = scratch.Context();
        await db.Database.MigrateAsync();

        var games = await db.Games.AsNoTracking().OrderBy(g => g.SortOrder).ToListAsync();
        Assert.Equal(["old-steam", "imported"], games.Select(g => g.Slug));
        Assert.All(games, g => Assert.Equal(GamePlatforms.Steam, g.Platform));
        Assert.All(games, g => Assert.Null(g.PlatformVariant));
        Assert.All(games, g => Assert.Null(g.TrophyListResearchedAt));
        Assert.Equal(620, games[0].SteamAppId);
        Assert.Equal(12, games[0].TrophiesUnlocked);
        Assert.NotNull(games[0].PlatinumAt);
        Assert.Empty(await db.GameTrophies.ToListAsync());
    }

    [Fact]
    public async Task A_trophy_key_is_unique_per_game_and_everything_goes_with_the_game()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var game = TrophyFixtures.Game(GamePlatforms.PlayStation, GamePlatforms.Ps5, "a");
        var other = TrophyFixtures.Game(GamePlatforms.Xbox, null, "b");
        db.Games.AddRange(game, other);
        db.GameTrophies.Add(TrophyFixtures.Trophy(game.Id, "first", 0));
        // The same key in another game is fine
        db.GameTrophies.Add(TrophyFixtures.Trophy(other.Id, "first", 0));
        db.GameCovers.Add(new GameCoverEntity { GameId = game.Id, ContentType = "image/png", Data = [0x89, 0x50, 0x4E, 0x47], Sha256 = new string('0', 64) });
        db.GameTrophyTranslations.Add(new GameTrophyTranslationEntity { GameId = game.Id, Language = "en", ItemsJson = "[]", SourceHash = "x" });
        await db.SaveChangesAsync();

        await using (var second = scratch.Context())
        {
            second.GameTrophies.Add(TrophyFixtures.Trophy(game.Id, "first", 1));
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }

        await db.Games.Where(g => g.Id == game.Id).ExecuteDeleteAsync();
        Assert.Equal(["b"], await db.Games.Select(g => g.Slug).ToListAsync());
        Assert.Equal(1, await db.GameTrophies.CountAsync());
        Assert.Equal(0, await db.GameCovers.CountAsync());
        Assert.Equal(0, await db.GameTrophyTranslations.CountAsync());
    }

    [Fact]
    public async Task A_new_game_without_a_platform_is_a_steam_game_and_the_dtos_carry_the_platform()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var steam = new GameEntity { Id = Guid.NewGuid(), Slug = "steam-game", Title = "Steam game", SteamAppId = 10 };
        var manual = TrophyFixtures.Game(GamePlatforms.PlayStation, GamePlatforms.Ps4, "manual");
        manual.TrophyListResearchedAt = DateTimeOffset.UtcNow;
        manual.FirstReleaseAt = new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero);
        db.Games.AddRange(steam, manual);
        await db.SaveChangesAsync();

        await using var read = scratch.Context();
        var stored = await read.Games.AsNoTracking().Include(g => g.Tabs).Include(g => g.ProgressChecks).ToListAsync();
        var steamDto = ApiEndpoints.MapToSummaryDto(stored.Single(g => g.Slug == "steam-game"));
        var manualDto = ApiEndpoints.MapToSummaryDto(stored.Single(g => g.Slug == "manual"));

        Assert.Equal("steam", steamDto.Platform);
        Assert.False(steamDto.HasTrophyList);
        Assert.Equal("playstation", manualDto.Platform);
        Assert.Equal("ps4", manualDto.PlatformVariant);
        Assert.True(manualDto.HasTrophyList);
        Assert.Equal(manual.FirstReleaseAt, manualDto.FirstReleaseAt);
    }
}

/// <summary>Ticks, counts and the platinum moment of manual games (TrophyProgress), incl. D3 and two servers on one file.</summary>
public sealed class TrophyProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ticking_counts_and_100_percent_is_platinum_on_a_platform_without_a_platinum_grade()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var game = await TrophyFixtures.SeedAsync(db, GamePlatforms.Xbox, null, ("a", null, null), ("b", null, null), ("c", null, "DLC"));

        var first = await TrophyProgress.SetUnlockedAsync(db, game, "a", true, Now, default);
        Assert.Equal(TrophyTickOutcome.Changed, first.Outcome);
        Assert.True(first.AnnounceTrophy);
        Assert.False(first.AnnouncePlatinum);
        Assert.Equal((3, 1), (first.Progress!.Total, first.Progress.Unlocked));

        await TrophyProgress.SetUnlockedAsync(db, game, "b", true, Now, default);
        var last = await TrophyProgress.SetUnlockedAsync(db, game, "c", true, Now.AddMinutes(1), default);
        Assert.True(last.AnnounceTrophy);
        Assert.True(last.AnnouncePlatinum);
        Assert.Equal(Now.AddMinutes(1), last.Progress!.PlatinumAt);

        var stored = await TrophyFixtures.ReadGameAsync(scratch, game);
        Assert.Equal((3, 3), (stored.TrophiesTotal, stored.TrophiesUnlocked));
        Assert.Equal(Now.AddMinutes(1), stored.PlatinumAt);
        Assert.NotNull(stored.TrophiesUpdatedAt);

        // Un-ticking is silent and takes the platinum date away again
        var undo = await TrophyProgress.SetUnlockedAsync(db, game, "a", false, Now.AddMinutes(2), default);
        Assert.Equal(TrophyTickOutcome.Changed, undo.Outcome);
        Assert.False(undo.AnnounceTrophy);
        Assert.False(undo.AnnouncePlatinum);
        Assert.True(undo.Progress!.PlatinumLost);
        Assert.Null((await TrophyFixtures.ReadGameAsync(scratch, game)).PlatinumAt);
    }

    [Fact]
    public async Task Ticking_twice_changes_nothing_the_second_time()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var game = await TrophyFixtures.SeedAsync(db, GamePlatforms.Epic, null, ("a", null, null), ("b", null, null));

        Assert.True((await TrophyProgress.SetUnlockedAsync(db, game, "a", true, Now, default)).AnnounceTrophy);
        var again = await TrophyProgress.SetUnlockedAsync(db, game, "a", true, Now.AddHours(1), default);
        Assert.Equal(TrophyTickOutcome.Unchanged, again.Outcome);
        Assert.False(again.AnnounceTrophy);
        // The first tick's time stays
        var trophy = await TrophyFixtures.ReadTrophyAsync(scratch, game, "a");
        Assert.Equal(Now, trophy.UnlockedAt);

        Assert.Equal(TrophyTickOutcome.Unchanged, (await TrophyProgress.SetUnlockedAsync(db, game, "b", false, Now, default)).Outcome);
        Assert.Equal(TrophyTickOutcome.TrophyNotFound, (await TrophyProgress.SetUnlockedAsync(db, game, "zzz", true, Now, default)).Outcome);
        Assert.Equal(TrophyTickOutcome.GameNotFound, (await TrophyProgress.SetUnlockedAsync(db, Guid.NewGuid(), "a", true, Now, default)).Outcome);
    }

    [Fact]
    public async Task On_playstation_platinum_is_the_platinum_trophy_not_100_percent()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var game = await TrophyFixtures.SeedAsync(db, GamePlatforms.PlayStation, GamePlatforms.Ps5,
            ("plat", "platinum", null), ("g", "gold", null), ("s", "silver", null), ("dlc", "bronze", "Story DLC"));

        await TrophyProgress.SetUnlockedAsync(db, game, "g", true, Now, default);
        // The DLC trophy does not count for the platinum trophy, but every base trophy is ticked only after "s"
        var dlc = await TrophyProgress.SetUnlockedAsync(db, game, "dlc", true, Now, default);
        Assert.Null(dlc.AutoTickedPlatinumKey);
        Assert.False(dlc.AnnouncePlatinum);

        // D3: the last base trophy ticks the platinum trophy - one normal popup for "s", then the platinum moment, never a popup for "plat"
        var last = await TrophyProgress.SetUnlockedAsync(db, game, "s", true, Now.AddMinutes(5), default);
        Assert.True(last.AnnounceTrophy);
        Assert.Equal("plat", last.AutoTickedPlatinumKey);
        Assert.True(last.AnnouncePlatinum);
        Assert.Equal((4, 4), (last.Progress!.Total, last.Progress.Unlocked));
        Assert.Equal(Now.AddMinutes(5), (await TrophyFixtures.ReadTrophyAsync(scratch, game, "plat")).UnlockedAt);

        // Un-ticking the DLC trophy: 3 of 4, still platinum (platinum is not 100 % on PlayStation)
        var dlcUndo = await TrophyProgress.SetUnlockedAsync(db, game, "dlc", false, Now.AddMinutes(6), default);
        Assert.Null(dlcUndo.AutoUntickedPlatinumKey);
        Assert.False(dlcUndo.Progress!.PlatinumLost);
        Assert.NotNull(dlcUndo.Progress.PlatinumAt);

        // D3: un-ticking a base trophy un-ticks the platinum trophy silently
        var baseUndo = await TrophyProgress.SetUnlockedAsync(db, game, "g", false, Now.AddMinutes(7), default);
        Assert.Equal("plat", baseUndo.AutoUntickedPlatinumKey);
        Assert.False(baseUndo.AnnounceTrophy);
        Assert.False(baseUndo.AnnouncePlatinum);
        Assert.True(baseUndo.Progress!.PlatinumLost);
        Assert.Null((await TrophyFixtures.ReadTrophyAsync(scratch, game, "plat")).UnlockedAt);
        Assert.Equal(1, baseUndo.Progress.Unlocked);
    }

    [Fact]
    public async Task Ticking_the_platinum_trophy_itself_is_the_platinum_moment_only()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var game = await TrophyFixtures.SeedAsync(db, GamePlatforms.PlayStation, GamePlatforms.Ps4, ("plat", "platinum", null), ("g", "gold", null));

        var result = await TrophyProgress.SetUnlockedAsync(db, game, "plat", true, Now, default);
        Assert.True(result.IsPlatinumTrophy);
        Assert.False(result.AnnounceTrophy);
        Assert.True(result.AnnouncePlatinum);
        Assert.Equal((2, 1), (result.Progress!.Total, result.Progress.Unlocked));
    }

    [Fact]
    public async Task A_playstation_list_without_a_platinum_trophy_reaches_platinum_at_100_percent()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var game = await TrophyFixtures.SeedAsync(db, GamePlatforms.PlayStation, GamePlatforms.Ps5, ("g", "gold", null), ("b", "bronze", null));

        Assert.False((await TrophyProgress.SetUnlockedAsync(db, game, "g", true, Now, default)).AnnouncePlatinum);
        Assert.True((await TrophyProgress.SetUnlockedAsync(db, game, "b", true, Now, default)).AnnouncePlatinum);
    }

    [Fact]
    public async Task Recompute_reads_the_table_and_never_touches_a_steam_game()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var steam = new GameEntity
        {
            Id = Guid.NewGuid(), Slug = "steam", Title = "Steam", SteamAppId = 70, TrophiesTotal = 5, TrophiesUnlocked = 5, PlatinumAt = Now,
        };
        db.Games.Add(steam);
        await db.SaveChangesAsync();

        var steamState = await TrophyProgress.RecomputeAsync(db, steam.Id, Now.AddDays(1), default);
        Assert.Equal((5, 5, Now), (steamState!.Total, steamState.Unlocked, steamState.PlatinumAt));
        var storedSteam = await TrophyFixtures.ReadGameAsync(scratch, steam.Id);
        Assert.Equal((5, 5, Now), (storedSteam.TrophiesTotal, storedSteam.TrophiesUnlocked, storedSteam.PlatinumAt));

        // A re-researched list saved with every trophy ticked: the recompute after saving reaches platinum once
        var game = await TrophyFixtures.SeedAsync(db, GamePlatforms.Gog, null, ("a", null, null), ("b", null, null));
        await db.GameTrophies.Where(t => t.GameId == game).ExecuteUpdateAsync(s => s.SetProperty(t => t.UnlockedAt, Now));
        var first = await TrophyProgress.RecomputeAsync(db, game, Now, default);
        Assert.True(first!.PlatinumReached);
        Assert.Equal((2, 2), (first.Total, first.Unlocked));
        Assert.False((await TrophyProgress.RecomputeAsync(db, game, Now, default))!.PlatinumReached);
        Assert.Null(await TrophyProgress.RecomputeAsync(db, Guid.NewGuid(), Now, default));
    }

    [Fact]
    public async Task Two_servers_ticking_the_same_trophy_announce_it_exactly_once()
    {
        using var scratch = new SqliteScratch();
        Guid game;
        await using (var setup = await scratch.MigratedAsync())
        {
            game = await TrophyFixtures.SeedAsync(setup, GamePlatforms.Xbox, null, ("a", null, null), ("b", null, null), ("c", null, null));
        }

        // Sequential: the second server finds the tick already set
        await using (var serverA = scratch.Context())
        await using (var serverB = scratch.Context())
        {
            Assert.True((await TrophyProgress.SetUnlockedAsync(serverA, game, "a", true, Now, default)).AnnounceTrophy);
            Assert.False((await TrophyProgress.SetUnlockedAsync(serverB, game, "a", true, Now, default)).AnnounceTrophy);
        }

        // At the same time: several contexts (each its own connection) race for one trophy
        for (var round = 0; round < 5; round++)
        {
            var key = round % 2 == 0 ? "b" : "c";
            var contexts = Enumerable.Range(0, 6).Select(_ => scratch.Context()).ToList();
            try
            {
                var results = await Task.WhenAll(contexts.Select(c => Task.Run(() => TrophyProgress.SetUnlockedAsync(c, game, key, true, Now, default))));
                Assert.Single(results, r => r.AnnounceTrophy);
            }
            finally
            {
                foreach (var context in contexts) await context.DisposeAsync();
            }
            await using var reset = scratch.Context();
            await TrophyProgress.SetUnlockedAsync(reset, game, key, false, Now, default);
        }
    }

    [Fact]
    public async Task Two_servers_ticking_the_last_two_trophies_at_once_give_one_platinum_moment()
    {
        for (var round = 0; round < 5; round++)
        {
            using var scratch = new SqliteScratch();
            Guid game;
            await using (var setup = await scratch.MigratedAsync())
            {
                game = await TrophyFixtures.SeedAsync(setup, GamePlatforms.PlayStation, GamePlatforms.Ps5,
                    ("plat", "platinum", null), ("x", "gold", null), ("y", "silver", null));
            }

            await using var serverA = scratch.Context();
            await using var serverB = scratch.Context();
            var results = await Task.WhenAll(
                Task.Run(() => TrophyProgress.SetUnlockedAsync(serverA, game, "x", true, Now, default)),
                Task.Run(() => TrophyProgress.SetUnlockedAsync(serverB, game, "y", true, Now, default)));

            Assert.All(results, r => Assert.True(r.AnnounceTrophy));
            Assert.Single(results, r => r.AnnouncePlatinum);
            Assert.Single(results, r => r.AutoTickedPlatinumKey == "plat");
            var stored = await TrophyFixtures.ReadGameAsync(scratch, game);
            Assert.Equal((3, 3), (stored.TrophiesTotal, stored.TrophiesUnlocked));
            Assert.NotNull(stored.PlatinumAt);
        }
    }
}

/// <summary>Platform ids and the validation of a manual game's platform choice.</summary>
public sealed class GamePlatformsTests
{
    [Theory]
    [InlineData("xbox", null, "xbox", null)]
    [InlineData(" Xbox ", "", "xbox", null)]
    [InlineData("playstation", "ps5", "playstation", "ps5")]
    [InlineData("PlayStation", " PS4 ", "playstation", "ps4")]
    [InlineData("nintendo", null, "nintendo", null)]
    [InlineData("battlenet", null, "battlenet", null)]
    public void Valid_choices_come_back_normalised(string platform, string? variant, string expectedPlatform, string? expectedVariant)
    {
        var result = GamePlatforms.Validate(platform, variant);
        Assert.True(result.Ok);
        Assert.Equal(new GamePlatformChoice(expectedPlatform, expectedVariant), result.Choice);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData(null, null, GamePlatformError.MissingPlatform)]
    [InlineData("  ", null, GamePlatformError.MissingPlatform)]
    [InlineData("stadia", null, GamePlatformError.UnknownPlatform)]
    [InlineData("steam", null, GamePlatformError.SteamNotManual)]
    [InlineData("playstation", null, GamePlatformError.VariantRequired)]
    [InlineData("playstation", "ps3", GamePlatformError.UnknownVariant)]
    [InlineData("xbox", "ps5", GamePlatformError.VariantNotAllowed)]
    public void Invalid_choices_are_refused_with_a_complete_reason(string? platform, string? variant, GamePlatformError expected)
    {
        var result = GamePlatforms.Validate(platform, variant);
        Assert.False(result.Ok);
        Assert.Equal(expected, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Message?.De));
        Assert.False(string.IsNullOrWhiteSpace(result.Message?.En));
    }

    [Fact]
    public void The_platform_properties_match_the_owner_decisions()
    {
        Assert.Equal(["steam", "playstation", "xbox", "epic", "gog", "nintendo", "ubisoft", "ea", "battlenet"], GamePlatforms.Ids);
        Assert.DoesNotContain("steam", GamePlatforms.ManualIds);
        Assert.Equal(["ps5", "ps4"], GamePlatforms.VariantsOf("playstation"));
        Assert.Empty(GamePlatforms.VariantsOf("xbox"));
        Assert.False(GamePlatforms.HasTrophies("nintendo"));
        Assert.All(GamePlatforms.Ids.Where(id => id != "nintendo"), id => Assert.True(GamePlatforms.HasTrophies(id)));
        Assert.Equal("trophies", GamePlatforms.TrophyKind("playstation"));
        Assert.Equal("achievements", GamePlatforms.TrophyKind("xbox"));
        Assert.True(GamePlatforms.HasPlatinumGrade("playstation"));
        Assert.False(GamePlatforms.HasPlatinumGrade("xbox"));
        Assert.True(GamePlatforms.IsSteam((string?)null));
        Assert.Equal("PlayStation 5", GamePlatforms.DisplayName("playstation", "ps5"));
        Assert.Equal("Nintendo Switch", GamePlatforms.DisplayName("nintendo"));
    }
}

/// <summary>D4: the tier of a manual trophy.</summary>
public sealed class TrophyTiersTests
{
    [Theory]
    [InlineData("playstation", "platinum", null, null, "Platinum")]
    [InlineData("playstation", "gold", null, 90.0, "Gold")]
    [InlineData("playstation", "silver", null, 1.0, "Silver")]
    [InlineData("playstation", "bronze", null, null, "Bronze")]
    [InlineData("playstation", null, null, 4.0, "Gold")]
    [InlineData("xbox", null, 100, null, "Gold")]
    [InlineData("xbox", null, 50, null, "Gold")]
    [InlineData("xbox", null, 49, null, "Silver")]
    [InlineData("xbox", null, 20, null, "Silver")]
    [InlineData("xbox", null, 19, null, "Bronze")]
    [InlineData("xbox", null, 5, 3.0, "Gold")]
    [InlineData("xbox", null, null, null, "Bronze")]
    [InlineData("epic", null, null, 30.0, "Silver")]
    [InlineData("epic", null, 100, null, "Bronze")]
    [InlineData("gog", "platinum", null, null, "Bronze")]
    [InlineData("ubisoft", null, null, 80.0, "Bronze")]
    public void The_tier_follows_grade_then_share_then_gamerscore(string platform, string? grade, int? points, double? rarity, string expected)
    {
        Assert.Equal(expected, TrophyTiers.For(platform, grade, points, rarity));
    }

    [Fact]
    public void Only_a_playstation_platinum_grade_is_ever_platinum()
    {
        foreach (var platform in GamePlatforms.Ids.Where(p => p != "playstation"))
        {
            foreach (var rarity in new double?[] { null, 0, 0.1, 10, 50, 100 })
            {
                Assert.NotEqual("Platinum", TrophyTiers.For(platform, "platinum", 1000, rarity));
            }
        }
    }

    [Fact]
    public void A_manual_item_is_a_complete_live_item_for_the_popup()
    {
        var item = new ManualTrophyItemDto("key", "Name", "Desc", null, false, "", "", true, DateTime.UtcNow, 3.5, true, "Gold", "Sehr selten",
            "ultraRare", false, "note", "gold", null, true, null, "Story", "Tip", 2, "Original");
        var live = item.ToLiveItem();
        Assert.Equal(("key", "Name", "Gold", true, "ultraRare", "note"), (live.ApiName, live.DisplayName, live.RarityTier, live.HasGlobalPercent, live.RarityBand, live.UserNote));
    }
}

internal static class TrophyFixtures
{
    public static GameEntity Game(string platform, string? variant, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        Title = "Game " + slug,
        Platform = platform,
        PlatformVariant = variant,
    };

    public static GameTrophyEntity Trophy(Guid gameId, string key, int sortOrder, string? grade = null, string? group = null) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        TrophyKey = key,
        SortOrder = sortOrder,
        Name = "Trophy " + key,
        Description = "Do " + key,
        Grade = grade,
        GroupName = group,
    };

    public static async Task<Guid> SeedAsync(CompanionDbContext db, string platform, string? variant, params (string Key, string? Grade, string? Group)[] trophies)
    {
        var game = Game(platform, variant, "g-" + Guid.NewGuid().ToString("N")[..8]);
        db.Games.Add(game);
        for (var i = 0; i < trophies.Length; i++)
        {
            db.GameTrophies.Add(Trophy(game.Id, trophies[i].Key, i, trophies[i].Grade, trophies[i].Group));
        }
        await db.SaveChangesAsync();
        return game.Id;
    }

    public static async Task<GameEntity> ReadGameAsync(SqliteScratch scratch, Guid id)
    {
        await using var db = scratch.Context();
        return await db.Games.AsNoTracking().SingleAsync(g => g.Id == id);
    }

    public static async Task<GameTrophyEntity> ReadTrophyAsync(SqliteScratch scratch, Guid gameId, string key)
    {
        await using var db = scratch.Context();
        return await db.GameTrophies.AsNoTracking().SingleAsync(t => t.GameId == gameId && t.TrophyKey == key);
    }
}
