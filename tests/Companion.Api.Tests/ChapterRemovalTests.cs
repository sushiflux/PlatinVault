using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>"Kapitel entfernen" for every chapter: which tabs may go and which tick marks go with a chapter.</summary>
public sealed class ChapterRemovalTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private static string Stored(params GuideSectionDto[] sections) => JsonSerializer.Serialize(sections, Json);

    private static GuideTabEntity Tab(string slug, params GuideSectionDto[] sections) =>
        new() { Id = Guid.NewGuid(), GameId = Guid.NewGuid(), Slug = slug, Title = slug, Icon = "mdi:star", SortOrder = 1, SectionsJson = Stored(sections) };

    private static GuideSectionDto Checklist(string tableId, params string[] itemIds) =>
        new("checklist_table", Table: new GuideTableDto(
            TableId: tableId,
            Caption: tableId,
            Checkable: true,
            SearchPlaceholder: null,
            CategoryFilterLabel: null,
            Categories: null,
            SecondaryFilterLabel: null,
            SecondaryCategories: null,
            Columns: [new GuideTableColumnDto("name", "Name", IsRowHeader: true)],
            Rows: itemIds.Select(id => new GuideTableRowDto(id, null, null, new Dictionary<string, string> { ["name"] = id })).ToList()));

    [Fact]
    public void A_normal_chapter_with_content_may_be_removed()
    {
        Assert.Equal(ChapterRemovalVerdict.Removable, ChapterRemoval.Check(Tab("weapons", Checklist("weapons", "w-1"))));
    }

    [Fact]
    public void The_walkthrough_may_be_removed()
    {
        Assert.Equal(ChapterRemovalVerdict.Removable, ChapterRemoval.Check(Tab(WalkthroughTab.Slug, new GuideSectionDto("story", Paragraphs: ["Step"]))));
    }

    [Fact]
    public void A_failed_placeholder_may_be_removed()
    {
        var placeholder = FailedChapters.CreateSection("de", "Alle Bosse", "bestiary", "Grund", null, null);
        Assert.Equal(ChapterRemovalVerdict.Removable, ChapterRemoval.Check(Tab("bestiary", placeholder)));
    }

    [Fact]
    public void The_story_and_lore_tab_is_not_removable_here()
    {
        Assert.Equal(ChapterRemovalVerdict.StoryLore, ChapterRemoval.Check(Tab(LoreTab.Slug, new GuideSectionDto("story", Paragraphs: ["Lore"]))));
    }

    [Fact]
    public void The_hidden_achievement_list_is_not_removable_by_slug_or_by_table()
    {
        Assert.Equal(ChapterRemovalVerdict.AchievementList, ChapterRemoval.Check(Tab("achievements", Checklist("x", "ACH_1"))));
        Assert.Equal(ChapterRemovalVerdict.AchievementList, ChapterRemoval.Check(Tab("steam-list", Checklist("steam-achievements", "ACH_1"))));
    }

    [Fact]
    public void Only_the_ticks_of_the_removed_chapters_own_rows_and_puzzles_are_orphaned()
    {
        var removed = Tab("secrets",
            Checklist("secrets", "s-1", "s-2"),
            new GuideSectionDto("puzzle", Puzzle: new GuidePuzzleDto(ItemId: "p-1")));
        var other = Tab("weapons", Checklist("weapons", "w-1"));
        var achievements = Tab("achievements", Checklist("steam-achievements", "ACH_1", "ACH_2"));

        var orphaned = ChapterRemoval.OrphanedCheckKeys(removed, [other, achievements]);

        Assert.Equal(["p-1", "s-1", "s-2"], orphaned.Order());
    }

    [Fact]
    public void An_id_that_still_exists_in_another_tab_keeps_its_tick()
    {
        // Hand-edited or imported data may share an id between two tabs: the tick still has a row to show on, so it stays
        var removed = Tab("secrets", Checklist("secrets", "shared", "own"));
        var other = Tab("weapons", Checklist("weapons", "shared"));

        Assert.Equal(["own"], ChapterRemoval.OrphanedCheckKeys(removed, [other]));
    }

    [Fact]
    public void A_chapter_without_checklists_orphans_nothing()
    {
        var removed = Tab("tips", new GuideSectionDto("story", Paragraphs: ["Text"]));

        Assert.Empty(ChapterRemoval.OrphanedCheckKeys(removed, [Tab("weapons", Checklist("weapons", "w-1"))]));
    }
}
