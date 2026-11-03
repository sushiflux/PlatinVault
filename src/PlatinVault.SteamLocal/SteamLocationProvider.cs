using Microsoft.Extensions.Logging;

namespace PlatinVault.SteamLocal;

/// <summary>
/// The Steam folder of this computer for a long-running host (server, bridge): <see cref="SteamLocator.Locate"/> with a short
/// cache, because the live detection asks several times per second. A change of the folder (Steam installed, the setting changed)
/// is picked up after at most <see cref="DefaultRefreshInterval"/>, a changed <see cref="ManualRoot"/> at once. Thread-safe.
/// Logs one line when the folder in use changes (found / lost), never per lookup.
/// </summary>
public sealed class SteamLocationProvider
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly SteamLocatorInput _input;
    private readonly ILogger? _logger;
    private readonly long _refreshMs;
    private readonly Func<long> _ticks;
    private readonly Lock _gate = new();
    private string? _manualRoot;
    private SteamLocation? _current;
    private long _locatedAt;
    private bool _logged;
    private string? _loggedRoot;
    private readonly bool _logInitialResult;

    /// <param name="input">Configuration, environment, file system; null = the real ones of this process.</param>
    /// <param name="logger">Receives the found / lost lines (German, like the other operator logs); optional.</param>
    /// <param name="refreshInterval">How long a result is reused (default <see cref="DefaultRefreshInterval"/>).</param>
    /// <param name="tickCount">Milliseconds clock for tests (default <see cref="Environment.TickCount64"/>).</param>
    /// <param name="logInitialResult">
    /// False for a host that reports the folder it starts with itself (the Linux bridge in <c>run</c>): the first result is not logged, only
    /// later changes (Steam installed or removed while it runs), so no line appears twice.
    /// </param>
    public SteamLocationProvider(SteamLocatorInput? input = null, ILogger? logger = null, TimeSpan? refreshInterval = null, Func<long>? tickCount = null,
        bool logInitialResult = true)
    {
        _logInitialResult = logInitialResult;
        _input = input ?? new SteamLocatorInput();
        _manualRoot = _input.ManualRoot;
        _logger = logger;
        _refreshMs = (long)(refreshInterval ?? DefaultRefreshInterval).TotalMilliseconds;
        _ticks = tickCount ?? (() => Environment.TickCount64);
    }

    public SteamPlatform Platform => _input.Platform;

    /// <summary>The folder from the host's configuration (null when none).</summary>
    public string? ConfiguredRoot => _input.ConfiguredRoot;

    /// <summary>The folder set by hand (the setting "Steam-Ordner"); setting it takes effect at the next lookup. Null/blank = none.</summary>
    public string? ManualRoot
    {
        get
        {
            lock (_gate) return _manualRoot;
        }
        set
        {
            lock (_gate)
            {
                _manualRoot = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                _current = null;
            }
        }
    }

    /// <summary>The locator input with the current <see cref="ManualRoot"/> (also used to check a new value before it is stored).</summary>
    public SteamLocatorInput Input
    {
        get
        {
            lock (_gate) return _input with { ManualRoot = _manualRoot };
        }
    }

    /// <summary>The Steam folder in use (cached for the refresh interval).</summary>
    public SteamLocation Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is not null && _ticks() - _locatedAt < _refreshMs) return _current;
            }
            return Refresh();
        }
    }

    /// <summary>Looks again now.</summary>
    public SteamLocation Refresh()
    {
        SteamLocatorInput input;
        lock (_gate) input = _input with { ManualRoot = _manualRoot };

        var location = SteamLocator.Locate(input);
        lock (_gate)
        {
            _current = location;
            _locatedAt = _ticks();
            LogChange(location);
        }
        return location;
    }

    /// <summary>Checks a folder given by hand against the rules of this platform (see <see cref="SteamLocator.CheckRoot"/>).</summary>
    public SteamRootCheck Check(string path) => SteamLocator.CheckRoot(path, Input);

    private void LogChange(SteamLocation location)
    {
        if (_logger is null) return;
        if (_logged && string.Equals(_loggedRoot, location.Root, StringComparison.Ordinal)) return;
        var first = !_logged;
        _logged = true;
        _loggedRoot = location.Root;
        if (first && !_logInitialResult) return;

        if (location.Root is null)
        {
            if (first)
            {
                // Normal on a server, NAS or Unraid: no line above debug level, so containers stay quiet
                _logger.LogDebug("Kein lokales Steam auf diesem Rechner gefunden ({Count} Orte geprüft).", location.Candidates.Count);
            }
            else
            {
                _logger.LogInformation("Lokales Steam nicht mehr gefunden - die Live-Erkennung aus den lokalen Steam-Dateien ist aus.");
            }
            return;
        }

        _logger.LogInformation("Lokales Steam gefunden: {Root} (gefunden über: {Method}).", location.Root, location.Method);
        if (location.IsExperimental)
        {
            _logger.LogWarning(
                "Steam unter Linux: Erkennung des laufenden Spiels über die Prozessliste (reaper SteamLaunch AppId=...), Erfolge aus " +
                "appcache/stats/UserGameStats_*.bin - EXPERIMENTELL, auf einem echten Linux-Steam noch nicht geprüft.");
        }
    }
}
