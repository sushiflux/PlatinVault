using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Companion.Api.Endpoints;
using Companion.Api.Settings;

namespace Companion.Api.Tests;

/// <summary>
/// UI design setting (ui.design): "light" (default) or "classic". Anything else is refused with a bilingual 400 before anything is
/// written, and a request without the field keeps what is stored.
/// </summary>
public sealed class UiDesignSettingTests
{
    [Theory]
    [InlineData("light", "light")]
    [InlineData("classic", "classic")]
    [InlineData(" Classic ", "classic")]
    [InlineData("LIGHT", "light")]
    public void Known_designs_are_accepted_in_any_case_and_trimmed(string submitted, string expected)
    {
        Assert.Equal(expected, SettingsStore.NormalizeUiDesign(submitted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("dark")]
    [InlineData("light;classic")]
    public void Unknown_designs_are_not_accepted(string? value)
    {
        Assert.Null(SettingsStore.NormalizeUiDesign(value));
        Assert.Equal(["light", "classic"], SettingsStore.UiDesigns);
        Assert.Equal("light", SettingsStore.DefaultUiDesign);
        Assert.Equal("ui.design", SettingsStore.KeyUiDesign);
    }

    [Fact]
    public async Task An_unknown_design_is_rejected_with_a_message_in_both_languages_before_anything_is_written()
    {
        var store = new SettingsStore(null!, null!);
        var request = new UpdateSettingsRequest(null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, UiDesign: "dark");

        var error = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(request));

        Assert.Contains("\"dark\"", error.Text.De);
        Assert.Contains("\"classic\"", error.Text.De);
        Assert.Contains("is unknown", error.Text.En);
    }

    [Fact]
    public async Task The_endpoint_defaults_to_light_saves_classic_keeps_it_on_null_and_refuses_unknown_values()
    {
        await using var server = await TestServer.StartAsync(null, _ => { });
        server.Client.DefaultRequestHeaders.Add("X-Requested-With", "PlatinVault");

        static string Read(string body) => JsonDocument.Parse(body).RootElement.GetProperty("uiDesign").GetString()!;
        static HttpRequestMessage Put(object body, string? language = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = JsonContent.Create(body) };
            if (language is not null) request.Headers.AcceptLanguage.ParseAdd(language);
            return request;
        }

        Assert.Equal("light", Read(await server.Client.GetStringAsync("/api/settings")));

        var ok = await server.Client.SendAsync(Put(new { uiDesign = " Classic " }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("classic", Read(await ok.Content.ReadAsStringAsync()));
        Assert.Equal("classic", Read(await server.Client.GetStringAsync("/api/settings")));

        // Explicit null and an omitted field both keep the stored value
        var explicitNull = await server.Client.SendAsync(Put(new { uiDesign = (string?)null, playSounds = false }));
        Assert.Equal("classic", Read(await explicitNull.Content.ReadAsStringAsync()));
        var omitted = await server.Client.SendAsync(Put(new { playSounds = true }));
        Assert.Equal("classic", Read(await omitted.Content.ReadAsStringAsync()));

        var bad = await server.Client.SendAsync(Put(new { uiDesign = "dark" }, "en"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("is unknown", await bad.Content.ReadAsStringAsync());
        Assert.Equal("classic", Read(await server.Client.GetStringAsync("/api/settings")));

        var back = await server.Client.SendAsync(Put(new { uiDesign = "light" }));
        Assert.Equal("light", Read(await back.Content.ReadAsStringAsync()));
    }
}
