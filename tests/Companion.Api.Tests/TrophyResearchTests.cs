using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// The research of the trophy list of a game added by hand on a real server with a temporary SQLite file and FAKE providers (nothing here talks to a real
/// AI provider): the AI job (<c>ai-trophies</c>: facts only without IGDB, usage, fallback, ticks kept), the own-subscription way (prompt, check, answer) and
/// the translation of the list (D2).
/// </summary>
public sealed class TrophyResearchTests
{
    // ---- Fake providers -----------------------------------------------------------------------------------------------------------------------

    private sealed class FakeAi : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        public Func<string, string> Gemini { get; set; } = _ => "{}";
        public Func<HttpResponseMessage>? GeminiFailure { get; set; }
        public Func<string, string>? Claude { get; set; }
        public List<(string Provider, string Prompt)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            if (host == "gemini.test")
            {
                var prompt = body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();
                lock (_lock) Calls.Add(("gemini", prompt));
                if (GeminiFailure is not null && !IsTranslation(prompt))
                {
                    return GeminiFailure();
                }
                var text = IsTranslation(prompt) ? Translate(prompt) : Gemini(prompt);
                var json = new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) }, ["finishReason"] = "STOP" }) };
                return Ok(json.ToJsonString());
            }
            if (host == "claude.test" && Claude is not null)
            {
                var prompt = body["messages"]![0]!["content"]!.GetValue<string>();
                lock (_lock) Calls.Add(("claude", prompt));
                var json = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Claude(prompt) }), ["stop_reason"] = "end_turn" };
                return Ok(json.ToJsonString());
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static bool IsTranslation(string prompt) => prompt.StartsWith("You translate the texts", StringComparison.Ordinal);

        // Every item comes back as "EN: <text>"
        private static string Translate(string prompt)
        {
            var items = JsonNode.Parse(prompt[(prompt.LastIndexOf("Items:", StringComparison.Ordinal) + 6)..].Trim())!.AsArray();
            return new JsonObject
            {
                ["items"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["id"] = i!["id"]!.GetValue<int>(), ["text"] = "EN: " + i["text"]!.GetValue<string>() }).ToArray()),
            }.ToJsonString();
        }

        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static async Task<(TestServer Server, FakeAi Ai)> StartAsync(bool claude = false)
    {
        var ai = new FakeAi();
        var server = await TestServer.StartAsync(desktop: null, b =>
        {
            b.Configuration["Companion:Ai:GeminiBaseUrl"] = "http://gemini.test/v1beta";
            b.Configuration["Companion:Ai:ClaudeBaseUrl"] = "http://claude.test/v1";
            b.Services.AddSingleton<IHttpClientFactory>(new FakeFactory(ai));
        });
        var secrets = server.Services.GetRequiredService<SecretStore>();
        secrets.SetSecret(SettingsStore.SecretGemini, "fake-gemini-key");
        if (claude)
        {
            secrets.SetSecret(SettingsStore.SecretClaude, "fake-claude-key");
        }
        return (server, ai);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(TestServer server, string url, object? body = null, string language = "de")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        request.Headers.Add("Accept-Language", language);
        var response = await server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<T> WithDbAsync<T>(TestServer server, Func<CompanionDbContext, Task<T>> work)
    {
        using var scope = server.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<CompanionDbContext>());
    }

    private static Task<Guid> SeedAsync(TestServer server, string slug, string platform, string? variant, string? factsSource = null, string? developer = null,
        string language = "de", params (string Key, string Name, string? Grade, bool Ticked)[] trophies) =>
        WithDbAsync(server, async db =>
        {
            var game = new GameEntity
            {
                Id = Guid.NewGuid(), Slug = slug, Title = "Astral Run", Platform = platform, PlatformVariant = variant, ContentLanguage = language,
                IsFavorite = true, FactsSource = factsSource, Developer = developer,
                TrophyListResearchedAt = trophies.Length > 0 ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            };
            db.Games.Add(game);
            for (var i = 0; i < trophies.Length; i++)
            {
                db.GameTrophies.Add(new GameTrophyEntity
                {
                    Id = Guid.NewGuid(), GameId = game.Id, TrophyKey = trophies[i].Key, Name = trophies[i].Name, SortOrder = i, Grade = trophies[i].Grade,
                    Description = "Do " + trophies[i].Name, UnlockedAt = trophies[i].Ticked ? DateTimeOffset.UtcNow.AddHours(-2) : null,
                });
            }
            await db.SaveChangesAsync();
            return game.Id;
        });

    private static async Task<JsonElement> RunJobAsync(TestServer server, string slug, object? body = null)
    {
        var (status, job) = await PostAsync(server, $"/api/games/{slug}/ai-trophies", body);
        Assert.True(status == HttpStatusCode.Accepted, job.ToString());
        Assert.Equal("trophies", job.GetProperty("kind").GetString());
        for (var i = 0; i < 200; i++)
        {
            var state = await server.Client.GetFromJsonAsync<JsonElement>($"/api/ai/jobs/{job.GetProperty("id").GetString()}");
            if (!state.GetProperty("running").GetBoolean())
            {
                return state;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The AI job did not finish.");
    }

    private const string PsAnswer = """
        { "game": "Astral Run", "platform": "ps5", "documented": true, "trophies": [
          { "id": "astral-champion", "name": "Astralmeister", "originalName": "Astral Champion", "grade": "platinum", "description": "Alle Trophäen.", "category": "Story" },
          { "id": "first-lap", "name": "Erste Runde", "originalName": "First Lap", "grade": "bronze", "description": "Beende das erste Rennen.", "tip": "Fahr einfach." },
          { "id": "frozen-rift", "name": "Eisriss", "grade": "gold", "group": "Frozen Rift DLC", "hidden": true } ],
          "facts": { "developers": ["Fake Dev"] },
          "sources": [ { "title": "PSNProfiles", "url": "https://psnprofiles.com/trophies/1-astral-run" } ] }
        """;

    private static Task<List<GameTrophyEntity>> TrophiesAsync(TestServer server, Guid id) =>
        WithDbAsync(server, db => db.GameTrophies.AsNoTracking().Where(t => t.GameId == id).OrderBy(t => t.SortOrder).ToListAsync());

    private static Task<GameEntity> GameAsync(TestServer server, Guid id) => WithDbAsync(server, db => db.Games.AsNoTracking().SingleAsync(g => g.Id == id));

    // ---- AI job -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_job_saves_the_list_and_never_overwrites_IGDB_facts()
    {
        var (server, ai) = await StartAsync();
        await using var _ = server;
        ai.Gemini = _ => PsAnswer;
        var id = await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb", developer: "Nebula Works");

        var done = await RunJobAsync(server, "astral-run");

        Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());
        var result = done.GetProperty("result");
        Assert.Equal(["gemini"], result.GetProperty("ai").GetProperty("providers").EnumerateArray().Select(p => p.GetString()));
        Assert.False(result.GetProperty("game").GetProperty("factsUpdated").GetBoolean());
        Assert.Equal(3, result.GetProperty("game").GetProperty("list").GetProperty("totalCount").GetInt32());
        var trophies = await TrophiesAsync(server, id);
        Assert.Equal(["astral-champion", "first-lap", "frozen-rift"], trophies.Select(t => t.TrophyKey));
        Assert.Equal(("Frozen Rift DLC", true), (trophies[2].GroupName, trophies[2].IsHidden));
        var game = await GameAsync(server, id);
        Assert.Equal(("igdb", "Nebula Works"), (game.FactsSource, game.Developer));
        Assert.NotNull(game.TrophyListResearchedAt);
        Assert.Contains("psnprofiles.com", game.TrophyListSourcesJson);
        // The prompt asked for the list of this version and not for facts
        var prompt = Assert.Single(ai.Calls).Prompt;
        Assert.Contains("PS5", prompt);
        Assert.DoesNotContain("\"facts\"", prompt);
    }

    [Fact]
    public async Task Without_IGDB_the_same_call_researches_the_facts()
    {
        var (server, ai) = await StartAsync();
        await using var _ = server;
        ai.Gemini = _ => PsAnswer;
        var id = await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5);

        var done = await RunJobAsync(server, "astral-run");

        Assert.True(done.GetProperty("result").GetProperty("game").GetProperty("factsUpdated").GetBoolean(), done.ToString());
        var game = await GameAsync(server, id);
        Assert.Equal(("ai", "Fake Dev"), (game.FactsSource, game.Developer));
        Assert.Contains("\"facts\"", Assert.Single(ai.Calls).Prompt);
    }

    [Fact]
    public async Task Gemini_out_of_quota_continues_with_Claude_and_the_usage_says_so()
    {
        var (server, ai) = await StartAsync(claude: true);
        await using var _ = server;
        ai.GeminiFailure = () => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"message":"Resource has been exhausted","status":"RESOURCE_EXHAUSTED"}}""", Encoding.UTF8, "application/json"),
        };
        ai.Claude = _ => PsAnswer;
        await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb");

        var done = await RunJobAsync(server, "astral-run");

        Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());
        var usage = done.GetProperty("result").GetProperty("ai");
        Assert.Equal(["claude"], usage.GetProperty("providers").EnumerateArray().Select(p => p.GetString()));
        var change = Assert.Single(usage.GetProperty("switches").EnumerateArray());
        Assert.Equal(("gemini", "claude", "quota"), (change.GetProperty("from").GetString(), change.GetProperty("to").GetString(), change.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task A_re_research_keeps_the_ticks_by_key_and_by_name_and_keeps_ticked_trophies_it_lacks()
    {
        var (server, ai) = await StartAsync();
        await using var _ = server;
        ai.Gemini = _ => PsAnswer;
        var id = await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb", trophies:
        [
            ("first-lap", "Erste Runde", TrophyTiers.Bronze, true),     // same key
            ("eis", "Eisriss", TrophyTiers.Gold, true),                  // other key, same name -> keeps "eis"
            ("old-ticked", "Weg", TrophyTiers.Bronze, true),             // not in the new list, ticked -> kept
            ("old-open", "Auch weg", TrophyTiers.Bronze, false),         // not in the new list, open -> removed
        ]);
        var before = await TrophiesAsync(server, id);

        var done = await RunJobAsync(server, "astral-run");

        var result = done.GetProperty("result").GetProperty("game");
        Assert.Equal((2, 1), (result.GetProperty("keptTicks").GetInt32(), result.GetProperty("retainedOldTicked").GetInt32()));
        var after = await TrophiesAsync(server, id);
        Assert.Equal(["astral-champion", "first-lap", "eis", "old-ticked"], after.Select(t => t.TrophyKey));
        Assert.Equal(before.Single(t => t.TrophyKey == "first-lap").UnlockedAt, after.Single(t => t.TrophyKey == "first-lap").UnlockedAt);
        Assert.Equal(before.Single(t => t.TrophyKey == "eis").Id, after.Single(t => t.TrophyKey == "eis").Id);
        Assert.NotNull(after.Single(t => t.TrophyKey == "old-ticked").UnlockedAt);
        Assert.Null(after.Single(t => t.TrophyKey == "astral-champion").UnlockedAt);   // nothing is ticked by a research
        // The keep list of the prompt named the old ids
        Assert.Contains("old-open | Auch weg", Assert.Single(ai.Calls).Prompt);
    }

    [Fact]
    public async Task No_reliable_list_fails_the_job_and_changes_nothing()
    {
        var (server, ai) = await StartAsync();
        await using var _ = server;
        ai.Gemini = _ => """{ "game": "Astral Run", "documented": false, "reason": "Nur die PS4-Liste ist dokumentiert." }""";
        var id = await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb", trophies: [("first-lap", "Erste Runde", "bronze", true)]);

        var done = await RunJobAsync(server, "astral-run");

        Assert.Contains("Nur die PS4-Liste ist dokumentiert.", done.GetProperty("error").GetString());
        Assert.Equal(["first-lap"], (await TrophiesAsync(server, id)).Select(t => t.TrophyKey));
    }

    [Fact]
    public async Task Steam_games_and_games_with_nothing_to_research_are_refused_and_unknown_games_are_404()
    {
        var (server, _) = await StartAsync();
        await using var __ = server;
        await WithDbAsync(server, async db =>
        {
            db.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "steam-game", Title = "Steam Game", SteamAppId = 4078110, ContentLanguage = "de" });
            return await db.SaveChangesAsync();
        });
        await SeedAsync(server, "meadow", GamePlatforms.Nintendo, null, factsSource: "igdb", developer: "Clover");

        var (steam, steamBody) = await PostAsync(server, "/api/games/steam-game/ai-trophies");
        var (nintendo, nintendoBody) = await PostAsync(server, "/api/games/meadow/ai-trophies", null, "en");
        var (steamPrompt, _) = await PostAsync(server, "/api/games/steam-game/manual/trophies-prompt");
        var (missing, _) = await PostAsync(server, "/api/games/nope/ai-trophies");

        Assert.Equal(HttpStatusCode.BadRequest, steam);
        Assert.Contains("Steam", steamBody.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, nintendo);
        Assert.Contains("nothing to research", nintendoBody.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, steamPrompt);
        Assert.Equal(HttpStatusCode.NotFound, missing);
    }

    // ---- Own subscription ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_prompt_check_and_answer_of_the_own_subscription_save_the_list()
    {
        var (server, ai) = await StartAsync();
        await using var _ = server;
        var id = await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb", trophies: [("first-lap", "Erste Runde", "bronze", true)]);

        var (promptStatus, prompt) = await PostAsync(server, "/api/games/astral-run/manual/trophies-prompt");
        var (checkStatus, check) = await PostAsync(server, "/api/games/astral-run/manual/trophies-check", new { text = "```json\n" + PsAnswer + "\n```" });
        var (answerStatus, answer) = await PostAsync(server, "/api/games/astral-run/manual/trophies-answer", new { text = PsAnswer });

        Assert.Equal(HttpStatusCode.OK, promptStatus);
        Assert.Equal("trophies", prompt.GetProperty("kind").GetString());
        Assert.Equal("platinvault-astral-run-trophies.md", prompt.GetProperty("fileName").GetString());
        Assert.Contains("platinvault-astral-run-trophies.json", prompt.GetProperty("prompt").GetString());
        Assert.Contains("first-lap | Erste Runde", prompt.GetProperty("prompt").GetString());
        Assert.Equal(HttpStatusCode.OK, checkStatus);
        Assert.True(check.GetProperty("ok").GetBoolean(), check.ToString());
        Assert.Equal((3, 1, 1, 1), (check.GetProperty("count").GetInt32(), check.GetProperty("hidden").GetInt32(), check.GetProperty("dlc").GetInt32(), check.GetProperty("keepsTicks").GetInt32()));
        Assert.Equal(1, check.GetProperty("byGrade").GetProperty("platinum").GetInt32());
        Assert.Contains(check.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "factsFromIgdbKept");
        Assert.Equal(HttpStatusCode.OK, answerStatus);
        Assert.Equal(1, answer.GetProperty("keptTicks").GetInt32());
        Assert.Equal(3, (await TrophiesAsync(server, id)).Count);
        Assert.Empty(ai.Calls);   // nothing was sent to a provider
    }

    [Fact]
    public async Task An_answer_with_errors_is_a_400_with_the_full_list_and_changes_nothing()
    {
        var (server, _) = await StartAsync();
        await using var __ = server;
        var id = await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb", trophies: [("first-lap", "Erste Runde", "bronze", true)]);
        const string twoPlatinum = """{ "game": "Astral Run", "trophies": [ { "id": "a", "name": "A", "grade": "platinum" }, { "id": "b", "name": "B", "grade": "platinum" } ] }""";

        var (status, body) = await PostAsync(server, "/api/games/astral-run/manual/trophies-answer", new { text = twoPlatinum });
        var (_, english) = await PostAsync(server, "/api/games/astral-run/manual/trophies-answer", new { text = "no json" }, "en");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Nicht gespeichert", body.GetProperty("title").GetString());
        Assert.Equal("severalPlatinum", body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("Not saved", english.GetProperty("title").GetString());
        Assert.Equal(["first-lap"], (await TrophiesAsync(server, id)).Select(t => t.TrophyKey));
    }

    // ---- Translation (D2) ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_translation_job_translates_the_list_texts_only_and_uses_the_official_English_names()
    {
        var (server, ai) = await StartAsync();
        await using var _ = server;
        ai.Gemini = _ => PsAnswer;
        var id = await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb");
        await RunJobAsync(server, "astral-run");

        async Task<TrophyTranslationView> ViewAsync() => await WithDbAsync(server, async db =>
        {
            var game = await db.Games.AsNoTracking().SingleAsync(g => g.Id == id);
            var trophies = await db.GameTrophies.AsNoTracking().Where(t => t.GameId == id).ToListAsync();
            return await TrophyTranslations.EvaluateAsync(db, game, trophies, Lang.English, null, CancellationToken.None);
        });
        Assert.Equal("missing", (await ViewAsync()).State);

        var (status, _) = await PostAsync(server, "/api/games/astral-run/translate?lang=en", null, "en");
        Assert.Equal(HttpStatusCode.OK, status);
        TrophyTranslationView view = await ViewAsync();
        for (var i = 0; i < 200 && view.State != "complete"; i++)
        {
            await Task.Delay(50);
            view = await ViewAsync();
        }

        Assert.Equal("complete", view.State);
        var overlay = view.Overlay!;
        Assert.Equal(["astral-champion", "first-lap", "frozen-rift"], overlay.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Astral Champion", overlay["astral-champion"].Name);           // the official English name, not a translation
        Assert.Equal("EN: Eisriss", overlay["frozen-rift"].Name);                   // no official name stored: translated
        Assert.Equal("EN: Beende das erste Rennen.", overlay["first-lap"].Description);
        Assert.Equal("EN: Frozen Rift DLC", overlay["frozen-rift"].Group);
        // Keys, grades and flags are not part of the translation
        Assert.DoesNotContain(ai.Calls.Where(c => c.Prompt.StartsWith("You translate", StringComparison.Ordinal)), c => c.Prompt.Contains("first-lap") || c.Prompt.Contains("\"bronze\""));
        Assert.Equal("Astral Champion", (await TrophiesAsync(server, id))[0].OriginalName);

        // A re-researched list is "missing" again until it is translated anew
        ai.Gemini = _ => PsAnswer.Replace("Beende das erste Rennen.", "Gewinne das erste Rennen.");
        await RunJobAsync(server, "astral-run");
        Assert.Equal("missing", (await ViewAsync()).State);
    }

    [Fact]
    public async Task The_trophy_endpoints_show_the_translation_once_it_is_complete_and_the_original_until_then()
    {
        var (server, ai) = await StartAsync();
        await using var _ = server;
        ai.Gemini = _ => PsAnswer;
        await SeedAsync(server, "astral-run", GamePlatforms.PlayStation, GamePlatforms.Ps5, factsSource: "igdb");
        await RunJobAsync(server, "astral-run");

        async Task<JsonElement> ListAsync(string lang) =>
            await server.Client.GetFromJsonAsync<JsonElement>($"/api/games/astral-run/trophies?lang={lang}");
        static string[] Names(JsonElement list) => list.GetProperty("achievements").EnumerateArray().Select(a => a.GetProperty("displayName").GetString()!).ToArray();

        // Before the translation: the original, and the state says it is missing
        var before = await ListAsync("en");
        Assert.Equal(("de", "missing"), (before.GetProperty("language").GetString(), before.GetProperty("translation").GetProperty("state").GetString()));
        Assert.Equal(["Astralmeister", "Erste Runde", "Eisriss"], Names(before));
        Assert.False(before.GetProperty("achievements")[0].GetProperty("isTranslated").GetBoolean());
        // The list in its own language is "original"
        Assert.Equal("original", (await ListAsync("de")).GetProperty("translation").GetProperty("state").GetString());

        var (status, started) = await PostAsync(server, "/api/games/astral-run/translate?lang=en", null, "en");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEqual("original", started.GetProperty("state").GetString());
        JsonElement translated = default;
        for (var i = 0; i < 200; i++)
        {
            translated = await ListAsync("en");
            if (translated.GetProperty("translation").GetProperty("state").GetString() == "complete")
            {
                break;
            }
            await Task.Delay(50);
        }

        // Complete: the translated texts, language "en", official English name where the list has one; keys/grades untouched
        Assert.Equal("complete", translated.GetProperty("translation").GetProperty("state").GetString());
        Assert.Equal("en", translated.GetProperty("language").GetString());
        Assert.Equal(["Astral Champion", "First Lap", "EN: Eisriss"], Names(translated));
        var first = translated.GetProperty("achievements")[1];
        Assert.Equal("EN: Beende das erste Rennen.", first.GetProperty("description").GetString());
        Assert.Equal("Beende das erste Rennen.", first.GetProperty("originalDescription").GetString());
        Assert.True(first.GetProperty("isTranslated").GetBoolean());
        Assert.Equal(("first-lap", "bronze"), (first.GetProperty("apiName").GetString(), first.GetProperty("grade").GetString()));
        Assert.Equal("EN: Frozen Rift DLC", translated.GetProperty("achievements")[2].GetProperty("group").GetString());
        // The original is still there for a German reader
        Assert.Equal(["Astralmeister", "Erste Runde", "Eisriss"], Names(await ListAsync("de")));

        // The answer of a tick is the list in the language of the request, translated as well
        var (_, ticked) = await PostAsync(server, "/api/games/astral-run/trophies/first-lap/unlock", new { unlocked = true }, "en");
        Assert.Equal("First Lap", Names(ticked)[1]);
        Assert.Equal("complete", ticked.GetProperty("translation").GetProperty("state").GetString());

        // A re-researched list is "missing" again until it is translated anew: the endpoint falls back to the original
        ai.Gemini = _ => PsAnswer.Replace("Beende das erste Rennen.", "Gewinne das erste Rennen.");
        await RunJobAsync(server, "astral-run");
        var after = await ListAsync("en");
        Assert.Equal("missing", after.GetProperty("translation").GetProperty("state").GetString());
        Assert.Equal("Erste Runde", Names(after)[1]);
    }

    [Fact]
    public async Task The_list_in_its_own_language_is_the_original_and_an_empty_list_needs_no_translation()
    {
        var game = new GameEntity { Platform = GamePlatforms.Xbox, ContentLanguage = Lang.German };
        var trophies = new List<GameTrophyEntity> { new() { TrophyKey = "a", Name = "A" } };

        Assert.Equal("original", TrophyTranslations.Evaluate(game, trophies, Lang.German, null, null).State);
        Assert.Equal("complete", TrophyTranslations.Evaluate(game, [], Lang.English, null, null).State);
        Assert.Equal("missing", TrophyTranslations.Evaluate(game, trophies, Lang.English, null, null).State);
        var row = new GameTrophyTranslationEntity { ItemsJson = """[{"key":"a","name":"EN A","description":"","tip":"","group":null,"category":""}]""", SourceHash = TrophyTranslations.Hash(trophies) };
        var complete = TrophyTranslations.Evaluate(game, trophies, Lang.English, row, null);
        Assert.Equal(("complete", "EN A"), (complete.State, complete.Overlay!["a"].Name));
        Assert.Equal("en", complete.ToDto().Language);
        trophies[0].Points = 99;   // points are no text: the translation stays current
        Assert.Equal("complete", TrophyTranslations.Evaluate(game, trophies, Lang.English, row, null).State);
        trophies[0].Name = "B";
        Assert.Equal("missing", TrophyTranslations.Evaluate(game, trophies, Lang.English, row, null).State);
        await Task.CompletedTask;
    }
}
