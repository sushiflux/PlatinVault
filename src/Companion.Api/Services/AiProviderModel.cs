using System.Security.Cryptography;
using System.Text;

namespace Companion.Api.Services;

/// <summary>The AI providers PlatinVault can talk to, in the canonical order used wherever no preference decides.</summary>
public static class AiProvider
{
    public const string Gemini = "gemini";
    public const string Claude = "claude";
    public const string OpenAi = "openai";

    public static readonly IReadOnlyList<string> All = [Gemini, Claude, OpenAi];

    /// <summary>"Claude " -> "claude"; anything that is not one of the three providers (including the legacy "auto"/"both") -> null.</summary>
    public static string? Normalize(string? id)
    {
        var value = id?.Trim().ToLowerInvariant();
        return value is Gemini or Claude or OpenAi ? value : null;
    }

    /// <summary>Gemini (Google Search) and Claude (web search tool) research live; this integration calls OpenAI without web search.</summary>
    public static bool HasWebSearch(string id) => id != OpenAi;
}

/// <summary>The three ways the player chooses the AI (setting <c>ai.selection_mode</c>).</summary>
public static class AiSelectionMode
{
    /// <summary>One provider (the "Standard-KI") writes a whole guide.</summary>
    public const string Default = "default";

    /// <summary>The chapters of a guide are spread over every provider that has a key.</summary>
    public const string Mixed = "mixed";

    /// <summary>Every AI action asks which provider to use; nothing switches automatically.</summary>
    public const string Ask = "ask";

    public static readonly IReadOnlyList<string> All = [Default, Mixed, Ask];

    public static string? Normalize(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is Default or Mixed or Ask ? v : null;
    }
}

/// <summary>The stored AI selection: mode, preferred provider (null = the first one that has a key) and the automatic-fallback switch.</summary>
public sealed record AiSelection(string Mode, string? DefaultProvider, bool AutoFallback)
{
    public static AiSelection Standard { get; } = new(AiSelectionMode.Default, null, true);

    /// <summary>
    /// The selection that belongs to the stored values, including the one-time migration of the former single <c>ai.provider</c>
    /// setting: "auto" became "Standard-KI = the first provider with a key", a fixed provider became "Standard-KI = that provider".
    /// </summary>
    /// <param name="legacyProvider">The old <c>ai.provider</c> value, or null.</param>
    /// <param name="firstKeyed">The first provider (canonical order) that has a key, or null; only used for the migration.</param>
    public static AiSelection Resolve(string? mode, string? defaultProvider, bool? autoFallback, string? legacyProvider, string? firstKeyed)
    {
        var fallback = autoFallback ?? true;
        var normalizedMode = AiSelectionMode.Normalize(mode);
        if (normalizedMode is not null)
        {
            return new AiSelection(normalizedMode, AiProvider.Normalize(defaultProvider), fallback);
        }

        if (!string.IsNullOrWhiteSpace(legacyProvider))
        {
            return new AiSelection(AiSelectionMode.Default, AiProvider.Normalize(defaultProvider) ?? AiProvider.Normalize(legacyProvider) ?? firstKeyed, fallback);
        }
        return new AiSelection(AiSelectionMode.Default, AiProvider.Normalize(defaultProvider), fallback);
    }
}

/// <summary>Why an AI request failed, as far as the choice of provider is concerned.</summary>
public enum AiFailureKind
{
    /// <summary>Not a reason to switch provider: a bad or unreadable answer, a rejected request, an unknown model.</summary>
    Other,

    /// <summary>Quota, credit or spending limit used up, or too many requests (HTTP 429/402).</summary>
    Quota,

    /// <summary>The key is rejected (HTTP 401/403, "API key not valid").</summary>
    InvalidKey,

    /// <summary>The provider is overloaded, down or did not answer (HTTP 5xx, timeout, no connection).</summary>
    Unavailable
}

public static class AiFailure
{
    public static bool IsKeyRejected(int status, string providerMessage)
    {
        var lower = providerMessage.ToLowerInvariant();
        return status is 401 or 403 ||
               (status == 400 && (lower.Contains("api key not valid") || lower.Contains("invalid api key") || lower.Contains("invalid x-api-key")));
    }

    /// <summary>Anthropic reports an empty balance as HTTP 400, OpenAI as 429 "insufficient_quota": the text decides, not only the status.</summary>
    public static bool IsCreditGone(int status, string providerMessage)
    {
        var lower = providerMessage.ToLowerInvariant();
        return status == 402 || lower.Contains("credit balance") || lower.Contains("insufficient_quota") || lower.Contains("billing") || lower.Contains("spend limit");
    }

    /// <summary>Status 0 means the request never got an answer (network failure or timeout).</summary>
    public static AiFailureKind Classify(int status, string providerMessage)
    {
        if (IsKeyRejected(status, providerMessage)) return AiFailureKind.InvalidKey;
        if (status == 404) return AiFailureKind.Other;
        if (IsCreditGone(status, providerMessage) || status == 429) return AiFailureKind.Quota;
        if (status == 0 || status == 408 || status >= 500) return AiFailureKind.Unavailable;
        return AiFailureKind.Other;
    }

    /// <summary>Only these failures hand the request to another provider; anything else would fail there as well.</summary>
    public static bool SwitchesProvider(this AiFailureKind kind) => kind != AiFailureKind.Other;

    public static string ToId(this AiFailureKind kind) => kind switch
    {
        AiFailureKind.Quota => "quota",
        AiFailureKind.InvalidKey => "invalidKey",
        AiFailureKind.Unavailable => "unavailable",
        _ => "other"
    };
}

public static class AiKeys
{
    /// <summary>A short, irreversible mark of a stored key: a changed key gives a different mark, so the state of the old key is forgotten.</summary>
    public static string Fingerprint(string? key) =>
        string.IsNullOrEmpty(key) ? string.Empty : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
}
