using Companion.Api.Data;
using Companion.Api.Endpoints;

namespace Companion.Api.Services;

/// <summary>Whether a chapter may be removed from a guide by the player, and if not, why.</summary>
public enum ChapterRemovalVerdict
{
    Removable,

    /// <summary>The hidden Steam achievement table: Steam reports what is unlocked, the live trophies tab reads its tips from it.</summary>
    AchievementList,

    /// <summary>The "Story &amp; Lore" tab: it is a section of its own, not a chapter of the navigation.</summary>
    StoryLore,
}

/// <summary>
/// The rules for removing one chapter of a guide ("Kapitel entfernen"), kept pure so they can be tested without a database. A chapter is any
/// tab of the chapter navigation - researched content, the walkthrough and failed-research placeholders alike. Not removable:
/// the hidden achievement list (the live trophies tab needs it) and the Story &amp; Lore tab (own area; "Story &amp; Lore" researches it anew).
/// </summary>
public static class ChapterRemoval
{
    public static ChapterRemovalVerdict Check(GuideTabEntity tab)
    {
        if (tab.Slug == LoreTab.Slug)
        {
            return ChapterRemovalVerdict.StoryLore;
        }
        return ApiEndpoints.IsAchievementList(tab, ApiEndpoints.DeserializeSections(tab.SectionsJson))
            ? ChapterRemovalVerdict.AchievementList
            : ChapterRemovalVerdict.Removable;
    }

    /// <summary>
    /// The tick marks (<c>progress_checks.ItemKey</c>) that only belonged to the removed chapter: every item id of its checklist rows and
    /// tickable puzzles minus every item id that still exists in another tab. Item ids are unique per game (<c>MakeItemIdsUnique</c>), so the
    /// subtraction only matters for hand-edited or imported data - but it guarantees that a tick that still shows up somewhere else (e.g. on
    /// the hidden achievement list, whose ids are Steam's achievement api names, which the live sync ticks) is never taken away.
    /// Marks whose id is in no tab at all are not ours to judge and are left alone. Rows with a <c>ref</c> own no tick (they show the tick of the row they point to),
    /// so they are neither orphaned nor do they keep a tick alive; rows elsewhere that pointed at a removed row are freed first (<see cref="GuideLinker"/>), the caller
    /// runs <c>GuideLinkMaintenance.Apply</c> BEFORE it deletes these marks so the freed rows can take the tick over.
    /// </summary>
    public static HashSet<string> OrphanedCheckKeys(GuideTabEntity removed, IEnumerable<GuideTabEntity> remaining)
    {
        var own = ApiEndpoints.CollectItemIds([removed]);
        own.ExceptWith(ApiEndpoints.CollectItemIds(remaining));
        return own;
    }
}
