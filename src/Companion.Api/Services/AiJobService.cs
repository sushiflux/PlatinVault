using System.Collections.Concurrent;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>Where a running AI action is: the step (a language-neutral id the UI turns into a sentence) and, for steps that count, how far.</summary>
public sealed record AiJobStep(string Stage, int Done, int Total, string? Title);

/// <summary>
/// What an AI action reports while it works: <c>Step("facts")</c>, <c>Step("blueprint")</c>, <c>Step("chapters", done, total, title)</c> ...
/// Every change (also a higher count or another chapter title) counts as progress; a step that does not change for long is shown as "slow".
/// </summary>
public sealed class AiJobProgress
{
    private readonly AiJob _job;

    internal AiJobProgress(AiJob job) => _job = job;

    public void Step(string stage, int done = 0, int total = 0, string? title = null) => _job.SetStep(new AiJobStep(stage, done, total, title));
}

/// <summary>
/// Where finished chapters go while a long research still runs. With a sink every chapter is saved the moment it is done (and a new guide's tile
/// right after its blueprint), so a research that is interrupted - the server restarts, the provider fails for good - keeps what was finished
/// and "complete guide" later continues with the missing chapters only. Without a sink everything is returned at the end, as before.
/// </summary>
public interface IAiChapterSink
{
    /// <summary>New guide only: the tile (no chapters yet) as it will be; the sink saves it and may change its <c>Slug</c> to a free one.</summary>
    Task GameReadyAsync(GameEntity game, CancellationToken ct);

    /// <summary>One chapter (also one that failed and carries an error box) is finished: save it.</summary>
    Task ChapterDoneAsync(GuideTabEntity tab, CancellationToken ct);
}

/// <summary>One background AI action (a new guide, the analysis, "complete guide", a chapter, Story &amp; Lore).</summary>
public sealed class AiJob
{
    private readonly Lock _lock = new();
    private AiJobStep _step = new("starting", 0, 0, null);
    private DateTimeOffset _stepChangedAt;
    private bool _running = true;
    private Msg? _error;
    private string? _providerMessage;
    private string? _aiProvider;
    private string? _aiFailure;
    private DateTimeOffset? _finishedAt;
    private object? _result;

    internal AiJob(string kind, string key, string? slug, TimeProvider clock, string? target = null, string? targetLabel = null)
    {
        Id = Guid.NewGuid();
        Kind = kind;
        Key = key;
        Slug = slug;
        Target = target;
        TargetLabel = targetLabel;
        Clock = clock;
        StartedAt = clock.GetUtcNow();
        _stepChangedAt = StartedAt;
    }

    public Guid Id { get; }

    public string Kind { get; }

    /// <summary>One job per key at a time (the game slug, or the title of a new game): a second click attaches to the running one.</summary>
    public string Key { get; }

    public string? Slug { get; }

    /// <summary>
    /// What exactly the job works on when its kind can target different things of one game (the chapter of a "redo", the topic of an "expand", the
    /// chapters of a "retry"); null = the whole game. A second start with the same kind but another target must not attach to this job.
    /// </summary>
    public string? Target { get; }

    /// <summary>The name of the target for a message (e.g. the chapter title), in the language of the guide.</summary>
    public string? TargetLabel { get; }

    public DateTimeOffset StartedAt { get; }

    internal TimeProvider Clock { get; }

    /// <summary>Where the usage (who answered, every change of provider) of the running action can be read; set by the runner.</summary>
    internal Func<AiUsage?>? UsageSource { get; set; }

    public AiJobSnapshot Snapshot()
    {
        lock (_lock)
        {
            var now = Clock.GetUtcNow();
            return new AiJobSnapshot(
                Id, Kind, Slug, _running, StartedAt, _finishedAt, _step, _stepChangedAt, _error, _providerMessage, _aiProvider, _aiFailure,
                UsageSource?.Invoke(), _result, now);
        }
    }

    internal void SetStep(AiJobStep step)
    {
        lock (_lock)
        {
            if (_step != step)
            {
                _step = step;
                _stepChangedAt = Clock.GetUtcNow();
            }
        }
    }

    internal void Succeed(object result)
    {
        lock (_lock)
        {
            _result = result;
            _running = false;
            _finishedAt = Clock.GetUtcNow();
        }
    }

    internal void Fail(Exception exception)
    {
        lock (_lock)
        {
            _error = AiProviderException.TextOf(exception);
            if (exception is AiProviderException ai)
            {
                _providerMessage = ai.ProviderMessage;
                _aiProvider = ai.Provider;
                _aiFailure = ai.Kind.ToId();
            }
            _running = false;
            _finishedAt = Clock.GetUtcNow();
        }
    }
}

/// <summary>A consistent look at a job at one moment. <see cref="Error"/> is a <see cref="Msg"/>: it is read in the language of the asking request.</summary>
public sealed record AiJobSnapshot(
    Guid Id,
    string Kind,
    string? Slug,
    bool Running,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    AiJobStep Step,
    DateTimeOffset StepChangedAt,
    Msg? Error,
    string? ProviderMessage,
    string? AiProvider,
    string? AiFailure,
    AiUsage? Usage,
    object? Result,
    DateTimeOffset Now)
{
    public int ElapsedSeconds => (int)(ElapsedMilliseconds / 1000);

    /// <summary>The running time without rounding: the UI anchors its clock on it, whole seconds made the shown seconds stall and jump.</summary>
    public long ElapsedMilliseconds => (long)Math.Max(0, ((FinishedAt ?? Now) - StartedAt).TotalMilliseconds);

    public int StepIdleSeconds => Running ? (int)Math.Max(0, (Now - StepChangedAt).TotalSeconds) : 0;
}

/// <summary>
/// AI actions that take minutes (research of a whole guide, many chapters) run in the background, like the guide translation: the request that
/// starts one answers at once with its id, the page polls <c>GET /api/ai/jobs/{id}</c> for the step, the time and finally the result. That keeps every
/// HTTP request short (no timeouts at a reverse proxy) and lets the page show what is really going on. Jobs live in memory: a restart of the
/// server forgets them (what a finished job saved is in the database).
/// </summary>
public sealed class AiJobService(IServiceScopeFactory scopeFactory, ILogger<AiJobService> logger, TimeProvider? clock = null)
{
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(40);
    private static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, AiJob> _jobs = new();
    private readonly Lock _startLock = new();

    public AiJobSnapshot? Get(Guid id) => _jobs.TryGetValue(id, out var job) ? job.Snapshot() : null;

    /// <summary>The running jobs of a game and the ones that finished a moment ago (so a reloaded page can pick up where it was).</summary>
    public IReadOnlyList<AiJobSnapshot> ForSlug(string slug) =>
        [.. _jobs.Values.Where(j => string.Equals(j.Slug, slug, StringComparison.OrdinalIgnoreCase)).Select(j => j.Snapshot()).OrderByDescending(s => s.StartedAt)];

    /// <summary>
    /// Starts <paramref name="work"/> in the background in its own scope (own database context), in the language of the caller. A job with the same
    /// key that is still running is returned instead (a double click or a second tab never starts the action twice) - the caller compares its kind and
    /// <see cref="AiJob.Target"/> with what it asked for and refuses when they differ.
    /// </summary>
    public AiJob Start(string kind, string key, string? slug, Func<IServiceProvider, AiJobProgress, CancellationToken, Task<object>> work, string? target = null, string? targetLabel = null)
    {
        var language = Lang.Current;
        AiJob job;
        lock (_startLock)
        {
            Forget();
            var running = _jobs.Values.FirstOrDefault(j => j.Key == key && j.Snapshot().Running);
            if (running is not null)
            {
                return running;
            }
            job = new AiJob(kind, key, slug, _clock, target, targetLabel);
            _jobs[job.Id] = job;
        }

        // Not tied to the request that asked for it (that one answers at once and the page polls)
        _ = Task.Run(() => RunAsync(job, language, work));
        return job;
    }

    private async Task RunAsync(AiJob job, string language, Func<IServiceProvider, AiJobProgress, CancellationToken, Task<object>> work)
    {
        using var languageScope = Lang.Use(language);
        try
        {
            using var timeout = new CancellationTokenSource(JobTimeout);
            await using var scope = scopeFactory.CreateAsyncScope();
            // The router of this scope knows what the action did with the providers, also while it still runs
            if (scope.ServiceProvider.GetService<AiRouter>() is { } router)
            {
                job.UsageSource = () => router.LastUsage;
            }
            job.Succeed(await work(scope.ServiceProvider, new AiJobProgress(job), timeout.Token));
        }
        catch (Exception ex)
        {
            logger.LogWarning("KI-Auftrag {Kind} ({Key}) fehlgeschlagen: {Message}", job.Kind, job.Key, ex.Message);
            job.Fail(ex);
        }
    }

    /// <summary>Drops jobs that finished a while ago.</summary>
    private void Forget()
    {
        var limit = _clock.GetUtcNow() - KeepFinished;
        foreach (var (id, job) in _jobs)
        {
            var snapshot = job.Snapshot();
            if (!snapshot.Running && snapshot.FinishedAt is { } finished && finished < limit)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }
}
