using Companion.Api.Data;
using Companion.Api.Localization;

namespace Companion.Api.Services;

public sealed partial class AiGuideGeneratorService
{
    // The prompt side of the cross-chapter links (see GuideLinker): when the guide already has chapters with tickable rows, a chapter that mentions one of
    // those items points at it with "ref" instead of creating a second row. Shared by every content prompt (API path, manual chapter, manual batch).

    /// <summary>The context with the items the chapter may point at: the tickable rows of the given chapters (not the walkthrough, not the lore tab).</summary>
    internal static GameContext WithLinkCandidates(GameContext context, IEnumerable<GuideTabEntity> tabs, ISet<Guid>? excludeTabs = null)
    {
        var groups = GuideLinker.BuildCandidateGroups(excludeTabs is null ? tabs : tabs.Where(t => !excludeTabs.Contains(t.Id)));
        return context with { LinkGroups = groups, LinkCandidates = GuideLinker.RenderCandidates(groups, GuideLinker.ChapterPromptCap, null) };
    }

    /// <summary>The context with the items the chapter being researched again has today, so the new version keeps their item ids (and with them the ticks).</summary>
    internal static GameContext WithKeepItems(GameContext context, GuideTabEntity tab)
    {
        var groups = FailedChapters.Read(tab.SectionsJson) is null ? GuideLinker.BuildKeepGroups(tab) : [];
        return context with { KeepGroups = groups.Count > 0 ? groups : null };
    }

    /// <summary>
    /// The mandatory rule for "ref" with the list of existing items (at most <paramref name="cap"/> rows, the ones relevant to <paramref name="relevance"/>
    /// first), plus - when the chapter is researched again - the list of its current items with the rule to keep their ids. Empty when there is nothing to say.
    /// </summary>
    internal static string LinkRules(GameContext context, string? relevance = null, int cap = GuideLinker.ChapterPromptCap)
    {
        var parts = new List<string>();
        var candidates = context.LinkGroups is { } groups ? GuideLinker.RenderCandidates(groups, cap, relevance) : context.LinkCandidates;
        if (!string.IsNullOrWhiteSpace(candidates))
        {
            parts.Add(context.Language == Lang.English
                ? $$"""
                  Existing items of this guide (links between chapters – mandatory rule). These chapters already have tickable rows (itemId | name, grouped by chapter):
                  {{candidates}}
                  When your chapter mentions one of these items (a quest, collectible, summon, weapon, character ...), do NOT create a second row or repeat its solution:
                  set "ref" to EXACTLY the itemId from the list – on your checklist row (the row keeps its own unique "itemId"; its cells name the item and say where/when you get
                  it in THIS chapter) or on the entry of a "side_quests" hint ({ "title": "...", "ref": "<itemId>" }). Copy the ids exactly; never invent one; leave "ref"
                  out for everything that is not in the list.
                  """
                : $$"""
                  Vorhandene Einträge dieses Guides (Verknüpfungen zwischen Kapiteln – verbindliche Regel). In diesen Kapiteln gibt es schon abhakbare Zeilen (itemId | Name, nach Kapitel gruppiert):
                  {{candidates}}
                  Erwähnt dein Kapitel einen dieser Einträge (eine Quest, ein Sammelobjekt, eine Beschwörung, eine Waffe, einen Charakter ...), lege KEINE zweite Zeile an und wiederhole
                  nicht seine Lösung: setze "ref" auf GENAU die itemId aus der Liste – an deiner Checklisten-Zeile (die Zeile behält ihre eigene eindeutige "itemId"; ihre Zellen nennen den
                  Eintrag und sagen, wo/wann man ihn in DIESEM Kapitel bekommt) oder am Eintrag eines "side_quests"-Hinweises ({ "title": "...", "ref": "<itemId>" }). Kopiere die Ids
                  exakt; erfinde nie eine; lass "ref" bei allem weg, was nicht in der Liste steht.
                  """);
        }

        if (context.KeepGroups is { Count: > 0 } keep && GuideLinker.RenderCandidates(keep, cap, relevance) is { Length: > 0 } current)
        {
            parts.Add(context.Language == Lang.English
                ? $$"""
                  This chapter is being written AGAIN – the player has already ticked some of its rows. Its current tickable rows (itemId | name):
                  {{current}}
                  Mandatory rule: for every entry of your new version that is the same thing as one of these rows, use EXACTLY that "itemId" again (copy it). Give new
                  ids only to entries that are new; never reuse an id for something else.
                  """
                : $$"""
                  Dieses Kapitel wird NEU geschrieben – der Spieler hat schon Zeilen davon abgehakt. Seine bisherigen abhakbaren Zeilen (itemId | Name):
                  {{current}}
                  Verbindliche Regel: Ist ein Eintrag deiner neuen Fassung dieselbe Sache wie eine dieser Zeilen, verwende GENAU deren "itemId" wieder (kopieren). Neue Ids nur
                  für Einträge, die neu sind; eine Id nie für etwas anderes wiederverwenden.
                  """);
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>The research rules plus the link rules (when there is a list): the one text every content prompt appends.</summary>
    private static string WithLinkRules(string rules, GameContext context, string? relevance = null, int cap = GuideLinker.ChapterPromptCap)
    {
        var links = LinkRules(context, relevance, cap);
        return links.Length == 0 ? rules : rules + "\n\n" + links;
    }
}
