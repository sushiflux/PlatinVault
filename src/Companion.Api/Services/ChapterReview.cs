using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>The kinds of finding a reviewer can report (brief <c>ki-pruefung.md</c>, section 2). Anything else is dropped when the answer is read.</summary>
public static class ReviewKinds
{
    /// <summary>Only one answer has the entry and the reviewer confirmed it: it was taken over.</summary>
    public const string Added = "added";

    /// <summary>The answers contradict each other: the reviewer chose the version that has a source.</summary>
    public const string Corrected = "corrected";

    /// <summary>An entry of one answer is wrong and was left out (reason, ideally a source).</summary>
    public const string Removed = "removed";

    /// <summary>No reliable source either way: both versions stay visible, the entry carries the "unklar" marker.</summary>
    public const string Unclear = "unclear";

    public static readonly IReadOnlyList<string> All = [Added, Corrected, Removed, Unclear];
}

/// <summary>
/// What a finding is about: a table row or a puzzle (<see cref="ItemId"/>), a card of a card grid (<see cref="Section"/> + <see cref="Card"/>), or a whole
/// section such as a boss card (<see cref="Section"/>). Empty = free text, which can only be read, never "taken".
/// </summary>
public sealed record ReviewTarget(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ItemId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Section = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Card = null)
{
    [JsonIgnore]
    public bool IsStructured => ItemId is not null || Section is not null;
}

/// <summary>
/// One finding of the reviewer. A/B/Result are short texts (the entry in answer A, in answer B, what the merged chapter says), Column names the table
/// column of a cell-level finding. Applied = the version the player took afterwards ("a", "b" or "result"), null = nothing taken yet.
/// </summary>
public sealed record ReviewFinding(
    string Kind,
    ReviewTarget Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Column,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? A,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? B,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Applied = null);

/// <summary>The summary of a review: Agreed = entries both answers have without any finding (counted by the code); the rest comes from the findings.</summary>
public sealed record ReviewCounts(int Agreed, int Added, int Corrected, int Removed, int Unclear, int WithoutSource = 0);

/// <summary>The findings that could be read, how many entries were dropped, and how many corrected/removed findings come without a web source.</summary>
internal sealed record FindingsRead(List<ReviewFinding> Findings, int Dropped, int WithoutSource);

/// <summary>The outcome of <see cref="ChapterReview.ApplyVersion"/>.</summary>
internal enum VersionOutcome
{
    Applied,

    /// <summary>The finding names no table row, card or section: it can only be read.</summary>
    NotStructured,

    /// <summary>The place the finding names exists in neither the chapter nor the stored version.</summary>
    NotFound
}

/// <summary>The chapter the reviewer wrote (title, icon, sections as JSON text) and its findings, read from an answer.</summary>
internal sealed record ReviewAnswerParts(string ChapterJson, FindingsRead Findings);

/// <summary>The merged chapter after the guard: sections with the "unklar" markers, the findings including the ones the guard added, the counts.</summary>
internal sealed record ReviewMerge(JsonArray Sections, List<ReviewFinding> Findings, ReviewCounts Counts, int PutBack);

/// <summary>
/// The rules around the review of a chapter by a second AI, free of storage and providers so they can be tested. The reviewer gets both chapters in full and
/// returns the merged chapter plus findings (decision C2): the code does not build the merge, it GUARDS it - every item id of A and B must still be there
/// or be named by a "removed" finding, and the "unklar" markers are written by the code from the findings (never trusted from the answer).
/// Entries are identified like this: table rows and puzzles by item id, cards by section + card title, bosses by title.
/// </summary>
internal static class ChapterReview
{
    // Length limits of every text of a finding (the answer is untrusted text that ends up in the database and the UI)
    internal const int MaxFindings = 200;
    internal const int MaxReason = 600;
    internal const int MaxVersionText = 800;
    internal const int MaxName = 240;
    internal const int MaxColumn = 80;
    internal const int MaxItemId = 200;
    internal const int MaxOpenListLines = 40;
    internal const int MaxOpenLine = 300;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    // ---- finding reader ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the "findings" of a review answer. Entries of an unknown kind or without a reason are dropped (counted, so the caller can log it); only
    /// absolute http(s) URLs survive as <c>source</c> (anything else is no source); every text is cut to its limit. A finding of kind corrected or removed
    /// without a source is kept but counted in <c>WithoutSource</c> (the manual way warns about it).
    /// </summary>
    public static FindingsRead ReadFindings(JsonNode? node)
    {
        var list = new List<ReviewFinding>();
        var dropped = 0;
        var withoutSource = 0;
        if (node is not JsonArray array)
        {
            return new FindingsRead(list, 0, 0);
        }
        foreach (var item in array)
        {
            if (list.Count >= MaxFindings)
            {
                dropped++;
                continue;
            }
            if (item is not JsonObject obj)
            {
                dropped++;
                continue;
            }
            var kind = Text(obj["kind"], 20)?.ToLowerInvariant();
            if (kind is null || !ReviewKinds.All.Contains(kind))
            {
                dropped++;
                continue;
            }
            var reason = Text(obj["reason"], MaxReason);
            if (reason is null)
            {
                // Every finding says why; one without a reason cannot be judged by the player
                dropped++;
                continue;
            }
            var target = ReadTarget(obj["target"]);
            var source = GuideSources.SanitizeUrl(Text(obj["source"], 2000));
            // Every kind but "unclear" is a decision that needs a source (the reviewer prompt says so)
            if (source is null && kind != ReviewKinds.Unclear)
            {
                withoutSource++;
            }
            list.Add(new ReviewFinding(kind, target, Text(obj["column"], MaxColumn), Text(obj["a"], MaxVersionText), Text(obj["b"], MaxVersionText),
                Text(obj["result"], MaxVersionText), source, reason));
        }
        return new FindingsRead(list, dropped, withoutSource);
    }

    private static ReviewTarget ReadTarget(JsonNode? node) =>
        node is JsonObject target
            ? new ReviewTarget(Text(target["itemId"], MaxItemId), Text(target["section"], MaxName), Text(target["card"], MaxName))
            : new ReviewTarget();

    /// <summary>A trimmed single string of at most <paramref name="max"/> characters; numbers and booleans are read as text; null for anything else or empty.</summary>
    private static string? Text(JsonNode? node, int max)
    {
        var text = node is JsonValue value ? value.TryGetValue<string>(out var s) ? s : value.ToString() : null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        return text.Length > max ? text[..(max - 1)].TrimEnd() + "…" : text;
    }

    // ---- reading a whole answer -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The chapter and the findings of a review answer already parsed to an object. The chapter sits in "chapter" (a model that put title/icon/sections
    /// straight into the answer is tolerated). Errors are complete sentences; the answer is unusable when there are any.
    /// </summary>
    public static ReviewAnswerParts? ReadAnswer(JsonObject root, List<ManualIssueDto> errors)
    {
        var chapter = root["chapter"] as JsonObject ?? (root["sections"] is JsonArray ? root : null);
        if (chapter is null)
        {
            errors.Add(ManualAnswers.Issue("noChapter", ServerTexts.Review.AnswerNoChapter));
            return null;
        }
        if (chapter["sections"] is not JsonArray { Count: > 0 } sections)
        {
            errors.Add(ManualAnswers.Issue("noSections", ServerTexts.Review.AnswerNoSections));
            return null;
        }
        // A review never drops a chapter: the flag has no meaning here and must not make the chapter code throw
        var copy = new JsonObject();
        foreach (var pair in chapter)
        {
            if (!pair.Key.Equals("notApplicable", StringComparison.OrdinalIgnoreCase) && !pair.Key.Equals("reason", StringComparison.OrdinalIgnoreCase))
            {
                copy[pair.Key] = pair.Value?.DeepClone();
            }
        }
        return new ReviewAnswerParts(copy.ToJsonString(), ReadFindings(root["findings"]));
    }

    // ---- inventory ---------------------------------------------------------------------------------------------------------------------

    /// <summary>One entry of a chapter that can be compared: its key, a readable name, the JSON node that carries it and, for rows and puzzles, the item id.</summary>
    internal sealed record Entry(string Key, string Name, string SectionName, JsonObject Node, string? ItemId);

    internal static string Norm(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>The name a section is addressed by: its title, else the caption of its table (a checklist table has no title of its own).</summary>
    internal static string SectionName(JsonObject section) =>
        Str(section["title"]) ?? Str((section["table"] as JsonObject)?["caption"]) ?? string.Empty;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    internal static string RowName(JsonObject table, JsonObject row)
    {
        var cells = row["cells"] as JsonObject;
        if (cells is not null && table["columns"] is JsonArray columns)
        {
            var header = columns.OfType<JsonObject>().FirstOrDefault(c => c["isRowHeader"] is JsonValue v && v.TryGetValue<bool>(out var flag) && flag)
                         ?? columns.OfType<JsonObject>().FirstOrDefault();
            if (Str(header?["key"]) is { } key && Str(cells[key]) is { } named)
            {
                return named;
            }
        }
        if (cells is not null)
        {
            foreach (var pair in cells)
            {
                if (Str(pair.Value) is { } any)
                {
                    return any;
                }
            }
        }
        return Str(row["itemId"]) ?? string.Empty;
    }

    internal static List<Entry> Entries(JsonArray sections)
    {
        var list = new List<Entry>();
        foreach (var section in sections.OfType<JsonObject>())
        {
            var type = Str(section["type"]);
            var sectionName = SectionName(section);
            switch (type)
            {
                case "checklist_table" when section["table"] is JsonObject table && table["rows"] is JsonArray rows:
                    foreach (var row in rows.OfType<JsonObject>())
                    {
                        if (Str(row["itemId"]) is { } id)
                        {
                            // A row is put back by the caption of its table (the section may carry a different title)
                            list.Add(new Entry("id:" + id, RowName(table, row), Str(table["caption"]) ?? sectionName, row, id));
                        }
                    }
                    break;
                case "puzzle":
                    var puzzleId = Str((section["puzzle"] as JsonObject)?["itemId"]);
                    list.Add(puzzleId is not null
                        ? new Entry("id:" + puzzleId, sectionName, sectionName, section, puzzleId)
                        : new Entry("puzzle:" + Norm(sectionName), sectionName, sectionName, section, null));
                    break;
                case "boss":
                    // A boss keeps a stable bossId across answers; without one the title identifies it
                    var bossId = Norm(Str(section["bossId"]));
                    list.Add(new Entry("boss:" + (bossId.Length > 0 ? bossId : Norm(sectionName)), sectionName, sectionName, section, null));
                    break;
                case "cards_grid" when section["cards"] is JsonArray cards:
                    foreach (var card in cards.OfType<JsonObject>())
                    {
                        if (Str(card["title"]) is { } cardTitle)
                        {
                            // By title alone: a card grid often has no title of its own, and a reviewer names it differently than the answers do
                            list.Add(new Entry("card:" + Norm(cardTitle), cardTitle, sectionName, card, null));
                        }
                    }
                    break;
            }
        }
        return list;
    }

    /// <summary>Every item id (rows and puzzles) of a chapter.</summary>
    internal static HashSet<string> ItemIds(JsonArray sections) =>
        Entries(sections).Where(e => e.ItemId is not null).Select(e => e.ItemId!).ToHashSet(StringComparer.Ordinal);

    /// <summary>The entry keys a finding refers to (a finding naming only a section may mean a boss, a puzzle or the whole section).</summary>
    private static IEnumerable<string> KeysOf(ReviewFinding finding)
    {
        var target = finding.Target;
        if (target.ItemId is { } id)
        {
            yield return "id:" + id;
        }
        else if (target.Card is { } card)
        {
            yield return "card:" + Norm(card);
        }
        else if (target.Section is { } only)
        {
            yield return "boss:" + Norm(only);
            yield return "puzzle:" + Norm(only);
        }
    }

    /// <summary>A short text that says what an entry is (the cells of a row, the title and place of a puzzle ...), for the a/b of a finding the code writes itself.</summary>
    private static string Describe(Entry entry)
    {
        string text;
        if (entry.Node["cells"] is JsonObject cells)
        {
            text = string.Join(" · ", cells.Select(p => Str(p.Value)).Where(v => v is not null));
        }
        else if (entry.Node["puzzle"] is JsonObject puzzle)
        {
            text = string.Join(" · ", new[] { entry.Name, Str(puzzle["location"]) }.Where(v => !string.IsNullOrEmpty(v)));
        }
        else
        {
            text = entry.Name;
        }
        return Clip(string.IsNullOrWhiteSpace(text) ? entry.Name : text, MaxVersionText) ?? entry.Name;
    }

    private static string? Clip(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        return trimmed.Length > max ? trimmed[..(max - 1)].TrimEnd() + "…" : trimmed;
    }

    // ---- the guard (decision C2) ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes sure the merged chapter lost nothing silently. Every item id of A and B that the result no longer has - and that no "removed" finding names -
    /// is put back (A's version first, else B's) and gets an "unclear" finding "the reviewer left the entry out without a reason". Changes
    /// <paramref name="result"/>; returns the findings including the ones added here.
    /// </summary>
    public static (List<ReviewFinding> Findings, int PutBack) Guard(JsonArray result, JsonArray a, JsonArray b, IReadOnlyList<ReviewFinding> findings, string language)
    {
        var present = ItemIds(result);
        var named = findings.Where(f => f.Kind == ReviewKinds.Removed && f.Target.ItemId is not null).Select(f => f.Target.ItemId!).ToHashSet(StringComparer.Ordinal);
        var all = new List<ReviewFinding>(findings);
        var entriesA = Entries(a);
        var entriesB = Entries(b);
        var putBack = 0;

        foreach (var source in new[] { entriesA, entriesB })
        {
            foreach (var entry in source.Where(e => e.ItemId is not null))
            {
                var id = entry.ItemId!;
                if (present.Contains(id) || named.Contains(id))
                {
                    continue;
                }
                PutBackEntry(result, entry);
                present.Add(id);
                putBack++;
                var inA = entriesA.FirstOrDefault(e => e.ItemId == id);
                var inB = entriesB.FirstOrDefault(e => e.ItemId == id);
                all.Add(new ReviewFinding(
                    ReviewKinds.Unclear,
                    new ReviewTarget(ItemId: id),
                    null,
                    inA is null ? null : Describe(inA),
                    inB is null ? null : Describe(inB),
                    null,
                    null,
                    ServerTexts.Review.DroppedWithoutReason.Resolve(language)));
            }
        }
        return (all, putBack);
    }

    /// <summary>Puts a row back into the table of the result that has the same caption, or - when there is none - into a section of its own before the sources.</summary>
    private static void PutBackEntry(JsonArray result, Entry entry)
    {
        if (entry.Node["cells"] is not null)
        {
            var table = FindTable(result, OwnTable(entry.Node), entry.SectionName);
            if (table is not null)
            {
                var row = (JsonObject)entry.Node.DeepClone();
                RemapRow(row, OwnTable(entry.Node), table);
                ((JsonArray)table["rows"]!).Add(row);
                return;
            }
            // The table is gone: a copy of its section with only this row (a later row of the same table finds it by caption)
            var parent = ParentSection(entry);
            if (parent is not null)
            {
                var copy = (JsonObject)parent.DeepClone();
                copy["table"]!["rows"] = new JsonArray(entry.Node.DeepClone());
                InsertBeforeSources(result, copy);
            }
            return;
        }
        InsertBeforeSources(result, entry.Node.DeepClone());
    }

    /// <summary>
    /// The table of a chapter that corresponds to a table of another answer: the same tableId, else the same caption (two independent answers name their
    /// tables differently, the reviewer's chapter may follow either).
    /// </summary>
    private static JsonObject? FindTable(JsonArray sections, JsonObject? like, string? caption)
    {
        var tables = sections.OfType<JsonObject>().Where(s => s["table"] is JsonObject t && t["rows"] is JsonArray).Select(s => (JsonObject)s["table"]!).ToList();
        var id = Norm(Str(like?["tableId"]));
        var wanted = Norm(caption);
        return (id.Length > 0 ? tables.FirstOrDefault(t => Norm(Str(t["tableId"])) == id) : null)
               ?? tables.FirstOrDefault(t => Norm(Str(t["caption"])) == wanted);
    }

    // The section a row belongs to is found again through its table (the entry only keeps the row node)
    private static JsonObject? ParentSection(Entry entry) => entry.Node.Parent?.Parent?.Parent as JsonObject;

    private static void InsertBeforeSources(JsonArray sections, JsonNode section)
    {
        var index = -1;
        for (var i = sections.Count - 1; i >= 0; i--)
        {
            if (sections[i] is JsonObject o && Str(o["type"]) == "sources")
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
        {
            sections.Insert(index, section);
        }
        else
        {
            sections.Add(section);
        }
    }

    // ---- counts ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The summary: "einig" = entries that BOTH answers have and no finding is about (counted by the code, never by the reviewer); added / corrected /
    /// removed / unclear = the number of findings of that kind (including the ones the guard added).
    /// </summary>
    public static ReviewCounts Count(JsonArray a, JsonArray b, IReadOnlyList<ReviewFinding> findings, int withoutSource = 0)
    {
        var shared = Entries(a).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        shared.IntersectWith(Entries(b).Select(e => e.Key));
        var flagged = findings.SelectMany(KeysOf).ToHashSet(StringComparer.Ordinal);
        // A finding that names a boss/puzzle by its title flags the entry whatever key it has (bosses are keyed by bossId)
        foreach (var finding in findings.Where(f => f.Target.ItemId is null && f.Target.Card is null && f.Target.Section is not null))
        {
            var wanted = Norm(finding.Target.Section);
            flagged.UnionWith(Entries(a).Concat(Entries(b)).Where(e => Norm(e.Name) == wanted || Norm(e.SectionName) == wanted).Select(e => e.Key));
        }
        return new ReviewCounts(
            shared.Count(k => !flagged.Contains(k)),
            findings.Count(f => f.Kind == ReviewKinds.Added),
            findings.Count(f => f.Kind == ReviewKinds.Corrected),
            findings.Count(f => f.Kind == ReviewKinds.Removed),
            findings.Count(f => f.Kind == ReviewKinds.Unclear),
            withoutSource);
    }

    // ---- the "unklar" markers (owner decision O4) -------------------------------------------------------------------------------------

    /// <summary>
    /// Removes the review markers of a chapter: the box "review_unclear" at the top and every <c>unclear</c> object of a section, card or row. Returns
    /// true when something was removed. Used on A before it goes to a second review (a marker is no part of the facts) and before markers are written again.
    /// </summary>
    public static bool StripMarkers(JsonArray sections)
    {
        var changed = false;
        for (var i = sections.Count - 1; i >= 0; i--)
        {
            if (sections[i] is JsonObject box && Str(box["type"]) == "alert" && Str(box["kind"]) == GuideAlertKinds.ReviewUnclear)
            {
                sections.RemoveAt(i);
                changed = true;
            }
        }
        foreach (var section in sections.OfType<JsonObject>())
        {
            changed |= section.Remove("unclear");
            if (section["cards"] is JsonArray cards)
            {
                foreach (var card in cards.OfType<JsonObject>())
                {
                    changed |= card.Remove("unclear");
                }
            }
            if (section["table"]?["rows"] is JsonArray rows)
            {
                foreach (var row in rows.OfType<JsonObject>())
                {
                    changed |= row.Remove("unclear");
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// Writes the markers of every OPEN "unclear" finding (not yet decided by the player): an <c>unclear</c> object { a, b, reason } on the entry it names
    /// and ONE calm box of kind <c>review_unclear</c> at the top that lists every open point. Existing markers are removed first, so the result always
    /// matches the findings. Also the entry names in the list are in the guide language.
    /// </summary>
    public static void ApplyMarkers(JsonArray sections, IReadOnlyList<ReviewFinding> findings, string language)
    {
        StripMarkers(sections);
        var open = findings.Where(f => f.Kind == ReviewKinds.Unclear && f.Applied is null).ToList();
        if (open.Count == 0)
        {
            return;
        }

        var items = new JsonArray();
        foreach (var finding in open)
        {
            var holder = FindHolder(sections, finding.Target);
            var name = NameOf(sections, finding.Target, holder);
            if (holder is not null)
            {
                var marker = new JsonObject();
                if (Clip(finding.A, MaxVersionText) is { } a) marker["a"] = a;
                if (Clip(finding.B, MaxVersionText) is { } b) marker["b"] = b;
                marker["reason"] = Clip(finding.Reason, MaxReason) ?? string.Empty;
                holder["unclear"] = marker;
            }
            if (items.Count < MaxOpenListLines)
            {
                var line = string.IsNullOrEmpty(name) ? finding.Reason : name + ": " + finding.Reason;
                items.Add(Clip(line, MaxOpenLine));
            }
        }
        var box = new JsonObject
        {
            ["type"] = "alert",
            ["kind"] = GuideAlertKinds.ReviewUnclear,
            ["variant"] = GuideAlertKinds.DefaultVariant(GuideAlertKinds.ReviewUnclear),
            ["title"] = ServerTexts.Review.UnclearTitle.Resolve(language),
            ["htmlContent"] = ServerTexts.Review.UnclearIntro(open.Count).Resolve(language),
            ["items"] = items
        };
        sections.Insert(0, box);
    }

    private static string NameOf(JsonArray sections, ReviewTarget target, JsonObject? holder)
    {
        if (target.ItemId is { } id)
        {
            var entry = Entries(sections).FirstOrDefault(e => e.ItemId == id);
            return entry?.Name ?? id;
        }
        if (target.Card is { } card)
        {
            return card;
        }
        return target.Section ?? (holder is null ? string.Empty : SectionName(holder));
    }

    // ---- finding lookup in a chapter ------------------------------------------------------------------------------------------------

    /// <summary>The JSON object a finding points at in a chapter: the row or puzzle of an item id, the card of a section, or a section; null when it is not there.</summary>
    internal static JsonObject? FindHolder(JsonArray sections, ReviewTarget target)
    {
        if (target.ItemId is { } id)
        {
            return Entries(sections).FirstOrDefault(e => e.ItemId == id)?.Node;
        }
        if (target.Card is { } cardTitle)
        {
            // The named section first; a card grid without a title (or one the reviewer named differently) is found by the card's own title
            var wanted = Norm(cardTitle);
            var named = target.Section is { } sectionName ? FindSection(sections, sectionName) : null;
            var scan = named is not null ? new[] { named } : sections.OfType<JsonObject>().ToArray();
            var found = scan.SelectMany(s => (s["cards"] as JsonArray ?? []).OfType<JsonObject>()).FirstOrDefault(c => Norm(Str(c["title"])) == wanted);
            return found ?? sections.OfType<JsonObject>().SelectMany(s => (s["cards"] as JsonArray ?? []).OfType<JsonObject>()).FirstOrDefault(c => Norm(Str(c["title"])) == wanted);
        }
        if (target.Section is { } onlySection)
        {
            // A boss is found by its title, else by its bossId (the reviewer may name either)
            return FindSection(sections, onlySection)
                   ?? sections.OfType<JsonObject>().FirstOrDefault(s => Str(s["type"]) == "boss" && Norm(Str(s["bossId"])) == Norm(onlySection));
        }
        return null;
    }

    private static JsonObject? FindSection(JsonArray sections, string name)
    {
        var wanted = Norm(name);
        return sections.OfType<JsonObject>().FirstOrDefault(s => Norm(SectionName(s)) == wanted);
    }

    // ---- taking another version (decision C6) -----------------------------------------------------------------------------------------

    /// <summary>
    /// Takes the stored version of one finding's place: the entry of <paramref name="source"/> (the sections of A or B as they were) replaces the entry in
    /// <paramref name="current"/>. Where the entry only exists on one side that is what happens: the source lacks it = it is removed from the chapter,
    /// the chapter lacks it = it is put back. With a <c>column</c> only that cell changes. Free-text findings (no table row, card or section) cannot be taken.
    /// </summary>
    public static VersionOutcome ApplyVersion(JsonArray current, ReviewFinding finding, JsonArray source)
    {
        var target = finding.Target;
        if (!target.IsStructured)
        {
            return VersionOutcome.NotStructured;
        }

        var from = FindHolder(source, target);
        var into = FindHolder(current, target);
        if (from is null && into is null)
        {
            return VersionOutcome.NotFound;
        }

        // One cell of a row: both rows exist and the column is known
        if (finding.Column is { } column && from is not null && into is not null && target.ItemId is not null
            && TryCell(from, into, column) is { } cell)
        {
            into["cells"]![cell.Key] = cell.Value;
            return VersionOutcome.Applied;
        }

        if (from is null)
        {
            return Remove(current, into!) ? VersionOutcome.Applied : VersionOutcome.NotFound;
        }
        if (into is null)
        {
            return Insert(current, from, target) ? VersionOutcome.Applied : VersionOutcome.NotFound;
        }
        return Replace(into, from) ? VersionOutcome.Applied : VersionOutcome.NotFound;
    }

    /// <summary>
    /// One cell: the finding names the column by its LABEL, and the two answers may use different column KEYS for it - so the key is looked up in each
    /// row's own table.
    /// </summary>
    private static (string Key, JsonNode Value)? TryCell(JsonObject from, JsonObject into, string column)
    {
        var intoKey = OwnTable(into) is { } intoTable ? ColumnKey(intoTable, column) : null;
        var fromKey = OwnTable(from) is { } fromTable ? ColumnKey(fromTable, column) : null;
        if (intoKey is null || fromKey is null || from["cells"] is not JsonObject fromCells || into["cells"] is not JsonObject)
        {
            return null;
        }
        return (intoKey, JsonValue.Create(Str(fromCells[fromKey]) ?? string.Empty)!);
    }

    /// <summary>The table object a row sits in (row -> "rows" array -> table).</summary>
    private static JsonObject? OwnTable(JsonObject row) => row.Parent?.Parent as JsonObject;

    /// <summary>
    /// Rewrites the cells of a row of one table so they fit another table: cells are matched by the column LABEL (the keys differ between independent
    /// answers); a cell whose column the target table does not have is dropped.
    /// </summary>
    private static void RemapRow(JsonObject row, JsonObject? fromTable, JsonObject? toTable)
    {
        if (fromTable is null || toTable is null || row["cells"] is not JsonObject cells)
        {
            return;
        }
        var labelOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in (fromTable["columns"] as JsonArray ?? []).OfType<JsonObject>().Where(c => Str(c["key"]) is not null))
        {
            labelOf[Str(column["key"])!] = Norm(Str(column["label"]) ?? Str(column["key"]));
        }
        var keyOfLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in (toTable["columns"] as JsonArray ?? []).OfType<JsonObject>().Where(c => Str(c["key"]) is not null))
        {
            targetKeys.Add(Str(column["key"])!);
            keyOfLabel.TryAdd(Norm(Str(column["label"]) ?? Str(column["key"])), Str(column["key"])!);
        }
        var remapped = new JsonObject();
        foreach (var (key, value) in cells)
        {
            string? targetKey = null;
            if (labelOf.TryGetValue(key, out var label) && keyOfLabel.TryGetValue(label, out var byLabel))
            {
                targetKey = byLabel;
            }
            else if (targetKeys.Contains(key))
            {
                targetKey = key;
            }
            if (targetKey is not null && !remapped.ContainsKey(targetKey))
            {
                remapped[targetKey] = value?.DeepClone();
            }
        }
        row["cells"] = remapped;
    }

    private static string? ColumnKey(JsonObject table, string column)
    {
        var wanted = Norm(column);
        foreach (var col in (table["columns"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var key = Str(col["key"]);
            if (key is not null && (Norm(key) == wanted || Norm(Str(col["label"])) == wanted))
            {
                return key;
            }
        }
        return null;
    }

    private static bool Remove(JsonArray current, JsonObject node)
    {
        if (node.Parent is JsonArray parent && parent.Contains(node))
        {
            parent.Remove(node);
            return true;
        }
        return false;
    }

    private static bool Insert(JsonArray current, JsonObject from, ReviewTarget target)
    {
        var copy = from.DeepClone();
        // A row: into the table with the same caption, its cells fitted to that table's columns
        if (target.ItemId is not null && from["cells"] is not null)
        {
            var sourceTable = OwnTable(from);
            var table = FindTable(current, sourceTable, Str(sourceTable?["caption"]));
            if (table is null)
            {
                return false;
            }
            RemapRow((JsonObject)copy, sourceTable, table);
            ((JsonArray)table["rows"]!).Add(copy);
            return true;
        }
        // A card: into the section with the same name
        if (target.Card is not null)
        {
            var section = (target.Section is { } sectionName ? FindSection(current, sectionName) : null)
                          ?? current.OfType<JsonObject>().FirstOrDefault(s => s["cards"] is JsonArray);
            if (section?["cards"] is JsonArray cards)
            {
                cards.Add(copy);
                return true;
            }
            return false;
        }
        // A puzzle or another whole section
        InsertBeforeSources(current, copy);
        return true;
    }

    private static bool Replace(JsonObject into, JsonObject from)
    {
        if (into.Parent is not JsonArray parent)
        {
            return false;
        }
        var copy = (JsonObject)from.DeepClone();
        // A row of the other answer: its cells fitted to this table's columns (matched by label)
        if (from["cells"] is not null)
        {
            RemapRow(copy, OwnTable(from), OwnTable(into));
        }
        // The links to other chapters belong to the chapter, not to a version of the entry
        if (copy["ref"] is null && into["ref"] is JsonNode keep)
        {
            copy["ref"] = keep.DeepClone();
        }
        var index = parent.IndexOf(into);
        if (index < 0)
        {
            return false;
        }
        parent[index] = copy;
        return true;
    }

    // ---- hash and stale state (decision C5) -------------------------------------------------------------------------------------------

    /// <summary>
    /// The hash a review remembers of the chapter it produced: SHA-256 of the sections WITHOUT the cross-chapter links ("ref" of rows and side quest
    /// entries) - the link maintenance changes them whenever ANOTHER chapter changes, which says nothing about this chapter's content.
    /// </summary>
    public static string HashSections(string? sectionsJson)
    {
        var text = sectionsJson ?? string.Empty;
        try
        {
            if (JsonNode.Parse(text) is JsonArray sections)
            {
                foreach (var holder in sections.OfType<JsonObject>().SelectMany(s =>
                             (s["table"]?["rows"] as JsonArray ?? []).OfType<JsonObject>().Concat((s["quests"] as JsonArray ?? []).OfType<JsonObject>())))
                {
                    holder.Remove("ref");
                }
                text = sections.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Not JSON: the raw text is hashed
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The stored review no longer describes the chapter (it was changed after the review): still readable, but other versions cannot be taken.</summary>
    public static bool IsStale(string storedResultHash, string? chapterSectionsJson) =>
        !string.Equals(storedResultHash, HashSections(chapterSectionsJson), StringComparison.Ordinal);

    // ---- building the merge -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Everything after the reviewer's answer was read and the chapter built: guard (C2), counts, markers. <paramref name="mergedSections"/> is changed
    /// in place; A and B are the sections as they were (without markers).
    /// </summary>
    public static ReviewMerge Merge(JsonArray mergedSections, JsonArray a, JsonArray b, FindingsRead read, string language)
    {
        // The markers are written by the code from the findings, never taken from the answer
        StripMarkers(mergedSections);
        RestoreSourceOrigins(mergedSections, a, b);
        var (findings, putBack) = Guard(mergedSections, a, b, read.Findings, language);
        var counts = Count(a, b, findings, read.WithoutSource);
        ApplyMarkers(mergedSections, findings, language);
        return new ReviewMerge(mergedSections, findings, counts, putBack);
    }

    /// <summary>
    /// The merged chapter went through the normalizer without trust, so every source of it is origin "ai". A source that A or B already carried as "cited" or
    /// "search" (the provider's own citations, kept at the time) gets that origin back, matched by URL - the reviewer's rewriting of the list must not
    /// downgrade it.
    /// </summary>
    public static void RestoreSourceOrigins(JsonArray merged, JsonArray a, JsonArray b)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in new[] { a, b })
        {
            foreach (var entry in Sources(answer))
            {
                var origin = Str(entry["origin"]);
                var url = Str(entry["url"]);
                if (url is null || origin is not ("cited" or "search"))
                {
                    continue;
                }
                // "cited" wins over "search" when both answers have the page
                if (!known.TryGetValue(url, out var seen) || (seen == "search" && origin == "cited"))
                {
                    known[url] = origin;
                }
            }
        }
        foreach (var entry in Sources(merged))
        {
            var url = Str(entry["url"]);
            var origin = Str(entry["origin"]);
            if (url is not null && (origin is null or "ai") && known.TryGetValue(url, out var restored))
            {
                entry["origin"] = restored;
            }
        }
    }

    private static IEnumerable<JsonObject> Sources(JsonArray sections) =>
        sections.OfType<JsonObject>().Where(s => Str(s["type"]) == "sources").SelectMany(s => (s["sources"] as JsonArray ?? []).OfType<JsonObject>());

    /// <summary>The chapter as the review prompt takes it: <c>{ "title", "icon", "sections" }</c> as compact JSON text, without review markers.</summary>
    public static string ChapterJson(string title, string icon, JsonArray sections)
    {
        var clean = (JsonArray)sections.DeepClone();
        StripMarkers(clean);
        return new JsonObject { ["title"] = title, ["icon"] = icon, ["sections"] = clean }.ToJsonString();
    }

    /// <summary>The sections of a stored chapter as a node, without review markers (answer A of a review).</summary>
    public static JsonArray CleanSections(string? sectionsJson)
    {
        var sections = ParseSections(sectionsJson);
        StripMarkers(sections);
        return sections;
    }

    public static JsonArray ParseSections(string? json)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json) as JsonArray ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializeSections(JsonArray sections) => sections.ToJsonString();

    public static string SerializeFindings(IReadOnlyList<ReviewFinding> findings) => JsonSerializer.Serialize(findings, Json);

    public static List<ReviewFinding> DeserializeFindings(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ReviewFinding>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializeCounts(ReviewCounts counts) => JsonSerializer.Serialize(counts, Json);

    public static ReviewCounts DeserializeCounts(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<ReviewCounts>(string.IsNullOrWhiteSpace(json) ? "{}" : json, Json) ?? new ReviewCounts(0, 0, 0, 0, 0);
        }
        catch (JsonException)
        {
            return new ReviewCounts(0, 0, 0, 0, 0);
        }
    }
}
