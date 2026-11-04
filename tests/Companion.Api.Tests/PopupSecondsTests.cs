using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Companion.Api.Endpoints;
using Companion.Api.Settings;

namespace Companion.Api.Tests;

/// <summary>
/// "Anzeigedauer der Trophäen-Popups" (setting live.popup_seconds): whole seconds from 2 to 10, default 5; everything else is refused
/// with a bilingual 400 before anything is written, and a request without the field keeps what is stored.
/// </summary>
public sealed class PopupSecondsTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void Whole_seconds_from_two_to_ten_are_accepted(double seconds)
    {
        Assert.Equal((int)seconds, SettingsStore.NormalizePopupSeconds(seconds));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5.5)]
    [InlineData(1.9999)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Anything_else_is_not_a_duration(double seconds)
    {
        Assert.Null(SettingsStore.NormalizePopupSeconds(seconds));
    }

    [Fact]
    public void The_default_is_five_seconds_the_value_the_popups_always_had()
    {
        Assert.Equal(5, SettingsStore.DefaultPopupSeconds);
        Assert.Equal(2, SettingsStore.MinPopupSeconds);
        Assert.Equal(10, SettingsStore.MaxPopupSeconds);
        Assert.Equal(5, SettingsStore.ParsePopupSeconds(null));
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("2", 2)]
    [InlineData("10", 10)]
    [InlineData("1", 5)]
    [InlineData("11", 5)]
    [InlineData("5.5", 5)]
    [InlineData("many", 5)]
    [InlineData("", 5)]
    public void A_stored_value_that_is_hand_edited_into_something_unusable_reads_as_the_default(string stored, int expected)
    {
        Assert.Equal(expected, SettingsStore.ParsePopupSeconds(stored));
    }

    [Fact]
    public async Task A_refused_value_names_both_languages_and_is_rejected_before_anything_is_written()
    {
        // The validation runs before the first database access, so no database is needed for this check
        var store = new SettingsStore(null!, null!);
        var request = new UpdateSettingsRequest(
            null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, PopupSeconds: 11);

        var error = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(request));

        Assert.Contains("\"11\"", error.Text.De);
        Assert.Contains("ganze Sekunden von 2 bis 10", error.Text.De);
        Assert.Contains("whole seconds from 2 to 10", error.Text.En);
    }

    [Fact]
    public async Task An_empty_database_reads_as_five_seconds()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        Assert.Equal(5, await SettingsStore.ReadPopupSecondsAsync(db));
    }

    [Fact]
    public async Task The_endpoint_defaults_to_five_accepts_two_and_ten_refuses_the_rest_with_400_and_persists()
    {
        await using var server = await TestServer.StartAsync(null, _ => { });
        server.Client.DefaultRequestHeaders.Add("X-Requested-With", "PlatinVault");

        static int Read(string body) => JsonDocument.Parse(body).RootElement.GetProperty("popupSeconds").GetInt32();
        static HttpRequestMessage Put(object body, string? language = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = JsonContent.Create(body) };
            if (language is not null) request.Headers.AcceptLanguage.ParseAdd(language);
            return request;
        }

        Assert.Equal(5, Read(await server.Client.GetStringAsync("/api/settings")));

        foreach (var accepted in new[] { 2, 10 })
        {
            var ok = await server.Client.SendAsync(Put(new { popupSeconds = accepted }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(accepted, Read(await ok.Content.ReadAsStringAsync()));
            Assert.Equal(accepted, Read(await server.Client.GetStringAsync("/api/settings")));
        }

        // Refused values leave the stored 10 untouched; the answer is complete in the requested language
        foreach (var refused in new object[] { 1, 11, 5.5, 0, -3 })
        {
            var bad = await server.Client.SendAsync(Put(new { popupSeconds = refused }, "en"));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("whole seconds from 2 to 10", await bad.Content.ReadAsStringAsync());
            Assert.Equal(10, Read(await server.Client.GetStringAsync("/api/settings")));
        }

        // A request without the field keeps the stored value
        var other = await server.Client.SendAsync(Put(new { playSounds = false }));
        Assert.Equal(10, Read(await other.Content.ReadAsStringAsync()));
    }
}
