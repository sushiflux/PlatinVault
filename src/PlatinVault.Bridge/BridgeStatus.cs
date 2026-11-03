namespace PlatinVault.Bridge;

internal enum ConnectionKind
{
    /// <summary>Nothing delivered yet.</summary>
    Starting,
    Online,
    /// <summary>Server not reachable (retrying quickly).</summary>
    Offline,
    /// <summary>The token is not accepted.</summary>
    Unauthorized,
    /// <summary>The server answers but refuses the data.</summary>
    Rejected,
}

/// <summary>What the tray icon and the status window show; an immutable picture of the bridge at one moment.</summary>
internal sealed record StatusSnapshot
{
    public ConnectionKind Connection { get; init; } = ConnectionKind.Starting;

    /// <summary>Reason shown with <see cref="ConnectionKind.Offline"/> / <see cref="ConnectionKind.Rejected"/>.</summary>
    public string? Detail { get; init; }

    public string ServerText { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? SteamId64 { get; init; }

    public uint AppId { get; init; }
    public string? GameName { get; init; }
    public int Achieved { get; init; }
    public int Total { get; init; }

    public string? LastUnlockName { get; init; }
    public DateTime? LastUnlockAt { get; init; }
    public DateTime? LastDeliveredAt { get; init; }

    public bool IsConnected => Connection == ConnectionKind.Online;
}

/// <summary>
/// Shared state between the polling loop (background thread) and the tray/status UI. The loop publishes, the UI
/// subscribes to <see cref="Changed"/> and re-reads; the event can fire on any thread.
/// </summary>
internal sealed class BridgeStatus(StatusSnapshot initial)
{
    private readonly object _gate = new();
    private StatusSnapshot _current = initial;

    public StatusSnapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public event Action<StatusSnapshot>? Changed;

    public void Update(Func<StatusSnapshot, StatusSnapshot> change)
    {
        StatusSnapshot next;
        lock (_gate)
        {
            next = change(_current);
            if (next == _current) return;
            _current = next;
        }
        Changed?.Invoke(next);
    }
}
