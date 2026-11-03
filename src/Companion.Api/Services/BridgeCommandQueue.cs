namespace Companion.Api.Services;

/// <summary>Where a launch command is on its way from the app to the gaming PC.</summary>
public enum LaunchState
{
    /// <summary>Queued, the bridge has not asked for it yet.</summary>
    Pending,

    /// <summary>Handed to the bridge, its confirmation is still missing.</summary>
    Delivered,

    /// <summary>The bridge handed the steam:// address to Windows (not proof that the game is already up).</summary>
    Done,

    /// <summary>The bridge reported that it could not start the game (see <see cref="LaunchFailureReason"/>).</summary>
    Failed,

    /// <summary>Nobody picked the command up, or the bridge never confirmed it, within the time limit.</summary>
    Expired,
}

/// <summary>Machine-readable reasons a bridge reports for a failed start (anything else is read as <see cref="StartFailed"/>).</summary>
public static class LaunchFailureReason
{
    /// <summary>Windows has no handler for steam:// - Steam is not installed on the gaming PC.</summary>
    public const string SteamNotFound = "steam-not-found";

    /// <summary>Any other error while handing the address to Windows.</summary>
    public const string StartFailed = "start-failed";

    public static string Normalize(string? reason) => reason == SteamNotFound ? SteamNotFound : StartFailed;
}

/// <summary>One launch command (immutable snapshot; the queue replaces it whenever the state changes).</summary>
public sealed record LaunchCommand(
    Guid Id,
    int AppId,
    string GameName,
    LaunchState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FinishedAt,
    string? FailureReason,
    string? FailureDetail)
{
    /// <summary>Still on its way: the bridge may get it (again) and the app keeps waiting for it.</summary>
    public bool IsOpen => State is LaunchState.Pending or LaunchState.Delivered;
}

/// <summary>Answer of <see cref="BridgeCommandQueue.Enqueue"/>: the command (null = the queue is full) and whether it is new.</summary>
public readonly record struct EnqueueResult(LaunchCommand? Command, bool Created);

/// <summary>
/// The command channel from the server to the PlatinVault Bridge on the gaming PC: "start this game in Steam". Kept in memory on
/// purpose - a command is only meaningful for a few seconds (a tap on a tablet), so after a server restart nothing is lost that
/// anyone still waits for.
/// <para>
/// Delivery is at-least-once, execution exactly-once: the bridge polls every few seconds and gets every OPEN command each time
/// until it reported a result; the bridge remembers the ids it has run and never runs one twice. So a lost HTTP answer costs
/// nothing, and a lost confirmation does not start a game twice. Only the fixed pattern <c>steam://run/&lt;number&gt;</c> is ever
/// built from a command (on the bridge side), and the numbers are AppIDs of the user's own games (checked by the endpoint).
/// </para>
/// <para>
/// A poll is also the proof that a bridge is there to run commands: <see cref="IsBridgeConnected"/> is true while the last poll is
/// recent. An older bridge that only pushes achievement states never polls, so it correctly counts as "cannot start games".
/// </para>
/// </summary>
public sealed class BridgeCommandQueue(ILogger<BridgeCommandQueue> logger, TimeProvider? clock = null)
{
    /// <summary>How long an unclaimed command stays valid. A tap that is not answered within this time is over - starting a game 5 minutes later would be a surprise.</summary>
    public static readonly TimeSpan CommandLifetime = TimeSpan.FromSeconds(30);

    /// <summary>How long a delivered command waits for the bridge's confirmation.</summary>
    public static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The bridge polls about every 2 s; this allows for several lost polls and a busy PC before it counts as gone.</summary>
    public static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(12);

    /// <summary>How long a finished command stays readable, so the app can still ask how it ended.</summary>
    public static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(2);

    /// <summary>Upper bound for commands on their way at the same time (there is one per game at most).</summary>
    public const int MaxOpen = 8;

    private const int MaxGameNameLength = 120;
    private const int MaxDetailLength = 200;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly List<LaunchCommand> _commands = [];
    private DateTimeOffset? _lastBridgeContact;

    /// <summary>True while a bridge that can run commands polled recently.</summary>
    public bool IsBridgeConnected
    {
        get
        {
            lock (_gate)
            {
                return _lastBridgeContact is { } last && _clock.GetUtcNow() - last < ConnectedWindow;
            }
        }
    }

    /// <summary>When the bridge last polled for commands (null since the server started if it never did).</summary>
    public DateTimeOffset? LastBridgeContactAt
    {
        get
        {
            lock (_gate)
            {
                return _lastBridgeContact;
            }
        }
    }

    /// <summary>Notes that the bridge is alive (every authenticated poll).</summary>
    public void RecordBridgeContact()
    {
        lock (_gate)
        {
            _lastBridgeContact = _clock.GetUtcNow();
        }
    }

    /// <summary>
    /// Queues a start for the game. At most one command per game is on its way: asking again while one is open returns that one
    /// (a double tap must not launch twice). A full queue returns no command.
    /// </summary>
    public EnqueueResult Enqueue(int appId, string gameName)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            Sweep(now);

            if (_commands.FirstOrDefault(c => c.IsOpen && c.AppId == appId) is { } existing)
            {
                return new EnqueueResult(existing, Created: false);
            }
            if (_commands.Count(c => c.IsOpen) >= MaxOpen)
            {
                return new EnqueueResult(null, Created: false);
            }

            var command = new LaunchCommand(Guid.NewGuid(), appId, Clean(gameName, MaxGameNameLength), LaunchState.Pending, now, null, null, null, null);
            _commands.Add(command);
            return new EnqueueResult(command, Created: true);
        }
    }

    /// <summary>
    /// The bridge asks for work: every command that is still open (new ones become <see cref="LaunchState.Delivered"/>). Counts as
    /// contact with the bridge.
    /// </summary>
    public IReadOnlyList<LaunchCommand> ClaimOpen()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            _lastBridgeContact = now;
            Sweep(now);

            for (var i = 0; i < _commands.Count; i++)
            {
                if (_commands[i].State != LaunchState.Pending)
                {
                    continue;
                }

                _commands[i] = _commands[i] with { State = LaunchState.Delivered, DeliveredAt = now };
                logger.LogInformation("Startbefehl {Id} (AppID {AppId}) von der Bridge abgeholt.", _commands[i].Id, _commands[i].AppId);
            }

            return _commands.Where(c => c.IsOpen).OrderBy(c => c.CreatedAt).ToList();
        }
    }

    /// <summary>
    /// The bridge reports how a command ended. Unknown ids are ignored (the command was forgotten meanwhile). A late report for a
    /// command that had already been given up on is still taken, because it tells the truth about what happened on the PC.
    /// </summary>
    public bool ApplyResult(Guid id, bool success, string? reason, string? detail)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            Sweep(now);

            var index = _commands.FindIndex(c => c.Id == id);
            if (index < 0)
            {
                return false;
            }

            var command = _commands[index];
            var acceptable = command.State == LaunchState.Delivered ||
                             (command.State == LaunchState.Expired && command.DeliveredAt is not null);
            if (!acceptable)
            {
                return false;
            }

            var cleanDetail = Clean(detail, MaxDetailLength);
            _commands[index] = success
                ? command with { State = LaunchState.Done, FinishedAt = now }
                : command with
                {
                    State = LaunchState.Failed,
                    FinishedAt = now,
                    FailureReason = LaunchFailureReason.Normalize(reason),
                    FailureDetail = cleanDetail.Length > 0 ? cleanDetail : null,
                };

            if (success)
            {
                logger.LogInformation("Spielstart {Id} (AppID {AppId}) von der Bridge ausgeführt.", id, command.AppId);
            }
            else
            {
                logger.LogWarning("Spielstart {Id} (AppID {AppId}) auf dem Gaming-PC fehlgeschlagen: {Reason} {Detail}",
                    id, command.AppId, LaunchFailureReason.Normalize(reason), cleanDetail);
            }
            return true;
        }
    }

    /// <summary>The command with this id, or null when it is unknown or was forgotten after <see cref="KeepFinished"/>.</summary>
    public LaunchCommand? Find(Guid id)
    {
        lock (_gate)
        {
            Sweep(_clock.GetUtcNow());
            return _commands.FirstOrDefault(c => c.Id == id);
        }
    }

    /// <summary>Seconds an open command may still wait (for a pending one: until it expires; for a delivered one: until the confirmation is due). 0 once it is over.</summary>
    public int RemainingSeconds(LaunchCommand command)
    {
        if (!command.IsOpen)
        {
            return 0;
        }

        var delivered = command.State == LaunchState.Delivered;
        var start = delivered ? command.DeliveredAt ?? command.CreatedAt : command.CreatedAt;
        var limit = delivered ? ConfirmationTimeout : CommandLifetime;
        return Math.Max(0, (int)Math.Ceiling((limit - (_clock.GetUtcNow() - start)).TotalSeconds));
    }

    // Expires what ran out of time and forgets old results. Called under the lock by every operation, so no timer is needed.
    private void Sweep(DateTimeOffset now)
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            var command = _commands[i];

            if (command.State == LaunchState.Pending && now - command.CreatedAt >= CommandLifetime)
            {
                _commands[i] = command with { State = LaunchState.Expired, FinishedAt = now };
                logger.LogWarning("Startbefehl {Id} (AppID {AppId}) wurde von keiner Bridge abgeholt und ist abgelaufen – läuft die PlatinVault Bridge auf dem Gaming-PC?",
                    command.Id, command.AppId);
            }
            else if (command.State == LaunchState.Delivered && now - (command.DeliveredAt ?? command.CreatedAt) >= ConfirmationTimeout)
            {
                _commands[i] = command with { State = LaunchState.Expired, FinishedAt = now };
                logger.LogWarning("Startbefehl {Id} (AppID {AppId}) wurde von der Bridge abgeholt, aber nie bestätigt.", command.Id, command.AppId);
            }
            else if (!command.IsOpen && command.FinishedAt is { } finished && now - finished >= KeepFinished)
            {
                _commands.RemoveAt(i);
            }
        }
    }

    // Text that came from outside (a game title, an error text of the PC): no control characters, bounded length.
    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
