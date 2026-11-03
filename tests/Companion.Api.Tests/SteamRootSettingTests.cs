using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// The setting "Steam-Ordner" end to end on the real server: validation with a message in the request's language (400), storing and
/// clearing, the live check endpoint, and what the settings and the system check report. The server gets its own Steam location
/// provider that never reads this PC's registry, so the tests do not depend on a Steam installation here.
/// </summary>
public sealed class SteamRootSettingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-steamroot-" + Guid.NewGuid().ToString("N")[..10]);

    public SteamRootSettingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private static Task<TestServer> StartAsync() => TestServer.StartAsync(desktop: null, b =>
        b.Services.AddSingleton(new SteamLocationProvider(new SteamLocatorInput
        {
            // Windows rules on Windows (the CI runs the tests on Windows too), without registry and environment
            Platform = SteamLocator.CurrentPlatform,
            RegistrySteamPath = () => null,
            Environment = _ => null,
            HomeDirectory = Path.Combine(Path.GetTempPath(), "pv-no-home"),
        })));

    private static HttpRequestMessage Json(HttpMethod method, string url, object body, string language = "de")
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        request.Headers.Add("Accept-Language", language);
        return request;
    }

    [Fact]
    public async Task A_folder_without_steamapps_is_rejected_with_an_explanation_in_the_request_language()
    {
        await using var server = await StartAsync();

        var de = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { steamRoot = _dir }));
        Assert.Equal(HttpStatusCode.BadRequest, de.StatusCode);
        Assert.Contains("steamapps", await de.Content.ReadAsStringAsync());
        Assert.Contains("kein Steam-Ordner", await de.Content.ReadAsStringAsync());

        var en = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { steamRoot = Path.Combine(_dir, "missing") }, "en"));
        Assert.Equal(HttpStatusCode.BadRequest, en.StatusCode);
        Assert.Contains("does not exist", await en.Content.ReadAsStringAsync());

        // A game library has "steamapps" too, but not the Steam client's own folders: it would switch the live detection off
        var library = Path.Combine(_dir, "SteamLibrary");
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        var libraryAnswer = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { steamRoot = library }));
        Assert.Equal(HttpStatusCode.BadRequest, libraryAnswer.StatusCode);
        Assert.Contains("Das ist eine Spielebibliothek, nicht der Steam-Ordner", await libraryAnswer.Content.ReadAsStringAsync());
        var libraryCheck = await (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/steam/local/check", new { path = library }, "en")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("libraryOnly", libraryCheck.GetProperty("problem").GetString());
        Assert.Contains("This is a game library, not the Steam folder", libraryCheck.GetProperty("message").GetString());

        var relative = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { steamRoot = "Steam" }, "en"));
        Assert.Equal(HttpStatusCode.BadRequest, relative.StatusCode);
        Assert.Contains("not a full path", await relative.Content.ReadAsStringAsync());

        // Nothing was stored
        var settings = await server.Client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("steamRoot").ValueKind);
    }

    [Fact]
    public async Task A_Steam_folder_is_stored_used_at_once_and_cleared_with_an_empty_value()
    {
        var steam = Path.Combine(_dir, "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam, "config"));
        await using var server = await StartAsync();

        var before = await server.Client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.False(before.GetProperty("steamLocal").GetProperty("found").GetBoolean());

        var saved = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { steamRoot = steam }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var settings = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(steam, settings.GetProperty("steamRoot").GetString());
        var local = settings.GetProperty("steamLocal");
        Assert.True(local.GetProperty("found").GetBoolean());
        Assert.Equal("manual", local.GetProperty("method").GetString());
        // Steam on the server's own computer: the UI stops pointing at the bridge (inside a container it does not count)
        Assert.Equal(!Services.SteamLocalInfo.InContainer(), local.GetProperty("readsLocally").GetBoolean());

        // null = keep: saving another setting leaves the folder alone
        var other = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { playSounds = false }));
        Assert.Equal(steam, (await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("steamRoot").GetString());

        var status = await server.Client.GetFromJsonAsync<JsonElement>("/api/system/status");
        var steamLocal = status.GetProperty("steamLocal");
        Assert.True(steamLocal.GetProperty("found").GetBoolean());
        Assert.Equal("manual", steamLocal.GetProperty("method").GetString());
        Assert.Equal(steam, steamLocal.GetProperty("storedRoot").GetString());
        Assert.Contains(steamLocal.GetProperty("candidates").EnumerateArray(), c => c.GetProperty("chosen").GetBoolean() && c.GetProperty("path").GetString() == steam);
        var check = status.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "steam-local");
        Assert.Equal("ok", check.GetProperty("status").GetString());
        Assert.Contains(steam, check.GetProperty("detail").GetString());

        var cleared = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { steamRoot = "" }));
        var after = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, after.GetProperty("steamRoot").ValueKind);
        Assert.False(after.GetProperty("steamLocal").GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task A_stored_folder_that_disappeared_is_a_warning_in_the_system_check()
    {
        var steam = Path.Combine(_dir, "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam, "config"));
        await using var server = await StartAsync();
        await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { steamRoot = steam }));

        Directory.Delete(steam, recursive: true);
        // The provider looks again after its short cache
        await Task.Delay(SteamLocationProvider.DefaultRefreshInterval + TimeSpan.FromMilliseconds(200));

        var status = await server.Client.GetFromJsonAsync<JsonElement>("/api/system/status");
        var check = status.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "steam-local");
        Assert.Equal("warn", check.GetProperty("status").GetString());
        Assert.Contains(steam, check.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_live_check_says_whether_a_folder_could_be_saved()
    {
        var steam = Path.Combine(_dir, "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam, "config"));
        await using var server = await StartAsync();

        var good = await (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/steam/local/check", new { path = steam + Path.DirectorySeparatorChar })))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(good.GetProperty("valid").GetBoolean());
        Assert.Equal("none", good.GetProperty("problem").GetString());
        Assert.Equal(steam, good.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, good.GetProperty("message").ValueKind);

        var bad = await (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/steam/local/check", new { path = _dir }, "en")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(bad.GetProperty("valid").GetBoolean());
        Assert.Equal("noSteamApps", bad.GetProperty("problem").GetString());
        Assert.Contains("steamapps", bad.GetProperty("message").GetString());

        // A state-changing endpoint needs the CSRF header like every other POST
        var withoutHeader = await server.Client.PostAsJsonAsync("/api/steam/local/check", new { path = steam });
        Assert.Equal(HttpStatusCode.BadRequest, withoutHeader.StatusCode);
    }
}
