using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PlatinVault.Desktop.Database;
using PlatinVault.Desktop.Server;
using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop.Ui;

/// <summary>
/// The program window: PlatinVault's own title bar (<see cref="ChromeWindow"/>: trophy, name, minimise / maximise-restore / close, still
/// with Aero Snap, snap layouts, resizing and the system menu) above the same React UI as in the browser, shown by WebView2. Until the
/// server is up it shows its own start screen (night-blue, logo, progress; the title bar continues that surface); a start error is shown
/// there completely with "open logs" / "quit". The first start opens maximised, later starts restore what the user left (see
/// <see cref="WindowPlacement"/>); closing only hides it (PlatinVault keeps running next to the clock for live trophies), "Quit" in the
/// tray ends it. The bar follows the light/dark choice of the app itself (the page reports it). Browser rules that make no sense in a
/// program are lifted: sound plays without a click first (autoplay policy), steam:// starts the game without the "open Steam?" question,
/// links to other sites open in the default browser instead of inside the window.
/// </summary>
internal sealed partial class MainWindow : ChromeWindow
{
    private readonly DesktopEnvironment _env;
    private readonly DesktopSettings _settings;
    private readonly StartScreen _start;
    private WebView2? _web;
    private Uri? _base;
    private readonly SteamLoginFlow _steamLogin = new();
    private readonly bool _persistPlacement;
    private bool _maximizedBeforeMinimize;

    /// <summary>Test aid: is a Steam sign-in under way right now?</summary>
    public bool SteamLoginActive => _steamLogin.IsActive(DateTime.UtcNow);
    private Uri? _server; // development run: the API server behind the UI address (Vite), also "own" for navigation
    private bool _allowClose;

    public MainWindow(DesktopEnvironment env, DesktopSettings settings)
    {
        _env = env;
        _settings = settings;
        // A test or off-screen run never writes its fake bounds into desktop.json (an -Offscreen run once stored -32000 / 3600 x 2250)
        _persistPlacement = !env.Quiet;
        Text = env.IsDev ? DesktopTexts.Dev.WindowTitle : "PlatinVault";
        Icon = AppIcons.GetIcon(32);
        TitleBar.Title = "PlatinVault";
        TitleBar.Badge = env.IsDev ? DesktopTexts.Dev.Badge : null;
        TitleBar.BrandMode = true;
        StartPosition = FormStartPosition.Manual;

        _start = new StartScreen { Name = "startScreen" };
        AddContent(_start);
        _start.QuitRequested += () => QuitRequested?.Invoke();
        _start.OpenLogsRequested += () => OpenFolder(env.Data.Logs);

        // Placement in the pixels of the target monitor: its scale is known now, DeviceDpi only once the handle exists
        var screens = CurrentScreens();
        var plan = WindowPlacement.Plan(new StoredWindow(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight, settings.WindowMaximized), screens);
        ApplyScale(screens[plan.Screen].Scale);
        Bounds = plan.Normal;
        var maximized = plan.Maximized && !env.Quiet;
        if (maximized) WindowState = FormWindowState.Maximized;
        _maximizedBeforeMinimize = maximized;
    }

    /// <summary>Tray "Quit" and the start screen's "Quit".</summary>
    public event Action? QuitRequested;

    /// <summary>The window was hidden by its close button for the first time (the tray shows a one-time hint).</summary>
    public event Action? HiddenToTray;

    public bool WebReady => _web?.CoreWebView2 is not null;

    public CoreWebView2? Core => _web?.CoreWebView2;

    /// <summary>The WebView2 environment of the window (the in-game overlay shares it: same browser process, same profile).</summary>
    public CoreWebView2Environment? Environment { get; private set; }

    /// <summary>The address the window's UI is loaded from (built UI of the server or, in development, the Vite dev server).</summary>
    public Uri? UiBase => _base;

    /// <summary>The page asked for the program's state (it loaded or reloaded); the answer goes back with <see cref="PostJson"/>.</summary>
    public event Action? HostHello;

    /// <summary>Sends a JSON message to the page (ignored while the page is not there).</summary>
    public void PostJson(string json)
    {
        try { _web?.CoreWebView2?.PostWebMessageAsJson(json); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { DesktopLog.Warn($"Nachricht an die Seite nicht zustellbar: {ex.Message}"); }
    }

    /// <summary>The start screen (tests: layout below the title bar).</summary>
    internal StartScreen StartSurface => _start;

    /// <summary>The WebView2 control once created (tests: it lies below the title bar, never over it).</summary>
    internal Control? WebSurface => _web;

    private int Px(int value) => (int)Math.Round(value * S);

    protected override void OnScaleChanged()
    {
        MinimumSize = new Size(Px(WindowPlacement.MinimumSize.Width), Px(WindowPlacement.MinimumSize.Height));
        // ApplyScale runs once from the base before the start screen exists
        if (_start is not null) _start.S = S;
    }

    /// <summary>Every monitor with its working area and its own scale (per-monitor DPI).</summary>
    internal static List<ScreenArea> CurrentScreens() =>
        [.. Screen.AllScreens.Select(s => new ScreenArea(s.Bounds, s.WorkingArea, s.Primary, ScreenDpi.ScaleOf(s.Bounds)))];

    /// <summary>Test run: the window exists and renders (WebView2 needs a shown window) but lies far outside every screen and is not in the taskbar or Alt+Tab.</summary>
    public void ShowOffscreen()
    {
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Normal;
        // 1440 x 900 at 100 %: the same layout in the picture whatever the PC's scaling is
        Bounds = new Rectangle(-32000, -32000, Px(1440), Px(900));
        Show();
        Location = new Point(-32000, -32000);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_offscreenStyle) cp.ExStyle |= 0x00000080 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE: never in Alt+Tab, never focused
            return cp;
        }
    }

    private bool _offscreenStyle;

    public void UseOffscreenStyle()
    {
        // Before the window handle exists: changing these later would recreate the handle (and break WebView2)
        _offscreenStyle = true;
        ShowInTaskbar = false;
    }

    public void ShowProgress(StepReport report) => _start.SetProgress(report.Value, report.Text);

    public void ShowError(Msg message, string? detail) => _start.SetError(message, detail, _env.Data.Logs);

    /// <summary>Creates WebView2 (own profile folder in the data folder) and opens the UI of the in-process server.</summary>
    public async Task OpenAppAsync(Uri baseUri, Uri? serverUri = null, bool offscreen = false)
    {
        _base = baseUri;
        _server = serverUri;
        var options = new CoreWebView2EnvironmentOptions
        {
            // A program plays its trophy sound without a click on the page first (Chromium's autoplay policy)
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
            Language = UiLang.Current == UiLang.English ? "en-GB" : "de-DE",
        };
        var environment = await CoreWebView2Environment.CreateAsync(null, _env.Data.WebView, options);
        Environment = environment;
        _web = new WebView2 { DefaultBackgroundColor = ThemeMode.Current.Page, Visible = false, Name = "web" };
        AddContent(_web);
        await _web.EnsureCoreWebView2Async(environment);

        var core = _web.CoreWebView2;
        var settings = core.Settings;
        var development = string.Equals(_env.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);
        settings.AreDevToolsEnabled = development;
        settings.AreDefaultContextMenusEnabled = development;
        settings.IsStatusBarEnabled = false;
        // The app has its own display size control; Ctrl+wheel would scale everything twice
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        // A test or off-screen run is never heard (see DesktopEnvironment.Silent)
        if (_env.Silent) core.IsMuted = true;

        // The page tells the window its light/dark choice, so the title bar (and our own dialogs) match the app, not only Windows
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ThemeReporterScript);
        core.WebMessageReceived += (_, e) =>
        {
            if (!IsOwn(e.Source)) return;
            string json;
            try { json = e.WebMessageAsJson; }
            catch (ArgumentException) { return; }
            if (OverlayMessages.IsHostHello(json))
            {
                HostHello?.Invoke();
                return;
            }
            if (ThemeFromMessage(json) is { } dark && dark != ThemeMode.Current.IsDark) ThemeMode.Set(dark);
        };

        core.NewWindowRequested += (_, e) =>
        {
            // target=_blank (Steam store, Twitch, help links): the default browser, never a second app window
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            // The app itself stays in the window; so does Steam's sign-in while it runs (it needs the window's cookie, see SteamLoginFlow)
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target))
            {
                e.Cancel = true;
                return;
            }
            if (_steamLogin.Decide(target, IsOwn(e.Uri), DateTime.UtcNow) == NavigationDecision.Allow) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        core.LaunchingExternalUriScheme += (_, e) =>
        {
            // steam://run/<number>: started directly (the "open Steam?" question of the browser is the thing a program avoids)
            if (SteamRun().IsMatch(e.Uri))
            {
                e.Cancel = true;
                try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true })?.Dispose(); }
                catch (System.ComponentModel.Win32Exception ex) { DesktopLog.Warn($"steam:// konnte nicht geöffnet werden: {ex.Message}"); }
            }
        };
        core.ProcessFailed += (_, e) => DesktopLog.Warn($"WebView2-Prozess {e.ProcessFailedKind} ({e.Reason}) - Fenster lädt neu.");

        var loaded = new TaskCompletionSource();
        core.NavigationCompleted += (_, _) => loaded.TrySetResult();
        core.Navigate(baseUri.ToString());
        await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(30)));

        _web.Visible = true;
        _web.BringToFront();
        _start.Visible = false;
        TitleBar.BrandMode = false;
        DesktopLog.Info($"Oberfläche geladen ({baseUri}, WebView2 {environment.BrowserVersionString}){(offscreen ? " - unsichtbar (Testlauf)" : string.Empty)}.");
    }

    /// <summary>
    /// Runs in every page before its own scripts: reports <c>&lt;html data-theme&gt;</c> ("light"/"dark", set by public/theme-init.js
    /// and App.tsx) once the document is there and whenever it changes. Only the top frame reports; the window accepts it only from the
    /// app's own origin.
    /// </summary>
    internal const string ThemeReporterScript = """
        (() => {
          if (window.top !== window || !window.chrome || !window.chrome.webview) return;
          let last = null;
          const report = () => {
            const theme = document.documentElement ? document.documentElement.dataset.theme : null;
            if ((theme === 'light' || theme === 'dark') && theme !== last) {
              last = theme;
              window.chrome.webview.postMessage({ pvTheme: theme });
            }
          };
          const watch = () => {
            report();
            new MutationObserver(report).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
          };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', watch, { once: true });
          else watch();
        })();
        """;

    /// <summary>"dark" -> true, "light" -> false, anything else (or no JSON object of that shape) -> null.</summary>
    internal static bool? ThemeFromMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("pvTheme", out var theme) || theme.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return theme.GetString() switch
            {
                "dark" => true,
                "light" => false,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^steam://run/\d{1,10}/?$", RegexOptions.IgnoreCase)]
    private static partial Regex SteamRun();

    private bool IsOwn(string uri) =>
        _base is not null && Uri.TryCreate(uri, UriKind.Absolute, out var target) &&
        (SameOrigin(target, _base) || (_server is not null && SameOrigin(target, _server)));

    private static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port;

    private static void OpenExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { DesktopLog.Warn($"Link konnte nicht geöffnet werden: {ex.Message}"); }
    }

    public static void OpenFolder(string folder)
    {
        FolderOpener.TryOpen(folder);
    }

    /// <summary>Opens a path of the app (e.g. "/games/elden-ring") unless the window already shows it, then brings the window up.</summary>
    public void ShowPath(string? path, bool onTop)
    {
        if (path is not null && _web?.CoreWebView2 is { } core && _base is not null)
        {
            var current = Uri.TryCreate(core.Source, UriKind.Absolute, out var source) ? source.AbsolutePath : string.Empty;
            if (!current.StartsWith(path, StringComparison.OrdinalIgnoreCase)) core.Navigate(new Uri(_base, path).ToString());
        }
        Bring(onTop);
    }

    /// <summary>Shows the window in front (over a borderless game when <paramref name="onTop"/>; topmost ends when it loses the focus).</summary>
    public void Bring(bool onTop = false)
    {
        if (!Visible) Show();
        // SW_RESTORE brings a minimised window back to what it was before: maximised stays maximised
        if (WindowState == FormWindowState.Minimized) ShowWindow(Handle, SwRestore);
        if (onTop) TopMost = true;
        Activate();
        SetForegroundWindow(Handle);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized) _maximizedBeforeMinimize = WindowState == FormWindowState.Maximized;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (TopMost) TopMost = false;
        base.OnDeactivate(e);
    }

    /// <summary>True when the user looks at this window right now (then the in-page popup is enough, no overlay).</summary>
    public bool IsForeground => Visible && WindowState != FormWindowState.Minimized && GetForegroundWindow() == Handle;

    public void AllowClose() => _allowClose = true;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveBounds();
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            // The close button hides: live trophies keep working; the tray quits
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    private void SaveBounds()
    {
        var maximized = WindowState switch
        {
            FormWindowState.Maximized => true,
            FormWindowState.Minimized => _maximizedBeforeMinimize,
            _ => false,
        };
        var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (WindowPlacement.ToStore(normal, maximized, CurrentScreens(), _persistPlacement) is not { } stored) return;
        _settings.WindowX = stored.X;
        _settings.WindowY = stored.Y;
        _settings.WindowWidth = stored.Width;
        _settings.WindowHeight = stored.Height;
        _settings.WindowMaximized = stored.Maximized;
        _settings.Save(_env.Data.SettingsFile);
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
