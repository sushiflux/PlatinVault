using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The provider's own error text travels to the user complete (it is folded under "technical details", never shortened to a fixed
/// length). Only a runaway body - a whole HTML error page - is cut by a very large guard.
/// </summary>
public sealed class AiProviderExceptionTests
{
    [Fact]
    public void A_long_provider_message_is_kept_complete()
    {
        // Longer than the old 800-character cut, like a Google error with quota details and help links
        var raw = string.Concat(Enumerable.Repeat("You exceeded your current quota, please check your plan and billing details. ", 40)) + "THE END";
        Assert.True(raw.Length > 2_500);

        var exception = new AiProviderException(Msg.Same("Gemini quota used up."), raw, status: 429);

        Assert.Equal(raw.Trim(), exception.ProviderMessage);
        Assert.EndsWith("THE END", exception.ProviderMessage);
    }

    [Fact]
    public void A_short_message_is_trimmed_but_not_changed()
    {
        var exception = new AiProviderException(Msg.Same("Key rejected."), "  API key not valid. Please pass a valid API key.  ", status: 400);

        Assert.Equal("API key not valid. Please pass a valid API key.", exception.ProviderMessage);
    }

    [Fact]
    public void No_message_stays_null()
    {
        Assert.Null(new AiProviderException(Msg.Same("Unavailable.")).ProviderMessage);
        Assert.Null(new AiProviderException(Msg.Same("Unavailable."), "   ").ProviderMessage);
    }

    [Fact]
    public void Only_a_runaway_body_is_cut_by_the_guard()
    {
        var exception = new AiProviderException(Msg.Same("Unavailable."), new string('x', 100_000), status: 502);

        Assert.NotNull(exception.ProviderMessage);
        Assert.InRange(exception.ProviderMessage!.Length, 20_000, 20_001);
        Assert.EndsWith("…", exception.ProviderMessage);
    }
}
