using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Hubs;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// A fake IGDB (and Twitch token) behind <see cref="IHttpClientFactory"/>: answers by endpoint and query, records every request, never touches
/// the network. Three games: 1001 "Fake Quest" (PS5 + PC, Main Game), 1003 a DLC of it (must be filtered from a name search), 1002 "Fake Quest 2"
/// (Remake, Xbox Series X|S); 1001 and 1002 form the collection "Fake Series".
/// </summary>
internal sealed class FakeIgdb : HttpMessageHandler, IHttpClientFactory
{
    private readonly object _lock = new();
    private readonly List<(string Endpoint, string Body)> _requests = [];

    /// <summary>Every IGDB call answers 500 with a message (the Twitch token call still works).</summary>
    public bool Fail { get; set; }

    public List<(string Endpoint, string Body)> Requests
    {
        get { lock (_lock) return [.. _requests]; }
    }

    public int Count(string endpoint, string? bodyContains = null) =>
        Requests.Count(r => r.Endpoint == endpoint && (bodyContains is null || r.Body.Contains(bodyContains, StringComparison.Ordinal)));

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url.StartsWith("https://id.twitch.tv/oauth2/token", StringComparison.Ordinal))
        {
            return Json("{\"access_token\":\"fake-token\",\"expires_in\":3600}");
        }
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var endpoint = url[(url.LastIndexOf('/') + 1)..];
        lock (_lock) _requests.Add((endpoint, body));

        if (Fail)
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"message\":\"boom\"}", Encoding.UTF8, "application/json") };
        }

        return endpoint switch
        {
            "game_types" => Json("[{\"id\":0,\"type\":\"Main Game\"},{\"id\":1,\"type\":\"DLC\"},{\"id\":8,\"type\":\"Remake\"},{\"id\":9,\"type\":\"Remaster\"}]"),
            "involved_companies" => Json(
                "[{\"id\":1,\"company\":{\"id\":10,\"name\":\"Fake Studio\"},\"developer\":true,\"publisher\":false}," +
                "{\"id\":2,\"company\":{\"id\":11,\"name\":\"Fake Publishing\"},\"developer\":false,\"publisher\":true}," +
                "{\"id\":3,\"company\":{\"id\":10,\"name\":\"Fake Studio\"},\"developer\":false,\"publisher\":true}]"),
            "game_time_to_beats" => Json("[{\"hastily\":36000,\"normally\":72000,\"completely\":144000,\"count\":5}]"),
            "games" => Json(Games(body)),
            _ => Json("[]"),
        };
    }

    private const string Ps5 = "{\"id\":167,\"name\":\"PlayStation 5\",\"abbreviation\":\"PS5\",\"slug\":\"ps5\"}";
    private const string Pc = "{\"id\":6,\"name\":\"PC (Microsoft Windows)\",\"abbreviation\":\"PC\",\"slug\":\"win\"}";
    private const string Series = "{\"id\":169,\"name\":\"Xbox Series X|S\",\"abbreviation\":\"Series X|S\",\"slug\":\"series-x-s\"}";

    private static string Games(string body)
    {
        if (body.Contains("fields name,url,summary", StringComparison.Ordinal))
        {
            // The facts of one game: where id = N
            var id = IdIn(body);
            return id switch
            {
                1001 => "[{\"id\":1001,\"name\":\"Fake Quest\",\"url\":\"https://www.igdb.com/games/fake-quest\",\"summary\":\"A made-up game.\",\"first_release_date\":1700000000," +
                        "\"aggregated_rating\":82.4,\"aggregated_rating_count\":9,\"rating\":71.6,\"rating_count\":120,\"total_rating\":76.5,\"total_rating_count\":129," +
                        "\"genres\":[{\"id\":12,\"name\":\"Role-playing (RPG)\"},{\"id\":31,\"name\":\"Adventure\"}]," +
                        $"\"platforms\":[{Ps5},{Pc}],\"cover\":{{\"id\":1,\"image_id\":\"co1abc\"}},\"artworks\":[{{\"id\":1,\"image_id\":\"ar1xyz\"}}],\"screenshots\":[{{\"id\":2,\"image_id\":\"sc2def\"}}]}}]",
                1002 => $"[{{\"id\":1002,\"name\":\"Fake Quest 2\",\"first_release_date\":1750000000,\"platforms\":[{Series}],\"screenshots\":[{{\"id\":3,\"image_id\":\"sc3ghi\"}}]}}]",
                _ => "[]",
            };
        }
        if (body.Contains("collections.name,collections.games", StringComparison.Ordinal))
        {
            return "[{\"id\":1001,\"collections\":[{\"id\":5,\"name\":\"Fake Series\",\"games\":[1001,1002]}]}]";
        }
        if (body.Contains("version_parent = null & parent_game = null", StringComparison.Ordinal))
        {
            return "[{\"id\":1001,\"name\":\"Fake Quest\",\"first_release_date\":1700000000,\"game_type\":0},{\"id\":1002,\"name\":\"Fake Quest 2\",\"first_release_date\":1750000000,\"game_type\":8}]";
        }
        if (body.TrimStart().StartsWith("search ", StringComparison.Ordinal) || body.Contains("first_release_date,cover.image_id,platforms.name", StringComparison.Ordinal))
        {
            if (body.Contains("where slug = \"fake-quest\"", StringComparison.Ordinal) || body.Contains("where id = 1001", StringComparison.Ordinal))
            {
                return Hit1001;
            }
            if (body.Contains("where id = 1002", StringComparison.Ordinal))
            {
                return Hit1002;
            }
            if (body.Contains("where id = ", StringComparison.Ordinal) || body.Contains("where slug = ", StringComparison.Ordinal))
            {
                return "[]";
            }
            return $"[{Hit1001[1..^1]},{{\"id\":1003,\"name\":\"Fake Quest - Extra Levels\",\"first_release_date\":1710000000,\"game_type\":1}},{Hit1002[1..^1]}]";
        }
        return "[]";
    }

    private static string Hit1001 =>
        $"[{{\"id\":1001,\"name\":\"Fake Quest\",\"slug\":\"fake-quest\",\"first_release_date\":1700000000,\"cover\":{{\"id\":1,\"image_id\":\"co1abc\"}},\"platforms\":[{Ps5},{Pc}],\"game_type\":0}}]";

    private static string Hit1002 =>
        $"[{{\"id\":1002,\"name\":\"Fake Quest 2\",\"slug\":\"fake-quest-2\",\"first_release_date\":1750000000,\"platforms\":[{Series}],\"game_type\":8}}]";

    private static int IdIn(string body)
    {
        var marker = "where id = ";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return 0;
        var digits = new string(body[(start + marker.Length)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var id) ? id : 0;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>The live hub of a test server: records everything sent to "all clients" (popups, platinum, pin changes).</summary>
internal sealed class HubRecorder : IHubContext<AchievementHub>, IClientProxy
{
    private readonly List<(string Method, object?[] Args)> _sent = [];

    public List<(string Method, object?[] Args)> Sent
    {
        get { lock (_sent) return [.. _sent]; }
    }

    public int Count(string method) => Sent.Count(s => s.Method == method);

    public IHubClients Clients => new Everyone(this);

    public IGroupManager Groups => throw new NotSupportedException();

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        lock (_sent) _sent.Add((method, args));
        return Task.CompletedTask;
    }

    private sealed class Everyone(IClientProxy proxy) : IHubClients
    {
        public IClientProxy All => proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Client(string connectionId) => proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;
        public IClientProxy Group(string groupName) => proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => proxy;
        public IClientProxy User(string userId) => proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => proxy;
    }
}

/// <summary>A real server on a temporary SQLite file with a fake hub and a fake IGDB; helpers to seed games and call the API.</summary>
internal sealed class ManualServer : IAsyncDisposable
{
    private ManualServer(TestServer server, FakeIgdb igdb, HubRecorder hub)
    {
        Server = server;
        Igdb = igdb;
        Hub = hub;
    }

    public TestServer Server { get; }
    public FakeIgdb Igdb { get; }
    public HubRecorder Hub { get; }
    public HttpClient Client => Server.Client;
    public IServiceProvider Services => Server.Services;

    /// <param name="credentials">Store Twitch Client-ID and Client-Secret (the fake IGDB answers for them).</param>
    public static async Task<ManualServer> StartAsync(bool credentials = true)
    {
        var igdb = new FakeIgdb();
        var hub = new HubRecorder();
        var server = await TestServer.StartAsync(null, b =>
        {
            b.Services.AddSingleton<IHttpClientFactory>(igdb);
            b.Services.AddSingleton<IHubContext<AchievementHub>>(hub);
        });
        server.Client.DefaultRequestHeaders.Add("X-Requested-With", "PlatinVault");
        if (credentials)
        {
            var secrets = server.Services.GetRequiredService<SecretStore>();
            secrets.SetSecret(SettingsStore.SecretTwitchClientId, "client-id-12345");
            secrets.SetSecret(SettingsStore.SecretTwitchClientSecret, "client-secret-12345");
        }
        return new ManualServer(server, igdb, hub);
    }

    public async Task<T> WithDbAsync<T>(Func<CompanionDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<CompanionDbContext>());
    }

    public Task WithDbAsync(Func<CompanionDbContext, Task> work) => WithDbAsync(async db =>
    {
        await work(db);
        return 0;
    });

    /// <summary>A manual game with the given trophies (key, grade, group), researched now; returns its id.</summary>
    public async Task<Guid> SeedAsync(string slug, string platform, string? variant, params (string Key, string? Grade, string? Group)[] trophies) =>
        await WithDbAsync(async db =>
        {
            var game = new GameEntity
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Title = "Game " + slug,
                Platform = platform,
                PlatformVariant = variant,
                ContentLanguage = "de",
                IsFavorite = true,
                TrophyListResearchedAt = trophies.Length > 0 ? DateTimeOffset.UtcNow : null,
            };
            db.Games.Add(game);
            for (var i = 0; i < trophies.Length; i++)
            {
                db.GameTrophies.Add(TrophyFixtures.Trophy(game.Id, trophies[i].Key, i, trophies[i].Grade, trophies[i].Group));
            }
            await db.SaveChangesAsync();
            await TrophyProgress.RecomputeAsync(db, game.Id, DateTimeOffset.UtcNow, CancellationToken.None);
            return game.Id;
        });

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => Client.PostAsJsonAsync(url, body ?? new { });

    public Task<HttpResponseMessage> PutAsync(string url, object body) => Client.PutAsJsonAsync(url, body);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, byte[]? body = null, string? contentType = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentType is not null) request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }
        return await Client.SendAsync(request);
    }

    public ValueTask DisposeAsync() => Server.DisposeAsync();

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "null" : text);
        return doc.RootElement.Clone();
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> SendJsonAsync(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        return (response.StatusCode, await ReadAsync(response));
    }
}
