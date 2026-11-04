using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Data;
using Companion.Api.Endpoints;

namespace Companion.Api.Services;

/// <summary>
/// What <see cref="GuideLinker.Reconcile"/> decided: the chapters whose sections changed (new JSON), the tick marks to create and the ones to remove,
/// and how many links were set or cleared (for logging and tests).
/// </summary>
internal sealed record LinkReconciliation(
    IReadOnlyDictionary<Guid, string> ChangedSections,
    IReadOnlySet<string> AddChecks,
    IReadOnlySet<string> RemoveChecks,
    int Linked,
    int Cleared)
{
    public bool IsEmpty => ChangedSections.Count == 0 && AddChecks.Count == 0 && RemoveChecks.Count == 0;
}

/// <summary>
/// Cross-chapter links: a checklist row (mostly in the walkthrough) or a side quest entry may carry <c>ref</c> = the item id of the DEFINING row in another
/// chapter of the same game. A row with a ref has no tick of its own - it shows and toggles the tick of the row it points to - so the walkthrough and
/// the espers/quests/collectibles chapter always agree.
///
/// This class keeps those links true after every change of the chapters (pure: it only reads and returns JSON, <see cref="GuideLinkMaintenance"/> applies it):
/// refs to rows that are gone are cleared (the row keeps its tick), and walkthrough rows / side quest entries without a ref are linked by NAME to a defining
/// row of another chapter - only when exactly one row matches, never a guess.
/// </summary>
internal static partial class GuideLinker
{
    // ---- Names -------------------------------------------------------------------------------------------------------------------

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotLetterOrDigit();

    // Words in front of a name that name the KIND of thing, not the thing ("Esper Ifrit", "Quest: The Lost Child", "Boss - Ifrit"). Folded form (lowercase, no diacritics).
    private static readonly HashSet<string> PrefixWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "le", "la", "les", "el", "los", "las", "il",
        "quest", "quests", "sidequest", "side", "nebenquest", "nebenmission", "nebenaufgabe", "mission", "aufgabe",
        "esper", "espers", "summon", "beschworung", "boss", "bosse", "weapon", "waffe", "item", "gegenstand",
        "collectible", "sammelobjekt", "chest", "truhe"
    };

    private const int MinKeyLength = 2;

    /// <summary>
    /// A name reduced to what identifies it: lowercase, diacritics folded ("Ätherschlüssel" == "atherschlussel"), text in brackets removed ("Ifrit (Esper)" ==
    /// "Ifrit"), everything but letters and digits is a single space.
    /// </summary>
    internal static string NormalizeName(string? name) => Fold(name, keepBrackets: false);

    /// <summary>
    /// Like <see cref="NormalizeName"/> but the bracket text stays part of the name ("Elixir (Höhle)" -> "elixir hohle"). Used as the FIRST key, so two
    /// things that differ only in their bracket note ("Elixir (Höhle)", "Elixir (Turm)") are never the same name.
    /// </summary>
    internal static string NormalizeNameWithBrackets(string? name) => Fold(name, keepBrackets: true);

    private static string Fold(string? name, bool keepBrackets)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }
        var withoutBrackets = keepBrackets ? name : Brackets().Replace(name, " ");
        // A name that is only a bracket ("(Esper)") keeps its text
        var source = string.IsNullOrWhiteSpace(withoutBrackets) ? name : withoutBrackets;
        var lower = source.Trim().ToLowerInvariant()
            .Replace("ß", "ss").Replace("æ", "ae").Replace("œ", "oe").Replace("ø", "o").Replace("đ", "d").Replace("ł", "l");
        var folded = new StringBuilder(lower.Length);
        foreach (var ch in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(ch);
            }
        }
        return NotLetterOrDigit().Replace(folded.ToString(), " ").Trim();
    }

    /// <summary>
    /// The keys a name is matched by, most specific first: the name with its bracket note (only when it has one), the full normalized name, then every form with
    /// leading kind words/articles stripped one by one ("quest the lost child" -> "the lost child" -> "lost child").
    /// </summary>
    internal static List<string> NameKeys(string? name)
    {
        var keys = new List<string>();
        var full = NormalizeName(name);
        if (full.Length < MinKeyLength)
        {
            return keys;
        }
        var bracketed = NormalizeNameWithBrackets(name);
        if (bracketed != full && bracketed.Length >= MinKeyLength)
        {
            keys.Add(bracketed);
        }
        keys.Add(full);
        var words = full.Split(' ');
        for (var skip = 1; skip < words.Length && PrefixWords.Contains(words[skip - 1]); skip++)
        {
            var rest = string.Join(' ', words.Skip(skip));
            if (rest.Length >= MinKeyLength)
            {
                keys.Add(rest);
            }
        }
        return keys;
    }

    // ---- Reading the JSON ----------------------------------------------------------------------------------------------------------

    private sealed class ParsedTab(int index, GuideTabEntity entity, JsonArray sections)
    {
        public int Index { get; } = index;
        public GuideTabEntity Entity { get; } = entity;
        public JsonArray Sections { get; } = sections;
        public bool Changed { get; set; }
        public bool IsWalkthrough => WalkthroughTab.Is(Entity);

        /// <summary>The hidden Steam achievement list: its ticks come from Steam, nothing links to or from it.</summary>
        public bool IsAchievementList { get; } = string.Equals(entity.Slug, "achievements", StringComparison.OrdinalIgnoreCase)
            || sections.OfType<JsonObject>().Any(s => s["table"]?["tableId"]?.ToString() == "steam-achievements");
    }

    private sealed record Definer(int Tab, int Section, string ItemId, string Name, List<string> Keys);

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    /// <summary>The checklist rows of a section that can be ticked (a "checklist_table" whose table is not explicitly non-checkable).</summary>
    private static IEnumerable<JsonObject> TickableRows(JsonObject section)
    {
        if (section["type"]?.ToString() != "checklist_table" || section["table"] is not JsonObject table)
        {
            return [];
        }
        if (table["checkable"] is JsonValue c && c.TryGetValue<bool>(out var checkable) && !checkable)
        {
            return [];
        }
        return table["rows"] is JsonArray rows ? rows.OfType<JsonObject>() : [];
    }

    /// <summary>The text of the row's name column: the column flagged as row header, else the first column, else the first non-empty cell.</summary>
    internal static string? RowName(JsonObject section, JsonObject row)
    {
        if (row["cells"] is not JsonObject cells)
        {
            return null;
        }
        var columns = section["table"]?["columns"] as JsonArray;
        var keys = new List<string>();
        var header = columns?.OfType<JsonObject>().FirstOrDefault(c => c["isRowHeader"] is JsonValue v && v.TryGetValue<bool>(out var flag) && flag);
        if (Text(header?["key"]) is { } headerKey) keys.Add(headerKey);
        if (Text(columns?.OfType<JsonObject>().FirstOrDefault()?["key"]) is { } firstKey) keys.Add(firstKey);
        foreach (var key in keys)
        {
            if (Text(cells[key]) is { } named)
            {
                return named;
            }
        }
        return cells.Select(p => Text(p.Value)).FirstOrDefault(t => t is not null);
    }

    private static Dictionary<string, Definer> CollectDefiners(IReadOnlyList<ParsedTab> tabs)
    {
        var definers = new Dictionary<string, Definer>(StringComparer.Ordinal);
        foreach (var tab in tabs.Where(t => !t.IsAchievementList))
        {
            for (var i = 0; i < tab.Sections.Count; i++)
            {
                if (tab.Sections[i] is not JsonObject section)
                {
                    continue;
                }
                foreach (var row in TickableRows(section))
                {
                    if (Text(row["itemId"]) is { } id && Text(row["ref"]) is null)
                    {
                        var name = RowName(section, row) ?? string.Empty;
                        definers.TryAdd(id, new Definer(tab.Index, i, id, name, NameKeys(name)));
                    }
                }
                if (section["type"]?.ToString() == "puzzle" && Text(section["puzzle"]?["itemId"]) is { } puzzleId)
                {
                    var name = Text(section["title"]) ?? string.Empty;
                    definers.TryAdd(puzzleId, new Definer(tab.Index, i, puzzleId, name, NameKeys(name)));
                }
            }
        }
        return definers;
    }

    /// <summary>How many of <see cref="NameKeys"/> are the name itself (with and without bracket note) - the rest are forms with kind words stripped.</summary>
    private static int PrimaryCount(string? name, List<string> keys)
    {
        if (keys.Count == 0)
        {
            return 0;
        }
        return keys.Count > 1 && keys[0] == NormalizeNameWithBrackets(name) && keys[1] == NormalizeName(name) ? 2 : 1;
    }

    /// <summary>
    /// A thing that wants a link by name (a walkthrough row, a side quest entry) or a thing that can be linked to (a defining row): its keys and how many of
    /// them are the plain name (<see cref="PrimaryCount"/>).
    /// </summary>
    internal sealed record NameCandidate(string Id, List<string> Keys, int Primary)
    {
        public static NameCandidate Of(string id, string? name)
        {
            var keys = NameKeys(name);
            return new NameCandidate(id, keys, PrimaryCount(name, keys));
        }

        public bool HasPrimary(string key) => Keys.Take(Primary).Contains(key, StringComparer.Ordinal);
    }

    /// <summary>
    /// Pairs sources with targets by name, strictly one to one (shared by the linker and by the tick transfer of a chapter that was researched again).
    /// The keys of a source are tried most specific first; a key decides when it names exactly ONE target (a target whose plain name is the key wins over
    /// targets that only match after stripping kind words) AND no other source carries the same key at the same tier. Several targets or several sources =
    /// ambiguous = no pair. Finally a target is paired with at most one source - two different rows are never merged into one.
    /// </summary>
    internal static Dictionary<string, string> PairOneToOne(IReadOnlyList<NameCandidate> sources, IReadOnlyList<NameCandidate> targets)
    {
        int Competitors(string key, bool primaryTier) =>
            sources.Count(s => primaryTier ? s.HasPrimary(key) : s.Keys.Contains(key, StringComparer.Ordinal));

        var proposals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            for (var k = 0; k < source.Keys.Count; k++)
            {
                var key = source.Keys[k];
                var primary = targets.Where(t => t.HasPrimary(key)).ToList();
                var hits = primary.Count > 0 ? primary : targets.Where(t => t.Keys.Contains(key, StringComparer.Ordinal)).ToList();
                if (hits.Count == 0)
                {
                    continue;
                }
                // Other sources with the same key at the same tier
                var others = Competitors(key, k < source.Primary) - 1;
                if (hits.Count == 1 && others <= 0)
                {
                    proposals[source.Id] = hits[0].Id;
                }
                break;
            }
        }

        // One target, one source: when two sources still ended up on the same target (through different keys), none of them is paired
        foreach (var crowded in proposals.GroupBy(p => p.Value).Where(g => g.Count() > 1).ToList())
        {
            foreach (var pair in crowded)
            {
                proposals.Remove(pair.Key);
            }
        }
        return proposals;
    }

    // ---- Reconcile ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Brings the links of all chapters of a game in line with the rows that exist now. <paramref name="checks"/> = the item keys that are ticked (before anything
    /// is deleted).
    /// 1) Every ref is resolved to the row that finally defines the item: a ref to a row that is itself a ref row follows that row's ref (W -> T -> U becomes
    ///    W -> U). A ref that ends nowhere (the row is gone) or in the same chapter is cleared; the row becomes a normal row again and keeps the tick it showed
    ///    (so removing or researching the defining chapter again never loses progress).
    /// 2) Walkthrough rows and side quest entries without a ref are linked by name to a defining row of another chapter, strictly one to one
    ///    (<see cref="PairOneToOne"/>): the name must be unique among the targets AND among the walkthrough's own rows and quest entries, and a target that a
    ///    walkthrough row already points at takes no second row. A row that had its own tick hands it over (the target is ticked if either was).
    /// </summary>
    internal static LinkReconciliation Reconcile(IReadOnlyCollection<GuideTabEntity> tabs, IReadOnlySet<string> checks)
    {
        var parsed = new List<ParsedTab>();
        foreach (var tab in tabs)
        {
            try
            {
                if (JsonNode.Parse(string.IsNullOrWhiteSpace(tab.SectionsJson) ? "[]" : tab.SectionsJson) is JsonArray sections)
                {
                    parsed.Add(new ParsedTab(parsed.Count, tab, sections));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // A chapter whose JSON cannot be read is not ours to judge
            }
        }

        // The ticks as they are after the decisions made so far; the difference to "checks" is what the caller has to write
        var effective = new HashSet<string>(checks, StringComparer.Ordinal);
        var touched = new HashSet<string>(StringComparer.Ordinal);
        var linked = 0;
        var cleared = 0;

        // 1) resolve every ref to its final definer, clear what ends nowhere
        var resolver = new RefResolver(parsed);
        foreach (var tab in parsed)
        {
            foreach (var section in tab.Sections.OfType<JsonObject>())
            {
                foreach (var row in TickableRows(section))
                {
                    if (Text(row["ref"]) is { } target)
                    {
                        var final = resolver.Final(target, tab.Index);
                        if (final is null)
                        {
                            row.Remove("ref");
                            tab.Changed = true;
                            cleared++;
                            // The row is a normal row again: it keeps the tick of the row it used to show
                            if (effective.Contains(target) && Text(row["itemId"]) is { } ownId)
                            {
                                effective.Add(ownId);
                                touched.Add(ownId);
                            }
                        }
                        else if (final != target)
                        {
                            row["ref"] = final;
                            tab.Changed = true;
                        }
                    }
                    else if (row.ContainsKey("ref"))
                    {
                        row.Remove("ref");
                        tab.Changed = true;
                    }
                }
                if (section["quests"] is JsonArray quests)
                {
                    foreach (var quest in quests.OfType<JsonObject>())
                    {
                        if (!quest.ContainsKey("ref"))
                        {
                            continue;
                        }
                        var current = Text(quest["ref"]);
                        var final = current is null ? null : resolver.Final(current, tab.Index);
                        if (final is null)
                        {
                            quest.Remove("ref");
                            tab.Changed = true;
                            cleared++;
                        }
                        else if (final != current)
                        {
                            quest["ref"] = final;
                            tab.Changed = true;
                        }
                    }
                }
            }
        }

        // 2) new links by name (rows that were just freed above count as normal rows now)
        var definers = CollectDefiners(parsed);
        foreach (var tab in parsed.Where(t => t.IsWalkthrough && !t.IsAchievementList))
        {
            var rows = new List<(JsonObject Row, NameCandidate Name)>();
            var quests = new List<(JsonObject Quest, NameCandidate Name)>();
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in tab.Sections.OfType<JsonObject>())
            {
                foreach (var row in TickableRows(section))
                {
                    if (Text(row["itemId"]) is not { } ownId)
                    {
                        continue;
                    }
                    // Every row counts as a source (also those that already have a ref), so a second "Elixir" never shares a key unnoticed
                    rows.Add((row, NameCandidate.Of("r:" + ownId, RowName(section, row))));
                    if (Text(row["ref"]) is { } existing)
                    {
                        taken.Add(existing);
                    }
                }
                if (section["type"]?.ToString() == "alert" && section["quests"] is JsonArray questList)
                {
                    foreach (var quest in questList.OfType<JsonObject>())
                    {
                        quests.Add((quest, NameCandidate.Of("q:" + quests.Count, Text(quest["title"]))));
                    }
                }
            }

            // A quest entry that repeats a row's name (or an earlier entry's) is a mention of the same thing, not a second thing
            // (it takes the link of the first source with that name, its "anchor", and does not count as a competitor)
            var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (_, name) in rows)
            {
                if (name.Keys.Count > 0)
                {
                    anchors.TryAdd(name.Keys[0], name.Id);
                }
            }
            var mentionOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (_, name) in quests)
            {
                if (name.Keys.Count > 0 && !anchors.TryAdd(name.Keys[0], name.Id))
                {
                    mentionOf[name.Id] = anchors[name.Keys[0]];
                }
            }

            var pool = definers.Values.Where(d => d.Tab != tab.Index && d.Keys.Count > 0)
                .Select(d => new NameCandidate(d.ItemId, d.Keys, PrimaryCount(d.Name, d.Keys))).ToList();
            var sources = rows.Select(r => r.Name).Concat(quests.Select(q => q.Name).Where(q => !mentionOf.ContainsKey(q.Id))).ToList();
            var pairs = PairOneToOne(sources, pool);
            foreach (var (mention, anchor) in mentionOf)
            {
                if (pairs.TryGetValue(anchor, out var anchorTarget))
                {
                    pairs[mention] = anchorTarget;
                }
            }

            foreach (var (row, name) in rows)
            {
                if (Text(row["ref"]) is not null || Text(row["itemId"]) is not { } ownId || !pairs.TryGetValue(name.Id, out var target) || !taken.Add(target))
                {
                    continue;
                }
                row["ref"] = target;
                tab.Changed = true;
                linked++;
                // Merge the ticks: ticked if either was; the ref row has none of its own
                if (effective.Remove(ownId))
                {
                    touched.Add(ownId);
                    effective.Add(target);
                    touched.Add(target);
                }
            }
            foreach (var (quest, name) in quests)
            {
                if (Text(quest["ref"]) is null && pairs.TryGetValue(name.Id, out var target))
                {
                    quest["ref"] = target;
                    tab.Changed = true;
                    linked++;
                }
            }
        }

        var changed = parsed.Where(t => t.Changed).ToDictionary(t => t.Entity.Id, t => t.Sections.ToJsonString());
        var addChecks = touched.Where(k => effective.Contains(k) && !checks.Contains(k)).ToHashSet(StringComparer.Ordinal);
        var removeChecks = touched.Where(k => !effective.Contains(k) && checks.Contains(k)).ToHashSet(StringComparer.Ordinal);
        return new LinkReconciliation(changed, addChecks, removeChecks, linked, cleared);
    }

    /// <summary>
    /// Follows refs to the row that finally defines an item. The final definer of a defining row is the row itself; of a ref row it is the final definer of
    /// ITS ref when that lies in another chapter than the ref row, else the ref row itself (it is released in the same pass and defines the item then).
    /// A ref loop ends at nothing. Rows of the hidden achievement list are never targets.
    /// </summary>
    private sealed class RefResolver
    {
        private readonly Dictionary<string, (int Tab, string? Ref)> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _memo = new(StringComparer.Ordinal);

        public RefResolver(IReadOnlyList<ParsedTab> tabs)
        {
            foreach (var tab in tabs.Where(t => !t.IsAchievementList))
            {
                foreach (var section in tab.Sections.OfType<JsonObject>())
                {
                    foreach (var row in TickableRows(section))
                    {
                        if (Text(row["itemId"]) is { } id)
                        {
                            _rows.TryAdd(id, (tab.Index, Text(row["ref"])));
                        }
                    }
                    if (section["type"]?.ToString() == "puzzle" && Text(section["puzzle"]?["itemId"]) is { } puzzleId)
                    {
                        _rows.TryAdd(puzzleId, (tab.Index, null));
                    }
                }
            }
        }

        /// <summary>The final definer a ref from chapter <paramref name="fromTab"/> to <paramref name="target"/> stands for, or null (clear the ref).</summary>
        public string? Final(string target, int fromTab)
        {
            var final = Resolve(target, new HashSet<string>(StringComparer.Ordinal));
            return final is not null && _rows[final].Tab != fromTab ? final : null;
        }

        private string? Resolve(string id, HashSet<string> visiting)
        {
            if (_memo.TryGetValue(id, out var known))
            {
                return known;
            }
            if (!_rows.TryGetValue(id, out var row))
            {
                return null;
            }
            if (row.Ref is null)
            {
                return _memo[id] = id;
            }
            if (!visiting.Add(id))
            {
                // A loop: this row is released and defines the item itself
                return id;
            }
            var next = Resolve(row.Ref, visiting);
            var result = next is not null && _rows[next].Tab != row.Tab ? next : id;
            visiting.Remove(id);
            return _memo[id] = result;
        }
    }

    // ---- What the UI shows ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The two maps of the game detail from the chapters as shown (guide order): <c>itemLinks</c> = for every defining item the places that reference it
    /// (checklist rows with a ref, side quest entries with a ref), <c>refTargets</c> = for every referenced item where its defining row lives.
    /// </summary>
    internal static (Dictionary<string, List<GuideItemLinkDto>> ItemLinks, Dictionary<string, GuideRefTargetDto> RefTargets) BuildMaps(IReadOnlyList<GuideTabDto> tabs)
    {
        var itemLinks = new Dictionary<string, List<GuideItemLinkDto>>(StringComparer.Ordinal);
        var refTargets = new Dictionary<string, GuideRefTargetDto>(StringComparer.Ordinal);
        var shown = tabs.Where(t => !t.IsAchievementList).ToList();

        // Where each defining item lives
        var homes = new Dictionary<string, GuideRefTargetDto>(StringComparer.Ordinal);
        foreach (var tab in shown)
        {
            for (var i = 0; i < tab.Sections.Count; i++)
            {
                var section = tab.Sections[i];
                if (section.Table is { Checkable: true, Rows: not null } table)
                {
                    foreach (var row in table.Rows.Where(r => string.IsNullOrEmpty(r.Ref) && !string.IsNullOrEmpty(r.ItemId)))
                    {
                        homes.TryAdd(row.ItemId, new GuideRefTargetDto(tab.Id, tab.Slug, tab.Title, i, DisplayName(table, row)));
                    }
                }
                if (!string.IsNullOrWhiteSpace(section.Puzzle?.ItemId))
                {
                    homes.TryAdd(section.Puzzle.ItemId, new GuideRefTargetDto(tab.Id, tab.Slug, tab.Title, i, section.Title ?? string.Empty));
                }
            }
        }

        void Add(string key, GuideItemLinkDto link, Guid tabId)
        {
            if (!homes.TryGetValue(key, out var home) || home.TabId == tabId)
            {
                return;
            }
            refTargets.TryAdd(key, home);
            if (!itemLinks.TryGetValue(key, out var list))
            {
                itemLinks[key] = list = [];
            }
            list.Add(link);
        }

        foreach (var tab in shown)
        {
            string? part = null;
            string? area = null;
            for (var i = 0; i < tab.Sections.Count; i++)
            {
                var section = tab.Sections[i];
                if (section.Type == "heading" && !string.IsNullOrWhiteSpace(section.Title))
                {
                    if (section.Level == 2)
                    {
                        area = section.Title.Trim();
                    }
                    else
                    {
                        part = section.Title.Trim();
                        area = null;
                    }
                    continue;
                }

                var path = string.Join(" · ", new[] { part, area }.Where(p => !string.IsNullOrEmpty(p)));
                foreach (var row in section.Table?.Rows ?? [])
                {
                    if (!string.IsNullOrEmpty(row.Ref))
                    {
                        Add(row.Ref, new GuideItemLinkDto("row", tab.Id, tab.Slug, tab.Title, i, row.ItemId, path), tab.Id);
                    }
                }
                foreach (var quest in section.Quests ?? [])
                {
                    if (!string.IsNullOrEmpty(quest.Ref))
                    {
                        Add(quest.Ref, new GuideItemLinkDto("quest", tab.Id, tab.Slug, tab.Title, i, null, path), tab.Id);
                    }
                }
            }
        }
        return (itemLinks, refTargets);
    }

    private static string DisplayName(GuideTableDto table, GuideTableRowDto row)
    {
        var key = table.Columns.FirstOrDefault(c => c.IsRowHeader)?.Key ?? table.Columns.FirstOrDefault()?.Key;
        if (key is not null && row.Cells.TryGetValue(key, out var named) && !string.IsNullOrWhiteSpace(named))
        {
            return named.Trim();
        }
        return row.Cells.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    }

    // ---- The list for the prompts -------------------------------------------------------------------------------------------------

    // Chapters whose items are the usual targets of a walkthrough mention come first when the list has to be cut
    private static readonly string[] PriorityTopics = ["quests", "sidequests", "collectibles", "characters", "bosses", "weapons", "equipment", "items"];

    private const int MaxNameLength = 60;

    /// <summary>The largest link list a walkthrough PART prompt carries (one call per part - the list repeats in every one).</summary>
    internal const int PartPromptCap = 100;

    /// <summary>The largest link list a chapter prompt (complete, add, redo, manual, batch) carries.</summary>
    internal const int ChapterPromptCap = 150;

    /// <summary>One block of the prompt list: a chapter (or a part of the chapter being replaced) with its tickable items in guide order.</summary>
    internal sealed record CandidateGroup(string Title, string? TopicId, IReadOnlyList<(string Id, string Name)> Items);

    /// <summary>
    /// The tickable items of the chapters that may be referenced (not the walkthrough, not the lore tab, not the achievement list, no ref rows), one group per
    /// chapter; chapters of the usual link targets (quests, collectibles, ...) first.
    /// </summary>
    internal static List<CandidateGroup> BuildCandidateGroups(IEnumerable<GuideTabEntity> tabs)
    {
        var groups = new List<CandidateGroup>();
        var ranked = tabs
            .Where(t => !WalkthroughTab.Is(t) && t.Slug != LoreTab.Slug && !string.Equals(t.Slug, "achievements", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => TopicRank(t.TopicId))
            .ThenBy(t => t.SortOrder);
        foreach (var tab in ranked)
        {
            if (Parse(tab.SectionsJson) is not { } sections || sections.OfType<JsonObject>().Any(s => s["table"]?["tableId"]?.ToString() == "steam-achievements"))
            {
                continue;
            }
            var items = DefiningItems(sections).Select(i => (i.Id, i.Name)).ToList();
            if (items.Count > 0)
            {
                groups.Add(new CandidateGroup(tab.Title, tab.TopicId, items));
            }
        }
        return groups;
    }

    /// <summary>
    /// The defining items of ONE chapter that is about to be researched again, grouped by its part headings (level 1; the walkthrough) so a part prompt can
    /// pick the items of its own part. Empty for a failed placeholder or a chapter without tickable items.
    /// </summary>
    internal static List<CandidateGroup> BuildKeepGroups(GuideTabEntity tab)
    {
        var groups = new List<CandidateGroup>();
        if (Parse(tab.SectionsJson) is not { } sections)
        {
            return groups;
        }
        var title = tab.Title;
        var items = new List<(string Id, string Name)>();
        foreach (var section in sections.OfType<JsonObject>())
        {
            if (section["type"]?.ToString() == "heading" && !(section["level"] is JsonValue level && level.TryGetValue<int>(out var l) && l == 2) && Text(section["title"]) is { } heading)
            {
                if (items.Count > 0)
                {
                    groups.Add(new CandidateGroup(title, tab.TopicId, items));
                }
                title = heading;
                items = [];
                continue;
            }
            items.AddRange(DefiningItems([section]).Select(i => (i.Id, i.Name)));
        }
        if (items.Count > 0)
        {
            groups.Add(new CandidateGroup(title, tab.TopicId, items));
        }
        return groups;
    }

    /// <summary>
    /// The tickable items of the given chapters as a compact list for a research prompt (grouped by chapter: "## Chapter" then "itemId | name"), at most
    /// <paramref name="cap"/> rows - see <see cref="RenderCandidates"/>. Empty when there is nothing to reference.
    /// </summary>
    internal static string BuildCandidateList(IEnumerable<GuideTabEntity> tabs, int cap = ChapterPromptCap) => RenderCandidates(BuildCandidateGroups(tabs), cap, null);

    /// <summary>
    /// Renders groups for a prompt with at most <paramref name="cap"/> rows. Items that are relevant to the prompt come first: an item whose group title or
    /// name shares a word with <paramref name="relevance"/> (the part's title and assignment, the chapter's title and focus). The rest is filled round robin
    /// (one row of every group per turn, groups in their order) so no chapter disappears. Within a group the guide order stays. The server links what is
    /// not in the list by name afterwards (<see cref="Reconcile"/>), so a cut list loses no link that a unique name can make.
    /// </summary>
    internal static string RenderCandidates(IReadOnlyList<CandidateGroup> groups, int cap, string? relevance)
    {
        var terms = Terms(relevance);
        var chosen = groups.Select(_ => new HashSet<int>()).ToArray();
        var total = 0;

        if (terms.Count > 0)
        {
            var scored = new List<(int Group, int Item, int Score)>();
            for (var g = 0; g < groups.Count; g++)
            {
                var groupHit = Terms(groups[g].Title).Overlaps(terms) ? 2 : 0;
                for (var i = 0; i < groups[g].Items.Count; i++)
                {
                    var score = groupHit + (Terms(groups[g].Items[i].Name).Overlaps(terms) ? 1 : 0);
                    if (score > 0)
                    {
                        scored.Add((g, i, score));
                    }
                }
            }
            foreach (var (group, item, _) in scored.OrderByDescending(s => s.Score).ThenBy(s => s.Group).ThenBy(s => s.Item))
            {
                if (total >= cap)
                {
                    break;
                }
                chosen[group].Add(item);
                total++;
            }
        }

        // Round robin over what is left
        for (var turn = 0; total < cap; turn++)
        {
            var any = false;
            for (var g = 0; g < groups.Count && total < cap; g++)
            {
                if (turn < groups[g].Items.Count)
                {
                    any = true;
                    if (chosen[g].Add(turn))
                    {
                        total++;
                    }
                }
            }
            if (!any)
            {
                break;
            }
        }

        var text = new StringBuilder();
        for (var g = 0; g < groups.Count; g++)
        {
            if (chosen[g].Count == 0)
            {
                continue;
            }
            text.Append("## ").Append(Shorten(groups[g].Title, MaxNameLength)).Append('\n');
            foreach (var index in chosen[g].Order())
            {
                text.Append(groups[g].Items[index].Id).Append(" | ").Append(Shorten(groups[g].Items[index].Name, MaxNameLength)).Append('\n');
            }
        }
        return text.ToString().TrimEnd('\n');
    }

    // Words of assignments and titles that say nothing about WHICH items matter (kind words like "espers" do and stay)
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "part", "teil", "chapter", "kapitel", "guide", "walkthrough", "komplettlosung", "with", "from", "this", "that", "these", "what", "where", "when",
        "mit", "von", "dies", "diese", "dieses", "alle", "aller", "allen", "every", "research", "recherchiere", "again", "noch", "einmal", "complete",
        "completely", "komplett", "thoroughly", "grundlich", "current", "version", "fassung", "bisherige", "sections", "abschnitte", "cover", "least",
        "same", "topics", "themen", "correct", "mistakes", "fehler", "missing", "fehlt", "ergänze", "erganze", "korrigiere", "decke", "mindestens", "dieselben"
    };

    /// <summary>The words of a text that can tell relevance (normalized, at least 4 letters, no filler words of assignments).</summary>
    private static HashSet<string> Terms(string? text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in NormalizeNameWithBrackets(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 4 && !NoiseWords.Contains(word))
            {
                words.Add(word);
            }
        }
        return words;
    }

    /// <summary>The defining items of a list of sections: tickable rows without a ref (name = the name column) and puzzles with an item id (name = their title).</summary>
    internal static IEnumerable<(string Id, string Name, int Section)> DefiningItems(IList<JsonNode?> sections)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i] is not JsonObject section)
            {
                continue;
            }
            foreach (var row in TickableRows(section))
            {
                if (Text(row["itemId"]) is { } id && Text(row["ref"]) is null && RowName(section, row) is { } name)
                {
                    yield return (id, name, i);
                }
            }
            if (section["type"]?.ToString() == "puzzle" && Text(section["puzzle"]?["itemId"]) is { } puzzleId && Text(section["title"]) is { } puzzleName)
            {
                yield return (puzzleId, puzzleName, i);
            }
        }
    }

    private static JsonArray? Parse(string? json)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json) as JsonArray;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // ---- Ticking --------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The key a tick of <paramref name="key"/> belongs to: when <paramref name="key"/> is the own id of a row that carries a ref (it has no tick of its own),
    /// the item the row points at; otherwise <paramref name="key"/> itself. A client that still sends the old own id of a row that became a ref row (a stale
    /// page) thus never creates a hidden tick nobody shows.
    /// </summary>
    internal static string TickKeyFor(IEnumerable<string?> sectionsJsons, string key)
    {
        string? target = null;
        foreach (var json in sectionsJsons)
        {
            // Cheap pre-check: most chapters do not mention the key at all
            if (string.IsNullOrEmpty(json) || !json.Contains(key, StringComparison.Ordinal) || Parse(json) is not { } sections)
            {
                continue;
            }
            foreach (var section in sections.OfType<JsonObject>())
            {
                foreach (var row in TickableRows(section))
                {
                    if (Text(row["itemId"]) != key)
                    {
                        continue;
                    }
                    if (Text(row["ref"]) is not { } reference)
                    {
                        // A defining row with this id exists: the key is a real tick of its own
                        return key;
                    }
                    target ??= reference;
                }
                if (section["type"]?.ToString() == "puzzle" && Text(section["puzzle"]?["itemId"]) == key)
                {
                    return key;
                }
            }
        }
        return target ?? key;
    }

    // ---- Researching a chapter again: keep the ticks --------------------------------------------------------------------------------

    /// <summary>The row of the new version an old defining row continues as: its own id and, when it came back as a ref row, the item it points at.</summary>
    internal sealed record IdCounterpart(string NewId, string? Ref);

    /// <summary>
    /// When a chapter is replaced by a new version: what each old DEFINING row (it may carry a tick) continues as, wherever that changes something.
    /// First by the same own id - a row that kept its id but now carries a "ref" is a counterpart (its tick belongs to the ref's target now). Then the old
    /// ids that are gone are paired with the new rows whose ids are new (defining rows AND ref rows) by strict one-to-one name matches
    /// (<see cref="PairOneToOne"/>, the linker's rules): "Elixir (Höhle)" and "Elixir (Turm)" stay apart, five rows called "Elixir" pair with nothing.
    /// An old row that is still a defining row with the same id is not listed (nothing to do).
    /// </summary>
    internal static Dictionary<string, IdCounterpart> PlanIdTransfer(string? oldJson, string? newJson)
    {
        var oldItems = Parse(oldJson) is { } oldSections ? DefiningItems(oldSections).DistinctBy(i => i.Id).ToList() : [];
        var newRows = Parse(newJson) is { } newSections ? AllItems(newSections).DistinctBy(i => i.Id).ToList() : [];
        var byId = newRows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var oldIds = oldItems.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        var result = new Dictionary<string, IdCounterpart>(StringComparer.Ordinal);
        foreach (var item in oldItems)
        {
            if (byId.TryGetValue(item.Id, out var same) && same.Ref is not null)
            {
                result[item.Id] = new IdCounterpart(same.Id, same.Ref);
            }
        }

        var sources = oldItems.Where(i => !byId.ContainsKey(i.Id)).Select(i => NameCandidate.Of(i.Id, i.Name)).ToList();
        var targets = newRows.Where(r => !oldIds.Contains(r.Id)).Select(r => NameCandidate.Of(r.Id, r.Name)).ToList();
        if (sources.Count > 0 && targets.Count > 0)
        {
            foreach (var (oldId, newId) in PairOneToOne(sources, targets))
            {
                result[oldId] = new IdCounterpart(newId, byId[newId].Ref);
            }
        }
        return result;
    }

    /// <summary>Every tickable row of a list of sections (defining rows and ref rows) and the puzzles with an item id: own id, name, ref.</summary>
    private static IEnumerable<(string Id, string Name, string? Ref)> AllItems(IList<JsonNode?> sections)
    {
        foreach (var section in sections.OfType<JsonObject>())
        {
            foreach (var row in TickableRows(section))
            {
                if (Text(row["itemId"]) is { } id)
                {
                    yield return (id, RowName(section, row) ?? string.Empty, Text(row["ref"]));
                }
            }
            if (section["type"]?.ToString() == "puzzle" && Text(section["puzzle"]?["itemId"]) is { } puzzleId)
            {
                yield return (puzzleId, Text(section["title"]) ?? string.Empty, null);
            }
        }
    }

    /// <summary>
    /// The row that finally defines the item a ref from chapter <paramref name="fromTabId"/> names (following ref chains, the same rules as
    /// <see cref="Reconcile"/>), or null when the ref ends nowhere or in its own chapter (the linker then clears it).
    /// </summary>
    internal static string? FinalTarget(IReadOnlyList<GuideTabEntity> tabs, string target, Guid fromTabId)
    {
        var parsed = new List<ParsedTab>();
        var fromIndex = -1;
        foreach (var tab in tabs)
        {
            if (Parse(tab.SectionsJson) is { } sections)
            {
                if (tab.Id == fromTabId)
                {
                    fromIndex = parsed.Count;
                }
                parsed.Add(new ParsedTab(parsed.Count, tab, sections));
            }
        }
        return new RefResolver(parsed).Final(target, fromIndex);
    }

    /// <summary>The chapter JSON with every ref (rows and side quest entries) renamed through <paramref name="map"/>; null when nothing changed.</summary>
    internal static string? RemapRefs(string? json, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0 || Parse(json) is not { } sections)
        {
            return null;
        }
        var changed = false;
        foreach (var section in sections.OfType<JsonObject>())
        {
            var holders = (section["table"]?["rows"] as JsonArray ?? []).OfType<JsonObject>()
                .Concat((section["quests"] as JsonArray ?? []).OfType<JsonObject>());
            foreach (var holder in holders)
            {
                if (Text(holder["ref"]) is { } target && map.TryGetValue(target, out var renamed))
                {
                    holder["ref"] = renamed;
                    changed = true;
                }
            }
        }
        return changed ? sections.ToJsonString() : null;
    }

    private static int TopicRank(string? topicId)
    {
        var index = topicId is null ? -1 : Array.IndexOf(PriorityTopics, topicId);
        return index >= 0 ? index : PriorityTopics.Length;
    }

    private static string Shorten(string value, int max)
    {
        var oneLine = Regex.Replace(value, @"\s+", " ").Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 1)].TrimEnd() + "…";
    }
}
