using Companion.Api.Endpoints;

namespace Companion.Api.Services;

/// <summary>An achievement unlock as the live tracker announces it (also over SignalR as "AchievementUnlocked").</summary>
public sealed record LiveUnlockEvent(LiveAchievementItemDto Achievement, string? GameName, int AppId);

/// <summary>100 % of a game (also over SignalR as "PlatinumUnlocked").</summary>
public sealed record LivePlatinumEvent(string GameName, int Total, int AppId);

/// <summary>
/// The live events of the server for listeners in the same process - the Windows program shows them as an overlay over the game
/// and as Windows notifications. Every place that sends "AchievementUnlocked"/"PlatinumUnlocked" over SignalR publishes here too.
/// A failing listener never breaks the server's own announcement.
/// </summary>
public sealed class LiveEventFeed(ILogger<LiveEventFeed> logger)
{
    public event Action<LiveUnlockEvent>? AchievementUnlocked;

    public event Action<LivePlatinumEvent>? PlatinumUnlocked;

    public void PublishUnlock(LiveUnlockEvent unlock) => Raise(AchievementUnlocked, unlock);

    public void PublishPlatinum(LivePlatinumEvent platinum) => Raise(PlatinumUnlocked, platinum);

    private void Raise<T>(Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ein Empfänger der Live-Ereignisse ist fehlgeschlagen.");
            }
        }
    }
}
