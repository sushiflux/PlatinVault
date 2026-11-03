using System.Net;
using System.Text;
using Companion.Api.Configuration;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Companion.Api.Tests;

/// <summary>
/// The real provider calls of <see cref="AiGuideGeneratorService"/> against fake providers (the base URLs are the same
/// <c>Companion:Ai:*BaseUrl</c> settings an operator would use for a proxy): quota, rejected key, outage and a plain bad request, with
/// and without the automatic fallback.
/// </summary>
public sealed class ProviderFallbackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-ai-test-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock _clock = new(Fakes.Start);
    private readonly AiProviderHealth _health;
    private readonly SecretStore _secrets;
    private readonly List<string> _requests = [];
    private readonly List<System.Text.Json.Nodes.JsonObject> _bodies = [];
    private readonly Dictionary<string, Func<HttpResponseMessage>> _providers = [];

    public ProviderFallbackTests()
    {
        Directory.CreateDirectory(_dir);
        _health = new AiProviderHealth(_clock);
        _secrets = new SecretStore(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_dir, "keys"))), Options.Create(new CompanionOptions { DataDir = _dir }));
        _secrets.SetSecret(SettingsStore.SecretGemini, "gemini-secret");
        _secrets.SetSecret(SettingsStore.SecretClaude, "claude-secret");
        _secrets.SetSecret(SettingsStore.SecretOpenAi, "openai-secret");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* temp files; the OS cleans up */ }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        configure?.Invoke(response);
        return response;
    }

    private static HttpResponseMessage GeminiOk() => Json(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"hi from gemini"}]}}]}""");

    private static HttpResponseMessage ClaudeOk() => Json(HttpStatusCode.OK, """{"content":[{"type":"text","text":"hi from claude"}]}""");

    private static HttpResponseMessage OpenAiOk() => Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"hi from openai"}}]}""");

    private AiGuideGeneratorService Generator()
    {
        var factory = new StubFactory(request =>
        {
            var host = request.RequestUri!.Host;
            var provider = host.Split('.')[0];
            _requests.Add(provider);
            if (request.Content is not null && System.Text.Json.Nodes.JsonNode.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult()) is System.Text.Json.Nodes.JsonObject sent)
            {
                _bodies.Add(sent);
            }
            return _providers[provider]();
        });
        var options = Options.Create(new CompanionOptions
        {
            DataDir = _dir,
            Ai = new AiProviderUrls { GeminiBaseUrl = "http://gemini.test/v1beta", ClaudeBaseUrl = "http://claude.test/v1", OpenAiBaseUrl = "http://openai.test/v1" }
        });
        // Only the provider calls are exercised: the Steam/IGDB/settings collaborators are never touched by them
        return new AiGuideGeneratorService(factory, null!, _secrets, null!, null!, null!, options, null!, NullLogger<AiGuideGeneratorService>.Instance);
    }

    private static SettingsResponseDto Settings() => new(
        GeminiModel: "gemini-3.8-flash", ClaudeModel: "claude-sonnet-4-5", OpenAiModel: "gpt-4o", SteamUserId: "", AutoDetectGame: true,
        PlaySounds: false, SoundVolume: 0, SoundTheme: "kristall", PopupPosition: "top-right", Language: "german", UiLanguage: "en", AutoTranslate: true,
        HasGeminiKey: true, GeminiKeyHint: "", HasClaudeKey: true, ClaudeKeyHint: "", HasOpenAiKey: true, OpenAiKeyHint: "", HasSteamKey: false, SteamKeyHint: "",
        HasTwitchKey: false, TwitchKeyHint: "", TwitchStreamsEnabled: false, AchievementNotesEnabled: true,
        AiSelectionMode: AiSelectionMode.Default, AiDefaultProvider: "gemini", AiAutoFallback: true,
        ResearchBlocks: new ResearchBlocksDto(null, [], null, null), ResearchBlocksPolicy: ResearchBlocks.Policy());

    private AiRun Run(bool autoFallback = true, string mode = AiSelectionMode.Default, bool research = true) =>
        AiRouter.Plan(new AiSelection(mode, "gemini", autoFallback), AiProvider.All, research, null, false, _health,
            p => AiKeys.Fingerprint(_secrets.GetSecret(AiRouter.SecretOf(p))));

    private string Fingerprint(string provider) => AiKeys.Fingerprint(_secrets.GetSecret(AiRouter.SecretOf(provider)));

    [Fact]
    public async Task Gemini_quota_with_a_retry_after_header_continues_with_Claude_and_pauses_Gemini_for_that_long()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"Resource has been exhausted","status":"RESOURCE_EXHAUSTED"}}""",
            r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120)));
        _providers["claude"] = ClaudeOk;
        var run = Run();

        var answer = await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        Assert.Equal("hi from claude", answer);
        Assert.Equal(["gemini", "claude"], _requests);
        Assert.Equal(new AiSwitch("gemini", "claude", AiFailureKind.Quota), Assert.Single(run.Usage().Switches));
        Assert.Equal(Fakes.Start + TimeSpan.FromSeconds(120), _health.Current("gemini", Fingerprint("gemini"))!.Until);
    }

    [Fact]
    public async Task Google_daily_quota_pauses_Gemini_for_the_time_Google_names_in_its_message()
    {
        // The wording of a real daily-quota answer of the Gemini API (free tier, checked 2026-11-01)
        _providers["gemini"] = () => Json(HttpStatusCode.TooManyRequests,
            """{"error":{"code":429,"message":"You exceeded your current quota, please check your plan and billing details.\n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 20, model: gemini-3.8-flash, description: Generate content requests per day limit exceeded on Free Tier.\nPlease retry in 21h35m54.309834445s.","status":"RESOURCE_EXHAUSTED"}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run();

        await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        var pause = _health.Current("gemini", Fingerprint("gemini"))!.Until!.Value - Fakes.Start;
        Assert.InRange(pause.TotalSeconds, (21 * 3600 + 35 * 60 + 54) - 1, (21 * 3600 + 35 * 60 + 55) + 1);
    }

    [Fact]
    public async Task A_message_without_a_time_falls_back_to_the_default_pause()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"Quota exceeded. Please retry in a moment."}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run();

        await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        Assert.Equal(Fakes.Start + AiProviderHealth.DefaultQuotaCooldown, _health.Current("gemini", Fingerprint("gemini"))!.Until);
    }

    [Fact]
    public async Task A_retry_delay_field_in_the_error_details_is_used_too()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.TooManyRequests,
            """{"error":{"code":429,"message":"Rate limit","status":"RESOURCE_EXHAUSTED","details":[{"retryDelay":"34s"}]}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run();

        await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        Assert.Equal(Fakes.Start + TimeSpan.FromSeconds(34), _health.Current("gemini", Fingerprint("gemini"))!.Until);
    }

    [Fact]
    public async Task A_second_chapter_does_not_ask_the_exhausted_provider_again()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"quota"}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run();
        var generator = Generator();

        await generator.CallAsync(run, run.Primary, "chapter 1", Settings(), CancellationToken.None);
        await generator.CallAsync(run, run.Primary, "chapter 2", Settings(), CancellationToken.None);
        await generator.CallAsync(run, run.Primary, "chapter 3", Settings(), CancellationToken.None);

        Assert.Equal(["gemini", "claude", "claude", "claude"], _requests);
    }

    [Fact]
    public async Task A_rejected_key_continues_with_the_next_provider_and_stays_noted_until_the_key_changes()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.BadRequest, """{"error":{"message":"API key not valid. Please pass a valid API key."}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run();

        await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        Assert.Equal(AiFailureKind.InvalidKey, run.Usage().Switches[0].Reason);
        _clock.Advance(TimeSpan.FromDays(2));
        Assert.NotNull(_health.Current("gemini", Fingerprint("gemini")));

        _secrets.SetSecret(SettingsStore.SecretGemini, "a-new-key");
        Assert.Null(_health.Current("gemini", Fingerprint("gemini")));
    }

    [Fact]
    public async Task An_outage_of_the_provider_continues_with_the_next_one()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"The model is overloaded."}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run();

        var answer = await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        Assert.Equal("hi from claude", answer);
        Assert.Equal(AiFailureKind.Unavailable, run.Usage().Switches[0].Reason);
    }

    [Fact]
    public async Task A_dropped_connection_counts_as_an_outage()
    {
        _providers["gemini"] = () => throw new HttpRequestException("no route to host");
        _providers["claude"] = ClaudeOk;
        var run = Run();

        var answer = await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        Assert.Equal("hi from claude", answer);
        Assert.Equal(AiFailureKind.Unavailable, run.Usage().Switches[0].Reason);
    }

    [Fact]
    public async Task Without_the_fallback_a_quota_failure_is_the_final_answer()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"quota"}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run(autoFallback: false);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None));

        Assert.Equal("gemini", ex.Provider);
        Assert.Equal(AiFailureKind.Quota, ex.Kind);
        Assert.Equal(["gemini"], _requests);
    }

    [Fact]
    public async Task A_plain_bad_request_does_not_switch_provider()
    {
        _providers["gemini"] = () => Json(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid JSON payload received."}}""");
        _providers["claude"] = ClaudeOk;
        var run = Run();

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None));

        Assert.Equal(AiFailureKind.Other, ex.Kind);
        Assert.Equal(["gemini"], _requests);
        Assert.Null(_health.Current("gemini", Fingerprint("gemini")));
    }

    [Fact]
    public async Task Claude_with_an_empty_balance_hands_over_to_Gemini()
    {
        _providers["claude"] = () => Json(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API."}}""");
        _providers["gemini"] = GeminiOk;
        var selection = new AiSelection(AiSelectionMode.Default, "claude", true);
        var run = AiRouter.Plan(selection, AiProvider.All, true, null, false, _health, Fingerprint);

        var answer = await Generator().CallAsync(run, run.Primary, "prompt", Settings(), CancellationToken.None);

        Assert.Equal("hi from gemini", answer);
        Assert.Equal(new AiSwitch("claude", "gemini", AiFailureKind.Quota), Assert.Single(run.Usage().Switches));
    }

    [Fact]
    public void The_notice_names_both_providers_in_both_languages_and_warns_about_missing_web_search()
    {
        var change = new AiSwitch("gemini", "claude", AiFailureKind.Quota);
        using (Companion.Api.Localization.Lang.Use("de"))
            Assert.Equal("Gemini-Kontingent aufgebraucht – weiter mit Claude.", AiUsageMapping.NoticeOf(change, research: true));
        using (Companion.Api.Localization.Lang.Use("en"))
            Assert.Equal("Gemini quota used up – continuing with Claude.", AiUsageMapping.NoticeOf(change, research: true));

        using (Companion.Api.Localization.Lang.Use("en"))
        {
            var toOpenAi = AiUsageMapping.NoticeOf(new AiSwitch("claude", "openai", AiFailureKind.InvalidKey), research: true);
            Assert.Contains("researches without web search", toOpenAi);
            Assert.DoesNotContain("without web search", AiUsageMapping.NoticeOf(new AiSwitch("claude", "openai", AiFailureKind.InvalidKey), research: false));
        }
    }

    // ---- What Claude is sent (the HTTP 400 of Claude Haiku 5.5 came from a non-default temperature) --------------------------------

    private static AiRun ClaudeRun(AiProviderHealth health, Func<string, string> fingerprint) =>
        AiRouter.Plan(new AiSelection(AiSelectionMode.Default, "claude", true), AiProvider.All, true, null, false, health, fingerprint);

    private async Task<System.Text.Json.Nodes.JsonObject> SendToClaudeAsync(string model, bool research)
    {
        _providers["claude"] = ClaudeOk;
        var run = ClaudeRun(_health, Fingerprint);
        var settings = Settings() with { ClaudeModel = model };
        await Generator().CallAsync(run, run.Primary, "prompt", settings, TestContext.Current.CancellationToken, research);
        return _bodies.Last();
    }

    [Theory]
    [InlineData("claude-haiku-5-5", "web_search_20250305", 16000)]
    [InlineData("claude-sonnet-5-5", "web_search_20260209", 16000)]
    [InlineData("claude-opus-5-5", "web_search_20260209", 16000)]
    [InlineData("claude-sonnet-5", "web_search_20260209", 16000)]
    [InlineData("claude-opus-4-8", "web_search_20260209", 32000)]
    [InlineData("claude-sonnet-4-6", "web_search_20260209", 32000)]
    [InlineData("claude-sonnet-4-5", "web_search_20250305", 32000)]
    [InlineData("claude-haiku-4-5", "web_search_20250305", 32000)]
    [InlineData("claude-3-7-sonnet-latest", "web_search_20250305", 8192)]
    public async Task Claude_research_sends_no_sampling_parameters_and_the_web_search_tool_of_its_model(string model, string tool, int maxTokens)
    {
        var body = await SendToClaudeAsync(model, research: true);

        Assert.False(body.ContainsKey("temperature"));
        Assert.False(body.ContainsKey("top_p"));
        Assert.False(body.ContainsKey("top_k"));
        Assert.Equal(model, body["model"]!.GetValue<string>());
        Assert.Equal(maxTokens, body["max_tokens"]!.GetValue<int>());
        var search = Assert.Single(body["tools"]!.AsArray())!.AsObject();
        Assert.Equal(tool, search["type"]!.GetValue<string>());
        Assert.Equal("web_search", search["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_translation_request_to_Claude_has_neither_tools_nor_temperature()
    {
        var body = await SendToClaudeAsync("claude-haiku-5-5", research: false);

        Assert.False(body.ContainsKey("tools"));
        Assert.False(body.ContainsKey("temperature"));
    }

    [Fact]
    public async Task The_error_text_of_Claude_ends_up_in_the_provider_message_so_it_can_be_shown_as_technical_details()
    {
        _providers["claude"] = () => Json(HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"temperature: only the value 1 is supported for this model"},"request_id":"req_1"}""");
        var run = ClaudeRun(_health, Fingerprint);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => Generator().CallAsync(run, run.Primary, "prompt", Settings(), TestContext.Current.CancellationToken));

        Assert.Equal("temperature: only the value 1 is supported for this model", ex.ProviderMessage);
        Assert.Equal("claude", ex.Provider);
        Assert.Equal(AiFailureKind.Other, ex.Kind);
    }

    [Fact]
    public async Task A_search_turn_the_server_paused_is_continued_with_the_answer_so_far()
    {
        var calls = 0;
        _providers["claude"] = () => ++calls == 1
            ? Json(HttpStatusCode.OK, """{"stop_reason":"pause_turn","content":[{"type":"server_tool_use","id":"srvtoolu_1","name":"web_search","input":{"query":"x"}}]}""")
            : ClaudeOk();
        var run = ClaudeRun(_health, Fingerprint);

        var answer = await Generator().CallAsync(run, run.Primary, "prompt", Settings(), TestContext.Current.CancellationToken);

        Assert.Equal("hi from claude", answer);
        Assert.Equal(2, calls);
        var second = _bodies[^1]["messages"]!.AsArray();
        Assert.Equal(2, second.Count);
        Assert.Equal("assistant", second[1]!["role"]!.GetValue<string>());
        Assert.Equal("server_tool_use", second[1]!["content"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task When_the_replacement_fails_as_well_the_skipped_provider_is_still_reported()
    {
        // The exact flow of the bug report: Gemini has no quota left, Claude takes over and answers with HTTP 400
        _health.Report("gemini", Fakes.Failure("gemini", 429), Fingerprint("gemini"));
        _providers["claude"] = () => Json(HttpStatusCode.BadRequest, """{"error":{"message":"invalid request"}}""");
        var run = Run();

        await Assert.ThrowsAsync<AiProviderException>(() => Generator().CallAsync(run, run.Primary, "prompt", Settings(), TestContext.Current.CancellationToken));

        Assert.Equal(["claude"], _requests);
        var change = Assert.Single(run.Usage().Switches);
        Assert.Equal(new AiSwitch("gemini", "claude", AiFailureKind.Quota), change);
        Assert.Empty(run.Usage().Providers);
    }
}
