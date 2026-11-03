using Companion.Api.Endpoints;
using Companion.Api.Hubs;
using Companion.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Tests;

/// <summary>
/// Live detection of Steam unlocks (bug 2026-11-02: "Olderion-Blues" unlocked while playing, no popup). Covers the comparison
/// (ledger), the merge of the local stats file into a stale remote list, the schedule of the 350 ms worker loop, the catch-up after
/// a server restart and the announcement log that lets a window catch up on what it missed while it was not connected.
/// </summary>
public class LiveUnlockDetectionTests
{
    private const uint App = 3259780;

    private static readonly DateTime UnlockedEarlier = new(2026, 11, 1, 22, 48, 59, DateTimeKind.Local);

    private static LiveAchievementItemDto Remote(string apiName, bool achieved, DateTime? unlockTime = null) => new(
        ApiName: apiName,
        DisplayName: $"Remote {apiName}",
        Description: "remote",
        OriginalDescription: null,
        IsTranslated: false,
        IconUrl: achieved ? $"color-{apiName}" : $"gray-{apiName}",
        IconGrayUrl: $"gray-{apiName}",
        Achieved: achieved,
        UnlockTime: unlockTime,
        GlobalPercent: 25,
        RarityTier: "Silver",
        RarityLabel: string.Empty,
        IsHidden: false,
        UserNote: string.Empty);

    private static SteamLocalStatsService.LocalAchievementStat Local(string apiName, bool achieved, DateTime? unlockTime = null) => new()
    {
        ApiName = apiName,
        DisplayName = $"Local {apiName}",
        Icon = $"local-color-{apiName}",
        IconGray = $"local-gray-{apiName}",
        Achieved = achieved,
        UnlockTime = unlockTime
    };

    private static List<LiveAchievementItemDto> Merge(List<LiveAchievementItemDto>? remote, params SteamLocalStatsService.LocalAchievementStat[] local) =>
        SteamLiveTrackerService.MergeLocalStats(remote, local, [], [], "de");

    private static List<UnlockState> Achieved(IEnumerable<LiveAchievementItemDto> list) =>
        [.. list.Where(a => a.Achieved).Select(a => new UnlockState(a.ApiName, a.UnlockTime))];

    // --- Ledger -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_comparison_only_takes_the_baseline_and_the_next_unlock_is_announced_exactly_once()
    {
        var ledgers = new UnlockLedgers();
        var before = new List<UnlockState> { new("0001", UnlockedEarlier), new("0002", UnlockedEarlier) };

        var baseline = ledgers.Diff(UnlockSource.Local, App, before, 40);
        Assert.True(baseline.IsBaseline);
        Assert.Empty(baseline.Fresh);

        var after = new List<UnlockState>(before) { new("0003", DateTime.Now) };
        Assert.Equal(["0003"], ledgers.Diff(UnlockSource.Local, App, after, 40).Fresh);
        Assert.Empty(ledgers.Diff(UnlockSource.Local, App, after, 40).Fresh);
    }

    [Fact]
    public void A_list_that_briefly_lacks_an_unlock_never_makes_it_new_a_second_time()
    {
        var ledgers = new UnlockLedgers();
        ledgers.Diff(UnlockSource.Local, App, [new("0001", null)], 40);
        Assert.Equal(["0003"], ledgers.Diff(UnlockSource.Local, App, [new("0001", null), new("0003", null)], 40).Fresh);

        // Half-written file / stale remote list without the unlock, then the full list again
        Assert.Empty(ledgers.Diff(UnlockSource.Local, App, [new("0001", null)], 40).Fresh);
        Assert.Empty(ledgers.Diff(UnlockSource.Local, App, [new("0001", null), new("0003", null)], 40).Fresh);
    }

    [Fact]
    public void An_empty_list_is_no_data_and_does_not_become_the_baseline()
    {
        var ledgers = new UnlockLedgers();
        var diff = ledgers.Diff(UnlockSource.Local, App, [], 0);
        Assert.False(diff.IsBaseline);
        Assert.True(ledgers.NeedsBaseline(UnlockSource.Local, App));
    }

    // --- Local stats file vs. stale remote list ---------------------------------------------------------------------------

    [Fact]
    public void A_stale_remote_list_never_hides_a_fresh_local_unlock_and_the_remote_catching_up_announces_nothing_again()
    {
        var ledgers = new UnlockLedgers();
        var staleRemote = new List<LiveAchievementItemDto> { Remote("0001", true, UnlockedEarlier), Remote("0003", false) };

        // Baseline: 0003 still locked everywhere
        ledgers.Diff(UnlockSource.Local, App, Achieved(Merge(staleRemote, Local("0001", true, UnlockedEarlier), Local("0003", false))), 2);

        // Steam writes the local file; the remote list is still the cached one from before the unlock
        var unlockedAt = new DateTime(2026, 11, 2, 12, 50, 55, DateTimeKind.Local);
        var merged = Merge(staleRemote, Local("0001", true, UnlockedEarlier), Local("0003", true, unlockedAt));
        var item = Assert.Single(merged, a => a.ApiName == "0003");
        Assert.True(item.Achieved);
        Assert.Equal(unlockedAt, item.UnlockTime);
        Assert.Equal("local-color-0003", item.IconUrl);
        Assert.Equal(["0003"], ledgers.Diff(UnlockSource.Local, App, Achieved(merged), merged.Count).Fresh);

        // A minute later Steam's servers know it too: no second popup
        var freshRemote = new List<LiveAchievementItemDto> { Remote("0001", true, UnlockedEarlier), Remote("0003", true, unlockedAt) };
        var later = Merge(freshRemote, Local("0001", true, UnlockedEarlier), Local("0003", true, unlockedAt));
        Assert.Empty(ledgers.Diff(UnlockSource.Local, App, Achieved(later), later.Count).Fresh);
    }

    [Fact]
    public void A_remote_list_without_the_local_names_does_not_swallow_local_unlocks()
    {
        // Before the fix the local file was only merged into entries of the remote list with the same name, so a remote list under
        // other names (Community XML without <apiname> gets random ids) left every local unlock out
        var foreignRemote = new List<LiveAchievementItemDto> { Remote(Guid.NewGuid().ToString(), false), Remote(Guid.NewGuid().ToString(), false) };

        var merged = Merge(foreignRemote, Local("0001", false), Local("0003", true, DateTime.Now));

        Assert.Equal(["0001", "0003"], merged.Select(a => a.ApiName));
        Assert.True(merged.Single(a => a.ApiName == "0003").Achieved);
    }

    // --- Schedule of the 350 ms loop --------------------------------------------------------------------------------------

    [Fact]
    public void A_changed_stats_file_is_checked_in_the_next_cycle_without_waiting_for_the_minute()
    {
        var clock = new ManualClock(Fakes.Start);
        var schedule = new LocalStatsSchedule(clock);

        Assert.Equal(LocalCheckReason.Initial, schedule.Due(100));
        schedule.Completed(100, LocalCheckReason.Initial, foundNew: false);
        Assert.Equal(LocalCheckReason.None, schedule.Due(100));

        clock.Advance(TimeSpan.FromMilliseconds(350));
        Assert.Equal(LocalCheckReason.FileChanged, schedule.Due(200));
        schedule.Completed(200, LocalCheckReason.FileChanged, foundNew: true);
        Assert.Equal(LocalCheckReason.None, schedule.Due(200));

        clock.Advance(LocalStatsSchedule.PeriodicRefresh);
        Assert.Equal(LocalCheckReason.Periodic, schedule.Due(200));
    }

    [Fact]
    public void A_file_change_that_showed_nothing_new_gets_a_second_look_a_moment_later()
    {
        var clock = new ManualClock(Fakes.Start);
        var schedule = new LocalStatsSchedule(clock);
        schedule.Completed(100, LocalCheckReason.Initial, foundNew: false);

        // Steam may write the file in steps: the first read can still be the old state
        Assert.Equal(LocalCheckReason.FileChanged, schedule.Due(200));
        schedule.Completed(200, LocalCheckReason.FileChanged, foundNew: false);
        Assert.Equal(LocalCheckReason.None, schedule.Due(200));

        clock.Advance(LocalStatsSchedule.SettleDelay);
        Assert.Equal(LocalCheckReason.Settle, schedule.Due(200));
        schedule.Completed(200, LocalCheckReason.Settle, foundNew: false);

        clock.Advance(LocalStatsSchedule.SettleDelay);
        Assert.Equal(LocalCheckReason.None, schedule.Due(200));
    }

    [Fact]
    public void A_failed_check_keeps_the_file_change_pending_instead_of_waiting_a_minute()
    {
        // The old loop marked the change as handled BEFORE reading the list; an exception there (logged at Debug level only) meant
        // the unlock was next looked at by the 60 s safety net
        var clock = new ManualClock(Fakes.Start);
        var schedule = new LocalStatsSchedule(clock);
        schedule.Completed(100, LocalCheckReason.Initial, foundNew: false);

        Assert.Equal(LocalCheckReason.FileChanged, schedule.Due(200));
        schedule.Failed();
        Assert.Equal(LocalCheckReason.None, schedule.Due(200));

        clock.Advance(LocalStatsSchedule.RetryDelay);
        Assert.Equal(LocalCheckReason.Retry, schedule.Due(200));
        schedule.Completed(200, LocalCheckReason.Retry, foundNew: true);
        Assert.Equal(LocalCheckReason.None, schedule.Due(200));
    }

    // --- Server restart -----------------------------------------------------------------------------------------------------

    [Fact]
    public void After_a_restart_an_unlock_after_the_stored_watermark_is_announced_with_the_baseline_and_nothing_older()
    {
        var now = new DateTimeOffset(2026, 11, 2, 11, 52, 0, TimeSpan.Zero);
        var watermark = now - TimeSpan.FromMinutes(2);
        var ledgers = new UnlockLedgers();

        var diff = ledgers.Diff(UnlockSource.Local, App,
            [
                new("0001", (watermark - TimeSpan.FromHours(20)).UtcDateTime),
                new("0003", (watermark + TimeSpan.FromSeconds(30)).UtcDateTime)
            ],
            40,
            UnlockLedgers.CatchUpAfter(watermark, now));

        Assert.True(diff.IsBaseline);
        Assert.Equal(["0003"], diff.Fresh);
        Assert.Equal(1, diff.CaughtUp);
    }

    [Fact]
    public void Without_a_stored_watermark_the_baseline_announces_nothing_and_old_unlocks_are_never_replayed()
    {
        var now = new DateTimeOffset(2026, 11, 2, 11, 52, 0, TimeSpan.Zero);
        Assert.Null(UnlockLedgers.CatchUpAfter(null, now));

        // A watermark from yesterday is capped: only the last minutes count
        var capped = UnlockLedgers.CatchUpAfter(now - TimeSpan.FromDays(1), now);
        Assert.Equal(now - UnlockLedgers.MaxCatchUp, capped);

        var ledgers = new UnlockLedgers();
        var diff = ledgers.Diff(UnlockSource.Local, App, [new("0001", (now - TimeSpan.FromHours(3)).UtcDateTime)], 40, capped);
        Assert.Empty(diff.Fresh);
    }

    [Fact]
    public void An_announced_unlock_is_not_announced_again_after_a_restart()
    {
        var unlock = new DateTimeOffset(2026, 11, 2, 11, 50, 55, TimeSpan.Zero);
        var announcedAt = unlock + TimeSpan.FromMilliseconds(40);
        var achieved = new List<UnlockState> { new("0003", unlock.UtcDateTime) };
        var watermark = UnlockLedgers.NextWatermark(achieved, announcedAt);

        var afterRestart = new UnlockLedgers().Diff(UnlockSource.Local, App, achieved, 40, UnlockLedgers.CatchUpAfter(watermark, announcedAt + TimeSpan.FromSeconds(20)));
        Assert.Empty(afterRestart.Fresh);
    }

    [Fact]
    public void A_bridge_clock_ahead_of_the_server_does_not_replay_its_unlock_after_a_restart()
    {
        var serverNow = new DateTimeOffset(2026, 11, 2, 11, 50, 0, TimeSpan.Zero);
        var bridgeTime = serverNow + TimeSpan.FromMinutes(1);
        var achieved = new List<UnlockState> { new("0003", bridgeTime.UtcDateTime) };

        var watermark = UnlockLedgers.NextWatermark(achieved, serverNow);
        Assert.Equal(bridgeTime, watermark);

        var afterRestart = new UnlockLedgers().Diff(UnlockSource.Bridge, App, achieved, 40, UnlockLedgers.CatchUpAfter(watermark, serverNow + TimeSpan.FromSeconds(30)));
        Assert.Empty(afterRestart.Fresh);
    }

    // --- Announcement & catch-up log ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unlock_sent_while_no_window_is_connected_stays_in_the_log_for_the_next_window()
    {
        var clock = new ManualClock(Fakes.Start);
        var log = new LiveEventLog(clock);
        var hub = new RecordingHubContext();
        var feed = new LiveEventFeed(NullLogger<LiveEventFeed>.Instance);
        LiveUnlockEvent? published = null;
        feed.AchievementUnlocked += e => published = e;
        var announcer = new LiveAnnouncer(hub, feed, log, new LiveConnectionCounter(), NullLogger<LiveAnnouncer>.Instance);

        await announcer.AnnounceUnlockAsync(Remote("0003", true, DateTime.Now), "Final Fantasy Resonance", (int)App, "lokal", CancellationToken.None);

        var sent = Assert.Single(hub.Sent);
        Assert.Equal("AchievementUnlocked", sent.Method);
        var meta = Assert.IsType<LiveEventMeta>(sent.Args[1]);
        Assert.Equal(log.InstanceId, meta.InstanceId);
        Assert.NotNull(published);

        var entry = Assert.Single(log.Recent());
        Assert.Equal(meta.EventId, entry.Id);
        Assert.Equal("0003", entry.Achievement?.ApiName);
        Assert.Equal(LiveEventLog.KindAchievement, entry.Kind);
    }

    [Fact]
    public async Task A_failing_signalr_send_still_reaches_the_overlay_and_the_log()
    {
        var log = new LiveEventLog();
        var feed = new LiveEventFeed(NullLogger<LiveEventFeed>.Instance);
        var overlay = 0;
        feed.AchievementUnlocked += _ => overlay++;
        var announcer = new LiveAnnouncer(new RecordingHubContext { Fail = true }, feed, log, new LiveConnectionCounter(), NullLogger<LiveAnnouncer>.Instance);

        await announcer.AnnounceUnlockAsync(Remote("0003", true), "Game", (int)App, "lokal", CancellationToken.None);

        Assert.Equal(1, overlay);
        Assert.Single(log.Recent());
    }

    [Fact]
    public void The_log_keeps_the_last_minutes_with_increasing_ids()
    {
        var clock = new ManualClock(Fakes.Start);
        var log = new LiveEventLog(clock);
        var first = log.AppendUnlock(Remote("0001", true), "Game", 1);
        clock.Advance(LiveEventLog.MaxAge - TimeSpan.FromSeconds(1));
        var second = log.AppendPlatinum("Game", 40, 1);

        Assert.True(second.Id > first.Id);
        Assert.Equal(2, log.Recent().Count);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal([second.Id], log.Recent().Select(e => e.Id));
        Assert.Equal(second.Id, log.LatestId);

        for (var i = 0; i < LiveEventLog.Capacity + 5; i++)
        {
            log.AppendUnlock(Remote($"x{i}", true), "Game", 1);
        }
        Assert.Equal(LiveEventLog.Capacity, log.Recent().Count);
    }

    private sealed class RecordingHubContext : IHubContext<AchievementHub>, IClientProxy
    {
        public bool Fail { get; init; }

        public List<(string Method, object?[] Args)> Sent { get; } = [];

        public IHubClients Clients => new AllClients(this);

        public IGroupManager Groups => throw new NotSupportedException();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("Verbindung verloren");
            Sent.Add((method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class AllClients(IClientProxy proxy) : IHubClients
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
