using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

public class AiProviderHealthTests
{
    private readonly ManualClock _clock = new(Fakes.Start);
    private readonly AiProviderHealth _health;

    public AiProviderHealthTests() => _health = new AiProviderHealth(_clock);

    [Fact]
    public void A_quota_failure_pauses_the_provider_for_an_hour_by_default()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 429), "fp");

        var state = _health.Current("gemini", "fp");
        Assert.NotNull(state);
        Assert.Equal(AiFailureKind.Quota, state.Kind);
        Assert.Equal(Fakes.Start + TimeSpan.FromHours(1), state.Until);

        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.NotNull(_health.Current("gemini", "fp"));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(_health.Current("gemini", "fp"));
    }

    [Fact]
    public void The_providers_retry_after_is_respected_within_sane_limits()
    {
        _health.Report("claude", Fakes.Failure("claude", 429, retryAfter: TimeSpan.FromSeconds(90)), "fp");
        Assert.Equal(Fakes.Start + TimeSpan.FromSeconds(90), _health.Current("claude", "fp")!.Until);

        _health.Report("openai", Fakes.Failure("openai", 429, retryAfter: TimeSpan.FromDays(9)), "fp");
        Assert.Equal(Fakes.Start + TimeSpan.FromHours(24), _health.Current("openai", "fp")!.Until);
    }

    [Fact]
    public void A_rejected_key_stays_noted_until_the_key_changes()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 403), "old-key");

        _clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(AiFailureKind.InvalidKey, _health.Current("gemini", "old-key")!.Kind);

        // A replaced key is a different key: the old verdict is forgotten
        Assert.Null(_health.Current("gemini", "new-key"));
        Assert.Null(_health.Current("gemini", "old-key"));
    }

    [Fact]
    public void An_outage_is_only_noted_for_a_short_while()
    {
        _health.Report("claude", Fakes.Failure("claude", 529), "fp");
        Assert.Equal(AiFailureKind.Unavailable, _health.Current("claude", "fp")!.Kind);

        _clock.Advance(AiProviderHealth.UnavailableCooldown + TimeSpan.FromSeconds(1));
        Assert.Null(_health.Current("claude", "fp"));
    }

    [Fact]
    public void The_same_problem_again_keeps_its_original_since()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 429), "fp");
        _clock.Advance(TimeSpan.FromMinutes(20));
        _health.Report("gemini", Fakes.Failure("gemini", 429), "fp");

        var state = _health.Current("gemini", "fp")!;
        Assert.Equal(Fakes.Start, state.Since);
        Assert.Equal(Fakes.Start + TimeSpan.FromMinutes(80), state.Until);
    }

    [Fact]
    public void A_success_and_a_reset_both_clear_the_state()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 429), "fp");
        _health.MarkOk("gemini");
        Assert.Null(_health.Current("gemini", "fp"));

        _health.Report("claude", Fakes.Failure("claude", 429), "fp");
        Assert.True(_health.Reset("claude"));
        Assert.False(_health.Reset("claude"));
        Assert.Null(_health.Current("claude", "fp"));
    }

    [Fact]
    public void Other_failures_are_not_noted()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 400, "bad request"), "fp");
        Assert.Null(_health.Current("gemini", "fp"));
    }
}

public class AiRunTests
{
    private readonly ManualClock _clock = new(Fakes.Start);
    private readonly AiProviderHealth _health;
    private readonly IReadOnlyList<string> _all = ["gemini", "claude", "openai"];

    public AiRunTests() => _health = new AiProviderHealth(_clock);

    private AiRun Run(string mode = AiSelectionMode.Default, string? defaultProvider = "gemini", bool autoFallback = true, string? overrideTo = null, bool research = true, bool background = false, IReadOnlyList<string>? keyed = null) =>
        AiRouter.Plan(new AiSelection(mode, defaultProvider, autoFallback), keyed ?? _all, research, overrideTo, background, _health, Fakes.Prints());

    private static Func<string, Task<string>> Answers(List<string> asked, Func<string, Exception?> failure) => provider =>
    {
        asked.Add(provider);
        return failure(provider) is { } error ? Task.FromException<string>(error) : Task.FromResult("answer from " + provider);
    };

    [Fact]
    public async Task The_standard_KI_answers_when_it_works()
    {
        var run = Run();
        var asked = new List<string>();

        var answer = await run.ExecuteAsync(run.Primary, Answers(asked, _ => null));

        Assert.Equal("answer from gemini", answer);
        Assert.Equal(["gemini"], asked);
        Assert.Empty(run.Usage().Switches);
        Assert.Equal(["gemini"], run.Usage().Providers);
    }

    [Fact]
    public async Task Quota_used_up_continues_with_the_next_provider_and_says_so()
    {
        var run = Run();
        var asked = new List<string>();

        var answer = await run.ExecuteAsync(run.Primary, Answers(asked, p => p == "gemini" ? Fakes.Failure("gemini", 429) : null));

        Assert.Equal("answer from claude", answer);
        Assert.Equal(["gemini", "claude"], asked);
        var change = Assert.Single(run.Usage().Switches);
        Assert.Equal(new AiSwitch("gemini", "claude", AiFailureKind.Quota), change);
        // The exhausted provider is remembered for the next action
        Assert.Equal(AiFailureKind.Quota, _health.Current("gemini", Fakes.Prints()("gemini"))!.Kind);
    }

    [Theory]
    [InlineData(401, AiFailureKind.InvalidKey)]
    [InlineData(503, AiFailureKind.Unavailable)]
    [InlineData(402, AiFailureKind.Quota)]
    public async Task Every_switching_failure_hands_over(int status, AiFailureKind kind)
    {
        var run = Run(defaultProvider: "claude");
        var asked = new List<string>();

        await run.ExecuteAsync(run.Primary, Answers(asked, p => p == "claude" ? Fakes.Failure("claude", status) : null));

        Assert.Equal(["claude", "gemini"], asked);
        Assert.Equal(kind, run.Usage().Switches[0].Reason);
    }

    [Fact]
    public async Task A_bad_answer_is_not_a_reason_to_switch()
    {
        var run = Run();
        var asked = new List<string>();

        await Assert.ThrowsAsync<LocalizedException>(() =>
            run.ExecuteAsync(run.Primary, Answers(asked, _ => new LocalizedException(ServerTexts.Ai.AnswerNotJson))));

        Assert.Equal(["gemini"], asked);
        Assert.Null(_health.Current("gemini", Fakes.Prints()("gemini")));
    }

    [Fact]
    public async Task A_rejected_request_is_not_a_reason_to_switch_either()
    {
        var run = Run();
        var asked = new List<string>();

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            run.ExecuteAsync(run.Primary, Answers(asked, _ => Fakes.Failure("gemini", 400, "Invalid JSON payload"))));

        Assert.Equal(AiFailureKind.Other, ex.Kind);
        Assert.Equal(["gemini"], asked);
    }

    [Fact]
    public async Task Without_the_fallback_the_failure_is_final_and_nothing_else_is_asked()
    {
        var run = Run(autoFallback: false);
        var asked = new List<string>();

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            run.ExecuteAsync(run.Primary, Answers(asked, _ => Fakes.Failure("gemini", 429))));

        Assert.Equal(AiFailureKind.Quota, ex.Kind);
        Assert.Equal(["gemini"], asked);
        Assert.Empty(run.Usage().Switches);
    }

    [Fact]
    public async Task A_provider_that_failed_in_this_run_is_not_asked_again_by_the_next_chapter()
    {
        var run = Run();
        var asked = new List<string>();
        Exception? Fail(string p) => p == "gemini" ? Fakes.Failure("gemini", 429) : null;

        for (var chapter = 0; chapter < 5; chapter++)
        {
            await run.ExecuteAsync(run.Primary, Answers(asked, Fail));
        }

        Assert.Single(asked, p => p == "gemini");
        Assert.Equal(5, asked.Count(p => p == "claude"));
        // One change, however many chapters went through it
        Assert.Single(run.Usage().Switches);
    }

    [Fact]
    public async Task Without_the_fallback_later_chapters_fail_at_once_without_asking_the_provider()
    {
        var run = Run(autoFallback: false);
        var asked = new List<string>();

        await Assert.ThrowsAsync<AiProviderException>(() => run.ExecuteAsync(run.Primary, Answers(asked, _ => Fakes.Failure("gemini", 429))));
        await Assert.ThrowsAsync<AiProviderException>(() => run.ExecuteAsync(run.Primary, Answers(asked, _ => Fakes.Failure("gemini", 429))));

        Assert.Single(asked);
    }

    [Fact]
    public async Task A_new_action_skips_a_provider_that_is_paused_and_still_names_the_change()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 429), Fakes.Prints()("gemini"));
        var run = Run();
        var asked = new List<string>();

        await run.ExecuteAsync(run.Primary, Answers(asked, _ => null));

        Assert.Equal(["claude"], asked);
        Assert.Equal(new AiSwitch("gemini", "claude", AiFailureKind.Quota), Assert.Single(run.Usage().Switches));
    }

    [Fact]
    public async Task Without_the_fallback_a_paused_provider_is_still_tried_so_the_player_sees_the_real_answer()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 429), Fakes.Prints()("gemini"));
        var run = Run(autoFallback: false);
        var asked = new List<string>();

        await run.ExecuteAsync(run.Primary, Answers(asked, _ => null));

        Assert.Equal(["gemini"], asked);
    }

    [Fact]
    public async Task The_last_provider_is_always_tried_even_when_it_is_paused()
    {
        foreach (var provider in _all)
        {
            _health.Report(provider, Fakes.Failure(provider, 429), Fakes.Prints()(provider));
        }
        var run = Run();
        var asked = new List<string>();

        var answer = await run.ExecuteAsync(run.Primary, Answers(asked, _ => null));

        Assert.Equal(["openai"], asked);
        Assert.Equal("answer from openai", answer);
    }

    [Fact]
    public async Task When_every_provider_fails_the_last_failure_is_thrown()
    {
        var run = Run();
        var asked = new List<string>();

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            run.ExecuteAsync(run.Primary, Answers(asked, p => Fakes.Failure(p, p == "gemini" ? 429 : 401))));

        Assert.Equal(["gemini", "claude", "openai"], asked);
        Assert.Equal("openai", ex.Provider);
    }

    [Fact]
    public async Task Research_tries_providers_with_web_search_before_OpenAI()
    {
        var run = Run(defaultProvider: "openai");
        var asked = new List<string>();

        await run.ExecuteAsync("openai", Answers(asked, p => p == "openai" ? Fakes.Failure("openai", 429) : null));

        Assert.Equal(["openai", "gemini"], asked);
    }

    [Fact]
    public void A_later_success_of_a_paused_provider_is_noted_in_the_health()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 503), Fakes.Prints()("gemini"));
        _health.MarkOk("gemini");
        Assert.Null(_health.Current("gemini", Fakes.Prints()("gemini")));
    }

    // ---- The plan: which provider(s) an action starts with ------------------------------------------------------------

    [Fact]
    public void Default_mode_uses_the_standard_KI_for_everything()
    {
        var run = Run(defaultProvider: "claude");
        Assert.Equal("claude", run.Primary);
        Assert.Equal(["claude"], run.Spread);
        Assert.Equal("claude", run.SpreadFor(7));
    }

    [Fact]
    public void A_standard_KI_without_a_key_falls_back_to_the_first_provider_that_has_one()
    {
        var run = Run(defaultProvider: "gemini", keyed: ["claude", "openai"]);
        Assert.Equal("claude", run.Primary);
    }

    [Fact]
    public void A_one_off_choice_beats_the_standard_KI_but_changes_nothing_stored()
    {
        var run = Run(defaultProvider: "gemini", overrideTo: "claude");
        Assert.Equal("claude", run.Primary);
        Assert.True(run.AutoFallback);
    }

    [Fact]
    public void A_one_off_choice_without_a_key_is_refused_with_the_usual_message()
    {
        var ex = Assert.Throws<LocalizedException>(() => Run(overrideTo: "openai", keyed: ["gemini"]));
        Assert.Equal(ServerTexts.Ai.NoKey("openai").De, ex.Text.De);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("both")]
    [InlineData("")]
    [InlineData(null)]
    public void The_former_override_words_mean_no_choice(string? legacy)
    {
        var run = Run(defaultProvider: "claude", overrideTo: legacy);
        Assert.Equal("claude", run.Primary);
    }

    [Fact]
    public void Mixed_mode_spreads_chapters_over_providers_with_web_search()
    {
        var run = Run(mode: AiSelectionMode.Mixed);
        Assert.Equal(AiSelectionMode.Mixed, run.Mode);
        Assert.Equal(["gemini", "claude"], run.Spread);
        Assert.Equal(["gemini", "claude", "gemini", "claude"], Enumerable.Range(0, 4).Select(run.SpreadFor));
    }

    [Fact]
    public void Mixed_mode_uses_OpenAI_only_for_research_when_it_is_the_only_key_and_always_for_translation()
    {
        Assert.Equal(["openai"], Run(mode: AiSelectionMode.Mixed, keyed: ["openai"]).Spread);
        Assert.Equal(["gemini", "claude", "openai"], Run(mode: AiSelectionMode.Mixed, research: false).Spread);
    }

    [Fact]
    public void Mixed_mode_leaves_out_a_paused_provider_while_the_fallback_is_on()
    {
        _health.Report("gemini", Fakes.Failure("gemini", 429), Fakes.Prints()("gemini"));
        Assert.Equal(["claude"], Run(mode: AiSelectionMode.Mixed).Spread);
        // With the fallback off the player keeps the rotation they chose
        Assert.Equal(["gemini", "claude"], Run(mode: AiSelectionMode.Mixed, autoFallback: false).Spread);
    }

    [Fact]
    public void Mixed_mode_with_a_one_off_choice_uses_just_that_provider()
    {
        var run = Run(mode: AiSelectionMode.Mixed, overrideTo: "claude");
        Assert.Equal(["claude"], run.Spread);
    }

    [Fact]
    public void Ask_mode_never_switches_by_itself_unless_nobody_can_be_asked()
    {
        Assert.False(Run(mode: AiSelectionMode.Ask, overrideTo: "claude").AutoFallback);
        Assert.False(Run(mode: AiSelectionMode.Ask).AutoFallback);
        Assert.True(Run(mode: AiSelectionMode.Ask, background: true).AutoFallback);
    }

    [Fact]
    public void Ask_mode_with_a_background_job_takes_the_standard_KI_and_never_spreads()
    {
        var run = Run(mode: AiSelectionMode.Ask, defaultProvider: "claude", background: true);
        Assert.Equal("claude", run.Primary);
        Assert.Equal(["claude"], run.Spread);
    }

    [Fact]
    public void Without_any_key_there_is_nothing_to_start()
    {
        var ex = Assert.Throws<LocalizedException>(() => Run(keyed: []));
        Assert.Equal(ServerTexts.Ai.NoProviderKey.De, ex.Text.De);
    }
}
