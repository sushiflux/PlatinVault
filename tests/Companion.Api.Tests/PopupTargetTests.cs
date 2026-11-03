using Companion.Api.Endpoints;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Tests;

/// <summary>
/// "Trophäen-Popup anzeigen" (setting live.popup_target): "app", "overlay" and "both" are accepted (default "both"), everything else is
/// rejected before anything is written; a request without the field keeps what is stored.
/// </summary>
public sealed class PopupTargetTests
{
    [Theory]
    [InlineData("app")]
    [InlineData("overlay")]
    [InlineData("both")]
    public void Every_known_target_is_accepted(string target)
    {
        Assert.Equal(target, SettingsStore.NormalizePopupTarget(target));
    }

    [Theory]
    [InlineData(" Overlay ", "overlay")]
    [InlineData("BOTH", "both")]
    public void Case_and_surrounding_spaces_do_not_matter(string submitted, string expected)
    {
        Assert.Equal(expected, SettingsStore.NormalizePopupTarget(submitted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("game")]
    [InlineData("browser")]
    [InlineData("app;overlay")]
    public void Unknown_values_are_not_a_target(string? value)
    {
        Assert.Null(SettingsStore.NormalizePopupTarget(value));
    }

    [Fact]
    public void The_default_is_both_and_part_of_the_list()
    {
        Assert.Equal("both", SettingsStore.DefaultPopupTarget);
        Assert.Equal(["app", "overlay", "both"], SettingsStore.PopupTargets);
    }

    [Fact]
    public void A_settings_answer_without_the_field_carries_the_default()
    {
        var request = new UpdateSettingsRequest(null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false);
        Assert.Null(request.PopupTarget);
    }

    [Fact]
    public async Task An_unknown_target_is_rejected_with_a_message_in_both_languages_before_anything_is_written()
    {
        // The validation runs before the first database access, so no database is needed for this check
        var store = new SettingsStore(null!, null!);
        var request = new UpdateSettingsRequest(
            null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, PopupTarget: "game");

        var error = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(request));

        Assert.Contains("\"game\"", error.Text.De);
        Assert.Contains("\"overlay\"", error.Text.De);
        Assert.Contains("is unknown", error.Text.En);
    }

    [Fact]
    public async Task The_stored_target_round_trips_and_a_missing_or_hand_edited_value_reads_as_both()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        Assert.False(await SettingsStore.HasPopupTargetAsync(db));
        Assert.Equal("both", await SettingsStore.ReadPopupTargetAsync(db));

        var store = new SettingsStore(db, null!);
        await store.SetPopupTargetAsync("Overlay");
        Assert.True(await SettingsStore.HasPopupTargetAsync(db));
        Assert.Equal("overlay", await SettingsStore.ReadPopupTargetAsync(db));

        var row = await db.AppSettings.FirstAsync(x => x.Key == SettingsStore.KeyPopupTarget);
        row.Value = "sideways";
        await db.SaveChangesAsync();
        Assert.Equal("both", await SettingsStore.ReadPopupTargetAsync(db));
    }

    [Fact]
    public async Task The_tray_switch_cannot_store_an_unknown_target()
    {
        var store = new SettingsStore(null!, null!);
        await Assert.ThrowsAsync<SettingsValidationException>(() => store.SetPopupTargetAsync("sideways"));
    }
}
