using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Tests;

public class AiJobServiceTests
{
    private readonly ManualClock _clock = new(Fakes.Start);
    private readonly AiJobService _jobs;

    public AiJobServiceTests() =>
        _jobs = new AiJobService(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<AiJobService>.Instance, _clock);

    private static async Task<AiJobSnapshot> Finished(AiJobService jobs, Guid id)
    {
        for (var i = 0; i < 400; i++)
        {
            var snapshot = jobs.Get(id)!;
            if (!snapshot.Running)
            {
                return snapshot;
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("The job did not finish.");
    }

    [Fact]
    public async Task A_job_runs_in_the_background_reports_its_steps_and_delivers_the_result()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var job = _jobs.Start("complete", "game:ff7", "ff7", async (sp, progress, ct) =>
        {
            progress.Step("facts");
            progress.Step("chapters", 2, 8, "Beste Waffen");
            started.SetResult();
            await release.Task;
            progress.Step("saving");
            return "the answer";
        });
        await started.Task;

        var running = _jobs.Get(job.Id)!;
        Assert.True(running.Running);
        Assert.Equal(new AiJobStep("chapters", 2, 8, "Beste Waffen"), running.Step);
        Assert.Null(running.Result);

        _clock.Advance(TimeSpan.FromSeconds(75));
        Assert.Equal(75, _jobs.Get(job.Id)!.ElapsedSeconds);

        release.SetResult();
        var done = await Finished(_jobs, job.Id);
        Assert.Equal("the answer", done.Result);
        Assert.Null(done.Error);
        Assert.Equal("saving", done.Step.Stage);
        Assert.Equal("ff7", Assert.Single(_jobs.ForSlug("FF7")).Slug);
    }

    [Fact]
    public async Task A_step_that_does_not_change_counts_up_the_idle_time_and_any_change_resets_it()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        AiJobProgress? progress = null;
        var job = _jobs.Start("lore", "game:x", "x", async (sp, p, ct) =>
        {
            progress = p;
            p.Step("loreParts", 0, 4, "Handlung");
            started.SetResult();
            await release.Task;
            return "ok";
        });
        await started.Task;

        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(360, _jobs.Get(job.Id)!.StepIdleSeconds);
        Assert.Equal(360, _jobs.Get(job.Id)!.ElapsedSeconds);

        progress!.Step("loreParts", 0, 4, "Handlung");
        Assert.Equal(360, _jobs.Get(job.Id)!.StepIdleSeconds);

        progress.Step("loreParts", 1, 4, "Figuren");
        Assert.Equal(0, _jobs.Get(job.Id)!.StepIdleSeconds);

        release.SetResult();
        await Finished(_jobs, job.Id);
    }

    [Fact]
    public async Task A_failure_keeps_our_sentence_the_providers_own_text_and_which_AI_failed()
    {
        var job = _jobs.Start("plan", "game:y", "y", (sp, progress, ct) =>
            throw Fakes.Failure("claude", 400, "temperature: only the value 1 is supported"));

        var done = await Finished(_jobs, job.Id);

        Assert.Null(done.Result);
        Assert.NotNull(done.Error);
        Assert.Equal("temperature: only the value 1 is supported", done.ProviderMessage);
        Assert.Equal("claude", done.AiProvider);
        Assert.Equal("other", done.AiFailure);
        var dto = done.ToDto();
        Assert.False(dto.Running);
        Assert.Null(dto.Result);
        Assert.Contains("Claude", dto.Error);
    }

    [Fact]
    public async Task A_localized_failure_is_read_in_the_language_of_whoever_asks()
    {
        var job = _jobs.Start("lore", "game:z", "z", (sp, progress, ct) => throw new LocalizedException(ServerTexts.Ai.NoProviderKey));
        var done = await Finished(_jobs, job.Id);

        using (Lang.Use("de")) Assert.Contains("KI-API-Key", done.ToDto().Error);
        using (Lang.Use("en")) Assert.Contains("AI API key", done.ToDto().Error);
    }

    [Fact]
    public async Task Another_kind_of_action_for_the_same_game_gets_the_running_job_back_so_the_endpoint_can_refuse_it()
    {
        var release = new TaskCompletionSource();
        var started = 0;
        var complete = _jobs.Start("complete", "game:busy", "busy", async (sp, p, ct) => { await release.Task; return "guide"; });
        var lore = _jobs.Start("lore", "game:busy", "busy", (sp, p, ct) => { Interlocked.Increment(ref started); return Task.FromResult<object>("lore"); });

        // The endpoint compares the kind and answers 409 instead of passing the "complete" job off as the lore research
        Assert.Same(complete, lore);
        Assert.Equal("complete", lore.Kind);
        Assert.Equal(0, started);

        using (Lang.Use(Lang.English))
        {
            var message = (string)ServerTexts.Ai.JobBusy(lore.Kind);
            Assert.StartsWith("“Complete guide” is running for this game", message);
        }
        using (Lang.Use(Lang.German))
        {
            Assert.Contains("„Guide vervollständigen“", (string)ServerTexts.Ai.JobBusy("complete"));
            Assert.StartsWith("Für dieses Spiel läuft gerade die Recherche für „Story & Lore“", (string)ServerTexts.Ai.JobBusy("lore"));
        }

        release.SetResult();
        await Finished(_jobs, complete.Id);
    }

    [Fact]
    public async Task A_second_start_for_the_same_key_attaches_to_the_running_job()
    {
        var release = new TaskCompletionSource();
        var first = _jobs.Start("complete", "game:dup", "dup", async (sp, p, ct) => { await release.Task; return "a"; });
        var second = _jobs.Start("complete", "game:dup", "dup", (sp, p, ct) => Task.FromResult<object>("b"));
        var other = _jobs.Start("lore", "game:other", "other", (sp, p, ct) => Task.FromResult<object>("c"));

        Assert.Same(first, second);
        Assert.NotEqual(first.Id, other.Id);

        release.SetResult();
        await Finished(_jobs, first.Id);

        // Finished: the same key starts a new job again
        var third = _jobs.Start("complete", "game:dup", "dup", (sp, p, ct) => Task.FromResult<object>("d"));
        Assert.NotEqual(first.Id, third.Id);
        Assert.Equal("d", (await Finished(_jobs, third.Id)).Result);
    }

    [Fact]
    public async Task The_job_runs_in_the_language_of_the_request_that_started_it()
    {
        string? seen = null;
        AiJob job;
        using (Lang.Use("en"))
        {
            job = _jobs.Start("expand", "game:lang", "lang", (sp, p, ct) =>
            {
                seen = Lang.Current;
                return Task.FromResult<object>("x");
            });
        }
        await Finished(_jobs, job.Id);

        Assert.Equal("en", seen);
    }

    [Fact]
    public async Task Finished_jobs_are_forgotten_after_half_an_hour()
    {
        var job = _jobs.Start("lore", "game:old", "old", (sp, p, ct) => Task.FromResult<object>("x"));
        await Finished(_jobs, job.Id);
        Assert.NotNull(_jobs.Get(job.Id));

        _clock.Advance(TimeSpan.FromMinutes(31));
        _jobs.Start("lore", "game:new", "new", (sp, p, ct) => Task.FromResult<object>("y"));

        Assert.Null(_jobs.Get(job.Id));
    }

    [Fact]
    public async Task The_status_names_a_change_of_AI_provider_while_the_job_runs_and_after_it()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        AiRun? run = null;
        var job = _jobs.Start("complete", "game:sw", "sw", async (sp, p, ct) =>
        {
            run = AiRouter.Plan(new AiSelection(AiSelectionMode.Default, "gemini", true), AiProvider.All, true, null, false, new AiProviderHealth(_clock), Fakes.Prints());
            started.SetResult();
            await release.Task;
            return "x";
        });
        await started.Task;
        job.UsageSource = () => run!.Usage();
        Assert.Null(job.Snapshot().ToDto().SwitchNotice);

        await run!.ExecuteAsync("gemini", provider => provider == "gemini" ? Task.FromException<string>(Fakes.Failure("gemini", 429)) : Task.FromResult("ok"), TestContext.Current.CancellationToken);

        using (Lang.Use("de")) Assert.Equal("Gemini-Kontingent aufgebraucht – weiter mit Claude.", job.Snapshot().ToDto().SwitchNotice);
        release.SetResult();
        var done = await Finished(_jobs, job.Id);
        using (Lang.Use("en")) Assert.Equal("Gemini quota used up – continuing with Claude.", done.ToDto().SwitchNotice);
    }
}
