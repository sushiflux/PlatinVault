using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>A clock the tests move by hand.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal static class Fakes
{
    public static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A failure as a provider would cause it.</summary>
    public static AiProviderException Failure(string provider, int status, string message = "", TimeSpan? retryAfter = null) =>
        new(ServerTexts.Ai.ProviderError(provider, status, message, "model"), message, status, null, provider, retryAfter);

    public static Func<string, string> Prints(Dictionary<string, string>? keys = null) =>
        provider => AiKeys.Fingerprint(keys is not null && keys.TryGetValue(provider, out var k) ? k : "key-" + provider);
}

public class AiFailureTests
{
    [Theory]
    [InlineData(429, "", AiFailureKind.Quota)]
    [InlineData(402, "", AiFailureKind.Quota)]
    [InlineData(400, "Your credit balance is too low to access the API", AiFailureKind.Quota)]
    [InlineData(429, "You exceeded your current quota, insufficient_quota", AiFailureKind.Quota)]
    [InlineData(401, "", AiFailureKind.InvalidKey)]
    [InlineData(403, "", AiFailureKind.InvalidKey)]
    [InlineData(400, "API key not valid. Please pass a valid API key.", AiFailureKind.InvalidKey)]
    [InlineData(503, "The model is overloaded", AiFailureKind.Unavailable)]
    [InlineData(500, "", AiFailureKind.Unavailable)]
    [InlineData(529, "overloaded_error", AiFailureKind.Unavailable)]
    [InlineData(0, "", AiFailureKind.Unavailable)]
    [InlineData(400, "Invalid JSON payload", AiFailureKind.Other)]
    [InlineData(404, "models/gemini-2.5-flash is not found", AiFailureKind.Other)]
    public void Classify_decides_whether_another_provider_may_take_over(int status, string message, AiFailureKind expected)
    {
        Assert.Equal(expected, AiFailure.Classify(status, message));
        Assert.Equal(expected != AiFailureKind.Other, expected.SwitchesProvider());
    }

    [Fact]
    public void The_exception_derives_its_kind_from_status_and_text()
    {
        Assert.Equal(AiFailureKind.Quota, Fakes.Failure("gemini", 429).Kind);
        Assert.Equal(AiFailureKind.Other, Fakes.Failure("claude", 400, "bad request").Kind);
    }
}

public class AiSelectionResolveTests
{
    [Fact]
    public void A_fresh_installation_uses_the_standard_selection()
    {
        var selection = AiSelection.Resolve(null, null, null, null, firstKeyed: "claude");
        Assert.Equal(new AiSelection(AiSelectionMode.Default, null, true), selection);
    }

    [Fact]
    public void The_former_auto_value_becomes_the_standard_KI_with_the_first_key()
    {
        var selection = AiSelection.Resolve(null, null, null, "auto", firstKeyed: "gemini");
        Assert.Equal(AiSelectionMode.Default, selection.Mode);
        Assert.Equal("gemini", selection.DefaultProvider);
        Assert.True(selection.AutoFallback);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("Claude ")]
    public void A_former_fixed_provider_becomes_the_standard_KI(string legacy)
    {
        var selection = AiSelection.Resolve(null, null, null, legacy, firstKeyed: "gemini");
        Assert.Equal("claude", selection.DefaultProvider);
    }

    [Fact]
    public void Stored_new_values_win_over_the_legacy_row()
    {
        var selection = AiSelection.Resolve("mixed", "openai", false, "gemini", firstKeyed: "gemini");
        Assert.Equal(new AiSelection(AiSelectionMode.Mixed, "openai", false), selection);
    }

    [Fact]
    public void A_nonsense_mode_counts_as_not_chosen()
    {
        var selection = AiSelection.Resolve("everything", null, null, null, null);
        Assert.Equal(AiSelectionMode.Default, selection.Mode);
    }
}
