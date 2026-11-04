using Companion.Api.Endpoints;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// Achievements for completing a numbered story chapter are always Bronze (owner's rule 2026-11-04), whatever their share; the rarity
/// band and the share itself stay as they are. The matching is narrow: a completion verb next to "Kapitel/Chapter" and a number.
/// </summary>
public sealed class ChapterCompletionTests
{
    [Theory]
    // The real Final Fantasy Resonance texts (Steam, German)
    [InlineData("Schließe Kapitel 5 ab.")]
    [InlineData("Schließe Kapitel 4 ab.")]
    // German
    [InlineData("Kapitel 1 abgeschlossen")]
    [InlineData("Kapitel 12 erfolgreich abgeschlossen!")]
    [InlineData("„Kapitel 3“ abgeschlossen")]
    [InlineData("Schließe das Kapitel 7 erfolgreich ab")]
    [InlineData("schließe kapitel iv ab")]
    [InlineData("Schliesse Kapitel 2 ab")]
    [InlineData("Beende Kapitel 3")]
    [InlineData("Kapitel XX beendet")]
    [InlineData("Kapitel 9 abschließen")]
    [InlineData("Erreiche das Ende von Kapitel 6.")]
    // English
    [InlineData("Complete Chapter 3")]
    [InlineData("Complete Chapter 5.")]
    [InlineData("Completed chapter IV")]
    [InlineData("Finish the Chapter 2!")]
    [InlineData("Finished Chapter 11")]
    [InlineData("Clear \"Chapter 8\"")]
    [InlineData("Cleared chapter 1")]
    [InlineData("Beat Chapter 4")]
    [InlineData("Chapter 3 completed")]
    [InlineData("Chapter 2 is cleared")]
    [InlineData("CHAPTER VII COMPLETE")]
    public void Completing_a_numbered_chapter_is_recognised(string text)
    {
        Assert.True(ChapterCompletion.IsChapterCompletion(text));
        Assert.True(ChapterCompletion.IsChapterCompletion("Something else", text));
    }

    [Theory]
    [InlineData("Schließe alle Kapitel im Schwierigkeitsgrad SCHWER (Hard Mode) auf 100% ab.")]
    [InlineData("Complete all chapters on Hard")]
    [InlineData("Schließe alle Kapitel ab")]
    [InlineData("Kapitelauswahl")]
    [InlineData("Chapter select")]
    [InlineData("Lies 5 Kapitel des Buches")]
    [InlineData("Lies Kapitel 5 des Buches")]
    [InlineData("Read 5 chapters of the book")]
    [InlineData("Schließe 5 Aufträge in Kapitel 3 ab")]
    [InlineData("Complete 5 quests in Chapter 3")]
    [InlineData("Sammle alle Gegenstände aus Kapitel 3")]
    [InlineData("Find every collectible in Chapter 3")]
    [InlineData("Kapitel 3")]
    [InlineData("Chapter 3")]
    [InlineData("Kapitel in der Mitte abgeschlossen")]
    [InlineData("Kapitel XXI abgeschlossen")]
    [InlineData("Chapters 1 to 3 completed")]
    [InlineData("Meister des Schicksals (Master of Fate)")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_not_a_chapter_completion(string? text)
    {
        Assert.False(ChapterCompletion.IsChapterCompletion(text));
    }

    [Fact]
    public void Texts_are_checked_one_by_one_never_glued_together()
    {
        // "Complete" ends one text and "Chapter 3" starts the next: no chapter completion
        Assert.False(ChapterCompletion.IsChapterCompletion("Complete", "Chapter 3"));
    }

    // ---- the tier -------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2.0, "Gold")]
    [InlineData(10.0, "Gold")]
    [InlineData(30.0, "Silver")]
    [InlineData(80.0, "Bronze")]
    public void Without_a_chapter_text_the_tier_still_comes_from_the_share(double pct, string expected)
    {
        Assert.Equal(expected, SteamLiveTrackerService.CalculateRarityTier(pct));
        Assert.Equal(expected, SteamLiveTrackerService.CalculateRarityTier(pct, "Meister des Schicksals", "Schließe alle Kapitel im Schwierigkeitsgrad SCHWER ab"));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(12.0)]
    [InlineData(45.0)]
    [InlineData(90.0)]
    public void A_chapter_completion_is_bronze_whatever_the_share_and_whichever_text_says_so(double pct)
    {
        Assert.Equal("Bronze", SteamLiveTrackerService.CalculateRarityTier(pct, "Auf der Suche nach der Wahrheit", "Schließe Kapitel 5 ab."));
        Assert.Equal("Bronze", SteamLiveTrackerService.CalculateRarityTier(pct, "Kapitel 1 abgeschlossen", null));
        Assert.Equal("Bronze", SteamLiveTrackerService.CalculateRarityTier(pct, "Viva la Revolución", "", englishName: "Complete Chapter 4"));
        Assert.Equal("Bronze", SteamLiveTrackerService.CalculateRarityTier(pct, "x", "y", null, originalDescription: "Complete Chapter 4."));
    }

    // ---- through the list build -------------------------------------------------------------------------------------------------

    private static SteamLocalStatsService.LocalAchievementStat Local(string api, string name, string description, string english = "") =>
        new() { ApiName = api, DisplayName = name, Description = description, EnglishName = english, IsLocalized = true };

    [Fact]
    public void The_local_list_build_makes_a_rare_chapter_achievement_bronze_but_keeps_its_band_and_share()
    {
        var local = new[]
        {
            Local("ACH_TRUTH", "Auf der Suche nach der Wahrheit", "Schließe Kapitel 5 ab."),
            Local("ACH_REVOLUTION", "Viva la Revolución", "Schließe Kapitel 4 ab."),
            Local("ACH_HARD", "Meister des Schicksals", "Schließe alle Kapitel im Schwierigkeitsgrad SCHWER ab."),
        };
        var shares = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["ACH_TRUTH"] = 3.2, ["ACH_REVOLUTION"] = 12.5, ["ACH_HARD"] = 3.0 };

        var list = SteamLiveTrackerService.MergeLocalStats(null, local, shares, [], "de");

        var truth = list.Single(a => a.ApiName == "ACH_TRUTH");
        Assert.Equal("Bronze", truth.RarityTier);
        Assert.Equal("ultraRare", truth.RarityBand);
        Assert.Equal("Extrem selten", truth.RarityLabel);
        Assert.Equal(3.2, truth.GlobalPercent);
        Assert.True(truth.HasGlobalPercent);

        var revolution = list.Single(a => a.ApiName == "ACH_REVOLUTION");
        Assert.Equal("Bronze", revolution.RarityTier);
        Assert.Equal("rare", revolution.RarityBand);

        // Not a numbered chapter: still decided by the share
        Assert.Equal("Gold", list.Single(a => a.ApiName == "ACH_HARD").RarityTier);
    }

    [Fact]
    public void The_cached_remote_list_and_the_final_pass_apply_the_rule_too()
    {
        LiveAchievementItemDto Item(string api, string name, string description) =>
            new(api, name, description, null, false, "", "", false, null, 25.0, "Silver", "", false, "") { HasGlobalPercent = false };
        var cached = new[] { Item("A", "Kapitel 1 abgeschlossen", ""), Item("B", "Boss", "Besiege den Boss") };
        var shares = new Dictionary<string, double> { ["A"] = 2.0, ["B"] = 2.0 };

        var applied = SteamLiveTrackerService.ApplyPercentages(cached, shares, "de");
        Assert.Equal("Bronze", applied[0].RarityTier);
        Assert.Equal("ultraRare", applied[0].RarityBand);
        Assert.Equal("Gold", applied[1].RarityTier);

        // A description that only arrived later (local file, translation) or an English name from the local stats still counts
        var late = new List<LiveAchievementItemDto> { Item("C", "Wahrheit", "Schließe Kapitel 5 ab."), Item("D", "Revolution", ""), Item("E", "Boss", "Besiege den Boss") with { RarityTier = "Gold" } };
        var result = SteamLiveTrackerService.ApplyChapterRule(late, [Local("D", "Revolution", "", "Complete Chapter 4")]);
        Assert.Equal("Bronze", result[0].RarityTier);
        Assert.Equal("Bronze", result[1].RarityTier);
        Assert.Equal("Gold", result[2].RarityTier);
    }
}
