using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// The second line of defence against chapters about things a game does not have (a bestiary in a factory simulation): the blueprint only plans a
/// topic the research confirmed, and the chapter prompt may still answer "this topic does not exist in this game" in one fixed, machine-readable form
/// - <c>{ "notApplicable": true, "reason": "..." }</c>. Such a chapter is dropped (no tab, no placeholder, a log line with the reason) instead of being
/// saved as filler. This holds for every path that writes a chapter from a chapter answer: a new guide, "complete guide", "research again", "add
/// chapter" and the manual subscription route.
/// The chapters every guide has (<see cref="Never"/>: walkthrough, roadmap, tips) are never dropped: their prompts do not offer the answer, and
/// an answer that gives it anyway is treated as unusable, not as "drop".
/// </summary>
public static class ChapterApplicability
{
    public const string Field = "notApplicable";

    // The walkthrough has its own flow (outline with "reliable": false) and never goes away because of a flag; roadmap and tips fit every game
    private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase) { WalkthroughTab.TopicId, "roadmap", "tips" };

    /// <summary>May a chapter of this topic be dropped when the research says the game does not have it? ("extra" and custom chapters may.)</summary>
    public static bool CanBeDropped(string? topicId) => !Never.Contains(topicId ?? string.Empty);

    /// <summary>
    /// True when the answer's TOP-LEVEL "notApplicable" is the boolean true (a string "true", a nested field or a missing field never count - a parser
    /// quirk must not delete a chapter); <paramref name="reason"/> is the short reason (may be empty).
    /// </summary>
    public static bool Says(JsonElement root, out string reason)
    {
        reason = string.Empty;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(Field, out var flag) || flag.ValueKind != JsonValueKind.True)
        {
            return false;
        }
        reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? (r.GetString() ?? string.Empty).Trim() : string.Empty;
        return true;
    }

    /// <summary>The same for an answer read as a node (manual route).</summary>
    public static bool Says(JsonObject root, out string reason)
    {
        reason = string.Empty;
        if (root[Field] is not JsonValue flag || flag.GetValueKind() != JsonValueKind.True)
        {
            return false;
        }
        reason = root["reason"] is JsonValue r && r.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;
        return true;
    }

    /// <summary>The paragraph of a chapter prompt that allows the "does not exist" answer (only for topics that may be dropped).</summary>
    public static string PromptRule(string gameTitle, string language) => language == Lang.English
        ? $$"""
          If your research shows that the topic of this chapter does NOT exist in "{{gameTitle}}" (e.g. enemies/bosses in a building or factory game without combat)
          or only so marginally that a chapter of its own is pointless: do NOT write the chapter, do not explain at length that it is missing and do not fill it with
          material from other topics. Answer instead with exactly this JSON object and nothing else:
          { "notApplicable": true, "reason": "One short sentence why (what the game has instead)" }
          """
        : $$"""
          Zeigt deine Recherche, dass es das Thema dieses Kapitels in „{{gameTitle}}“ NICHT gibt (z. B. Gegner/Bosse in einem Aufbau- oder Fabrikspiel ohne Kampf)
          oder nur so am Rande, dass ein eigenes Kapitel sinnlos ist: Schreibe das Kapitel NICHT, erkläre nicht lang, dass es fehlt, und fülle es nicht mit Stoff aus
          anderen Themen auf. Antworte stattdessen genau mit diesem JSON-Objekt und sonst nichts:
          { "notApplicable": true, "reason": "Ein kurzer Satz, warum (was das Spiel stattdessen hat)" }
          """;
}

/// <summary>The research says the planned chapter's topic does not exist in the game: the chapter is dropped, not saved.</summary>
public sealed class ChapterNotApplicableException(string title, string? topicId, string reason)
    : Exception($"Chapter '{title}' does not apply to this game: {reason}")
{
    public string Title { get; } = title;

    public string? TopicId { get; } = topicId;

    public string Reason { get; } = reason;
}
