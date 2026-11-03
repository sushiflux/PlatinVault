using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Companion.Api.Desktop;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Companion.Api.Tests;

/// <summary>
/// The real server (CompanionHost) on a loopback port with a temporary SQLite file, its background workers left out, optionally with a
/// fake <see cref="IDesktopHost"/> like the Windows program registers one. Nothing of the real profile is touched.
/// </summary>
internal sealed class TestServer : IAsyncDisposable
{
    private readonly string _folder;
    private readonly WebApplication _app;

    private TestServer(string folder, WebApplication app, HttpClient client)
    {
        _folder = folder;
        _app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    public static async Task<TestServer> StartAsync(IDesktopHost? desktop)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pv-apitest-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(folder);
        var builder = CompanionHost.CreateBuilder(new CompanionHostOptions
        {
            EnvironmentName = "Production",
            ConfigureBuilder = b =>
            {
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Companion:DataDir"] = Path.Combine(folder, "data"),
                    ["Companion:Database:Path"] = Path.Combine(folder, "db", "platinvault.db"),
                    ["Companion:Backups:Dir"] = Path.Combine(folder, "backups"),
                    ["Companion:Updates:Enabled"] = "false",
                });
                b.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
                b.Logging.ClearProviders();
                if (desktop is not null) b.Services.AddSingleton(desktop);
            },
        });
        // No Steam monitor, history recorder or backup worker: the tests only need the endpoints
        foreach (var worker in builder.Services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Assembly == typeof(CompanionHost).Assembly).ToList())
        {
            builder.Services.Remove(worker);
        }
        var app = await CompanionHost.BuildAsync(builder);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient { BaseAddress = new Uri(address.Replace("[::]", "127.0.0.1")), Timeout = TimeSpan.FromSeconds(30) };
        return new TestServer(folder, app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { /* a handle may linger for a moment; the temp folder is cleaned by Windows */ }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class DesktopEndpointsTests
{
    private sealed class FakeDesktopHost(bool openWorks = true) : IDesktopHost
    {
        public List<string> Opened { get; } = [];

        public IReadOnlyList<DesktopFolder> Folders { get; } =
        [
            new(DesktopFolderKinds.Program, @"C:\Test\Programs\PlatinVault"),
            new(DesktopFolderKinds.Data, @"C:\Test\PlatinVault"),
            new(DesktopFolderKinds.Backups, @"C:\Test\PlatinVault\backups"),
            new(DesktopFolderKinds.Logs, @"C:\Test\PlatinVault\logs"),
        ];

        public LocalLaunchResult LaunchGame(int appId) => LocalLaunchResult.Ok();

        public DesktopFolderOpenResult OpenFolder(string kind)
        {
            Opened.Add(kind);
            return openWorks ? new DesktopFolderOpenResult(true, null) : new DesktopFolderOpenResult(false, "explorer.exe was not found");
        }
    }

    private static HttpRequestMessage Post(string url, bool withHeader = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (withHeader) request.Headers.Add("X-Requested-With", "PlatinVault");
        return request;
    }

    [Fact]
    public async Task Browser_and_container_hosts_answer_404()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var list = await server.Client.GetAsync("/api/desktop/folders");
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Contains("Windows", await list.Content.ReadAsStringAsync());

        foreach (var kind in new[] { "program", "data", "nonsense" })
        {
            var open = await server.Client.SendAsync(Post($"/api/desktop/folders/{kind}/open"));
            Assert.Equal(HttpStatusCode.NotFound, open.StatusCode);
        }
    }

    [Fact]
    public async Task Desktop_host_lists_the_four_folders_with_their_paths()
    {
        var host = new FakeDesktopHost();
        await using var server = await TestServer.StartAsync(host);
        var response = await server.Client.GetAsync("/api/desktop/folders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var folders = json.RootElement.GetProperty("folders").EnumerateArray()
            .Select(f => (f.GetProperty("kind").GetString(), f.GetProperty("path").GetString())).ToList();
        Assert.Equal(
        [
            ("program", @"C:\Test\Programs\PlatinVault"),
            ("data", @"C:\Test\PlatinVault"),
            ("backups", @"C:\Test\PlatinVault\backups"),
            ("logs", @"C:\Test\PlatinVault\logs"),
        ], folders);
    }

    [Theory]
    [InlineData("program")]
    [InlineData("data")]
    [InlineData("backups")]
    [InlineData("logs")]
    public async Task Opening_a_known_folder_answers_204_and_asks_the_host_for_exactly_that_kind(string kind)
    {
        var host = new FakeDesktopHost();
        await using var server = await TestServer.StartAsync(host);
        var response = await server.Client.SendAsync(Post($"/api/desktop/folders/{kind}/open"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([kind], host.Opened);
    }

    [Fact]
    public async Task Opening_needs_the_csrf_header()
    {
        var host = new FakeDesktopHost();
        await using var server = await TestServer.StartAsync(host);
        var response = await server.Client.SendAsync(Post("/api/desktop/folders/data/open", withHeader: false));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Opened);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("Program")]
    [InlineData("C%3A%5CWindows")]
    [InlineData("..%2F..%2FWindows")]
    public async Task Only_the_four_known_kinds_are_accepted_a_path_is_never_taken_from_the_request(string kind)
    {
        var host = new FakeDesktopHost();
        await using var server = await TestServer.StartAsync(host);
        var response = await server.Client.SendAsync(Post($"/api/desktop/folders/{kind}/open"));
        // 400 for an unknown kind; a path-like segment may also be a route miss (never a 2xx)
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"status {(int)response.StatusCode}");
        Assert.Empty(host.Opened);
    }

    [Fact]
    public async Task Unknown_kind_is_a_400_with_a_complete_message_in_the_requested_language()
    {
        var host = new FakeDesktopHost();
        await using var server = await TestServer.StartAsync(host);
        var request = Post("/api/desktop/folders/nonsense/open");
        request.Headers.Add("Accept-Language", "en");
        var response = await server.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("not a known folder", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_host_that_cannot_open_the_folder_answers_500_with_the_reason()
    {
        var host = new FakeDesktopHost(openWorks: false);
        await using var server = await TestServer.StartAsync(host);
        var response = await server.Client.SendAsync(Post("/api/desktop/folders/logs/open"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("explorer.exe was not found", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_host_that_lists_no_folders_opens_nothing()
    {
        // The default interface members (a host that does not know folders)
        await using var server = await TestServer.StartAsync(new BareHost());
        var list = await server.Client.GetFromJsonAsync<JsonElement>("/api/desktop/folders");
        Assert.Equal(0, list.GetProperty("folders").GetArrayLength());
        var open = await server.Client.SendAsync(Post("/api/desktop/folders/data/open"));
        Assert.Equal(HttpStatusCode.NotFound, open.StatusCode);
    }

    private sealed class BareHost : IDesktopHost
    {
        public LocalLaunchResult LaunchGame(int appId) => LocalLaunchResult.Ok();
    }
}
