using Companion.Api.Endpoints;
using Companion.Api.Settings;

namespace Companion.Api.Tests;

/// <summary>The popup position setting: the nine screen spots plus "fullscreen" are accepted, everything else is rejected before anything is written.</summary>
public sealed class PopupPositionTests
{
    public static TheoryData<string> Known =>
    [
        "top-left", "top-center", "top-right",
        "center-left", "center", "center-right",
        "bottom-left", "bottom-center", "bottom-right",
        "fullscreen",
    ];

    [Theory]
    [MemberData(nameof(Known))]
    public void Every_known_position_is_accepted(string position)
    {
        Assert.Equal(position, SettingsStore.NormalizePopupPosition(position));
    }

    [Fact]
    public void Fullscreen_is_in_the_allow_list()
    {
        Assert.Contains("fullscreen", SettingsStore.PopupPositions);
        Assert.Equal(10, SettingsStore.PopupPositions.Length);
    }

    [Theory]
    [InlineData("  Fullscreen ", "fullscreen")]
    [InlineData("TOP-LEFT", "top-left")]
    public void Case_and_surrounding_spaces_do_not_matter(string submitted, string expected)
    {
        Assert.Equal(expected, SettingsStore.NormalizePopupPosition(submitted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("full-screen")]
    [InlineData("left")]
    [InlineData("top-right; drop table")]
    public void Unknown_values_are_not_a_position(string? value)
    {
        Assert.Null(SettingsStore.NormalizePopupPosition(value));
    }

    [Fact]
    public async Task An_unknown_position_is_rejected_with_a_message_in_both_languages_before_anything_is_written()
    {
        // The validation runs before the first database access, so no database is needed for this check
        var store = new SettingsStore(null!, null!);
        var request = new UpdateSettingsRequest(
            null, null, null, null, null, null, null, null, "sideways", null, null,
            null, false, null, false, null, false, null, false);

        var error = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(request));

        Assert.Contains("\"sideways\"", error.Text.De);
        Assert.Contains("fullscreen", error.Text.De);
        Assert.Contains("is unknown", error.Text.En);
        Assert.Contains("fullscreen", error.Text.En);
    }
}
