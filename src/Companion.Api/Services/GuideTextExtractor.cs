using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>One human-readable string inside a guide chapter, with the place it sits in (so a translation can be written back).</summary>
internal sealed class TextSlot
{
    private readonly JsonObject? _object;
    private readonly string? _key;
    private readonly JsonArray? _array;
    private readonly int _index;

    public TextSlot(JsonObject container, string key, string text)
    {
        _object = container;
        _key = key;
        Text = text;
    }

    public TextSlot(JsonArray container, int index, string text)
    {
        _array = container;
        _index = index;
        Text = text;
    }

    public string Text { get; }

    public void Set(string value)
    {
        if (_object is not null)
        {
            _object[_key!] = value;
        }
        else
        {
            _array![_index] = value;
        }
    }
}

/// <summary>
/// Finds the texts of a guide chapter that a person reads, and nothing else. Translating works on this list - never on the JSON
/// itself - so the model cannot change a structure, a key, an itemId, a number or a URL: it only ever sees and returns plain
/// sentences by number, and the app writes them back into a copy of the original. Fields that are NOT text: type, variant, icon,
/// columns (number), badgeVariant, tableId, checkable, spoiler, isRowHeader, itemId, column keys, cell keys, kind (alert), puzzle.missable/documented,
/// level (heading), apiName (alert), bossId, the HP value, every "source", achievements (API names), counts[].count, the "sources" list (titles and URLs).
/// </summary>
internal static class GuideTextExtractor
{
    /// <summary>All translatable strings of a chapter's sections, in document order (a string that occurs twice appears twice).</summary>
    public static List<TextSlot> Collect(JsonArray sections)
    {
        var slots = new List<TextSlot>();
        foreach (var section in sections.OfType<JsonObject>())
        {
            AddString(slots, section, "title");
            AddString(slots, section, "htmlContent");
            AddStringArray(slots, section, "paragraphs");

            if (Child(section, "cards") is JsonArray cards)
            {
                foreach (var card in cards.OfType<JsonObject>())
                {
                    AddString(slots, card, "title");
                    AddString(slots, card, "badgeText");
                    AddString(slots, card, "introText");
                    if (Child(card, "bullets") is JsonArray bullets)
                    {
                        foreach (var bullet in bullets.OfType<JsonObject>())
                        {
                            AddString(slots, bullet, "title");
                            AddString(slots, bullet, "body");
                        }
                    }
                }
            }

            // Puzzle: where it is, what it needs, the hints and the solution steps are texts; itemId, missable, documented and the source (names of sites) are not
            if (Child(section, "puzzle") is JsonObject puzzle)
            {
                AddString(slots, puzzle, "location");
                AddString(slots, puzzle, "requirements");
                AddStringArray(slots, puzzle, "hints");
                AddStringArray(slots, puzzle, "steps");
            }

            // Alert "items" (what to do before a point of no return); "apiName" is an id, never text
            AddStringArray(slots, section, "items");

            // Boss card and overview: the readable fields. Not text: bossId, achievements (API names), the HP value and every "source" (names of
            // sites), the overview counts' numbers, flags
            AddString(slots, section, "location");
            AddString(slots, section, "recommendedLevel");
            AddString(slots, section, "party");
            foreach (var key in new[] { "weaknesses", "resistances", "immunities", "phases", "tactics", "rewards", "keyItems", "missables" })
            {
                AddStringArray(slots, section, key);
            }
            if (Child(section, "hp") is JsonArray hp)
            {
                foreach (var entry in hp.OfType<JsonObject>())
                {
                    AddString(slots, entry, "appliesTo");
                }
            }
            if (Child(section, "attacks") is JsonArray attacks)
            {
                foreach (var attack in attacks.OfType<JsonObject>())
                {
                    AddString(slots, attack, "name");
                    AddString(slots, attack, "note");
                }
            }
            if (Child(section, "quests") is JsonArray quests)
            {
                // "ref" is an id, never text
                foreach (var quest in quests.OfType<JsonObject>())
                {
                    AddString(slots, quest, "title");
                    AddString(slots, quest, "availableFrom");
                    AddString(slots, quest, "missable");
                    AddString(slots, quest, "reward");
                }
            }
            if (Child(section, "counts") is JsonArray counts)
            {
                foreach (var count in counts.OfType<JsonObject>())
                {
                    AddString(slots, count, "label");
                }
            }

            if (Child(section, "table") is JsonObject table)
            {
                AddString(slots, table, "caption");
                AddString(slots, table, "searchPlaceholder");
                AddString(slots, table, "categoryFilterLabel");
                AddString(slots, table, "secondaryFilterLabel");
                AddStringArray(slots, table, "categories");
                AddStringArray(slots, table, "secondaryCategories");

                if (Child(table, "columns") is JsonArray columns)
                {
                    foreach (var column in columns.OfType<JsonObject>())
                    {
                        AddString(slots, column, "label");
                    }
                }

                if (Child(table, "rows") is JsonArray rows)
                {
                    foreach (var row in rows.OfType<JsonObject>())
                    {
                        // The category of a row must stay equal to an entry of "categories": identical texts get ONE translation
                        AddString(slots, row, "filterCategory");
                        AddString(slots, row, "filterSecondary");
                        if (Child(row, "cells") is JsonObject cells)
                        {
                            foreach (var key in cells.Select(p => p.Key).ToList())
                            {
                                AddString(slots, cells, key);
                            }
                        }
                    }
                }
            }
        }
        return slots;
    }

    /// <summary>
    /// True when a string is something to translate: it contains letters and is not a link. Numbers, percentages, "—" and the like are
    /// left alone (and never sent to the AI).
    /// </summary>
    public static bool NeedsTranslation(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || !trimmed.Any(char.IsLetter))
        {
            return false;
        }
        return !(trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ||
               trimmed.Contains(' ');
    }

    /// <summary>The distinct strings that have to be translated, in order of first appearance.</summary>
    public static List<string> DistinctTexts(IEnumerable<TextSlot> slots) =>
        slots.Select(s => s.Text).Where(NeedsTranslation).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Writes the translations into the slots (every slot whose text has an entry); returns how many slots changed.</summary>
    public static int Apply(IEnumerable<TextSlot> slots, IReadOnlyDictionary<string, string> translations)
    {
        var changed = 0;
        foreach (var slot in slots)
        {
            if (translations.TryGetValue(slot.Text, out var translated))
            {
                slot.Set(translated);
                changed++;
            }
        }
        return changed;
    }

    /// <summary>The itemIds of all checklist rows and tickable puzzles, in document order.</summary>
    public static List<string> ItemIds(JsonArray sections)
    {
        var ids = new List<string>();
        foreach (var section in sections.OfType<JsonObject>())
        {
            if (Child(section, "table") is JsonObject table && Child(table, "rows") is JsonArray rows)
            {
                ids.AddRange(rows.OfType<JsonObject>().Select(r => Child(r, "itemId")?.ToString() ?? string.Empty));
            }
            if (Child(section, "puzzle") is JsonObject puzzle && Child(puzzle, "itemId")?.ToString() is { Length: > 0 } puzzleId)
            {
                ids.Add(puzzleId);
            }
        }
        return ids;
    }

    /// <summary>
    /// The chapter with every translatable text blanked: two chapters with the same skeleton are the same structure (same sections,
    /// cards, rows, ids, keys, numbers) whatever language their texts are in.
    /// </summary>
    public static string Skeleton(JsonArray sections)
    {
        var copy = (JsonArray)sections.DeepClone();
        // Only what is translated is blanked: numbers, links and symbols must be identical in both
        foreach (var slot in Collect(copy).Where(s => NeedsTranslation(s.Text)))
        {
            slot.Set(string.Empty);
        }
        return copy.ToJsonString();
    }

    /// <summary>
    /// Re-checks a translated chapter: it must survive the same normalization as AI output and have exactly the skeleton and the
    /// itemIds of the original. Throws <see cref="LocalizedException"/> otherwise (the translation is then discarded).
    /// </summary>
    public static JsonArray ValidateTranslated(JsonArray original, JsonArray translated, string chapterTitle)
    {
        JsonArray normalized;
        using (var doc = JsonDocument.Parse(translated.ToJsonString()))
        {
            normalized = GuideSectionNormalizer.Normalize(doc.RootElement, NormalizeOptions.Trusted);
        }

        // The normalizer drops typed fields that belong to another section type; a chapter stored with such a stray field compares without it
        var reference = (JsonArray)original.DeepClone();
        foreach (var section in reference.OfType<JsonObject>())
        {
            if (section["type"] is JsonValue type && type.TryGetValue<string>(out var typeName))
            {
                GuideSectionReader.StripForeign(section, typeName);
            }
        }
        if (!ItemIds(reference).SequenceEqual(ItemIds(normalized), StringComparer.Ordinal) ||
            !string.Equals(Skeleton(reference), Skeleton(normalized), StringComparison.Ordinal))
        {
            throw new LocalizedException(ServerTexts.Translation.StructureChanged(chapterTitle));
        }
        return normalized;
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------

    private static JsonNode? Child(JsonObject obj, string name)
    {
        foreach (var property in obj)
        {
            if (property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }
        return null;
    }

    private static string? KeyOf(JsonObject obj, string name) =>
        obj.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void AddString(List<TextSlot> slots, JsonObject obj, string name)
    {
        var key = KeyOf(obj, name);
        if (key is not null && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            slots.Add(new TextSlot(obj, key, text));
        }
    }

    private static void AddStringArray(List<TextSlot> slots, JsonObject obj, string name)
    {
        if (Child(obj, name) is not JsonArray array)
        {
            return;
        }
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                slots.Add(new TextSlot(array, i, text));
            }
        }
    }
}
