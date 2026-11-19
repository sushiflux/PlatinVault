using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The reader of a researched trophy list (<see cref="TrophyList"/>) and the merge of a re-research (<see cref="TrophyMerge"/>) - pure, no database, no
/// provider. The reader is the same for the AI job and the answer pasted from the own subscription; only the platinum rule is stricter on the latter.
/// </summary>
public sealed class TrophyListTests
{
    private static TrophyListExpectations Ps5(bool strict = false, bool facts = false) =>
        new(GamePlatforms.PlayStation, GamePlatforms.Ps5, "Astral Run", WantTrophies: true, WantFacts: facts, StrictPlatinum: strict);

    private static TrophyListExpectations Xbox(bool facts = false) =>
        new(GamePlatforms.Xbox, null, "Harbor Lights", WantTrophies: true, WantFacts: facts, StrictPlatinum: true);

    private static string Answer(string trophies, string extra = "") =>
        $$"""{ "game": "Astral Run", "platform": "ps5", "documented": true, "trophies": [{{trophies}}], "sources": [{ "title": "PSNProfiles", "url": "https://psnprofiles.com/trophies/1-astral-run" }]{{extra}} }""";

    private const string Platinum = """{ "id": "astral-champion", "name": "Astral Champion", "grade": "platinum", "description": "Unlock all trophies." }""";
    private const string Gold = """{ "id": "dark-star", "name": "Dark Star", "grade": "gold", "description": "Defeat the Dark Star.", "hidden": true, "missable": true, "category": "Story" }""";

    // ---- Reader: errors -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_answer_that_is_no_json_is_an_error_with_nothing_to_save()
    {
        var read = TrophyList.Read("Leider kann ich das nicht.", Ps5());

        Assert.False(read.Ok);
        Assert.NotEmpty(read.Errors);
        Assert.Empty(read.Trophies);
    }

    [Fact]
    public void A_cut_off_answer_is_an_error()
    {
        var full = Answer(Platinum + "," + Gold);
        var read = TrophyList.Read(full[..(full.Length / 2)], Ps5());

        Assert.False(read.Ok);
        Assert.Empty(read.Trophies);
    }

    [Fact]
    public void Documented_false_is_the_no_reliable_list_error_with_the_reason()
    {
        using var _ = Lang.Use(Lang.English);
        var read = TrophyList.Read("""{ "game": "Astral Run", "documented": false, "reason": "Only the PS4 list is documented." }""", Ps5());

        var error = Assert.Single(read.Errors);
        Assert.Equal("noReliableList", error.Code);
        Assert.Contains("Only the PS4 list is documented.", error.Message);
        Assert.Contains("Nothing was saved", error.Message);
    }

    [Fact]
    public void An_empty_list_counts_as_no_reliable_list()
    {
        var read = TrophyList.Read(Answer(""), Ps5());

        Assert.Equal("noReliableList", Assert.Single(read.Errors).Code);
    }

    [Theory]
    [InlineData("""{ "game": "Astral Run", "documented": true }""", "noTrophies")]
    [InlineData("""{ "game": "Astral Run", "trophies": { "a": 1 } }""", "trophiesNotList")]
    public void A_missing_or_wrong_list_field_is_an_error(string text, string code)
    {
        Assert.Equal(code, Assert.Single(TrophyList.Read(text, Ps5()).Errors).Code);
    }

    [Fact]
    public void More_than_the_limit_is_refused_and_many_is_a_warning()
    {
        static string Entries(int count) => string.Join(",", Enumerable.Range(0, count).Select(i => $$"""{ "id": "t-{{i}}", "name": "Trophy {{i}}", "points": 10 }"""));

        var tooMany = TrophyList.Read($$"""{ "game": "Harbor Lights", "trophies": [{{Entries(TrophyList.MaxTrophies + 1)}}] }""", Xbox());
        var many = TrophyList.Read($$"""{ "game": "Harbor Lights", "trophies": [{{Entries(TrophyList.ManyTrophies + 1)}}] }""", Xbox());

        Assert.Equal("tooMany", Assert.Single(tooMany.Errors).Code);
        Assert.True(many.Ok);
        Assert.Contains(many.Warnings, w => w.Code == "manyTrophies");
    }

    [Fact]
    public void PlayStation_entries_without_a_valid_grade_are_an_error_naming_them()
    {
        var read = TrophyList.Read(Answer(Platinum + """, { "id": "x", "name": "No Grade", "grade": "diamond" }"""), Ps5());

        var error = Assert.Single(read.Errors);
        Assert.Equal("missingGrade", error.Code);
        Assert.Contains("No Grade", error.Message);
    }

    // ---- Reader: platinum ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Several_platinum_trophies_are_an_error_on_the_own_subscription_way()
    {
        var read = TrophyList.Read(Answer(Platinum + """, { "id": "second", "name": "Second Platinum", "grade": "platinum" }"""), Ps5(strict: true));

        Assert.Equal("severalPlatinum", Assert.Single(read.Errors).Code);
        Assert.Empty(read.Trophies);
    }

    [Fact]
    public void The_AI_research_keeps_only_the_first_platinum_trophy_with_a_warning()
    {
        var read = TrophyList.Read(Answer(Platinum + "," + Gold + """, { "id": "second", "name": "Second Platinum", "grade": "platinum" }"""), Ps5());

        Assert.True(read.Ok);
        Assert.Equal(["astral-champion", "dark-star"], read.Trophies.Select(t => t.Key));
        Assert.Contains(read.Warnings, w => w.Code == "extraPlatinumDropped" && w.Message.Contains("Second Platinum"));
    }

    [Fact]
    public void A_list_without_platinum_is_only_a_warning()
    {
        var read = TrophyList.Read(Answer(Gold), Ps5());

        Assert.True(read.Ok);
        Assert.Contains(read.Warnings, w => w.Code == "noPlatinum");
    }

    // ---- Reader: values -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Values_are_read_and_checked_and_unknown_fields_are_ignored()
    {
        var read = TrophyList.Read(Answer(Platinum + "," + Gold + """, { "id": "dlc-one", "name": "Ein Stern", "originalName": "One Star", "grade": "bronze", "group": "Frozen Rift DLC", "rarityPercent": 12.345, "script": "<script>alert(1)</script>", "tip": "  Sammle alles.  " }"""), Ps5());

        Assert.True(read.Ok, string.Join(" | ", read.Errors.Select(e => e.Message)));
        var gold = read.Trophies[1];
        Assert.Equal(("dark-star", TrophyTiers.Gold, true, true, "Story"), (gold.Key, gold.Grade, gold.Hidden, gold.Missable, gold.Category));
        Assert.Null(gold.Group);
        var dlc = read.Trophies[2];
        Assert.Equal(("One Star", "Frozen Rift DLC", 12.35, "Sammle alles."), (dlc.OriginalName, dlc.Group, dlc.RarityPercent, dlc.Tip));
        Assert.Equal("https://psnprofiles.com/trophies/1-astral-run", Assert.Single(read.Sources).Url);
    }

    [Fact]
    public void Points_and_shares_out_of_range_are_dropped_with_a_warning_and_PlayStation_ignores_points()
    {
        var xbox = TrophyList.Read("""{ "game": "Harbor Lights", "trophies": [ { "id": "a", "name": "A", "points": 5000, "rarityPercent": 140 }, { "id": "b", "name": "B", "points": "30" } ], "sources": ["TrueAchievements – https://www.trueachievements.com/game/Harbor-Lights"] }""", Xbox());
        var ps = TrophyList.Read(Answer("""{ "id": "p", "name": "P", "grade": "platinum", "points": 90 }"""), Ps5());

        Assert.True(xbox.Ok);
        Assert.Equal([null, 30], xbox.Trophies.Select(t => t.Points));
        Assert.Null(xbox.Trophies[0].RarityPercent);
        Assert.Contains(xbox.Warnings, w => w.Code == "pointsDropped");
        Assert.Contains(xbox.Warnings, w => w.Code == "rarityDropped");
        Assert.Equal("TrueAchievements", Assert.Single(xbox.Sources).Title);
        Assert.Null(Assert.Single(ps.Trophies).Points);
    }

    [Fact]
    public void Bad_and_duplicate_ids_are_made_from_the_name_and_unique()
    {
        var read = TrophyList.Read(Answer(Platinum + """, { "id": "Bad Id!", "name": "Über den Wolken" , "grade": "bronze" }, { "id": "astral-champion", "name": "Copy", "grade": "bronze" }, { "name": "  ", "grade": "bronze" }"""), Ps5());

        Assert.True(read.Ok);
        Assert.Equal(["astral-champion", "ueber-den-wolken", "astral-champion-2"], read.Trophies.Select(t => t.Key));
        Assert.Contains(read.Warnings, w => w.Code == "idsMadeUnique");
        Assert.Contains(read.Warnings, w => w.Code == "withoutName");
    }

    [Fact]
    public void Long_texts_are_shortened_and_texts_stay_plain_data()
    {
        var long_ = new string('x', 900);
        var read = TrophyList.Read(Answer($$"""{ "id": "a", "name": "<b>Bold</b>", "grade": "platinum", "description": "{{long_}}" }"""), Ps5());

        var item = Assert.Single(read.Trophies);
        Assert.Equal("<b>Bold</b>", item.Name);                 // stored as text, escaped by the UI like every text
        Assert.True(item.Description.Length <= 600);
        Assert.Contains(read.Warnings, w => w.Code == "textsShortened");
    }

    [Fact]
    public void No_sources_another_platform_and_another_game_are_warnings()
    {
        var read = TrophyList.Read("""{ "game": "Meadow Quest", "platform": "ps4", "trophies": [ { "id": "a", "name": "A", "grade": "platinum" } ] }""", Ps5());

        Assert.True(read.Ok);
        Assert.Contains(read.Warnings, w => w.Code == "noSources");
        Assert.Contains(read.Warnings, w => w.Code == "otherPlatform" && w.Message.Contains("ps5"));
        Assert.Contains(read.Warnings, w => w.Code is not ("noSources" or "otherPlatform"));
    }

    [Fact]
    public void Facts_are_read_with_plausible_values_only()
    {
        var read = TrophyList.Read(Answer(Platinum, """, "facts": { "releaseDate": "2023-10-12", "developers": ["Nebula Works"], "publishers": ["Orbit Games", "orbit games"], "genres": ["Racing"], "summary": "Ein Rennspiel.", "hoursComplete": 99999 }"""), Ps5(facts: true));
        var year = TrophyList.Read(Answer(Platinum, """, "facts": { "releaseDate": "2019" }"""), Ps5(facts: true));
        var bad = TrophyList.Read(Answer(Platinum, """, "facts": { "releaseDate": "soon" }"""), Ps5(facts: true));

        var facts = read.Facts!;
        Assert.Equal(new DateTimeOffset(2023, 10, 12, 0, 0, 0, TimeSpan.Zero), facts.FirstReleaseAt);
        Assert.Equal(["Orbit Games"], facts.Publishers);
        Assert.Null(facts.HoursComplete);
        Assert.Contains(read.Warnings, w => w.Code == "factsValueDropped" && w.Message.Contains("hoursComplete"));
        Assert.Equal(("2019", new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero)), (year.Facts!.ReleaseDate, year.Facts.FirstReleaseAt));
        Assert.Null(bad.Facts);
        Assert.Contains(bad.Warnings, w => w.Code == "noFacts");
    }

    [Fact]
    public void Facts_only_without_facts_is_an_error()
    {
        var read = TrophyList.Read("""{ "game": "Meadow Quest" }""", new(GamePlatforms.Nintendo, null, "Meadow Quest", WantTrophies: false, WantFacts: true, StrictPlatinum: true));

        Assert.False(read.Ok);
        Assert.Contains(read.Errors, e => e.Code == "noFacts");
    }

    [Fact]
    public void Code_fences_and_text_around_the_answer_are_tolerated()
    {
        var read = TrophyList.Read("Hier ist die Liste:\n```json\n" + Answer(Platinum) + "\n```\nViel Spaß!", Ps5());

        Assert.True(read.Ok);
        Assert.Single(read.Trophies);
    }

    // ---- Merge --------------------------------------------------------------------------------------------------------------------------------

    private static readonly Guid GameId = Guid.NewGuid();

    private static GameTrophyEntity Old(string key, string name, bool ticked, int order, string? original = null) => new()
    {
        Id = Guid.NewGuid(), GameId = GameId, TrophyKey = key, Name = name, OriginalName = original, SortOrder = order,
        UnlockedAt = ticked ? new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) : null,
    };

    private static TrophyListItem New(string key, string name, string? original = null) =>
        new(key, name, original, "", TrophyTiers.Bronze, null, null, false, false, null, "", "");

    [Fact]
    public void The_same_key_continues_the_old_row_and_keeps_its_tick()
    {
        var old = new[] { Old("first-lap", "First Lap", ticked: true, 0) };

        var plan = TrophyMerge.Plan(old, [New("first-lap", "Erste Runde")]);

        var entry = Assert.Single(plan.Entries);
        Assert.Same(old[0], entry.Existing);
        Assert.Equal("first-lap", entry.Key);
        Assert.Equal(1, plan.KeptTicks);
        Assert.Empty(plan.Retained);
    }

    [Fact]
    public void A_new_key_with_the_same_name_takes_the_old_key()
    {
        var old = new[] { Old("lap-1", "First Lap", ticked: true, 0) };

        var plan = TrophyMerge.Plan(old, [New("first-lap", "First Lap")]);

        Assert.Equal("lap-1", Assert.Single(plan.Entries).Key);
        Assert.Equal(1, plan.KeptTicks);
    }

    [Fact]
    public void A_list_in_the_other_language_matches_by_the_official_English_name()
    {
        var oldGerman = new[] { Old("lap-1", "Erste Runde", ticked: true, 0, original: "First Lap") };
        var oldEnglish = new[] { Old("lap-1", "First Lap", ticked: true, 0) };

        var toEnglish = TrophyMerge.Plan(oldGerman, [New("first-lap", "First Lap")]);
        var toGerman = TrophyMerge.Plan(oldEnglish, [New("first-lap", "Erste Runde", "First Lap")]);
        var otherGerman = TrophyMerge.Plan(oldGerman, [New("x", "Ganz anders", "First Lap")]);

        Assert.Equal("lap-1", Assert.Single(toEnglish.Entries).Key);
        Assert.Equal("lap-1", Assert.Single(toGerman.Entries).Key);
        Assert.Equal("lap-1", Assert.Single(otherGerman.Entries).Key);
        Assert.All([toEnglish, toGerman, otherGerman], p => Assert.Equal(1, p.KeptTicks));
    }

    [Fact]
    public void Two_entries_with_the_same_name_are_never_merged_by_name()
    {
        var old = new[] { Old("a", "Collector", ticked: true, 0) };

        var plan = TrophyMerge.Plan(old, [New("c1", "Collector"), New("c2", "Collector")]);

        Assert.All(plan.Entries, e => Assert.Null(e.Existing));
        Assert.Same(old[0], Assert.Single(plan.Retained));
    }

    [Fact]
    public void Ticked_trophies_the_new_list_lacks_are_kept_unticked_ones_are_removed()
    {
        var ticked = Old("gone-ticked", "Gone Ticked", ticked: true, 0);
        var unticked = Old("gone", "Gone", ticked: false, 1);

        var plan = TrophyMerge.Plan([ticked, unticked], [New("new-one", "New One")]);

        Assert.Same(ticked, Assert.Single(plan.Retained));
        Assert.Same(unticked, Assert.Single(plan.Removed));
        Assert.Equal(1, plan.RetainedOldTicked);
        Assert.Equal(0, plan.KeptTicks);
    }

    [Fact]
    public void The_same_key_wins_over_a_name_and_every_key_of_the_merged_list_is_unique()
    {
        var star = Old("star", "Old Star", ticked: true, 0);
        var moon = Old("moon", "Moon", ticked: false, 1);
        var sun = Old("sun", "Sun", ticked: true, 2);

        // "star" keeps its row although renamed; "Moon" under a new id takes "moon"; "Sun" is gone but ticked (kept); "comet" is new
        var plan = TrophyMerge.Plan([star, moon, sun], [New("star", "Star Renamed"), New("moon-new", "Moon"), New("comet", "Comet")]);

        Assert.Equal(["star", "moon", "comet"], plan.Entries.Select(e => e.Key));
        Assert.Same(star, plan.Entries[0].Existing);
        Assert.Same(moon, plan.Entries[1].Existing);
        Assert.Null(plan.Entries[2].Existing);
        Assert.Same(sun, Assert.Single(plan.Retained));
        var keys = plan.Entries.Select(e => e.Key).Concat(plan.Retained.Select(r => r.TrophyKey)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ---- Check result and the manual answer -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_check_counts_grades_hidden_dlc_and_what_saving_would_do_with_the_ticks()
    {
        var read = TrophyList.Read(Answer(Platinum + "," + Gold + """, { "id": "dlc", "name": "DLC", "grade": "bronze", "group": "Rift" }"""), Ps5(strict: true));
        var existing = new[] { Old("dark-star", "Dark Star", ticked: true, 0), Old("removed", "Removed", ticked: false, 1) };

        var check = AiGuideGeneratorService.CheckResult(read, existing, GamePlatforms.PlayStation);

        Assert.True(check.Ok);
        Assert.Equal(3, check.Count);
        Assert.Equal(new Dictionary<string, int> { ["platinum"] = 1, ["gold"] = 1, ["silver"] = 0, ["bronze"] = 1 }, check.ByGrade);
        Assert.Equal((1, 1, 1, 1), (check.Hidden, check.Dlc, check.KeepsTicks, check.DropsTicks));
        Assert.Equal(3, check.Preview.Count);
    }

    [Fact]
    public void A_pasted_answer_decides_what_it_covers_and_never_overwrites_IGDB_facts()
    {
        var igdbGame = new GameEntity { Title = "Astral Run", Platform = GamePlatforms.PlayStation, PlatformVariant = GamePlatforms.Ps5, FactsSource = "igdb" };
        var aiGame = new GameEntity { Title = "Astral Run", Platform = GamePlatforms.PlayStation, PlatformVariant = GamePlatforms.Ps5 };
        var nintendo = new GameEntity { Title = "Meadow Quest", Platform = GamePlatforms.Nintendo, FactsSource = "igdb" };
        var withFacts = Answer(Platinum, """, "facts": { "developers": ["Nebula Works"] }""");

        var (igdbRead, igdbInclude) = AiGuideGeneratorService.ReadManualTrophyAnswer(igdbGame, withFacts);
        var (aiRead, aiInclude) = AiGuideGeneratorService.ReadManualTrophyAnswer(aiGame, withFacts);
        var (nintendoRead, _) = AiGuideGeneratorService.ReadManualTrophyAnswer(nintendo, """{ "game": "Meadow Quest", "facts": { "developers": ["Clover"] } }""");

        Assert.Equal(new TrophyResearchInclude(true, false), igdbInclude);
        Assert.Null(igdbRead.Facts);
        Assert.Contains(igdbRead.Warnings, w => w.Code == "factsFromIgdbKept");
        Assert.Equal(new TrophyResearchInclude(true, true), aiInclude);
        Assert.Equal(["Nebula Works"], aiRead.Facts!.Developers);
        Assert.False(nintendoRead.Ok);   // Nintendo with IGDB facts: nothing to save
    }

    [Fact]
    public void The_default_of_a_research_follows_platform_and_facts_source()
    {
        var ps = new GameEntity { Platform = GamePlatforms.PlayStation, PlatformVariant = GamePlatforms.Ps5, FactsSource = "igdb", Developer = "x" };
        var bare = new GameEntity { Platform = GamePlatforms.Epic };
        var nintendoDone = new GameEntity { Platform = GamePlatforms.Nintendo, FactsSource = "ai", Developer = "Clover" };

        Assert.Equal(new TrophyResearchInclude(true, false), AiGuideGeneratorService.DefaultInclude(ps, null));
        Assert.Equal(new TrophyResearchInclude(true, false), AiGuideGeneratorService.DefaultInclude(ps, new(null, true)));   // IGDB facts are never researched
        Assert.Equal(new TrophyResearchInclude(true, true), AiGuideGeneratorService.DefaultInclude(bare, null));
        Assert.False(AiGuideGeneratorService.DefaultInclude(nintendoDone, null).Any);
        Assert.Equal(new TrophyResearchInclude(false, true), AiGuideGeneratorService.DefaultInclude(nintendoDone, new(null, true)));
    }

    [Fact]
    public void Provider_sources_come_first_and_replace_the_no_sources_warning()
    {
        var read = TrophyList.Read("""{ "game": "Astral Run", "trophies": [ { "id": "a", "name": "A", "grade": "platinum" } ] }""", Ps5());
        var meta = new AiSourceMeta([new GuideSourceDto("PowerPyx", "https://www.powerpyx.com/astral-run-trophy-guide/", GuideSources.Cited)], []);

        var merged = AiGuideGeneratorService.WithProviderSources(read, meta);

        Assert.Equal("https://www.powerpyx.com/astral-run-trophy-guide/", Assert.Single(merged.Sources).Url);
        Assert.DoesNotContain(merged.Warnings, w => w.Code == "noSources");
        Assert.Contains(read.Warnings, w => w.Code == "noSources");
    }

    // ---- Context ------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_manual_context_never_builds_the_hidden_Steam_achievement_chapter()
    {
        var game = ManualPromptFixtures.Ps5(Lang.German);
        var context = ManualPromptFixtures.Context(game, ManualPromptFixtures.Ps5Trophies(game.Id), Lang.German);
        var steam = new AiGuideGeneratorService.GameContext("Suncraft", "", [("ACH", "A", "B")], [], ResearchBlocks.Selection.Empty, "", Lang.German);

        Assert.NotEmpty(context.Achievements);                          // the researched list is in the context ...
        Assert.False(AiGuideGeneratorService.BuildsSteamAchievementTab(context));   // ... but never becomes a hidden Steam tab
        Assert.True(AiGuideGeneratorService.BuildsSteamAchievementTab(steam));
        Assert.Equal(["astral-champion", "first-lap", "dark-star"], context.Achievements.Select(a => a.ApiName));
        Assert.StartsWith("[Platin] ", context.Achievements[0].Description);
        Assert.Contains("rpg", context.TagProfiles);
        Assert.Contains("racing", context.TagProfiles);
    }
}
