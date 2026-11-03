using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>What the translation job of one game and language is doing / did last (kept in memory; the translations themselves are in the database).</summary>
public sealed record GuideTranslationJobInfo(bool Running, Msg? Error, string? ProviderMessage, DateTimeOffset? FinishedAt, AiUsage? Ai = null);

/// <summary>
/// Translation of a whole guide into the other app language, in the background: the original stays untouched, each chapter is
/// translated (AI, plain generation without web search) into <see cref="GuideTabTranslationEntity"/>, the card/header texts into
/// <see cref="GameTranslationEntity"/>. One job per game and language; it runs independently of the request that started it.
/// A translation is "current" while the hash of its original still matches - so changing the original (complete guide, add chapter,
/// Story &amp; Lore again) makes exactly the changed chapters stale, and the next view translates them again.
/// </summary>
public sealed class GuideTranslationService(IServiceScopeFactory scopeFactory, ILogger<GuideTranslationService> logger)
{
    // After a failure (no key, quota used up) a job is not started again by itself for this long - a page that is opened again and
    // again must not hammer the provider; "try again" (force) overrides it
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(40);

    private sealed class Job
    {
        public bool Running;
        public Msg? Error;
        public string? ProviderMessage;
        public DateTimeOffset? FinishedAt;
        // The AI run of the job: tells (live, while it works) whether it had to change provider
        public AiRun? Run;
    }

    private readonly ConcurrentDictionary<(Guid GameId, string Language), Job> _jobs = new();
    private readonly Lock _jobLock = new();

    public GuideTranslationJobInfo? GetJob(Guid gameId, string language) =>
        _jobs.TryGetValue((gameId, language), out var job) ? new GuideTranslationJobInfo(job.Running, job.Error, job.ProviderMessage, job.FinishedAt, job.Run?.Usage()) : null;

    /// <summary>Starts the job unless one is running (or failed a moment ago and <paramref name="force"/> is not set). True = a job was started now.</summary>
    public bool TryStart(Guid gameId, string language, bool force)
    {
        var key = (gameId, language);
        lock (_jobLock)
        {
            if (_jobs.TryGetValue(key, out var existing))
            {
                if (existing.Running)
                {
                    return false;
                }
                if (!force && existing.Error is not null && existing.FinishedAt is { } at && DateTimeOffset.UtcNow - at < FailureCooldown)
                {
                    return false;
                }
            }
            _jobs[key] = new Job { Running = true };
        }

        // Not tied to the request that asked for it (that one answers at once and the page polls the status)
        _ = Task.Run(() => RunAsync(gameId, language));
        return true;
    }

    private async Task RunAsync(Guid gameId, string language)
    {
        Msg? error = null;
        string? providerMessage = null;
        try
        {
            using var timeout = new CancellationTokenSource(JobTimeout);
            await TranslateAsync(gameId, language, timeout.Token);
        }
        catch (Exception ex)
        {
            error = AiProviderException.TextOf(ex);
            providerMessage = (ex as AiProviderException)?.ProviderMessage;
            logger.LogWarning("Übersetzung des Guides {GameId} nach {Language} fehlgeschlagen: {Message}", gameId, language, ex.Message);
        }
        finally
        {
            lock (_jobLock)
            {
                if (_jobs.TryGetValue((gameId, language), out var job))
                {
                    job.Running = false;
                    job.Error = error;
                    job.ProviderMessage = providerMessage;
                    job.FinishedAt = DateTimeOffset.UtcNow;
                }
            }
        }
    }

    private async Task TranslateAsync(Guid gameId, string language, CancellationToken ct)
    {
        using var languageScope = Lang.Use(language);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<AiGuideGeneratorService>();
        var steamClient = scope.ServiceProvider.GetRequiredService<SteamClient>();

        var game = await db.Games.AsNoTracking().Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Id == gameId, ct)
                   ?? throw new LocalizedException(ServerTexts.Translation.GameGone);
        var source = AiGuideGeneratorService.GuideLanguage(game);
        if (source == language)
        {
            return;
        }

        var tabIds = game.Tabs.Select(t => t.Id).ToList();
        var existing = await db.GuideTabTranslations.AsNoTracking()
            .Where(t => t.Language == language && tabIds.Contains(t.TabId))
            .ToDictionaryAsync(t => t.TabId, ct);
        var pendingTabs = game.Tabs
            .OrderBy(t => t.SortOrder)
            .Where(t => !existing.TryGetValue(t.Id, out var done) || done.SourceHash != GuideTranslations.HashTab(t))
            .ToList();
        var gameRow = await db.GameTranslations.AsNoTracking().FirstOrDefaultAsync(t => t.GameId == gameId && t.Language == language, ct);
        var gamePending = gameRow is null || gameRow.SourceHash != GuideTranslations.HashGame(game);

        // Texts the app itself wrote (the starter chapter, the default card texts) are translated without the AI
        // (structures are compared, not strings: stored JSON may differ in spacing and key order from freshly built text)
        var starterSource = JsonNode.Parse(ApiEndpoints.CreateStarterTab(new GameEntity { Id = game.Id, Title = game.Title, ContentLanguage = source }).SectionsJson);
        var starterTarget = ApiEndpoints.CreateStarterTab(new GameEntity { Id = game.Id, Title = game.Title, ContentLanguage = language });
        var aiTabs = new List<GuideTabEntity>();
        foreach (var tab in pendingTabs)
        {
            if (tab.Slug == "overview" && JsonNode.DeepEquals(JsonNode.Parse(tab.SectionsJson), starterSource))
            {
                await SaveTabAsync(tab, language, starterTarget.Title, starterTarget.SectionsJson, ct);
            }
            else
            {
                aiTabs.Add(tab);
            }
        }

        var sourceDefaults = AiGuideGeneratorService.DefaultsFor(source);
        var gameTexts = new[] { game.Subtitle, game.EstimatedHours, game.MissablesInfo, game.DifficultyInfo, game.GenreTags };
        var gameIsDefault = gameTexts.SequenceEqual([sourceDefaults.Subtitle, sourceDefaults.EstimatedHours, sourceDefaults.MissablesInfo, sourceDefaults.DifficultyInfo, sourceDefaults.GenreTags]);
        if (gamePending && gameIsDefault)
        {
            var d = AiGuideGeneratorService.DefaultsFor(language);
            await SaveGameAsync(game, language, d.Subtitle, d.EstimatedHours, d.MissablesInfo, d.DifficultyInfo, d.GenreTags, ct);
            gamePending = false;
        }

        if (aiTabs.Count == 0 && !gamePending)
        {
            return;
        }

        // From here on the AI is needed: no stored key stops the job with the usual "store an AI key" message
        var settings = await ai.GetSettingsAsync(ct);
        var run = await ai.BeginTranslationRunAsync(ct);
        lock (_jobLock)
        {
            if (_jobs.TryGetValue((gameId, language), out var current))
            {
                current.Run = run;
            }
        }
        using var gate = new SemaphoreSlim(3);
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var achievementTexts = game.SteamAppId is > 0 && aiTabs.Any(IsAchievementTab)
            ? await steamClient.FetchSteamAchievementsSchemaAsync(game.SteamAppId.Value, language, ct)
            : [];

        var jobs = aiTabs.Select(tab => TranslateTabAsync(tab)).ToList();
        if (gamePending)
        {
            jobs.Add(TranslateGameAsync());
        }
        var errors = (await Task.WhenAll(jobs)).Where(e => e is not null).Select(e => e!).ToList();
        ai.LogSwitches(run, "Guide übersetzen");
        if (errors.Count > 0)
        {
            // Everything that worked is saved already; the job reports the most useful failure (the provider's, with its text)
            throw errors.FirstOrDefault(e => e is AiProviderException) ?? errors[0];
        }

        async Task<Exception?> TranslateTabAsync(GuideTabEntity tab)
        {
            try
            {
                var original = JsonNode.Parse(string.IsNullOrWhiteSpace(tab.SectionsJson) ? "[]" : tab.SectionsJson) as JsonArray ?? [];
                var working = (JsonArray)original.DeepClone();
                var slots = GuideTextExtractor.Collect(working);
                var texts = GuideTextExtractor.DistinctTexts(slots);
                if (GuideTextExtractor.NeedsTranslation(tab.Title) && !texts.Contains(tab.Title))
                {
                    texts.Add(tab.Title);
                }

                var map = await ai.TranslateTextsAsync(game.Title, tab.Title, texts, source, language, run, settings, gate, abort.Token);
                GuideTextExtractor.Apply(slots, map);

                // The Steam achievement list is built from Steam's own texts in the target language wherever Steam has them
                OverlaySteamTexts(working, achievementTexts);

                var validated = GuideTextExtractor.ValidateTranslated(original, working, tab.Title);
                var title = IsAchievementTab(tab) && System.Text.RegularExpressions.Regex.IsMatch(tab.Title, @"^\d+\.\s")
                    ? AiGuideGeneratorService.AchievementTabTitle(tab.SortOrder, language)
                    : map.GetValueOrDefault(tab.Title, tab.Title);
                await SaveTabAsync(tab, language, title, validated.ToJsonString(), ct);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Kapitel '{Chapter}' konnte nicht übersetzt werden: {Message}", tab.Title, ex.Message);
                // Every provider refused this chapter: the rest would fail the same way, so stop the other chapters instead of asking again
                if (ex is AiProviderException)
                {
                    abort.Cancel();
                }
                return ex is OperationCanceledException ? new LocalizedException(ServerTexts.Ai.NoUsableAnswer(run.Primary)) : ex;
            }
        }

        async Task<Exception?> TranslateGameAsync()
        {
            try
            {
                var texts = gameTexts.Where(GuideTextExtractor.NeedsTranslation).Distinct(StringComparer.Ordinal).ToList();
                var map = await ai.TranslateTextsAsync(game.Title, game.Title, texts, source, language, run, settings, gate, abort.Token);
                string Tr(string value) => map.GetValueOrDefault(value, value);
                await SaveGameAsync(game, language, Tr(game.Subtitle), Tr(game.EstimatedHours), Tr(game.MissablesInfo), Tr(game.DifficultyInfo), Tr(game.GenreTags), ct);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Kartentexte von {Game} konnten nicht übersetzt werden: {Message}", game.Title, ex.Message);
                if (ex is AiProviderException)
                {
                    abort.Cancel();
                }
                return ex is OperationCanceledException ? new LocalizedException(ServerTexts.Ai.NoUsableAnswer(run.Primary)) : ex;
            }
        }
    }

    private static bool IsAchievementTab(GuideTabEntity tab) =>
        string.Equals(tab.Slug, "achievements", StringComparison.OrdinalIgnoreCase) ||
        tab.SectionsJson.Contains("\"steam-achievements\"", StringComparison.Ordinal);

    /// <summary>Row id = Steam API name: Steam's own name and condition in the target language replace the translated ones.</summary>
    private static void OverlaySteamTexts(JsonArray sections, List<(string ApiName, string DisplayName, string Description)> steamTexts)
    {
        if (steamTexts.Count == 0)
        {
            return;
        }
        var byName = steamTexts.GroupBy(t => t.ApiName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections.OfType<JsonObject>())
        {
            if (section["table"] is not JsonObject { } table || table["tableId"]?.ToString() != "steam-achievements" || table["rows"] is not JsonArray rows)
            {
                continue;
            }
            foreach (var row in rows.OfType<JsonObject>())
            {
                if (row["cells"] is not JsonObject cells || row["itemId"]?.ToString() is not { } id || !byName.TryGetValue(id, out var steam))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(steam.DisplayName) && cells.ContainsKey("name")) cells["name"] = steam.DisplayName;
                if (!string.IsNullOrWhiteSpace(steam.Description) && cells.ContainsKey("description")) cells["description"] = steam.Description;
            }
        }
    }

    private async Task SaveTabAsync(GuideTabEntity tab, string language, string title, string sectionsJson, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var row = await db.GuideTabTranslations.FirstOrDefaultAsync(t => t.TabId == tab.Id && t.Language == language, ct);
        if (row is null)
        {
            row = new GuideTabTranslationEntity { TabId = tab.Id, Language = language };
            db.GuideTabTranslations.Add(row);
        }
        row.Title = title.Length > 240 ? title[..240] : title;
        row.SectionsJson = sectionsJson;
        row.SourceHash = GuideTranslations.HashTab(tab);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The chapter was deleted while it was being translated: nothing to keep
            logger.LogDebug(ex, "Übersetzung von Kapitel {Tab} nicht gespeichert (Kapitel gelöscht?).", tab.Id);
        }
    }

    private async Task SaveGameAsync(GameEntity game, string language, string subtitle, string hours, string missables, string difficulty, string genres, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var row = await db.GameTranslations.FirstOrDefaultAsync(t => t.GameId == game.Id && t.Language == language, ct);
        if (row is null)
        {
            row = new GameTranslationEntity { GameId = game.Id, Language = language };
            db.GameTranslations.Add(row);
        }
        static string Cut(string value, int max) => value.Length > max ? value[..max] : value;
        row.Subtitle = Cut(subtitle, 500);
        row.EstimatedHours = Cut(hours, 160);
        row.MissablesInfo = Cut(missables, 240);
        row.DifficultyInfo = Cut(difficulty, 240);
        row.GenreTags = Cut(genres, 240);
        row.SourceHash = GuideTranslations.HashGame(game);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogDebug(ex, "Übersetzung der Kartentexte von {Game} nicht gespeichert (Spiel gelöscht?).", game.Id);
        }
    }
}

/// <summary>
/// What a game page needs to know about the translation of its guide into one language. State: "original" = that language IS the
/// guide's own, "complete" = every chapter and the card texts have a current translation (the translated version is shown),
/// "missing" = something is not translated yet (the original is shown, see Running / Error).
/// </summary>
internal sealed record TranslationView(
    string Language,
    string SourceLanguage,
    string State,
    IReadOnlyDictionary<Guid, GuideTabTranslationEntity> Tabs,
    GameTranslationEntity? Game,
    int Total,
    int Translated,
    GuideTranslationJobInfo? Job)
{
    public bool UseTranslation => State == "complete";

    public GuideTranslationDto ToDto() => new(
        Language,
        SourceLanguage,
        State,
        Total,
        Translated,
        Running: Job?.Running == true,
        // A failure is only news while there is still something to translate
        Error: State == "missing" && Job is { Running: false, Error: { } error } ? error.ToString() : null,
        ProviderMessage: State == "missing" && Job is { Running: false, Error: not null } ? Job.ProviderMessage : null,
        // Said while the job runs and shortly after it: "Gemini quota used up – continuing with Claude." A job that finished long ago is no news
        // any more (the notice would pop up on every visit of the game page for as long as the server process lives).
        AiNotice: Job is { Ai.Switches.Count: > 0 } job && (job.Running || job.FinishedAt is { } at && DateTimeOffset.UtcNow - at < NoticeWindow)
            ? AiUsageMapping.NoticeOf(job.Ai.Switches[^1], job.Ai.Research)
            : null);

    /// <summary>How long after the end of a translation job the change of AI is still reported to a game page.</summary>
    private static readonly TimeSpan NoticeWindow = TimeSpan.FromMinutes(10);
}

internal static class GuideTranslations
{
    public static string HashText(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string HashTab(GuideTabEntity tab) => HashText(tab.Title + "\n" + tab.SectionsJson);

    public static string HashGame(GameEntity game) =>
        HashText(string.Join('\u001f', game.Subtitle, game.EstimatedHours, game.MissablesInfo, game.DifficultyInfo, game.GenreTags));

    /// <summary>Compares the stored translations of a game (its <c>Tabs</c> must be loaded) with the original.</summary>
    public static async Task<TranslationView> EvaluateAsync(CompanionDbContext db, GameEntity game, string language, GuideTranslationJobInfo? job, CancellationToken ct)
    {
        var source = AiGuideGeneratorService.GuideLanguage(game);
        if (language == source)
        {
            return new TranslationView(language, source, "original", new Dictionary<Guid, GuideTabTranslationEntity>(), null, game.Tabs.Count, game.Tabs.Count, null);
        }

        var tabIds = game.Tabs.Select(t => t.Id).ToList();
        var stored = await db.GuideTabTranslations.AsNoTracking()
            .Where(t => t.Language == language && tabIds.Contains(t.TabId))
            .ToListAsync(ct);
        var current = new Dictionary<Guid, GuideTabTranslationEntity>();
        foreach (var tab in game.Tabs)
        {
            var row = stored.FirstOrDefault(t => t.TabId == tab.Id);
            if (row is not null && row.SourceHash == HashTab(tab))
            {
                current[tab.Id] = row;
            }
        }

        var gameRow = await db.GameTranslations.AsNoTracking().FirstOrDefaultAsync(t => t.GameId == game.Id && t.Language == language, ct);
        var gameCurrent = gameRow is not null && gameRow.SourceHash == HashGame(game) ? gameRow : null;
        var complete = current.Count == game.Tabs.Count && gameCurrent is not null;
        return new TranslationView(language, source, complete ? "complete" : "missing", current, gameCurrent, game.Tabs.Count, current.Count, job);
    }

    /// <summary>Only the card/header texts (the dashboard list needs no chapter check).</summary>
    public static async Task<Dictionary<Guid, GameTranslationEntity>> CurrentGameTextsAsync(CompanionDbContext db, IReadOnlyList<GameEntity> games, string language, CancellationToken ct)
    {
        var rows = await db.GameTranslations.AsNoTracking().Where(t => t.Language == language).ToListAsync(ct);
        var result = new Dictionary<Guid, GameTranslationEntity>();
        foreach (var game in games)
        {
            if (AiGuideGeneratorService.GuideLanguage(game) == language)
            {
                continue;
            }
            var row = rows.FirstOrDefault(r => r.GameId == game.Id);
            if (row is not null && row.SourceHash == HashGame(game))
            {
                result[game.Id] = row;
            }
        }
        return result;
    }
}
