using System.Text.Json;

namespace PlatinVault.Desktop;

/// <summary>
/// Settings of the Windows program itself (not of the app - those live in the database): ports chosen once, window position,
/// notification/hotkey switches. Stored as <c>desktop.json</c> in the data folder, written atomically.
/// </summary>
internal sealed class DesktopSettings
{
    /// <summary>Loopback port of the server. Kept stable: the window's stored preferences (localStorage) belong to this address.</summary>
    public int? ServerPort { get; set; }

    /// <summary>
    /// Address the server listens on. Prepared for the later phone/tablet access in the home network (off by default, opt-in, paired
    /// devices only); in this version only loopback is used - see <see cref="Server.ServerBinding"/>.
    /// </summary>
    public string ServerBindAddress { get; set; } = "127.0.0.1";

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// The former tray switch of the overlay. The overlay now follows the app setting "Trophäen-Popup anzeigen" (live.popup_target,
    /// one value for the app, the browser and the tray); a stored "false" is moved over once (-> "app") and then removed.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("OverlayEnabled")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyOverlayEnabled { get; set; }

    public bool NotificationsEnabled { get; set; } = true;

    public bool HotkeyEnabled { get; set; } = true;

    /// <summary>The one-time "PlatinVault keeps running next to the clock" notice was shown.</summary>
    public bool TrayHintShown { get; set; }

    public DateTimeOffset? LastUpdateCheck { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static DesktopSettings Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(path), Json) ?? new DesktopSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DesktopLog.Warn($"Einstellungen ({path}) nicht lesbar, verwende Standardwerte: {ex.Message}");
        }
        return new DesktopSettings();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DesktopLog.Warn($"Einstellungen konnten nicht gespeichert werden: {ex.Message}");
        }
    }
}
