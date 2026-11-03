using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The size of a guide (budget by research scope, topics only with evidence, merged/priority) and the language of chapter titles.
/// </summary>
public sealed class ChapterPlanningTests
{
    private static readonly IReadOnlySet<string> NoTags = new HashSet<string>();

    private static JsonElement Blueprint(Action<JsonObject> configure)
    {
        var root = new JsonObject { ["profiles"] = new JsonArray("management") };
        configure(root);
        return JsonDocument.Parse(root.ToJsonString()).RootElement;
    }

    private static JsonObject Topic(string id, bool applicable, int? priority = null, string? title = null)
    {
        var topic = new JsonObject { ["id"] = id, ["applicable"] = applicable };
        if (priority is { } p) topic["priority"] = p;
        if (title is not null) topic["title"] = title;
        return topic;
    }

    private static List<string> KeptIds(JsonElement root, string? scope, int existing = 0, int extras = 0) =>
        AiGuideGeneratorService.PickTopics(root, NoTags, Lang.German, scope, existing, extras).Where(p => p.Kept).Select(p => p.Topic.Id).ToList();

    [Theory]
    [InlineData(null, 8, 14)]
    [InlineData("detailed", 8, 14)]
    [InlineData("compact", 5, 8)]
    [InlineData("exhaustive", 12, 24)]
    [InlineData("something-unknown", 8, 14)]
    public void The_scope_sets_the_size_and_detailed_is_the_default(string? scope, int min, int max)
    {
        Assert.Equal((min, max), ChapterBudget.For(scope));
    }

    [Fact]
    public void The_room_is_the_budget_minus_the_chapters_the_guide_already_has()
    {
        Assert.Equal(14, ChapterBudget.Room(null, 0));
        Assert.Equal(8, ChapterBudget.Room("detailed", 6));
        Assert.Equal(0, ChapterBudget.Room("compact", 12));
    }

    [Fact]
    public void A_big_answer_is_cut_to_the_budget_most_important_first_and_the_extra_chapters_take_their_slots_first()
    {
        // The walkthrough takes no slot (see PuzzleAndWalkthroughTests), so it is left out here; "research" stays unmentioned (typical for the profile)
        var ids = AiGuideGeneratorService.Topics(Lang.German).Select(t => t.Id).Where(id => id is not ("walkthrough" or "research")).Take(20).ToList();
        // every topic "applicable", priority 3 for the first half and 1 for the second half: the second half must win
        var root = Blueprint(r => r["topics"] = new JsonArray(ids.Select((id, i) => (JsonNode)Topic(id, true, i < 10 ? 3 : 1)).ToArray()));

        var kept = KeptIds(root, null, existing: 0, extras: 3);

        // 14 minus 3 extra chapters; the ten priority-1 topics all made it. The last slot goes to the priority-3 topic that is typical (the roadmap,
        // first in the list) - not to "research": typical for the profile, but the answer said nothing about it, so it is no candidate at all
        Assert.Equal(14 - 3, kept.Count);
        Assert.All(ids.Skip(10), id => Assert.Contains(id, kept));
        Assert.Contains("roadmap", kept);
        Assert.DoesNotContain("tips", kept);
        Assert.DoesNotContain("research", kept);
    }

    [Fact]
    public void A_compact_guide_of_a_small_game_stays_small_and_a_topic_the_game_does_not_have_is_not_forced_by_the_genre()
    {
        // "management" is typical with economy/production/research: the AI says the game has none of them
        var root = Blueprint(r => r["topics"] = new JsonArray(
            Topic("economy", false), Topic("production", false), Topic("research", false), Topic("roadmap", true, 1), Topic("tips", true, 2), Topic("crafting", true, 1)));

        var kept = KeptIds(root, "compact");

        Assert.DoesNotContain("economy", kept);
        Assert.DoesNotContain("production", kept);
        Assert.DoesNotContain("research", kept);
        Assert.Equal(["roadmap", "tips", "crafting"], kept.OrderBy(k => k == "roadmap" ? 0 : k == "tips" ? 1 : 2));
    }

    [Fact]
    public void Only_roadmap_and_tips_have_a_safety_net_a_genre_typical_topic_the_answer_says_nothing_about_is_not_added()
    {
        var root = Blueprint(r => r["topics"] = new JsonArray(Topic("crafting", true, 1)));

        var kept = KeptIds(root, null);

        // roadmap and tips fit every game: nothing said = kept. economy/production/research are typical for "management", but without the
        // research confirming them they are no chapter (the old safety net put a bestiary into a factory sim this way)
        Assert.Contains("roadmap", kept);
        Assert.Contains("tips", kept);
        Assert.Contains("crafting", kept);
        Assert.DoesNotContain("economy", kept);
        Assert.DoesNotContain("production", kept);
        Assert.DoesNotContain("research", kept);
        Assert.Equal(3, kept.Count);
    }

    [Fact]
    public void A_factory_sim_whose_tags_look_like_survival_gets_no_bestiary()
    {
        // Little Big Workshop (AppID 574720): top Steam tags include "Crafting" and "Base Building" but not "Survival"
        string[] tags = ["Simulation", "Strategy", "Management", "Building", "Resource Management", "Economy", "Automation", "Sandbox", "Singleplayer", "Indie", "Crafting", "3D", "Cute", "Funny", "Base Building"];
        var profiles = AiGuideGeneratorService.DetectProfilesFromTags(tags);
        Assert.Contains("management", profiles);
        Assert.DoesNotContain("survival", profiles);

        // Even with a survival profile, a bestiary the answer does not confirm is no chapter
        var root = JsonDocument.Parse(new JsonObject
        {
            ["profiles"] = new JsonArray("management", "survival"),
            ["topics"] = new JsonArray(Topic("economy", true, 1), Topic("production", true, 1), Topic("research", true, 1), Topic("roadmap", true, 1), Topic("tips", true, 2))
        }.ToJsonString()).RootElement;
        var kept = AiGuideGeneratorService.PickTopics(root, profiles, Lang.German, null, 0, 0).Where(p => p.Kept).Select(p => p.Topic.Id).ToList();
        Assert.DoesNotContain("bestiary", kept);
        Assert.DoesNotContain("crafting", kept);
        Assert.DoesNotContain("basebuilding", kept);
        // There is no minimum: five confirmed topics are five chapters, the "detailed" range of 8 to 14 is not filled up
        Assert.Equal(5, kept.Count);
    }

    [Fact]
    public void A_real_survival_game_is_still_recognised_by_its_tags()
    {
        Assert.Contains("survival", AiGuideGeneratorService.DetectProfilesFromTags(["Survival", "Crafting", "Open World"]));
        Assert.Contains("survival", AiGuideGeneratorService.DetectProfilesFromTags(["Survival", "Base Building"]));
        Assert.Contains("survival", AiGuideGeneratorService.DetectProfilesFromTags(["Open World Survival Craft"]));
        Assert.DoesNotContain("survival", AiGuideGeneratorService.DetectProfilesFromTags(["Base Building", "City Builder"]));
    }

    [Theory]
    [InlineData(Lang.German, "KEINE Mindestzahl")]
    [InlineData(Lang.English, "NO minimum")]
    public void The_budget_note_is_a_ceiling_without_a_minimum(string language, string phrase)
    {
        var note = ChapterBudget.PromptNote(null, language, 0);
        Assert.Contains(phrase, note);
        Assert.DoesNotContain(language == Lang.German ? "insgesamt 8 bis 14" : "8 to 14 chapters in total", note);
    }
    [Fact]
    public void Chapters_the_guide_already_has_use_up_room_and_covered_topics_are_never_planned_again()
    {
        var root = Blueprint(r => r["topics"] = new JsonArray(
            new JsonObject { ["id"] = "roadmap", ["applicable"] = true, ["covered"] = true },
            Topic("tips", false), Topic("economy", false), Topic("production", false), Topic("research", false),
            Topic("weapons", true, 1), Topic("armor", true, 2), Topic("builds", true, 3)));

        var kept = KeptIds(root, "compact", existing: 6);

        Assert.DoesNotContain("roadmap", kept);
        // compact = at most 8, six exist: room for two; weapons (1) and armor (2) win over builds (3)
        Assert.Contains("weapons", kept);
        Assert.Contains("armor", kept);
        Assert.DoesNotContain("builds", kept);
    }

    [Theory]
    [InlineData("Fahrplan & Verpassbares (Roadmap & Missables)", "Fahrplan & Verpassbares")]
    [InlineData("Tipps & Tricks (Tips & Tricks)", "Tipps & Tricks")]
    [InlineData("Gegner, Bosse & Bestiarium (Enemies, Bosses & Bestiary)", "Gegner, Bosse & Bestiarium")]
    [InlineData("Beste Waffen / Best Weapons", "Beste Waffen")]
    [InlineData("Geister (Ember)", "Geister (Ember)")]
    [InlineData("Speedrun (Any%)", "Speedrun (Any%)")]
    [InlineData("Dorfausbau", "Dorfausbau")]
    [InlineData("  Kochen, Angeln & Bankett  ", "Kochen, Angeln & Bankett")]
    public void A_translation_in_brackets_is_removed_from_a_German_title_and_nothing_else(string title, string expected)
    {
        Assert.Equal(expected, ChapterTitles.Clean(title, Lang.German));
    }

    [Theory]
    [InlineData("Früh stark werden & Power-Spikes (Early Power Spikes)", "Früh stark werden & Power-Spikes")]
    [InlineData("Builds & Skill-Reihenfolge (Builds & Skill Order)", "Builds & Skill-Reihenfolge")]
    [InlineData("Fähigkeiten & Upgrade-Reihenfolge (Skills & Upgrade Order)", "Fähigkeiten & Upgrade-Reihenfolge")]
    [InlineData("Crafting, Ressourcen & Upgrades (Crafting, Resources & Upgrades)", "Crafting, Ressourcen & Upgrades")]
    [InlineData("Bekannte Bugs & Workarounds (Known Bugs & Workarounds)", "Bekannte Bugs & Workarounds")]
    [InlineData("Dorfausbau & Wiederaufbau (Village Rebuilding)", "Dorfausbau & Wiederaufbau")]
    [InlineData("Rätsel & Geheimnisse (Riddles & Secrets)", "Rätsel & Geheimnisse")]
    [InlineData("Geister, Ember & Automatisierung (Ghosts & Automation)", "Geister, Ember & Automatisierung")]
    [InlineData("Kochen, Angeln & Bankett (Cooking, Fishing & Banquet)", "Kochen, Angeln & Bankett")]
    [InlineData("Beste Werkzeuge & Waffen (Best Tools & Weapons)", "Beste Werkzeuge & Waffen")]
    [InlineData("Steuerung & Tricks (Controls & Tricks)", "Steuerung & Tricks")]
    public void Real_bilingual_titles_of_a_German_guide_lose_their_English_translation(string title, string expected)
    {
        Assert.Equal(expected, ChapterTitles.Clean(title, Lang.German));
    }

    [Theory]
    [InlineData("Best weapons (Beste Waffen)", "Best weapons")]
    [InlineData("Tips & tricks", "Tips & tricks")]
    [InlineData("Village (Dorf)", "Village")]
    public void The_same_for_an_English_guide(string title, string expected)
    {
        Assert.Equal(expected, ChapterTitles.Clean(title, Lang.English));
    }
}
