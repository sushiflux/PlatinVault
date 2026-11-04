using System.Runtime.InteropServices;
using System.Text.Json;
using Companion.Api.Endpoints;
using Companion.Api.Settings;
using Microsoft.Web.WebView2.Core;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop.Ui;

/// <summary>
/// "Trophäen-Popup anzeigen" (setting <c>live.popup_target</c>, one value for the app, the browser and the tray switch): where an unlock
/// appears in the Windows program. Pure, unit-tested.
/// </summary>
internal static class OverlayRouting
{
    public const string App = "app";
    public const string Overlay = "overlay";
    public const string Both = "both";

    public static string Normalize(string? target) => SettingsStore.NormalizePopupTarget(target) ?? SettingsStore.DefaultPopupTarget;

    /// <summary>
    /// Whether the overlay shows an unlock: "overlay" always (also over the program's own window), "both" only while the program's
    /// window is not the foreground window (otherwise the user already sees the in-app popup - never twice on one screen), "app" never.
    /// The app window decides for itself with the same setting (utils/popupTarget.ts) and plays the sound in every case.
    /// </summary>
    public static bool ShowsOverlay(string? target, bool mainWindowForeground) => Normalize(target) switch
    {
        Overlay => true,
        Both => !mainWindowForeground,
        _ => false,
    };

    /// <summary>The tray switch "Trophäen-Overlay über dem Spiel" is on for "overlay" and "both".</summary>
    public static bool TrayChecked(string? target) => Normalize(target) != App;

    /// <summary>Clicking the tray switch: on -> "app"; off -> "both" (the default).</summary>
    public static string TrayToggled(string? target) => TrayChecked(target) ? App : Both;

    /// <summary>
    /// The former switch "OverlayEnabled" of desktop.json: switched off and no popup target stored yet -> "app" (the user keeps what
    /// they chose); anything else -> null (nothing to migrate).
    /// </summary>
    public static string? MigrateLegacySwitch(bool? legacyOverlayEnabled, bool targetStored) =>
        legacyOverlayEnabled == false && !targetStored ? App : null;
}

/// <summary>The JSON messages between the program and its pages (overlay page and main window). Pure, unit-tested.</summary>
internal static class OverlayMessages
{
    // The same JSON shape as SignalR sends to the app (camelCase): the overlay page hands it to the very same components
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <param name="seconds">How long the popup stays (setting live.popup_seconds); the page clamps it to 2..10 and uses 5 when it is missing.</param>
    public static string Achievement(LiveAchievementItemDto achievement, string? gameName, string position, int seconds, string language) =>
        JsonSerializer.Serialize(new { pvOverlay = new { kind = "achievement", achievement, gameName, position, seconds, language } }, Web);

    public static string Platinum(string gameName, int total, string language) =>
        JsonSerializer.Serialize(new { pvOverlay = new { kind = "platinum", gameName, total, language } }, Web);

    /// <summary>To the main window's page: whether the overlay works (with "only in the game" the page then leaves the popup to it).</summary>
    public static string HostState(bool overlay) => JsonSerializer.Serialize(new { pvHost = new { overlay } }, Web);

    public const string SettingsChanged = """{"pvSettingsChanged":true}""";

    /// <summary>
    /// The overlay page's state ("ready" / "busy" / "idle") and how many events it had received when it said so (an "idle" sent while
    /// a new event was on its way must not hide that event), or null for any other message.
    /// </summary>
    public static (string State, long Received)? ReadOverlayState(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pvOverlayState", out var state) ||
                state.ValueKind != JsonValueKind.String || state.GetString() is not ("ready" or "busy" or "idle"))
            {
                return null;
            }
            var received = root.TryGetProperty("received", out var count) && count.TryGetInt64(out var n) ? n : 0;
            return (state.GetString()!, received);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether an "idle" of the page may hide the window: only when the page had received every event presented so far.</summary>
    public static bool IdleCoversAll(long received, long presented) => received >= presented;

    /// <summary>The main window's page asks for the program's state once it has loaded.</summary>
    public static bool IsHostHello(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("pvHostHello", out var hello) && hello.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Where the overlay window lies: the whole monitor of the game (the foreground window) - a borderless game covers the taskbar too.</summary>
internal static class OverlayPlacement
{
    /// <summary>A test or off-screen development run: a 1280 x 720 "monitor" far outside every screen, at 100 %.</summary>
    public static readonly Rectangle Offscreen = new(-32000, -32000, 1280, 720);

    public static (Rectangle Bounds, float Scale) ForForegroundWindow()
    {
        var bounds = Screen.FromHandle(GetForegroundWindow()).Bounds;
        return (bounds, ScreenDpi.ScaleOf(bounds));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

/// <summary>
/// The trophy popup over the game, drawn by the SAME web components as in the app (route <c>/overlay</c> of the same frontend build): a
/// transparent, click-through, always-on-top window that never takes the focus and never appears in Alt+Tab or the taskbar
/// (WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE).
/// WebView2 is hosted with VISUAL hosting (CoreWebView2CompositionController): the page renders into a DirectComposition visual of
/// this window, with per-pixel alpha (DefaultBackgroundColor transparent, the page itself transparent); a window without redirection
/// bitmap shows only that visual. The layered + transparent styles make every click go to the game (layered-window hit testing) - no
/// input is ever forwarded to the page. Windowed hosting cannot do this (its own child window, opaque). The window covers the monitor of
/// the game and is shown only while the page reports "busy"; between popups it is hidden and the page is invisible (IsVisible false:
/// no rendering, no timers of note). Works over games in windowed and borderless mode; a game in EXCLUSIVE fullscreen owns the screen and
/// covers every other window, this one included.
/// </summary>
internal sealed class WebOverlayWindow : Form
{
    private readonly bool _offscreen;
    private CoreWebView2CompositionController? _composition;
    private DirectComposition.IDCompositionDevice? _device;
    private DirectComposition.IDCompositionTarget? _target;
    private DirectComposition.IDCompositionVisual? _visual;
    private Uri? _page;
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 60_000 };
    private bool _shown;
    private long _presented;
    private TaskCompletionSource? _ready;

    public WebOverlayWindow(bool offscreen)
    {
        _offscreen = offscreen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = OverlayPlacement.Offscreen;
        Text = "PlatinVault Overlay";
        // Safety net: a page that never reports "idle" (crashed renderer) must not leave an invisible window over the game for ever
        _watchdog.Tick += (_, _) => HideOverlay();
    }

    /// <summary>True once the page has loaded and said "ready"; false before, after a failure and after a renderer crash until reloaded.</summary>
    public bool Available { get; private set; }

    /// <summary>Raised when <see cref="Available"/> changes (the main window's page needs to know, see OverlayMessages.HostState).</summary>
    public event Action<bool>? AvailabilityChanged;

    /// <summary>Raised after the window was put on screen for a popup (a test run captures a picture then).</summary>
    public event Action? Presented;

    public CoreWebView2? Core => _composition?.CoreWebView2;

    /// <summary>Milliseconds the start took (logged; the warm hidden instance is created once at program start).</summary>
    public long CreationMilliseconds { get; private set; }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NoRedirectionBitmap | Layered | Transparent | Topmost | ToolWindow | NoActivate;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // A layered window is invisible until its attributes are set; full opacity - the per-pixel alpha comes from the composition visual
        SetLayeredWindowAttributes(Handle, 0, 255, LwaAlpha);
    }

    // No GDI painting: the window's content is the composition visual only
    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    /// <summary>Every popup places the window itself (monitor bounds in pixels): Windows must not resize it on a DPI change.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;
        base.OnDpiChanged(e);
    }

    /// <summary>
    /// Creates the hidden overlay in the program's WebView2 environment (same browser process, same profile as the main window) and
    /// loads the overlay page. Throws when visual hosting is not possible; the program then leaves every popup to the app window.
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment environment, Uri page, bool devTools, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        _page = page;
        _ = Handle;

        Marshal.ThrowExceptionForHR(DirectComposition.DCompositionCreateDevice2(IntPtr.Zero, typeof(DirectComposition.IDCompositionDevice).GUID, out var devicePtr));
        try
        {
            _device = (DirectComposition.IDCompositionDevice)Marshal.GetObjectForIUnknown(devicePtr);
        }
        finally
        {
            Marshal.Release(devicePtr);
        }
        _device.CreateTargetForHwnd(Handle, true, out _target);
        _device.CreateVisual(out _visual);
        _target.SetRoot(_visual);

        _composition = await environment.CreateCoreWebView2CompositionControllerAsync(Handle);
        var controller = (CoreWebView2Controller)_composition;
        controller.DefaultBackgroundColor = Color.Transparent;
        controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
        // The scale is set for the monitor of every popup (per-monitor DPI), not followed automatically
        controller.ShouldDetectMonitorScaleChanges = false;
        controller.RasterizationScale = 1.0;
        controller.Bounds = new Rectangle(Point.Empty, OverlayPlacement.Offscreen.Size);
        _composition.RootVisualTarget = _visual;
        _device.Commit();

        var core = _composition.CoreWebView2;
        // The overlay page is silent by design (the app window plays the sound once per unlock); muted on top, so it can never be heard
        core.IsMuted = true;
        var settings = core.Settings;
        settings.AreDevToolsEnabled = devTools;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        // The overlay page never goes anywhere else and never opens windows
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (!IsOwn(e.Uri)) e.Cancel = true;
        };
        core.WebMessageReceived += (_, e) =>
        {
            if (!IsOwn(e.Source)) return;
            string json;
            try { json = e.WebMessageAsJson; }
            catch (ArgumentException) { return; }
            if (OverlayMessages.ReadOverlayState(json) is not { } message) return;
            if (message.State == "ready")
            {
                // Also after a reload (renderer crash): the page starts counting from zero again
                _presented = 0;
                _ready?.TrySetResult();
                SetAvailable(true);
            }
            else if (message.State == "idle" && OverlayMessages.IdleCoversAll(message.Received, _presented))
            {
                HideOverlay();
            }
        };
        core.ProcessFailed += (_, e) =>
        {
            DesktopLog.Warn($"Overlay: WebView2-Prozess {e.ProcessFailedKind} ({e.Reason}) - Overlay lädt neu, Popups bis dahin nur in der App.");
            HideOverlay();
            SetAvailable(false);
            try { core.Reload(); }
            catch (Exception ex) when (ex is InvalidOperationException or COMException) { DesktopLog.Warn($"Overlay nicht neu ladbar: {ex.Message}"); }
        };

        controller.IsVisible = true; // the page must run once to load and say "ready"; the window itself stays hidden
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        core.Navigate(page.ToString());
        var finished = await Task.WhenAny(_ready.Task, Task.Delay(TimeSpan.FromSeconds(20), ct));
        controller.IsVisible = false;
        CreationMilliseconds = started.ElapsedMilliseconds;
        if (finished != _ready.Task)
        {
            throw new TimeoutException($"Overlay-Seite {page} hat sich nicht innerhalb von 20 s gemeldet.");
        }
    }

    /// <summary>
    /// Puts the window over the monitor of the game (test runs: off-screen), without activating it, and hands the event to the page.
    /// The page shows it with the app's own components and reports "idle" when its queue is done - then the window hides again.
    /// </summary>
    public void Present(string messageJson)
    {
        if (_composition is null || !Available) return;
        var (bounds, scale) = _offscreen ? (OverlayPlacement.Offscreen, 1f) : OverlayPlacement.ForForegroundWindow();
        var controller = (CoreWebView2Controller)_composition;
        controller.RasterizationScale = scale;
        controller.Bounds = new Rectangle(Point.Empty, bounds.Size);
        SetWindowPos(Handle, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
        controller.IsVisible = true;
        _shown = true;
        _watchdog.Stop();
        _watchdog.Start();
        _presented++;
        _composition.CoreWebView2.PostWebMessageAsJson(messageJson);
        Presented?.Invoke();
    }

    /// <summary>Hides the window and stops the page from rendering (nothing to show).</summary>
    public void HideOverlay()
    {
        _watchdog.Stop();
        if (!_shown) return;
        _shown = false;
        if (_composition is not null) ((CoreWebView2Controller)_composition).IsVisible = false;
        if (IsHandleCreated) SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoActivate | SwpHideWindow | SwpNoMove | SwpNoSize | SwpNoZOrder);
    }

    /// <summary>True while a popup is on screen (tests).</summary>
    public bool IsPresenting => _shown;

    private void SetAvailable(bool available)
    {
        if (Available == available) return;
        Available = available;
        AvailabilityChanged?.Invoke(available);
    }

    private bool IsOwn(string uri) =>
        _page is not null && Uri.TryCreate(uri, UriKind.Absolute, out var target) &&
        target.Scheme == _page.Scheme && target.Host == _page.Host && target.Port == _page.Port;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watchdog.Dispose();
            if (_composition is not null)
            {
                try { _composition.RootVisualTarget = null; }
                catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
                ((CoreWebView2Controller)_composition).Close();
                _composition = null;
            }
            ReleaseCom(ref _visual);
            ReleaseCom(ref _target);
            ReleaseCom(ref _device);
        }
        base.Dispose(disposing);
    }

    private static void ReleaseCom<T>(ref T? value) where T : class
    {
        if (value is not null) Marshal.FinalReleaseComObject(value);
        value = null;
    }

    private const int NoRedirectionBitmap = 0x00200000;
    private const int Layered = 0x00080000;
    private const int Transparent = 0x00000020;
    private const int Topmost = 0x00000008;
    private const int ToolWindow = 0x00000080;
    private const int NoActivate = 0x08000000;
    private const uint LwaAlpha = 0x2;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

/// <summary>
/// The few DirectComposition calls visual hosting needs (dcomp.h): a device without a rendering device, a target for the window and one
/// visual that WebView2 attaches its own tree to (the same steps as Microsoft's WebView2APISample, BuildDCompTreeUsingVisual).
/// Only methods up to the last one used are declared; their order is the vtable order of dcomp.h.
/// </summary>
internal static class DirectComposition
{
    [DllImport("dcomp.dll")]
    public static extern int DCompositionCreateDevice2(IntPtr renderingDevice, in Guid iid, out IntPtr device);

    [ComImport, Guid("C37EA93A-E7AA-450D-B16F-9746CB0407F3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDCompositionDevice
    {
        void Commit();
        void WaitForCommitCompletion();
        void GetFrameStatistics(IntPtr statistics);
        void CreateTargetForHwnd(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IDCompositionTarget target);
        void CreateVisual(out IDCompositionVisual visual);
    }

    [ComImport, Guid("eacdd04c-117e-4e17-88f4-d1b12b0e3d89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDCompositionTarget
    {
        void SetRoot(IDCompositionVisual? visual);
    }

    [ComImport, Guid("4d93059d-097b-4651-9a60-f0f25116e2f3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDCompositionVisual
    {
    }
}
