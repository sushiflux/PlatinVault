namespace PlatinVault.Bridge;

/// <summary>
/// The command channel of the bridge: every couple of seconds it asks the server whether something is to be done on this PC
/// (today: "start this game in Steam", sent when someone taps Play in PlatinVault on a tablet or another PC), runs it, and
/// reports how it ended with the next request.
/// <para>
/// Separate loop next to <see cref="BridgeRunner"/> on purpose: the achievement loop only talks to the server when something
/// changed or once a minute (it carries the full achievement list), which would make "Play" wait up to a minute.
/// </para>
/// <para>
/// Delivery is at-least-once (the server repeats an open command with every poll until it got our result), execution is
/// exactly-once: the ids that were run are remembered, a repeat is skipped, only its result is sent again. A result is kept
/// until the server stops listing the command - that is the server's acknowledgement.
/// </para>
/// <para>
/// What this can never do: build a URL from server text. A launch command carries a number; the address is
/// <c>steam://run/&lt;number&gt;</c> built by <see cref="SteamUrl"/> from a validated integer, and any other command type is
/// answered with a failure instead of being interpreted.
/// </para>
/// </summary>
internal sealed class CommandPoller(BridgeClient client, ISteamLauncher launcher, Action<string, bool> announce)
{
    /// <summary>Gap between two polls. This is the latency the user sees between tapping Play and the launch (on average half of it).</summary>
    public const int PollMs = 2_000;

    // Back-off after trouble: a flaky network is retried soon, a refused request (wrong token, old server) rarely
    private const int TransientRetryMs = 5_000;
    private const int RefusedRetryMs = 30_000;

    // Ids that were run, so a repeated command is never run twice. Bounded: old ids are long expired on the server.
    private const int MaxRememberedIds = 256;
    private const int MaxNameLength = 80;

    private readonly HashSet<string> _handled = new(StringComparer.Ordinal);
    private readonly Queue<string> _handledOrder = new();

    // Results the server has not acknowledged yet (it still lists the command), by command id
    private readonly Dictionary<string, BridgeCommandResultDto> _unacknowledged = new(StringComparer.Ordinal);

    private bool _channelDown;
    private string? _lastUnexpectedError;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int delay;
            try
            {
                delay = await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Whatever goes wrong while running a command must not end the channel. Log each distinct problem once.
                var text = $"{ex.GetType().Name}: {ex.Message}";
                if (text != _lastUnexpectedError)
                {
                    _lastUnexpectedError = text;
                    BridgeLog.Warn(BridgeTexts.Loop.UnexpectedError(text));
                    BridgeLog.Debug(ex.ToString());
                }
                delay = TransientRetryMs;
            }

            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One request/answer round. Returns how long to wait before the next one.</summary>
    internal async Task<int> PollOnceAsync(CancellationToken ct)
    {
        var result = await client.PollAsync(_unacknowledged.Values.ToList(), ct).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case SendOutcome.Ok:
                if (_channelDown)
                {
                    _channelDown = false;
                    BridgeLog.Debug(BridgeTexts.Launch.ChannelRestored);
                }
                if (result.Commands is { } commands) Process(commands);
                return PollMs;

            case SendOutcome.Transient:
                NoteChannelProblem(result.Reason);
                return TransientRetryMs;

            default:
                // Unauthorized / Rejected: the achievement loop reports these (tray icon, log); here it just waits longer
                NoteChannelProblem(result.Outcome == SendOutcome.Unauthorized ? "HTTP 401" : result.Reason);
                return RefusedRetryMs;
        }
    }

    private void NoteChannelProblem(string? reason)
    {
        if (_channelDown) return;
        _channelDown = true;
        BridgeLog.Debug(BridgeTexts.Launch.ChannelProblem(reason ?? "?"));
    }

    private void Process(IReadOnlyList<BridgeCommandDto> commands)
    {
        // The server stops listing a command once it has our result (or gave up on it): nothing left to report for those
        var open = commands.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _unacknowledged.Keys.Where(id => !open.Contains(id)).ToList())
            _unacknowledged.Remove(id);

        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Id) || _handled.Contains(command.Id)) continue;

            Remember(command.Id);
            _unacknowledged[command.Id] = Run(command);
        }
    }

    private BridgeCommandResultDto Run(BridgeCommandDto command)
    {
        var isLaunch = string.Equals(command.Type, "launch", StringComparison.OrdinalIgnoreCase);
        if (!isLaunch || command.AppId is < 1 or > int.MaxValue)
        {
            BridgeLog.Warn(BridgeTexts.Launch.IgnoredCommand(command.Id, command.Type ?? "?", command.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return new BridgeCommandResultDto(command.Id, "failed", "start-failed", "unsupported command");
        }

        var appId = (uint)command.AppId;
        var name = DisplayName(command.GameName, appId);

        BridgeLog.Info(BridgeTexts.Launch.Starting(name, appId));
        announce(BridgeTexts.Launch.Balloon(name), false);

        var launched = launcher.Launch(appId);
        switch (launched.Outcome)
        {
            case LaunchOutcome.Started:
                BridgeLog.Success(BridgeTexts.Launch.StartedLog(name, appId));
                return new BridgeCommandResultDto(command.Id, "done", null, null);

            case LaunchOutcome.SteamNotFound:
                BridgeLog.Warn(BridgeTexts.Launch.SteamMissingLog(name, appId, launched.Detail ?? "?"));
                announce(BridgeTexts.Launch.BalloonSteamMissing(name), true);
                return new BridgeCommandResultDto(command.Id, "failed", "steam-not-found", launched.Detail);

            default:
                BridgeLog.Warn(BridgeTexts.Launch.FailedLog(name, appId, launched.Detail ?? "?"));
                announce(BridgeTexts.Launch.BalloonFailed(name), true);
                return new BridgeCommandResultDto(command.Id, "failed", "start-failed", launched.Detail);
        }
    }

    private void Remember(string id)
    {
        _handled.Add(id);
        _handledOrder.Enqueue(id);
        while (_handledOrder.Count > MaxRememberedIds) _handled.Remove(_handledOrder.Dequeue());
    }

    // The title is text from the server: shown in a balloon and the log, so strip control characters and keep it short.
    private static string DisplayName(string? title, uint appId)
    {
        var clean = string.IsNullOrWhiteSpace(title) ? string.Empty : new string(title.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return $"AppID {appId}";
        return clean.Length <= MaxNameLength ? clean : clean[..(MaxNameLength - 1)] + "…";
    }
}
