using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlatinVault.Versioning;

namespace Companion.Api.Tests;

/// <summary>
/// The one stored update channel ("updates.channel": none | beta | nightly) that replaced the two switches "updates.beta_channel" and
/// "updates.nightly_channel": the ids, the carry-over of every old combination (nightly wins over beta, the old rows are removed), the
/// validation of a submitted value and what the settings answer reports.
/// </summary>
public sealed class UpdateChannelTests
{
    private static async Task SeedAsync(CompanionDbContext db, params (string Key, string Value)[] rows)
    {
        foreach (var (key, value) in rows)
        {
            db.AppSettings.Add(new AppSettingEntity { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, string>> UpdateRowsAsync(SqliteScratch scratch)
    {
        // A fresh context: what is really in the file, not what the store's context has tracked
        await using var check = scratch.Context();
        return await check.AppSettings.AsNoTracking().Where(x => x.Key.StartsWith("updates.")).ToDictionaryAsync(x => x.Key, x => x.Value);
    }

    private static SettingsStore StoreWithSecrets(SqliteScratch scratch, CompanionDbContext db)
    {
        var dir = Path.Combine(scratch.Root, "secrets");
        Directory.CreateDirectory(dir);
        var secrets = new SecretStore(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys"))), Options.Create(new CompanionOptions { DataDir = dir }));
        return new SettingsStore(db, secrets);
    }

    // ---- ids and rules -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_ids_round_trip_and_unknown_values_are_not_a_channel()
    {
        foreach (var id in UpdateChannels.AllIds)
        {
            Assert.Equal(id, UpdateChannels.ToId(UpdateChannels.TryParse(id)!.Value));
        }
        Assert.Equal(new[] { "none", "beta", "nightly" }, UpdateChannels.AllIds);
        Assert.Equal(UpdateChannel.Nightly, UpdateChannels.TryParse(" NIGHTLY "));
        Assert.Null(UpdateChannels.TryParse("true"));
        Assert.Null(UpdateChannels.TryParse("alpha"));
        Assert.Null(UpdateChannels.TryParse(""));
        Assert.Null(UpdateChannels.TryParse(null));
        Assert.Equal("none", SettingsStore.DefaultUpdateChannel);
    }

    [Theory]
    [InlineData(UpdateChannel.None, "latest")]
    [InlineData(UpdateChannel.Beta, "beta")]
    [InlineData(UpdateChannel.Nightly, "nightly")]
    public void Every_channel_needs_its_own_image_tag(UpdateChannel channel, string tag) =>
        Assert.Equal(tag, UpdateChannels.ImageTag(channel));

    [Theory]
    // old beta switch, old nightly switch, expected channel (all four combinations; the nightly switch was the wider one and wins)
    [InlineData("false", "false", UpdateChannel.None)]
    [InlineData("true", "false", UpdateChannel.Beta)]
    [InlineData("false", "true", UpdateChannel.Nightly)]
    [InlineData("true", "true", UpdateChannel.Nightly)]
    // a missing or unreadable old value counts as off
    [InlineData(null, null, UpdateChannel.None)]
    [InlineData("yes", "1", UpdateChannel.None)]
    [InlineData("True", null, UpdateChannel.Beta)]
    [InlineData(null, "True", UpdateChannel.Nightly)]
    public void The_two_old_switches_map_to_one_channel(string? beta, string? nightly, UpdateChannel expected) =>
        Assert.Equal(expected, SettingsStore.ChannelFromLegacySwitches(beta, nightly));

    // ---- carry-over of an existing installation -----------------------------------------------------------------------------

    [Theory]
    [InlineData("false", "false", "none")]
    [InlineData("true", "false", "beta")]
    [InlineData("false", "true", "nightly")]
    [InlineData("true", "true", "nightly")]
    public async Task An_installation_with_the_old_switches_keeps_its_channel_and_loses_the_old_rows(string beta, string nightly, string expected)
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SeedAsync(db, (SettingsStore.KeyLegacyBetaUpdates, beta), (SettingsStore.KeyLegacyNightlyUpdates, nightly));

        var store = new SettingsStore(db, null!);
        Assert.Equal(UpdateChannels.TryParse(expected), await store.GetUpdateChannelAsync());

        var rows = await UpdateRowsAsync(scratch);
        Assert.Equal(expected, rows[SettingsStore.KeyUpdateChannel]);
        Assert.DoesNotContain(SettingsStore.KeyLegacyBetaUpdates, rows.Keys);
        Assert.DoesNotContain(SettingsStore.KeyLegacyNightlyUpdates, rows.Keys);

        // A second read (new request, new store) sees the same channel and nothing is left to carry over
        await using var again = scratch.Context();
        Assert.Equal(UpdateChannels.TryParse(expected), await new SettingsStore(again, null!).GetUpdateChannelAsync());
    }

    [Fact]
    public async Task Only_one_old_row_is_enough_to_carry_over()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SeedAsync(db, (SettingsStore.KeyLegacyBetaUpdates, "true"));
        Assert.Equal(UpdateChannel.Beta, await new SettingsStore(db, null!).GetUpdateChannelAsync());
        var rows = await UpdateRowsAsync(scratch);
        Assert.Equal("beta", rows[SettingsStore.KeyUpdateChannel]);
        Assert.DoesNotContain(SettingsStore.KeyLegacyBetaUpdates, rows.Keys);
    }

    [Fact]
    public async Task A_fresh_installation_has_no_channel_and_nothing_is_written()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        Assert.Equal(UpdateChannel.None, await new SettingsStore(db, null!).GetUpdateChannelAsync());
        Assert.Empty(await UpdateRowsAsync(scratch));
    }

    [Fact]
    public async Task A_stored_channel_wins_over_leftover_old_rows_which_are_then_removed()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SeedAsync(db,
            (SettingsStore.KeyUpdateChannel, "beta"),
            (SettingsStore.KeyLegacyNightlyUpdates, "true"));

        Assert.Equal(UpdateChannel.Beta, await new SettingsStore(db, null!).GetUpdateChannelAsync());
        var rows = await UpdateRowsAsync(scratch);
        Assert.Equal("beta", rows[SettingsStore.KeyUpdateChannel]);
        Assert.DoesNotContain(SettingsStore.KeyLegacyNightlyUpdates, rows.Keys);
    }

    [Fact]
    public async Task A_hand_edited_channel_value_falls_back_to_the_old_switches_or_none()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SeedAsync(db, (SettingsStore.KeyUpdateChannel, "sideways"));
        Assert.Equal(UpdateChannel.None, await new SettingsStore(db, null!).GetUpdateChannelAsync());

        await using var withLegacy = scratch.Context();
        withLegacy.AppSettings.Add(new AppSettingEntity { Key = SettingsStore.KeyLegacyBetaUpdates, Value = "true", UpdatedAt = DateTimeOffset.UtcNow });
        await withLegacy.SaveChangesAsync();
        Assert.Equal(UpdateChannel.Beta, await new SettingsStore(withLegacy, null!).GetUpdateChannelAsync());
        Assert.Equal("beta", (await UpdateRowsAsync(scratch))[SettingsStore.KeyUpdateChannel]);
    }

    // ---- the settings answer and request ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("true", "true", "nightly")]
    [InlineData("true", "false", "beta")]
    public async Task The_settings_answer_reports_the_carried_over_channel_as_one_field(string beta, string nightly, string expected)
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SeedAsync(db, (SettingsStore.KeyLegacyBetaUpdates, beta), (SettingsStore.KeyLegacyNightlyUpdates, nightly));

        var answer = await StoreWithSecrets(scratch, db).GetAsync();

        Assert.Equal(expected, answer.UpdateChannel);
        Assert.DoesNotContain(SettingsStore.KeyLegacyBetaUpdates, (await UpdateRowsAsync(scratch)).Keys);
    }

    [Fact]
    public async Task Saving_a_channel_stores_it_removes_old_rows_and_a_request_without_the_field_keeps_it()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SeedAsync(db, (SettingsStore.KeyLegacyBetaUpdates, "true"));
        var store = StoreWithSecrets(scratch, db);

        await store.UpdateAsync(new UpdateSettingsRequest(null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, UpdateChannel: " Nightly "));
        var rows = await UpdateRowsAsync(scratch);
        Assert.Equal("nightly", rows[SettingsStore.KeyUpdateChannel]);
        Assert.DoesNotContain(SettingsStore.KeyLegacyBetaUpdates, rows.Keys);

        // null = keep what is stored
        await store.UpdateAsync(new UpdateSettingsRequest(null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, PopupTarget: "app"));
        Assert.Equal("nightly", (await UpdateRowsAsync(scratch))[SettingsStore.KeyUpdateChannel]);

        // and back to none
        await store.UpdateAsync(new UpdateSettingsRequest(null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, UpdateChannel: "none"));
        Assert.Equal("none", (await UpdateRowsAsync(scratch))[SettingsStore.KeyUpdateChannel]);
        Assert.Equal(UpdateChannel.None, await new SettingsStore(db, null!).GetUpdateChannelAsync());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("alpha")]
    [InlineData("")]
    public async Task An_unknown_channel_is_rejected_before_anything_is_written(string value)
    {
        // The validation runs before the first database access, so no database is needed for this check
        var store = new SettingsStore(null!, null!);
        var request = new UpdateSettingsRequest(null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, UpdateChannel: value);
        var error = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(request));
        Assert.Contains($"\"{value}\"", error.Text.De);
        Assert.Contains("\"nightly\"", error.Text.De);
        Assert.Contains("is unknown", error.Text.En);
    }
}
