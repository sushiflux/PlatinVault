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
    int? Total);

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

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly List<LiveEventEntry> _entries = [];
    private long _lastId;

    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    public LiveEventEntry AppendUnlock(LiveAchievementItemDto achievement, string? gameName, int appId) =>
        Append(KindAchievement, achievement, gameName, appId, null);

    public LiveEventEntry AppendPlatinum(string gameName, int total, int appId) =>
        Append(KindPlatinum, null, gameName, appId, total);

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

    private LiveEventEntry Append(string kind, LiveAchievementItemDto? achievement, string? gameName, int appId, int? total)
    {
        lock (_lock)
        {
            var entry = new LiveEventEntry(++_lastId, _clock.GetUtcNow(), kind, achievement, gameName, appId, total);
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

/// <summary>Sent as the second argument of "AchievementUnlocked"/"PlatinumUnlocked": the window remembers it to skip a replayed event.</summary>
public sealed record LiveEventMeta(long EventId, string InstanceId);

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
    public async Task AnnounceUnlockAsync(LiveAchievementItemDto achievement, string? gameName, int appId, string source, CancellationToken ct)
    {
        var entry = eventLog.AppendUnlock(achievement, gameName, appId);
        var windows = connections.Count;
        try
        {
            await hubContext.Clients.All.SendAsync("AchievementUnlocked", achievement, new LiveEventMeta(entry.Id, eventLog.InstanceId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Popup für {Name} konnte nicht an die Fenster gesendet werden; sie holen es beim nächsten Verbinden nach.", achievement.DisplayName);
        }
        liveEvents.PublishUnlock(new LiveUnlockEvent(achievement, gameName, appId));

        logger.LogInformation(
            "Neues Steam-Achievement freigeschaltet ({Source}): {Name} [{ApiName}] ({Tier}) in {Game} - Popup Nr. {EventId} an {Windows} verbundene(s) Fenster gesendet.",
            source, achievement.DisplayName, achievement.ApiName, achievement.RarityTier, gameName, entry.Id, windows);
        if (windows == 0)
        {
            logger.LogWarning(
                "Kein PlatinVault-Fenster mit dem Live-Kanal verbunden - das Popup für {Name} erscheint erst, wenn sich ein Fenster (neu) verbindet (höchstens {Minutes} Minuten lang).",
                achievement.DisplayName, (int)LiveEventLog.MaxAge.TotalMinutes);
        }

        await UnpinUnlockedAsync(achievement, appId, ct);
    }

    /// <summary>
    /// With the setting "live.pins_after_unlock" = "remove" an announced unlock is taken off the game's pinned achievements ("Im Blick"),
    /// then every window is told with "PinsChanged". This is the one place both unlock sources (local worker, bridge) pass, so the rule
    /// exists once. Runs after the popup went out and never fails the announcement.
    /// </summary>
    private async Task UnpinUnlockedAsync(LiveAchievementItemDto achievement, int appId, CancellationToken ct)
    {
        if (scopeFactory is null || appId <= 0 || string.IsNullOrWhiteSpace(achievement.ApiName))
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
            var change = await PinnedAchievements.RemoveAsync(db, (uint)appId, [achievement.ApiName], ct);
            if (change.Changed)
            {
                await PinEndpoints.BroadcastChangedAsync(hubContext, (uint)appId, logger, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Angehefteter Erfolg {Name} konnte nach dem Freischalten nicht gelöst werden.", achievement.ApiName);
        }
    }

    public async Task AnnouncePlatinumAsync(string gameName, int total, int appId, string source, CancellationToken ct)
    {
        var entry = eventLog.AppendPlatinum(gameName, total, appId);
        try
        {
            await hubContext.Clients.All.SendAsync("PlatinumUnlocked", new { gameName, total }, new LiveEventMeta(entry.Id, eventLog.InstanceId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Platin-Moment für {Game} konnte nicht an die Fenster gesendet werden; sie holen ihn beim nächsten Verbinden nach.", gameName);
        }
        liveEvents.PublishPlatinum(new LivePlatinumEvent(gameName, total, appId));
        logger.LogInformation("100% PLATIN ERREICHT in {Game} ({Source}) - an {Windows} verbundene(s) Fenster gesendet.", gameName, source, connections.Count);
    }
}
