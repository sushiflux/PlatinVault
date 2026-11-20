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
/// The review of a chapter on a real server (temporary SQLite file) with FAKE providers - nothing talks to a real AI API: the job (second research by one AI, review
/// by another, save), the four combinations with the player's own chat, the refusals, the guard of the second research (no silent change of provider), a
/// truncated review answer, ticks that survive, "take the other version", the stale state and the summary in the guide.
/// </summary>
public sealed class ChapterReviewEndpointTests
{
    // ---- fake providers (Gemini and Claude by host name) ------------------------------------------------------------------------------------

    private sealed class FakeAi : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        public Func<string, string> Gemini { get; set; } = _ => "{}";
        public Func<string, string> Claude { get; set; } = _ => "{}";
        public Func<string, HttpResponseMessage?>? GeminiRaw { get; set; }
        public Func<string, HttpResponseMessage?>? ClaudeRaw { get; set; }
        public List<(string Provider, string Prompt)> Calls { get; } = [];

        public List<string> PromptsOf(string provider)
        {
            lock (_lock) return [.. Calls.Where(c => c.Provider == provider).Select(c => c.Prompt)];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            switch (request.RequestUri!.Host)
            {
                case "gemini.test":
                {
                    var prompt = body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();
                    lock (_lock) Calls.Add(("gemini", prompt));
                    if (GeminiRaw?.Invoke(prompt) is { } raw) return raw;
                    var json = new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Gemini(prompt) }) }, ["finishReason"] = "STOP" }) };
                    return Ok(json.ToJsonString());
                }
                case "claude.test":
                {
                    var prompt = body["messages"]![0]!["content"]!.GetValue<string>();
                    lock (_lock) Calls.Add(("claude", prompt));
                    if (ClaudeRaw?.Invoke(prompt) is { } raw) return raw;
                    var json = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Claude(prompt) }), ["stop_reason"] = "end_turn" };
                    return Ok(json.ToJsonString());
                }
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static async Task<(TestServer Server, FakeAi Ai)> StartAsync(bool gemini = true, bool claude = true, bool openai = false)
    {
        var ai = new FakeAi();
        var server = await TestServer.StartAsync(desktop: null, b =>
        {
            b.Configuration["Companion:Ai:GeminiBaseUrl"] = "http://gemini.test/v1beta";
            b.Configuration["Companion:Ai:ClaudeBaseUrl"] = "http://claude.test/v1";
            b.Services.AddSingleton<IHttpClientFactory>(new FakeFactory(ai));
        });
        var secrets = server.Services.GetRequiredService<SecretStore>();
        if (gemini) secrets.SetSecret(SettingsStore.SecretGemini, "fake-gemini");
        if (claude) secrets.SetSecret(SettingsStore.SecretClaude, "fake-claude");
        if (openai) secrets.SetSecret(SettingsStore.SecretOpenAi, "fake-openai");
        return (server, ai);
    }

    // ---- data -----------------------------------------------------------------------------------------------------------------------------

    private const string ChapterA = """
        [ { "type": "checklist_table", "table": { "tableId": "w", "caption": "Waffen", "checkable": true, "columns": [ { "key": "name", "label": "Name" }, { "key": "material", "label": "Material" } ],
            "rows": [ { "itemId": "schwert", "cells": { "name": "Schwert", "material": "Eisen" } }, { "itemId": "bogen", "cells": { "name": "Bogen", "material": "Eibe" } } ] } } ]
        """;

    // Other column keys than A ("n"/"m"): independent answers never agree on them
    private const string AnswerB = """
        { "game": "Suncraft", "title": "Waffen", "icon": "mdi:sword", "sections": [ { "type": "checklist_table", "table": { "tableId": "w", "caption": "Waffen", "checkable": true,
          "columns": [ { "key": "n", "label": "Name" }, { "key": "m", "label": "Material" } ],
          "rows": [ { "itemId": "schwert", "cells": { "n": "Schwert", "m": "Stahl" } }, { "itemId": "axt", "cells": { "n": "Axt", "m": "Holz" } } ] } } ] }
        """;

    private const string Merged = """
        [ { "type": "checklist_table", "table": { "tableId": "w", "caption": "Waffen", "checkable": true, "columns": [ { "key": "name", "label": "Name" }, { "key": "material", "label": "Material" } ],
            "rows": [ { "itemId": "schwert", "cells": { "name": "Schwert", "material": "Stahl" } }, { "itemId": "bogen", "cells": { "name": "Bogen", "material": "Eibe" } },
                      { "itemId": "axt", "cells": { "name": "Axt", "material": "Holz" } } ] } } ]
        """;

    private const string GoodFindings = """
        [ { "kind": "corrected", "target": { "itemId": "schwert" }, "column": "Material", "a": "Eisen", "b": "Stahl", "result": "Stahl", "source": "https://example.org/schwert", "reason": "Das Schwert ist aus Stahl." },
          { "kind": "added", "target": { "itemId": "axt" }, "b": "Axt – Holz", "source": "https://example.org/axt", "reason": "Die Axt gibt es." },
          { "kind": "unclear", "target": { "itemId": "bogen" }, "a": "Bogen – Eibe", "reason": "Keine Quelle nennt das Material des Bogens." } ]
        """;

    private static string ReviewAnswer(string sections = Merged, string findings = GoodFindings, string game = "Suncraft") =>
        $$"""{ "game": "{{game}}", "steamAppId": null, "findings": {{findings}}, "chapter": { "title": "Waffen", "icon": "mdi:sword", "sections": {{sections}} } }""";

    private static bool IsReviewPrompt(string prompt) => prompt.Contains("<<<ANTWORT_A", StringComparison.Ordinal);

    private sealed record Seeded(Guid Real, Guid Achievements, Guid Lore, Guid Walkthrough);

    private static async Task<Seeded> SeedAsync(TestServer server)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = new GameEntity { Id = Guid.NewGuid(), Slug = "suncraft", Title = "Suncraft", ContentLanguage = Lang.German };
        var real = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-1-waffen", Title = "Waffen", SortOrder = 1, GroupKey = "combat", SectionsJson = ChapterA };
        var walkthrough = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = WalkthroughTab.Slug, Title = "Komplettlösung", SortOrder = 0, SectionsJson = ChapterA.Replace("\"w\"", "\"wt\"").Replace("schwert", "wt-1").Replace("bogen", "wt-2") };
        var achievements = new GuideTabEntity
        {
            Id = Guid.NewGuid(), GameId = game.Id, Slug = "steam-achievements", Title = "Erfolge", SortOrder = 2,
            SectionsJson = """[ { "type": "checklist_table", "table": { "tableId": "steam-achievements", "caption": "Erfolge", "checkable": true, "columns": [], "rows": [ { "itemId": "ACH_KOL", "cells": {} } ] } } ]"""
        };
        var lore = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = LoreTab.Slug, Title = LoreTab.Title, SortOrder = LoreTab.SortOrder, SectionsJson = "[]" };
        game.Tabs = [real, achievements, lore, walkthrough];
        game.ProgressChecks =
        [
            new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "schwert", CheckedAt = DateTimeOffset.UtcNow },
            new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "bogen", CheckedAt = DateTimeOffset.UtcNow },
        ];
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new Seeded(real.Id, achievements.Id, lore.Id, walkthrough.Id);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(TestServer server, string url, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        request.Headers.Add("Accept-Language", "de");
        var response = await server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(TestServer server, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept-Language", "de");
        var response = await server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<JsonElement> WaitForJobAsync(TestServer server, JsonElement job)
    {
        var id = job.GetProperty("id").GetString();
        for (var i = 0; i < 200; i++)
        {
            var state = await server.Client.GetFromJsonAsync<JsonElement>($"/api/ai/jobs/{id}");
            if (!state.GetProperty("running").GetBoolean())
            {
                return state;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The AI job did not finish.");
    }

    private static async Task<(GuideTabEntity Tab, List<string> Checks, ChapterReviewEntity? Review)> StoredAsync(TestServer server, Guid tabId)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var tab = await db.GuideTabs.AsNoTracking().FirstAsync(t => t.Id == tabId);
        var checks = await db.ProgressChecks.AsNoTracking().Select(p => p.ItemKey).OrderBy(k => k).ToListAsync();
        return (tab, checks, await db.ChapterReviews.AsNoTracking().FirstOrDefaultAsync(r => r.TabId == tabId));
    }

    private static string ReviewUrl(Guid tab, string suffix = "") => $"/api/games/suncraft/chapters/{tab}/review{suffix}";

    private static async Task<JsonElement> RunFullReviewAsync(TestServer server, Seeded seed, object? body = null)
    {
        var (status, job) = await PostAsync(server, ReviewUrl(seed.Real), body ?? new { researcher = "gemini", reviewer = "claude" });
        Assert.Equal(HttpStatusCode.Accepted, status);
        return await WaitForJobAsync(server, job);
    }

    private static void WireHappyPath(FakeAi ai)
    {
        ai.Gemini = _ => AnswerB;
        ai.Claude = _ => ReviewAnswer();
    }

    // ---- the full job -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_job_researches_B_with_one_AI_reviews_with_the_other_and_saves_the_merged_chapter_with_the_ticks()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            WireHappyPath(ai);
            var seed = await SeedAsync(server);

            var (status, job) = await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini", reviewer = "claude" });
            Assert.Equal(HttpStatusCode.Accepted, status);
            Assert.Equal("review", job.GetProperty("kind").GetString());
            var done = await WaitForJobAsync(server, job);
            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());

            // Who did what: B = Gemini (the research prompt of "redo"), the review = Claude (author-blind prompt with both chapters inside)
            var geminiPrompt = Assert.Single(ai.PromptsOf("gemini"));
            Assert.False(IsReviewPrompt(geminiPrompt));
            Assert.Contains("bisherige Fassung hat diese Abschnitte", geminiPrompt);
            var claudePrompt = Assert.Single(ai.PromptsOf("claude"));
            Assert.True(IsReviewPrompt(claudePrompt));
            Assert.Contains("Eibe", claudePrompt);       // answer A is in it
            Assert.Contains("Holz", claudePrompt);       // so is answer B
            Assert.DoesNotContain("Gemini", claudePrompt, StringComparison.OrdinalIgnoreCase);

            // The stored chapter: merged, in place (same tab and address), A's id for entries both had, the marker written by the code
            var (tab, checks, review) = await StoredAsync(server, seed.Real);
            Assert.Equal("tab-1-waffen", tab.Slug);
            Assert.Equal(["bogen", "schwert"], checks);   // both ticks survive the merge
            Assert.Contains("\"axt\"", tab.SectionsJson);
            Assert.Contains("Stahl", tab.SectionsJson);
            Assert.Contains("review_unclear", tab.SectionsJson);
            var bogen = JsonNode.Parse(tab.SectionsJson)!.AsArray().SelectMany(s => s!["table"]?["rows"]?.AsArray() ?? []).Single(r => (string?)r!["itemId"] == "bogen")!;
            Assert.Equal("Bogen – Eibe", (string?)bogen["unclear"]!["a"]);

            // The stored review: both answers as they were, the findings, the counts, who and the hash of the saved chapter
            Assert.NotNull(review);
            Assert.Equal("gemini", review!.ResearcherProvider);
            Assert.Equal("claude", review.ReviewerProvider);
            Assert.Equal("de", review.Language);
            Assert.Contains("Eisen", review.OriginalSectionsJson);
            Assert.Contains("Stahl", review.SecondSectionsJson);
            Assert.DoesNotContain("unclear", review.OriginalSectionsJson);
            Assert.False(ChapterReview.IsStale(review.ResultHash, tab.SectionsJson));
            Assert.Equal(new ReviewCounts(0, 1, 1, 0, 1), ChapterReview.DeserializeCounts(review.CountsJson) with { WithoutSource = 0 });

            // The job result: game + ai like every AI result, plus the review and both usages
            var result = done.GetProperty("result");
            Assert.Equal("full", result.GetProperty("mode").GetString());
            Assert.Equal(["gemini", "claude"], result.GetProperty("ai").GetProperty("providers").EnumerateArray().Select(p => p.GetString()).ToArray());
            Assert.Equal("gemini", result.GetProperty("usage").GetProperty("researcher").GetProperty("providers")[0].GetString());
            Assert.Equal("claude", result.GetProperty("usage").GetProperty("reviewer").GetProperty("providers")[0].GetString());
            Assert.Equal(3, result.GetProperty("review").GetProperty("findings").GetArrayLength());
            Assert.False(result.GetProperty("review").GetProperty("stale").GetBoolean());
            Assert.Equal("Waffen", result.GetProperty("game").GetProperty("tabs").EnumerateArray().First(t => t.GetProperty("slug").GetString() == "tab-1-waffen").GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task The_guide_carries_a_small_review_summary_per_chapter_and_GET_review_reads_the_stored_one()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            WireHappyPath(ai);
            var seed = await SeedAsync(server);
            Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(server, ReviewUrl(seed.Real))).Status);
            var before = (await GetAsync(server, "/api/games/suncraft")).Body.GetProperty("tabs").EnumerateArray().First(t => t.GetProperty("slug").GetString() == "tab-1-waffen");
            Assert.True(before.GetProperty("review").ValueKind == JsonValueKind.Null);

            await RunFullReviewAsync(server, seed);

            var tab = (await GetAsync(server, "/api/games/suncraft")).Body.GetProperty("tabs").EnumerateArray().First(t => t.GetProperty("slug").GetString() == "tab-1-waffen");
            var line = tab.GetProperty("review");
            Assert.Equal("gemini", line.GetProperty("researcherProvider").GetString());
            Assert.Equal("claude", line.GetProperty("reviewerProvider").GetString());
            Assert.Equal(1, line.GetProperty("counts").GetProperty("corrected").GetInt32());
            Assert.False(line.GetProperty("stale").GetBoolean());
            // The marker of the open point is part of the chapter the guide serves
            Assert.Contains(tab.GetProperty("sections").EnumerateArray(), s => s.GetProperty("kind").GetString() == "review_unclear");

            var (status, review) = await GetAsync(server, ReviewUrl(seed.Real));
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("schwert", review.GetProperty("findings")[0].GetProperty("target").GetProperty("itemId").GetString());
            Assert.Equal("Material", review.GetProperty("findings")[0].GetProperty("column").GetString());
        }
    }

    [Fact]
    public async Task Removing_the_chapter_removes_its_review()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            WireHappyPath(ai);
            var seed = await SeedAsync(server);
            await RunFullReviewAsync(server, seed);
            Assert.NotNull((await StoredAsync(server, seed.Real)).Review);

            var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/games/suncraft/chapters/{seed.Real}");
            request.Headers.Add("X-Requested-With", "PlatinVault");
            Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(request)).StatusCode);

            using var scope = server.Services.CreateScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CompanionDbContext>().ChapterReviews.CountAsync());
        }
    }

    // ---- the guard in the job ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_entry_the_reviewer_drops_without_a_finding_is_put_back_and_marked_unclear()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            ai.Gemini = _ => AnswerB;
            // The reviewer lost "bogen" and says nothing about it
            var withoutBogen = Merged.Replace("""{ "itemId": "bogen", "cells": { "name": "Bogen", "material": "Eibe" } },""", string.Empty);
            ai.Claude = _ => ReviewAnswer(withoutBogen, "[]");
            var seed = await SeedAsync(server);

            var done = await RunFullReviewAsync(server, seed);

            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());
            var (tab, checks, review) = await StoredAsync(server, seed.Real);
            Assert.Contains("\"bogen\"", tab.SectionsJson);
            Assert.Equal(["bogen", "schwert"], checks);
            var findings = ChapterReview.DeserializeFindings(review!.FindingsJson);
            var finding = Assert.Single(findings);
            Assert.Equal("unclear", finding.Kind);
            Assert.Equal("bogen", finding.Target.ItemId);
            Assert.Contains("ohne Begründung", finding.Reason);
            Assert.Contains("warnings", done.GetProperty("result").ToString());
        }
    }

    // ---- refusals -----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_walkthrough_the_achievement_list_and_Story_and_Lore_are_refused_and_unknown_ones_are_not_found()
    {
        var (server, _) = await StartAsync();
        await using (server)
        {
            var seed = await SeedAsync(server);
            var body = new { researcher = "gemini", reviewer = "claude" };

            var walkthrough = await PostAsync(server, ReviewUrl(seed.Walkthrough), body);
            Assert.Equal(HttpStatusCode.Conflict, walkthrough.Status);
            Assert.Contains("Komplettlösung", walkthrough.Body.GetProperty("detail").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(server, ReviewUrl(seed.Achievements), body)).Status);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(server, ReviewUrl(seed.Lore), body)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, ReviewUrl(Guid.NewGuid()), body)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, $"/api/games/nope/chapters/{seed.Real}/review", body)).Status);
        }
    }

    [Fact]
    public async Task A_reviewer_without_web_search_or_a_key_and_a_review_by_the_same_AI_are_refused_with_complete_sentences()
    {
        var (server, ai) = await StartAsync(gemini: true, claude: true, openai: true);
        await using (server)
        {
            var seed = await SeedAsync(server);

            var noSearch = await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini", reviewer = "openai" });
            Assert.Equal(HttpStatusCode.BadRequest, noSearch.Status);
            Assert.Contains("nicht im Web suchen", noSearch.Body.GetProperty("detail").GetString());

            var same = await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "claude", reviewer = "claude" });
            Assert.Equal(HttpStatusCode.BadRequest, same.Status);
            Assert.Contains("nicht von derselben KI", same.Body.GetProperty("detail").GetString());

            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini" })).Status);                       // no reviewer
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, ReviewUrl(seed.Real), new { reviewer = "claude" })).Status);                          // no second answer
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini", reviewer = "claude", secondAnswer = AnswerB })).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "nobody", reviewer = "claude" })).Status);
            Assert.Empty(ai.Calls);   // nothing was asked of any AI
        }
    }

    [Fact]
    public async Task With_only_one_key_a_review_by_API_is_refused_and_says_a_review_needs_two_AIs()
    {
        var (server, ai) = await StartAsync(gemini: true, claude: false);
        await using (server)
        {
            var seed = await SeedAsync(server);

            var (status, body) = await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini", reviewer = "gemini" });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("zwei verschiedene KIs", body.GetProperty("detail").GetString());
            // No key for the reviewer
            var noKey = await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini", reviewer = "claude" });
            Assert.Equal(HttpStatusCode.BadRequest, noKey.Status);
            Assert.Contains("API-Key", noKey.Body.GetProperty("detail").GetString());
            Assert.Empty(ai.Calls);
        }
    }

    // ---- failures keep the chapter ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_research_that_says_the_topic_does_not_exist_ends_the_job_and_the_chapter_stays()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            ai.Gemini = _ => """{ "notApplicable": true, "reason": "Gibt es nicht." }""";
            var seed = await SeedAsync(server);
            var before = (await StoredAsync(server, seed.Real)).Tab.SectionsJson;

            var done = await RunFullReviewAsync(server, seed);

            Assert.Contains("bleibt unverändert", done.GetProperty("error").GetString());
            var (tab, checks, review) = await StoredAsync(server, seed.Real);
            Assert.Equal(before, tab.SectionsJson);
            Assert.Equal(["bogen", "schwert"], checks);
            Assert.Null(review);
            Assert.Empty(ai.PromptsOf("claude"));   // no review of a second answer that does not exist
        }
    }

    [Fact]
    public async Task A_failing_second_research_does_not_switch_to_the_reviewer_and_names_who_failed()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            ai.GeminiRaw = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("""{ "error": { "message": "Quota exceeded for this key." } }""", Encoding.UTF8, "application/json") };
            var seed = await SeedAsync(server);

            var done = await RunFullReviewAsync(server, seed);

            var error = done.GetProperty("error").GetString();
            Assert.Contains("zweite Recherche", error);
            Assert.Contains("Gemini", error);
            Assert.Equal("gemini", done.GetProperty("aiProvider").GetString());
            Assert.Equal("quota", done.GetProperty("aiFailure").GetString());
            Assert.Contains("Quota exceeded", done.GetProperty("providerMessage").GetString());   // the provider's own text stays complete
            Assert.Empty(ai.PromptsOf("claude"));       // the fixed run never falls back to the other AI
            Assert.Null((await StoredAsync(server, seed.Real)).Review);
        }
    }

    [Fact]
    public async Task A_review_answer_that_is_cut_off_twice_ends_with_the_complete_message_and_the_chapter_stays()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            ai.Gemini = _ => AnswerB;
            ai.ClaudeRaw = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "content": [ { "type": "text", "text": "{ \"game\": \"Suncraft\", \"findings\": [ { \"kind\": \"corr" } ], "stop_reason": "max_tokens" }""", Encoding.UTF8, "application/json")
            };
            var seed = await SeedAsync(server);
            var before = (await StoredAsync(server, seed.Real)).Tab.SectionsJson;

            var done = await RunFullReviewAsync(server, seed);

            var error = done.GetProperty("error").GetString();
            Assert.Contains("Die Prüfung durch Claude", error);
            Assert.Contains("zweiten, kompakteren Versuch", error);   // the same message as every cut-off content call
            Assert.Equal(2, ai.PromptsOf("claude").Count);             // one compact retry
            Assert.Equal(before, (await StoredAsync(server, seed.Real)).Tab.SectionsJson);
            Assert.Null((await StoredAsync(server, seed.Real)).Review);
        }
    }

    [Fact]
    public async Task A_review_answer_that_cannot_be_used_leaves_the_chapter_alone_and_says_why()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            ai.Gemini = _ => AnswerB;
            ai.Claude = _ => """{ "game": "Suncraft", "findings": [] }""";   // no chapter
            var seed = await SeedAsync(server);

            var done = await RunFullReviewAsync(server, seed);

            Assert.Contains("nicht verwendbar", done.GetProperty("error").GetString());
            Assert.Contains("chapter", done.GetProperty("error").GetString());
            Assert.Null((await StoredAsync(server, seed.Real)).Review);
            Assert.Equal(["bogen", "schwert"], (await StoredAsync(server, seed.Real)).Checks);
        }
    }

    [Fact]
    public async Task A_chapter_changed_while_the_review_ran_is_not_overwritten()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            var seed = await SeedAsync(server);
            ai.Gemini = _ => AnswerB;
            ai.Claude = _ =>
            {
                // Somebody (a second window) edits the chapter while the reviewer works
                using var scope = server.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                var tab = db.GuideTabs.First(t => t.Id == seed.Real);
                tab.SectionsJson = tab.SectionsJson.Replace("Eibe", "Esche");
                db.SaveChanges();
                return ReviewAnswer();
            };

            var done = await RunFullReviewAsync(server, seed);

            Assert.Contains("seit dem Start der Prüfung geändert", done.GetProperty("error").GetString());
            Assert.Contains("Esche", (await StoredAsync(server, seed.Real)).Tab.SectionsJson);
            Assert.Null((await StoredAsync(server, seed.Real)).Review);
        }
    }

    // ---- B from a chat, review by API (combination 2) ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_answer_pasted_from_a_chat_is_checked_then_reviewed_by_the_API_and_recorded_as_manual()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            ai.Claude = _ => ReviewAnswer();
            var seed = await SeedAsync(server);

            // A pasted answer that is not a chapter is refused right away (400 with the reasons), nothing is started
            var bad = await PostAsync(server, ReviewUrl(seed.Real), new { reviewer = "claude", secondAnswer = "Hier ist kein JSON." });
            Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
            Assert.True(bad.Body.GetProperty("errors").GetArrayLength() > 0);
            Assert.Empty(ai.Calls);

            var done = await RunFullReviewAsync(server, seed, new { reviewer = "claude", secondAnswer = "Hier meine Antwort:\n```json\n" + AnswerB + "\n```" });

            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());
            Assert.Empty(ai.PromptsOf("gemini"));       // B was not researched by an API
            var (_, _, review) = await StoredAsync(server, seed.Real);
            Assert.Equal("manual", review!.ResearcherProvider);
            Assert.Equal("claude", review.ReviewerProvider);
            Assert.Equal(JsonValueKind.Null, done.GetProperty("result").GetProperty("usage").GetProperty("researcher").ValueKind);
        }
    }

    // ---- B by API, review by chat (combination 3) and B + review by chat (combination 4) --------------------------------------------------------------

    [Fact]
    public async Task B_by_API_and_review_by_chat_hands_out_the_second_answer_and_the_prompt_and_the_pasted_review_is_saved()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            ai.Gemini = _ => AnswerB;
            var seed = await SeedAsync(server);

            var (status, job) = await PostAsync(server, ReviewUrl(seed.Real, "/second"), new { researcher = "gemini" });
            Assert.Equal(HttpStatusCode.Accepted, status);
            var done = await WaitForJobAsync(server, job);
            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());
            var prepared = done.GetProperty("result");
            Assert.Equal("second", prepared.GetProperty("mode").GetString());
            Assert.Equal("gemini", prepared.GetProperty("ai").GetProperty("providers")[0].GetString());
            var prompt = prepared.GetProperty("prompt").GetProperty("prompt").GetString()!;
            Assert.True(IsReviewPrompt(prompt));
            Assert.Contains("Eibe", prompt);
            Assert.Contains("Holz", prompt);
            Assert.Empty(ai.PromptsOf("claude"));       // the review is the player's chat, nothing is sent to a provider

            var answer = new { secondSections = prepared.GetProperty("secondSections"), answer = "Meine Prüfung:\n" + ReviewAnswer(), baseHash = prepared.GetProperty("baseHash").GetString(), researcher = "gemini" };
            var check = await PostAsync(server, ReviewUrl(seed.Real, "/manual-check"), answer);
            Assert.Equal(HttpStatusCode.OK, check.Status);
            Assert.True(check.Body.GetProperty("ok").GetBoolean());
            Assert.Equal(1, check.Body.GetProperty("preview").GetProperty("counts").GetProperty("unclear").GetInt32());
            Assert.Null((await StoredAsync(server, seed.Real)).Review);   // a check saves nothing

            var saved = await PostAsync(server, ReviewUrl(seed.Real, "/manual-answer"), answer);
            Assert.Equal(HttpStatusCode.OK, saved.Status);
            var (tab, checks, review) = await StoredAsync(server, seed.Real);
            Assert.Equal(["bogen", "schwert"], checks);
            Assert.Contains("\"axt\"", tab.SectionsJson);
            Assert.Equal("gemini", review!.ResearcherProvider);
            Assert.Equal("manual", review.ReviewerProvider);
            Assert.Equal("manual", saved.Body.GetProperty("review").GetProperty("reviewerProvider").GetString());
            Assert.Empty(ai.PromptsOf("claude"));
        }
    }

    [Fact]
    public async Task B_and_review_both_by_chat_manual_prompt_checks_B_and_the_answer_is_checked_against_the_chapter_state()
    {
        var (server, ai) = await StartAsync(gemini: false, claude: false);
        await using (server)
        {
            var seed = await SeedAsync(server);

            var bad = await PostAsync(server, ReviewUrl(seed.Real, "/manual-prompt"), new { secondAnswer = "kein json" });
            Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
            Assert.True(bad.Body.GetProperty("errors").GetArrayLength() > 0);

            var (status, prepared) = await PostAsync(server, ReviewUrl(seed.Real, "/manual-prompt"), new { secondAnswer = AnswerB });
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(IsReviewPrompt(prepared.GetProperty("prompt").GetProperty("prompt").GetString()!));
            var sections = prepared.GetProperty("secondSections");
            var baseHash = prepared.GetProperty("baseHash").GetString();

            // The answer is judged: no chapter in it
            var noChapter = await PostAsync(server, ReviewUrl(seed.Real, "/manual-check"), new { secondSections = sections, answer = """{ "game": "Suncraft", "findings": [] }""", baseHash });
            Assert.False(noChapter.Body.GetProperty("ok").GetBoolean());
            Assert.Equal("noChapter", noChapter.Body.GetProperty("errors")[0].GetProperty("code").GetString());
            var refused = await PostAsync(server, ReviewUrl(seed.Real, "/manual-answer"), new { secondSections = sections, answer = """{ "game": "Suncraft", "findings": [] }""", baseHash });
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.True(refused.Body.GetProperty("errors").GetArrayLength() > 0);

            // A review without a source gets a warning, but it is saved (the chat may have no web search)
            var withoutSource = ReviewAnswer(findings: """[ { "kind": "corrected", "target": { "itemId": "schwert" }, "column": "Material", "a": "Eisen", "b": "Stahl", "reason": "Ohne Quelle." } ]""");
            var warned = await PostAsync(server, ReviewUrl(seed.Real, "/manual-check"), new { secondSections = sections, answer = withoutSource, baseHash });
            Assert.True(warned.Body.GetProperty("ok").GetBoolean());
            Assert.Contains(warned.Body.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "findingsWithoutSource");

            // The chapter changed since the second answer was handed out: 409 and nothing saved
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                var tab = db.GuideTabs.First(t => t.Id == seed.Real);
                tab.SectionsJson = tab.SectionsJson.Replace("Eibe", "Esche");
                db.SaveChanges();
            }
            var stale = await PostAsync(server, ReviewUrl(seed.Real, "/manual-answer"), new { secondSections = sections, answer = ReviewAnswer(), baseHash });
            Assert.Equal(HttpStatusCode.Conflict, stale.Status);
            Assert.Contains("geändert", stale.Body.GetProperty("detail").GetString());
            Assert.Null((await StoredAsync(server, seed.Real)).Review);
            Assert.Empty(ai.Calls);
        }
    }

    // ---- take the other version, stale state -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Taking_the_other_version_replaces_the_entry_keeps_the_ticks_and_the_review_stays_current_until_the_chapter_changes_otherwise()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            WireHappyPath(ai);
            var seed = await SeedAsync(server);
            await RunFullReviewAsync(server, seed);
            var calls = ai.Calls.Count;

            // Finding 0 = the corrected material of the sword: A said "Eisen"
            var (status, chosen) = await PostAsync(server, ReviewUrl(seed.Real, "/findings/0/choose"), new { version = "a" });
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("a", chosen.GetProperty("review").GetProperty("findings")[0].GetProperty("applied").GetString());
            Assert.False(chosen.GetProperty("review").GetProperty("stale").GetBoolean());
            var (tab, checks, review) = await StoredAsync(server, seed.Real);
            Assert.Contains("Eisen", tab.SectionsJson);
            Assert.DoesNotContain("Stahl", tab.SectionsJson);
            Assert.Equal(["bogen", "schwert"], checks);
            Assert.False(ChapterReview.IsStale(review!.ResultHash, tab.SectionsJson));   // the review follows the change it made itself
            Assert.Equal(calls, ai.Calls.Count);                                           // no AI call for this

            // The open point is decided: its marker and the box at the top are gone
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(server, ReviewUrl(seed.Real, "/findings/2/choose"), new { version = "result" })).Status);
            var afterDecision = (await StoredAsync(server, seed.Real)).Tab.SectionsJson;
            Assert.DoesNotContain("review_unclear", afterDecision);
            Assert.DoesNotContain("\"unclear\"", afterDecision);

            // "added": taking A removes the entry only B had; its row and (not existing) tick go, the others stay
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(server, ReviewUrl(seed.Real, "/findings/1/choose"), new { version = "a" })).Status);
            Assert.DoesNotContain("\"axt\"", (await StoredAsync(server, seed.Real)).Tab.SectionsJson);
            // The reviewer's decision cannot be restored after another version was taken
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(server, ReviewUrl(seed.Real, "/findings/1/choose"), new { version = "result" })).Status);

            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, ReviewUrl(seed.Real, "/findings/0/choose"), new { version = "c" })).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, ReviewUrl(seed.Real, "/findings/9/choose"), new { version = "a" })).Status);
        }
    }

    [Fact]
    public async Task A_chapter_changed_after_the_review_makes_it_stale_still_readable_and_no_other_version_can_be_taken()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            WireHappyPath(ai);
            var seed = await SeedAsync(server);
            await RunFullReviewAsync(server, seed);

            // A redo (or any replace) changes the chapter: the review no longer describes it
            ai.Gemini = _ => AnswerB.Replace("Holz", "Eiche");
            var (_, job) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", null);
            Assert.True((await WaitForJobAsync(server, job)).GetProperty("error").ValueKind == JsonValueKind.Null);

            var review = (await GetAsync(server, ReviewUrl(seed.Real))).Body;
            Assert.True(review.GetProperty("stale").GetBoolean());
            Assert.Equal(3, review.GetProperty("findings").GetArrayLength());       // still readable
            var chosen = await PostAsync(server, ReviewUrl(seed.Real, "/findings/0/choose"), new { version = "a" });
            Assert.Equal(HttpStatusCode.Conflict, chosen.Status);
            Assert.Contains("veraltet", chosen.Body.GetProperty("detail").GetString());
            var line = (await GetAsync(server, "/api/games/suncraft")).Body.GetProperty("tabs").EnumerateArray().First(t => t.GetProperty("slug").GetString() == "tab-1-waffen").GetProperty("review");
            Assert.True(line.GetProperty("stale").GetBoolean());
        }
    }

    [Fact]
    public async Task A_second_review_replaces_the_first_and_works_on_the_chapter_without_the_old_markers()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            WireHappyPath(ai);
            var seed = await SeedAsync(server);
            await RunFullReviewAsync(server, seed);

            await RunFullReviewAsync(server, seed);

            using var scope = server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            Assert.Equal(1, await db.ChapterReviews.CountAsync(r => r.TabId == seed.Real));
            var review = await db.ChapterReviews.AsNoTracking().FirstAsync(r => r.TabId == seed.Real);
            Assert.DoesNotContain("unclear", review.OriginalSectionsJson);   // answer A of the second review is the merged chapter, but without markers
            // The prompt of the second review carries no marker box either
            Assert.All(ai.PromptsOf("claude").Where(IsReviewPrompt), p => Assert.DoesNotContain("review_unclear", p));
        }
    }

    // ---- a running job of another chapter ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_start_for_the_same_chapter_attaches_and_the_review_of_another_chapter_is_refused_while_one_runs()
    {
        var (server, ai) = await StartAsync();
        await using (server)
        {
            var release = new ManualResetEventSlim(false);
            ai.Gemini = _ => { release.Wait(TimeSpan.FromSeconds(20)); return AnswerB; };
            ai.Claude = _ => ReviewAnswer();
            var seed = await SeedAsync(server);
            var other = Guid.NewGuid();
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                var game = await db.Games.FirstAsync(g => g.Slug == "suncraft");
                db.GuideTabs.Add(new GuideTabEntity { Id = other, GameId = game.Id, Slug = "tab-9-ruestung", Title = "Rüstung", SortOrder = 9, SectionsJson = ChapterA.Replace("\"w\"", "\"r\"").Replace("schwert", "r1").Replace("bogen", "r2") });
                await db.SaveChangesAsync();
            }

            var (_, first) = await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini", reviewer = "claude" });
            var same = await PostAsync(server, ReviewUrl(seed.Real), new { researcher = "gemini", reviewer = "claude" });
            var another = await PostAsync(server, ReviewUrl(other), new { researcher = "gemini", reviewer = "claude" });
            release.Set();

            Assert.Equal(HttpStatusCode.Accepted, same.Status);
            Assert.Equal(first.GetProperty("id").GetString(), same.Body.GetProperty("id").GetString());
            Assert.Equal(HttpStatusCode.Conflict, another.Status);
            await WaitForJobAsync(server, first);
        }
    }
}
