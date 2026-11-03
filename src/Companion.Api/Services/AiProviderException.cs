using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// An AI provider (Gemini, Claude, OpenAI) refused or failed. <see cref="Message"/> is OUR short explanation (what happened, what
/// to do) in the language of whoever reads it; the provider's own text - English, long, technical - is kept apart in
/// <see cref="ProviderMessage"/> so the UI can fold it under "technical details" (problem JSON extension <c>providerMessage</c>).
/// <see cref="Kind"/> says whether the request may continue with another provider (see <see cref="AiRun"/>).
/// </summary>
public sealed class AiProviderException(
    Msg explanation,
    string? providerMessage = null,
    int status = 0,
    Exception? inner = null,
    string? provider = null,
    TimeSpan? retryAfter = null,
    AiFailureKind? kind = null,
    bool truncated = false)
    : Exception(explanation.De, inner)
{
    // Only a guard against a whole HTML error page or a runaway body: real provider texts are far shorter and must reach the user complete
    private const int MaxProviderMessage = 20_000;

    public Msg Text { get; } = explanation;

    /// <summary>The provider's raw error text (complete; only a body beyond the guard of <c>MaxProviderMessage</c> characters is cut), or null when there is none.</summary>
    public string? ProviderMessage { get; } = Shorten(providerMessage);

    /// <summary>HTTP status of the provider's answer; 0 when there was none (network failure, unreadable answer).</summary>
    public int Status { get; } = status;

    /// <summary>Which provider failed ("gemini" / "claude" / "openai"), or null when it is not known.</summary>
    public string? Provider { get; } = provider;

    /// <summary>How long the provider asked us to wait (Retry-After header or the delay in Google's error), or null.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>Quota / key / availability problems hand the request on to another provider; everything else does not.</summary>
    public AiFailureKind Kind { get; } = kind ?? AiFailure.Classify(status, providerMessage ?? string.Empty);

    /// <summary>
    /// The provider stopped because the answer reached its output limit (Claude <c>stop_reason: max_tokens</c>, Gemini <c>finishReason: MAX_TOKENS</c>,
    /// OpenAI <c>finish_reason: length</c>): the text is cut off and unusable as JSON. Never a reason to switch provider (the kind is Other); the caller
    /// may ask the same provider once more for a more compact answer (see AiGuideGeneratorService.CallContentAsync).
    /// </summary>
    public bool IsTruncated { get; } = truncated;

    public override string Message => Text.ToString();

    /// <summary>The text of an exception in both languages (our own exceptions keep both; anything else is one fixed text).</summary>
    public static Msg TextOf(Exception exception) => exception switch
    {
        AiProviderException ai => ai.Text,
        LocalizedException localized => localized.Text,
        _ => Msg.Same(exception.Message)
    };

    private static string? Shorten(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        return trimmed.Length > MaxProviderMessage ? trimmed[..MaxProviderMessage] + "…" : trimmed;
    }
}
