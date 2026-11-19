using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Hubs;
using Companion.Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Companion.Api.Services;

/// <summary>
/// Number of windows (browser tabs, the Windows program's WebView) connected to the live hub right now. Only used for the log:
/// "sent to 0 windows" is what a missing popup looks like when the server did detect the unlock.
/// </summary>
public sealed class LiveConnectionCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Connected() => Interlocked.Increment(ref _count);

    public void Disconnected() => Interlocked.Decrement(ref _count);
}

/// <summary>One announced live event, kept briefly so a window that was not connected at that moment can still show it.</summary>
public sealed record LiveEventEntry(
    long Id,
    DateTimeOffset At,
    string Kind,
    LiveAchievementItemDto? Achievement,
    string? GameName,
    int AppId,
    int? Total,
    // "manual" = ticked by hand in a game added by hand (AppId 0); null = detected by Steam
    string? Source = null);

/// <summary>
/// The recent live events of this server process. SignalR only reaches windows that are connected at the moment of sending; a
/// window that was (re)connecting - backend restart, network blip, a page that loaded before the backend was up - fetches what it
/// missed and compares the ids with the last one it showed. Ids restart with every process, so <see cref="InstanceId"/> tells the
/// window whether its last id still refers to this log.
/// </summary>
public sealed class LiveEventLog(TimeProvider? clock = null)
{
    public const int Capacity = 50;
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    public const string KindAchievement = "achievement";
    public const string KindPlatinum = "platinum";

    /// <summary>Source of an event the player caused by ticking a trophy of a game added by hand (<see cref="LiveEventEntry.Source"/>).</summary>
    public const string SourceManual = "manual";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly List<LiveEventEntry> _entries = [];
    private long _lastId;

    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    public LiveEventEntry AppendUnlock(LiveAchievementItemDto achievement, string? gameName, int appId, string? source = null) =>
        Append(KindAchievement, achievement, gameName, appId, null, source);

    public LiveEventEntry AppendPlatinum(string gameName, int total, int appId, string? source = null) =>
        Append(KindPlatinum, null, gameName, appId, total, source);

    /// <summary>Events of the last <see cref="MaxAge"/>, oldest first.</summary>
    public IReadOnlyList<LiveEventEntry> Recent()
    {
        lock (_lock)
        {
            Trim();
            return [.. _entries];
        }
    }

    public long LatestId
    {
        get
        {
            lock (_lock)
            {
                return _lastId;
            }
        }
    }

    private LiveEventEntry Append(string kind, LiveAchievementItemDto? achievement, string? gameName, int appId, int? total, string? source)
    {
        lock (_lock)
        {
            var entry = new LiveEventEntry(++_lastId, _clock.GetUtcNow(), kind, achievement, gameName, appId, total, source);
            _entries.Add(entry);
            Trim();
            return entry;
        }
    }

    private void Trim()
    {
        var oldest = _clock.GetUtcNow() - MaxAge;
        _entries.RemoveAll(e => e.At < oldest);
        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(0, _entries.Count - Capacity);
        }
    }
}

/// <summary>
/// Sent as the second argument of "AchievementUnlocked"/"PlatinumUnlocked": the window remembers the ids to skip a replayed event.
/// GameName = the game the event belongs to (a window cannot guess it for a game that is not running); Source "manual" = ticked by hand.
/// </summary>
public sealed record LiveEventMeta(long EventId, string InstanceId, string? GameName = null, string? Source = null);

/// <summary>
/// The one place that announces a detected unlock or platinum: event log (catch-up for windows that were not connected), SignalR,
/// the in-process feed (overlay and notifications of the Windows program) and the log line that says how many windows got it.
/// The local stats worker and the bridge both announce through here.
/// </summary>
public sealed class LiveAnnouncer(
    IHubContext<AchievementHub> hubContext,
    LiveEventFeed liveEvents,
    LiveEventLog eventLog,
    LiveConnectionCounter connections,
    ILogger<LiveAnnouncer> logger,
    IServiceScopeFactory? scopeFactory = null)
{
    public Task AnnounceUnlockAsync(LiveAchievementItemDto achievement, string? gameName, int appId, string source, CancellationToken ct) =>
        AnnounceUnlockCoreAsync(achievement, gameName, appId, gameId: null, source, ct);

    /// <summary>
    /// A trophy the player ticked by hand in a game added by hand (no Steam AppID): the same popup, sound and catch-up as a live unlock, but it
    /// goes to the app windows only - never to the Windows overlay over a game (see <see cref="LiveEventFeed"/>). With the setting
    /// "live.pins_after_unlock" = "remove" the trophy leaves the pinned ones of this game.
    /// </summary>
    public Task AnnounceManualUnlockAsync(Guid gameId, LiveAchievementItemDto achievement, string gameName, CancellationToken ct) =>
        AnnounceUnlockCoreAsync(achievement, gameName, appId: 0, gameId, LiveEventLog.SourceManual, ct);

    private async Task AnnounceUnlockCoreAsync(LiveAchievementItemDto achievement, string? gameName, int appId, Guid? gameId, string source, CancellationToken ct)
    {
        var isManual = source == LiveEventLog.SourceManual;
        var entry = eventLog.AppendUnlock(achievement, gameName, appId, isManual ? LiveEventLog.SourceManual : null);
        var windows = connections.Count;
        try
        {
            await hubContext.Clients.All.SendAsync(
                "AchievementUnlocked", achievement, new LiveEventMeta(entry.Id, eventLog.InstanceId, gameName, entry.Source), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Popup für {Name} konnte nicht an die Fenster gesendet werden; sie holen es beim nächsten Verbinden nach.", achievement.DisplayName);
        }
        liveEvents.PublishUnlock(new LiveUnlockEvent(achievement, gameName, appId, entry.Source));

        if (isManual)
        {
            logger.LogInformation(
                "Trophäe von Hand abgehakt: {Name} [{ApiName}] ({Tier}) in {Game} - Popup Nr. {EventId} an {Windows} verbundene(s) Fenster gesendet.",
                achievement.DisplayName, achievement.ApiName, achievement.RarityTier, gameName, entry.Id, windows);
        }
        else
        {
            logger.LogInformation(
                "Neues Steam-Achievement freigeschaltet ({Source}): {Name} [{ApiName}] ({Tier}) in {Game} - Popup Nr. {EventId} an {Windows} verbundene(s) Fenster gesendet.",
                source, achievement.DisplayName, achievement.ApiName, achievement.RarityTier, gameName, entry.Id, windows);
        }
        if (windows == 0)
        {
            logger.LogWarning(
                "Kein PlatinVault-Fenster mit dem Live-Kanal verbunden - das Popup für {Name} erscheint erst, wenn sich ein Fenster (neu) verbindet (höchstens {Minutes} Minuten lang).",
                achievement.DisplayName, (int)LiveEventLog.MaxAge.TotalMinutes);
        }

        await UnpinUnlockedAsync(achievement, appId, gameId, ct);
    }

    /// <summary>
    /// With the setting "live.pins_after_unlock" = "remove" an announced unlock is taken off the game's pinned achievements ("Im Blick"),
    /// then every window is told with "PinsChanged". This is the one place both unlock sources (local worker, bridge) and the manual tick pass,
    /// so the rule exists once. Runs after the popup went out and never fails the announcement.
    /// </summary>
    private async Task UnpinUnlockedAsync(LiveAchievementItemDto achievement, int appId, Guid? gameId, CancellationToken ct)
    {
        if (scopeFactory is null || (appId <= 0 && gameId is null) || string.IsNullOrWhiteSpace(achievement.ApiName))
        {
            return;
        }
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            if (await SettingsStore.ReadPinsAfterUnlockAsync(db, ct) != "remove")
            {
                return;
            }
            var change = gameId is { } id
                ? await PinnedAchievements.RemoveForGameAsync(db, id, [achievement.ApiName], ct)
                : await PinnedAchievements.RemoveAsync(db, (uint)appId, [achievement.ApiName], ct);
            if (change.Changed)
            {
                await PinEndpoints.BroadcastChangedAsync(hubContext, gameId is null ? (uint)appId : 0, logger, ct, gameId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Angehefteter Erfolg {Name} konnte nach dem Freischalten nicht gelöst werden.", achievement.ApiName);
        }
    }

    public Task AnnouncePlatinumAsync(string gameName, int total, int appId, string source, CancellationToken ct) =>
        AnnouncePlatinumCoreAsync(gameName, total, appId, source, ct);

    /// <summary>The platinum moment of a game added by hand (the platinum trophy, or 100 % where there is none); app windows only, like <see cref="AnnounceManualUnlockAsync"/>.</summary>
    public Task AnnounceManualPlatinumAsync(string gameName, int total, CancellationToken ct) =>
        AnnouncePlatinumCoreAsync(gameName, total, appId: 0, LiveEventLog.SourceManual, ct);

    private async Task AnnouncePlatinumCoreAsync(string gameName, int total, int appId, string source, CancellationToken ct)
    {
        var isManual = source == LiveEventLog.SourceManual;
        var entry = eventLog.AppendPlatinum(gameName, total, appId, isManual ? LiveEventLog.SourceManual : null);
        try
        {
            await hubContext.Clients.All.SendAsync("PlatinumUnlocked", new { gameName, total }, new LiveEventMeta(entry.Id, eventLog.InstanceId, gameName, entry.Source), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Platin-Moment für {Game} konnte nicht an die Fenster gesendet werden; sie holen ihn beim nächsten Verbinden nach.", gameName);
        }
        liveEvents.PublishPlatinum(new LivePlatinumEvent(gameName, total, appId, entry.Source));
        logger.LogInformation("100% PLATIN ERREICHT in {Game} ({Source}) - an {Windows} verbundene(s) Fenster gesendet.", gameName, source, connections.Count);
    }
}
