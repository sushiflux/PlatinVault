using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatinVault.Bridge;

/// <summary>
/// What the running service writes for the <c>status</c> command (state folder, <c>status.json</c>). Plain data, no token. The command
/// is a separate process, so this file is how it learns what the service is doing.
/// </summary>
internal sealed class LinuxStatusFile
{
    public int Pid { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string TokenHint { get; set; } = string.Empty;
    public string Connection { get; set; } = "starting";
    public string? Detail { get; set; }
    public string? SteamId64 { get; set; }
    public uint AppId { get; set; }
    public string? GameName { get; set; }
    public int Achieved { get; set; }
    public int Total { get; set; }
    public string? LastUnlockName { get; set; }
    public string? LastUnlockAt { get; set; }

    /// <summary>ISO-8601 UTC time of the last write; the command treats an old value as "not running".</summary>
    public string UpdatedAt { get; set; } = string.Empty;

    public static string ConnectionName(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Online => "online",
        ConnectionKind.Offline => "offline",
        ConnectionKind.Unauthorized => "unauthorized",
        ConnectionKind.Rejected => "rejected",
        _ => "starting",
    };

    public static LinuxStatusFile From(StatusSnapshot s, int pid, string tokenHint, DateTime nowUtc) => new()
    {
        Pid = pid,
        Version = s.Version,
        Server = s.ServerText,
        TokenHint = tokenHint,
        Connection = ConnectionName(s.Connection),
        Detail = s.Detail,
        SteamId64 = s.SteamId64,
        AppId = s.AppId,
        GameName = s.GameName,
        Achieved = s.Achieved,
        Total = s.Total,
        LastUnlockName = s.LastUnlockName,
        LastUnlockAt = s.LastUnlockAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        UpdatedAt = nowUtc.ToString("O", CultureInfo.InvariantCulture),
    };

    /// <summary>Reads the file; null when there is none or it is unreadable.</summary>
    public static LinuxStatusFile? Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), LinuxJsonContext.Default.LinuxStatusFile)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, LinuxJsonContext.Default.LinuxStatusFile));
        File.Move(temp, path, overwrite: true);
    }

    public DateTime? UpdatedAtUtc =>
        DateTime.TryParse(UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : null;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LinuxStatusFile))]
internal sealed partial class LinuxJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Keeps <c>status.json</c> up to date while the service runs: a write soon after every change of the <see cref="BridgeStatus"/> (at most
/// four per second) and one every 30 seconds so the file shows the service is alive.
/// </summary>
internal sealed class LinuxStatusWriter(BridgeStatus status, string path, string tokenHint, int pid)
{
    private static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _changed = new(0, 1);

    public async Task RunAsync(CancellationToken ct)
    {
        status.Changed += OnChanged;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Write();
                try
                {
                    await _changed.WaitAsync(Heartbeat, ct).ConfigureAwait(false);
                    await Task.Delay(MinGap, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            status.Changed -= OnChanged;
        }
    }

    private void OnChanged(StatusSnapshot _)
    {
        try { _changed.Release(); }
        catch (SemaphoreFullException) { /* a write is already due */ }
    }

    private void Write()
    {
        try
        {
            LinuxStatusFile.From(status.Current, pid, tokenHint, DateTime.UtcNow).Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The state folder is not writable: the service itself must not suffer from that
            BridgeLog.Debug($"status.json: {ex.Message}");
        }
    }

    /// <summary>Removes the file at a clean stop, so the command does not show a stale "running" for the next two minutes.</summary>
    public void DeleteFile()
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* nothing to do */ }
    }
}
