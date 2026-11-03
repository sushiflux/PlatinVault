using System.Runtime.InteropServices;

namespace PlatinVault.Desktop.Ui;

/// <summary>
/// System-wide shortcut that opens the guide of the running game over the game: Ctrl+Alt+G (AltGr+G types nothing on German or
/// English keyboards, games rarely use Ctrl+Alt). When another program already owns it, Ctrl+Alt+Shift+G is tried; when that is taken
/// too, there is no hotkey and the tray says so (RegisterHotKey fails instead of stealing the keys).
/// </summary>
internal sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    private const int Id = 0x5056; // "PV"
    private bool _registered;

    public GlobalHotkey() => CreateHandle(new CreateParams { Caption = "PlatinVault hotkey" });

    public event Action? Pressed;

    /// <summary>The keys that are registered, for the user ("Strg+Alt+G"); null when none could be registered.</summary>
    public string? Keys { get; private set; }

    public static string Describe(bool shift, string language) =>
        (language == "en" ? "Ctrl+Alt+" : "Strg+Alt+") + (shift ? (language == "en" ? "Shift+" : "Umschalt+") : string.Empty) + "G";

    public bool Register(string language)
    {
        Unregister();
        foreach (var shift in new[] { false, true })
        {
            var modifiers = ModControl | ModAlt | ModNoRepeat | (shift ? ModShift : 0);
            if (RegisterHotKey(Handle, Id, modifiers, (uint)System.Windows.Forms.Keys.G))
            {
                _registered = true;
                Keys = Describe(shift, language);
                DesktopLog.Info($"Tastenkombination {Keys} registriert.");
                return true;
            }
        }
        Keys = null;
        DesktopLog.Warn("Tastenkombination Strg+Alt+G und Strg+Alt+Umschalt+G sind von anderen Programmen belegt - keine Tastenkombination.");
        return false;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(Handle, Id);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && (int)m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
