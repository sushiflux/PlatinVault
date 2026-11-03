using System.Text.Json;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The chapter groups of the side navigation: every standard topic has a group, the labels exist in both languages, and the title
/// classifier puts the real chapters of the existing guides (German, English and bilingual titles) where a player expects them.
/// </summary>
public sealed class GuideGroupsTests
{
    private static readonly string[] Languages = [Lang.German, Lang.English];

    [Fact]
    public void Every_standard_topic_belongs_to_exactly_one_known_group()
    {
        foreach (var topic in AiGuideGeneratorService.Topics(Lang.German))
        {
            var group = GuideGroups.ForTopic(topic.Id);
            Assert.True(group is not null, $"Topic '{topic.Id}' has no group in GuideGroups.TopicGroups.");
            Assert.True(GuideGroups.IsValid(group), $"Topic '{topic.Id}' maps to the unknown group '{group}'.");
        }
    }

    [Fact]
    public void The_topic_table_has_no_entry_for_a_topic_that_does_not_exist()
    {
        var ids = AiGuideGeneratorService.Topics(Lang.German).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = GuideGroups.StandardTopicGroups.Keys.Where(id => !ids.Contains(id)).ToList();
        Assert.True(stale.Count == 0, $"GuideGroups.TopicGroups lists topics that are gone: {string.Join(", ", stale)}");
    }

    [Fact]
    public void Every_group_is_used_by_a_standard_topic_or_by_the_title_rules()
    {
        // A group nobody can land in would be an empty heading in the navigation for ever
        var used = GuideGroups.StandardTopicGroups.Values.ToHashSet();
        used.Add(GuideGroups.Classify("Bekannte Bugs & Workarounds"));
        Assert.All(GuideGroups.Keys, key => Assert.Contains(key, used));
    }

    [Fact]
    public void A_game_specific_or_unknown_topic_has_no_group_of_its_own()
    {
        Assert.Null(GuideGroups.ForTopic("extra"));
        Assert.Null(GuideGroups.ForTopic("does-not-exist"));
        Assert.Null(GuideGroups.ForTopic(null));
        Assert.Equal(GuideGroups.Combat, GuideGroups.ForTopic("WEAPONS"));
    }

    [Fact]
    public void Every_group_has_a_label_in_both_languages_and_the_labels_differ_between_groups()
    {
        foreach (var language in Languages)
        {
            var labels = GuideGroups.Keys.Select(key => GuideGroups.Label(key, language)).ToList();
            Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
            Assert.Equal(labels.Count, labels.Distinct().Count());
        }
        Assert.Equal("Kampf & Taktik", GuideGroups.Label(GuideGroups.Combat, Lang.German));
        Assert.Equal("Fortschritt & Freischaltungen", GuideGroups.Label(GuideGroups.Progress, Lang.German));
        Assert.Equal("Combat & tactics", GuideGroups.Label(GuideGroups.Combat, Lang.English));
        Assert.Equal("Progression & unlocks", GuideGroups.Label(GuideGroups.Progress, Lang.English));
        // An unknown language falls back to German like everywhere else on the server
        Assert.Equal(GuideGroups.Label(GuideGroups.Start, Lang.German), GuideGroups.Label(GuideGroups.Start, "fr"));
    }

    [Fact]
    public void An_import_keeps_a_valid_group_key_and_drops_an_unknown_one()
    {
        const string json = """
        {
          "format": "platinvault-guide", "formatVersion": 1,
          "game": { "title": "Test" },
          "tabs": [
            { "title": "Waffen", "groupKey": "Story" },
            { "title": "Rüstung", "groupKey": "nonsense" },
            { "title": "Bosse" }
          ]
        }
        """;
        var tabs = GuideImport.Parse(JsonDocument.Parse(json).RootElement).Tabs;
        Assert.Equal([GuideGroups.Story, null, null], tabs.Select(t => t.GroupKey));
    }

    // ---- The real chapters of the existing guides -------------------------------------------------------------------------

    [Theory]
    // final-fantasy-resonance
    [InlineData("1. Roadmap & Early-OP", GuideGroups.Start)]
    [InlineData("2. Alle 26 Visionen & Builds", GuideGroups.Combat)]
    [InlineData("3. XP, Gil & MRP Farming", GuideGroups.Progress)]
    [InlineData("4. Beste Waffen & Rüstung", GuideGroups.Combat)]
    [InlineData("5. Espers, Cave Masters & Bosse", GuideGroups.Combat)]
    [InlineData("6. Alle 40 Steam-Achievements", GuideGroups.Extras)]
    [InlineData("7. Bestiarium & Schwächen", GuideGroups.Combat)]
    // final-fantasy-vii-rebirth
    [InlineData("1. Roadmap, Early-OP & Queen's Blood", GuideGroups.Start)]
    [InlineData("2. Alle 7 Regionen & Protosubstanz", GuideGroups.Story)]
    // final-fantasy-vii-remake-intergrade
    [InlineData("2. Chadley, Quests & 9 Kleider", GuideGroups.Explore)]
    [InlineData("3. XP, AP, Gil & OP-Items", GuideGroups.Progress)]
    [InlineData("4. Alle 28 Waffen & 31 Discs", GuideGroups.Combat)]
    [InlineData("5. Esper & Superbosse", GuideGroups.Combat)]
    [InlineData("6. Alle 63 Achievements", GuideGroups.Extras)]
    // suncraft (bilingual titles "Deutsch (English)")
    [InlineData("Fahrplan & Verpassbares (Roadmap & Missables)", GuideGroups.Start)]
    [InlineData("Tipps & Tricks (Tips & Tricks)", GuideGroups.Start)]
    [InlineData("Früh stark werden & Power-Spikes (Early Power Spikes)", GuideGroups.Start)]
    [InlineData("Komplettlösung (Walkthrough)", GuideGroups.Start)]
    [InlineData("Gebiete & Karte (Regions & Map)", GuideGroups.Story)]
    [InlineData("Beste Werkzeuge & Waffen (Best Tools & Weapons)", GuideGroups.Combat)]
    [InlineData("Beste Rüstung & Accessoires (Best Armor & Accessories)", GuideGroups.Combat)]
    [InlineData("Builds & Skill-Reihenfolge (Builds & Skill Order)", GuideGroups.Combat)]
    [InlineData("Fähigkeiten & Upgrade-Reihenfolge (Skills & Upgrade Order)", GuideGroups.Combat)]
    [InlineData("Gegner, Bosse & Bestiarium (Enemies, Bosses & Bestiary)", GuideGroups.Combat)]
    [InlineData("Sammelobjekte & Fundorte (Collectibles & Locations)", GuideGroups.Explore)]
    [InlineData("Nebenquests & Aktivitäten (Side Quests & Activities)", GuideGroups.Explore)]
    [InlineData("Begleiter, Fraktionen & Beziehungen (Companions & Relationships)", GuideGroups.Characters)]
    [InlineData("Crafting, Ressourcen & Upgrades (Crafting, Resources & Upgrades)", GuideGroups.Economy)]
    [InlineData("Dorfausbau & Wiederaufbau (Village Rebuilding)", GuideGroups.Economy)]
    [InlineData("Enden & Entscheidungen", GuideGroups.Story)]
    [InlineData("Geld & Wirtschaft (Money & Economy)", GuideGroups.Economy)]
    [InlineData("Produktion, Abläufe & Effizienz (Production & Automation)", GuideGroups.Economy)]
    [InlineData("Forschung, Upgrades & Freischaltungen (Research & Unlocks)", GuideGroups.Economy)]
    [InlineData("Steuerung & Tricks (Controls & Tricks)", GuideGroups.Start)]
    [InlineData("Bekannte Bugs & Workarounds (Known Bugs & Workarounds)", GuideGroups.Extras)]
    [InlineData("Rätsel & Geheimnisse (Riddles & Secrets)", GuideGroups.Explore)]
    [InlineData("Geister, Ember & Automatisierung (Ghosts & Automation)", GuideGroups.Economy)]
    [InlineData("Kochen, Angeln & Bankett (Cooking, Fishing & Banquet)", GuideGroups.Economy)]
    public void The_real_chapter_titles_land_in_the_expected_group(string title, string expected) =>
        Assert.Equal(expected, GuideGroups.Classify(title));

    [Theory]
    // Plain English titles (a guide written in English)
    [InlineData("Roadmap & Missables", GuideGroups.Start)]
    [InlineData("Best Weapons & Armor", GuideGroups.Combat)]
    [InlineData("Enemies, Bosses & Bestiary", GuideGroups.Combat)]
    [InlineData("Regions & Map", GuideGroups.Story)]
    [InlineData("Endings & Decisions", GuideGroups.Story)]
    [InlineData("Companions & Relationships", GuideGroups.Characters)]
    [InlineData("Crafting & Resources", GuideGroups.Economy)]
    [InlineData("Collectibles & Locations", GuideGroups.Explore)]
    [InlineData("Side Quests & Activities", GuideGroups.Explore)]
    [InlineData("Known Bugs & Workarounds", GuideGroups.Extras)]
    [InlineData("Online & Co-op", GuideGroups.Extras)]
    public void English_titles_are_classified_too(string title, string expected) =>
        Assert.Equal(expected, GuideGroups.Classify(title, Lang.English));

    [Theory]
    // Word boundaries: "Karte" is a place, "Karten" are cards; "map" is not inside "Roadmap"
    [InlineData("Karte", GuideGroups.Story)]
    [InlineData("Roadmap", GuideGroups.Start)]
    [InlineData("3.  Karte & Gebiete", GuideGroups.Story)]
    [InlineData("12) Waffen", GuideGroups.Combat)]
    public void Word_boundaries_and_chapter_numbers_are_handled(string title, string expected) =>
        Assert.Equal(expected, GuideGroups.Classify(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Völlig unbekanntes Thema")]
    public void A_title_no_rule_recognises_falls_back_to_extras(string? title) =>
        Assert.Equal(GuideGroups.Extras, GuideGroups.Classify(title));

    [Fact]
    public void Classify_does_not_depend_on_the_language_argument()
    {
        foreach (var title in new[] { "Waffen & Rüstung", "Regions & Map", "Fahrplan (Roadmap)" })
        {
            Assert.Equal(GuideGroups.Classify(title), GuideGroups.Classify(title, Lang.English));
            Assert.Equal(GuideGroups.Classify(title), GuideGroups.Classify(title, Lang.German));
        }
    }

    // ---- Stored key, topic and title together -----------------------------------------------------------------------------

    [Fact]
    public void An_explicit_valid_key_wins_over_the_title_and_an_invalid_one_is_ignored()
    {
        Assert.Equal(GuideGroups.Story, GuideGroups.Resolve(GuideGroups.Story, "Waffen & Rüstung"));
        Assert.Equal(GuideGroups.Combat, GuideGroups.Resolve(null, "Waffen & Rüstung"));
        Assert.Equal(GuideGroups.Combat, GuideGroups.Resolve("", "Waffen & Rüstung"));
        Assert.Equal(GuideGroups.Combat, GuideGroups.Resolve("no-such-group", "Waffen & Rüstung"));
    }

    [Fact]
    public void KeyFor_prefers_the_topic_and_falls_back_to_the_title()
    {
        // The topic decides even when the (game-specific) title would point elsewhere
        Assert.Equal(GuideGroups.Economy, GuideGroups.KeyFor("crafting", "Waffen schmieden"));
        // "extra" and unknown ids have no group: the title decides
        Assert.Equal(GuideGroups.Combat, GuideGroups.KeyFor("extra", "Bosse & Gegner"));
        Assert.Equal(GuideGroups.Explore, GuideGroups.KeyFor(null, "Rätsel & Geheimnisse"));
    }

    [Fact]
    public void The_titles_of_the_standard_topics_land_where_the_topic_table_puts_them()
    {
        // A consistency net: the keyword rules and the topic table are two ways to the same answer, so a new or renamed topic title
        // (or a new keyword) that makes them disagree means one of them drifted. A deliberate difference goes into the set below.
        // "abilities" (metroidvania: abilities that open areas) is progression by topic, but a title with "Fähigkeiten" alone reads as combat skills
        var allowedDifferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "abilities" };
        var different = new List<string>();
        foreach (var language in Languages)
        {
            foreach (var topic in AiGuideGeneratorService.Topics(language))
            {
                var byTitle = GuideGroups.Classify(topic.Title, language);
                var byTopic = GuideGroups.ForTopic(topic.Id);
                if (byTitle != byTopic && !allowedDifferences.Contains(topic.Id))
                {
                    different.Add($"{language}:{topic.Id} \"{topic.Title}\" title={byTitle} topic={byTopic}");
                }
            }
        }
        Assert.True(different.Count == 0, "These standard topics are classified differently by title than by the topic table:\n" + string.Join('\n', different));
    }

    // ---- Progression is not combat (Little Big Workshop, 2026-11-02) --------------------------------------------------------

    [Theory]
    [InlineData("leveling")]
    [InlineData("metaprogression")]
    [InlineData("abilities")]
    [InlineData("cars")]
    public void Progression_topics_have_their_own_group(string topicId) =>
        Assert.Equal(GuideGroups.Progress, GuideGroups.ForTopic(topicId));

    [Theory]
    [InlineData("weapons")]
    [InlineData("armor")]
    [InlineData("builds")]
    [InlineData("bestiary")]
    [InlineData("tactics")]
    [InlineData("combos")]
    public void Combat_topics_stay_combat(string topicId) =>
        Assert.Equal(GuideGroups.Combat, GuideGroups.ForTopic(topicId));

    [Theory]
    // The progression chapter of a factory sim (no combat at all): it showed up under "Kampf & Fortschritt"
    [InlineData("Fabrikstufe & Erfahrung", GuideGroups.Progress)]
    [InlineData("Level-Empfehlungen, Leveln & Farming", GuideGroups.Progress)]
    [InlineData("Meilensteine & Fabrikalter", GuideGroups.Progress)]
    [InlineData("Ranks & experience", GuideGroups.Progress)]
    [InlineData("Unlocks & meta progression", GuideGroups.Progress)]
    // Combat words still win where they are there
    [InlineData("Waffen-Upgrades & Builds", GuideGroups.Combat)]
    [InlineData("Gegner, Bosse & Bestiarium", GuideGroups.Combat)]
    public void Progression_titles_are_told_apart_from_combat_titles(string title, string expected) =>
        Assert.Equal(expected, GuideGroups.Classify(title));

    [Fact]
    public void A_stored_combat_key_from_before_the_split_moves_a_progression_chapter_and_keeps_a_combat_chapter()
    {
        // Little Big Workshop's "Fabrikstufe & Erfahrung" was stored with "combat" (the old "Kampf & Fortschritt")
        Assert.Equal(GuideGroups.Progress, GuideGroups.Resolve(GuideGroups.Combat, "Fabrikstufe & Erfahrung"));
        Assert.Equal(GuideGroups.Combat, GuideGroups.Resolve(GuideGroups.Combat, "Gegner, Bosse & Bestiarium"));
        // A combat chapter whose title says nothing stays where it was stored
        Assert.Equal(GuideGroups.Combat, GuideGroups.Resolve(GuideGroups.Combat, "Völlig unbekanntes Thema"));
        // Every other stored key is untouched, also when the title reads as progression
        Assert.Equal(GuideGroups.Economy, GuideGroups.Resolve(GuideGroups.Economy, "Fabrikstufe & Erfahrung"));
        Assert.Equal(GuideGroups.Progress, GuideGroups.Resolve(GuideGroups.Progress, "Waffen"));
        Assert.True(GuideGroups.IsValid(GuideGroups.Progress));
    }
}
