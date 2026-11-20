using System.Text.Json;
using Companion.Api.Services;

namespace Companion.Api.Endpoints;

// The contract of the AI review of guide chapters ("Prüf-KI"). Routes: ChapterReviewEndpoints.

/// <summary>
/// Body of <c>POST /api/games/{slug}/chapters/{tabId}/review</c>. Reviewer = who decides ("gemini" | "claude"; needs web search). Exactly one of
/// Researcher (an API provider researches the second answer) and SecondAnswer (the text pasted from the player's own chat, the answer to the
/// "research chapter again" prompt with <c>tabId</c> + <c>replace</c>) is given.
/// </summary>
public sealed record ChapterReviewRequest(string? Researcher, string? Reviewer, string? SecondAnswer);

/// <summary>Body of <c>POST .../review/second</c>: which API provider researches the second answer (the review itself is done by the player's chat).</summary>
public sealed record ChapterReviewSecondRequest(string? Researcher);

/// <summary>Body of <c>POST .../review/manual-prompt</c>: the second answer pasted from a chat.</summary>
public sealed record ChapterReviewPromptRequest(string? SecondAnswer);

/// <summary>
/// Body of <c>POST .../review/manual-check</c> and <c>.../manual-answer</c>: SecondSections = the sections of the second answer exactly as the server handed
/// them out (they are normalized again), Answer = the review answer pasted from the chat, BaseHash = the hash the server handed out with the second answer
/// (the chapter must not have changed since), Researcher = who wrote the second answer ("gemini" | "claude" | "openai" | "manual", default "manual").
/// </summary>
public sealed record ChapterReviewAnswerRequest(JsonElement? SecondSections, string? Answer, string? BaseHash, string? Researcher);

/// <summary>Body of <c>POST .../review/findings/{index}/choose</c>: Version = "a" (answer A, the chapter as it was), "b" (the second answer) or "result" (accept the reviewer's decision).</summary>
public sealed record ChapterReviewChooseRequest(string? Version);

/// <summary>
/// The stored review of a chapter (<c>GET .../review</c>). Findings are in the guide language (Language); Stale = the chapter was changed after the review, the
/// review is still readable but "take another version" is refused (409). Each finding carries <c>applied</c> (the version taken afterwards) when there is one.
/// </summary>
public sealed record ChapterReviewDto(
    Guid TabId,
    DateTimeOffset ReviewedAt,
    string Language,
    string ResearcherProvider,
    string ReviewerProvider,
    ReviewCounts Counts,
    List<ReviewFinding> Findings,
    bool Stale);

/// <summary>Who answered in a review job: Researcher = the second research (null for an answer pasted from a chat), Reviewer = the review.</summary>
public sealed record ChapterReviewUsageDto(AiUsageDto? Researcher, AiUsageDto Reviewer);

/// <summary>
/// The result of the review job (<c>GET /api/ai/jobs/{id}</c> → <c>result</c>) and of <c>manual-answer</c>: shaped like <c>AiResultDto&lt;GameDetailDto&gt;</c>
/// (<c>game</c>, <c>ai</c>) plus the review. Ai.Providers = everyone who answered (second research first). Warnings = what was ignored or put back.
/// Mode is "full".
/// </summary>
public sealed record ChapterReviewResultDto(
    GameDetailDto Game,
    AiUsageDto Ai,
    ChapterReviewDto Review,
    ChapterReviewUsageDto Usage,
    List<ManualIssueDto> Warnings,
    string Mode = "full");

/// <summary>
/// The second answer prepared for a review through the player's chat (result of the job of <c>POST .../review/second</c> and the answer of
/// <c>manual-prompt</c>): SecondSections = the normalized sections to send back with the pasted review, BaseHash = the state of the chapter they belong to,
/// Prompt = the review prompt to copy (chapter as it is + second answer inside), Ai = who researched the second answer (null when it was pasted). Mode is "second".
/// </summary>
public sealed record ChapterReviewPreparedDto(string Mode, JsonElement SecondSections, string BaseHash, ManualPromptDto Prompt, AiUsageDto? Ai);

/// <summary>What a pasted review answer would do (<c>manual-check</c>, nothing is saved): Ok = it can be saved; Errors/Warnings are complete sentences.</summary>
public sealed record ChapterReviewCheckDto(bool Ok, List<ManualIssueDto> Errors, List<ManualIssueDto> Warnings, ChapterReviewPreviewDto? Preview);

public sealed record ChapterReviewPreviewDto(ReviewCounts Counts, List<ReviewFinding> Findings, int SectionCount, int PutBack);

/// <summary>The answer of "take another version": the guide as it is now and the review with the finding marked as applied.</summary>
public sealed record ChapterReviewChosenDto(GameDetailDto Game, ChapterReviewDto Review);
