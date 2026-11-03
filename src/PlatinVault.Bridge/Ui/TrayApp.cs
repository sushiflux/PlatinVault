using System.Diagnostics;

namespace PlatinVault.Bridge.Ui;

/// <summary>
/// The running bridge as the user sees it: a tray icon (grey while not connected) with tooltip, context menu and a status
/// window. The polling loop runs on a background thread and publishes through <see cref="BridgeStatus"/>; everything here
/// happens on the UI thread.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string BalloonTitle = BridgeEnvironment.ProductName;

    private readonly BridgeEnvironment _env;
    private readonly BridgeStatus _status;
    private readonly InstanceGuard _guard;
    private readonly string _serverUrl;
    private readonly Action _requestStop;

    private readonly Control _marshal = new();
    private readonly Icon _normalIcon;
    private readonly Icon _dimmedIcon;
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _openServerItem;
    private readonly ToolStripMenuItem _openHandbookItem;
    private readonly ToolStripMenuItem _openLogItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem _uninstallItem;
    private readonly System.Windows.Forms.Timer _oneShot = new();

    private StatusForm? _statusForm;
    private bool _dimmedShown;
    private string? _lastBalloon;
    private int _statusWindowRequests;
    private readonly System.Windows.Forms.Timer _commandTimer = new() { Interval = 400 };

    public TrayApp(BridgeEnvironment env, CliOptions cli, BridgeStatus status, InstanceGuard guard, string serverUrl, Action requestStop)
    {
        _env = env;
        _status = status;
        _guard = guard;
        _serverUrl = serverUrl;
        _requestStop = requestStop;

        // Gives the UI thread a handle to marshal calls from other threads onto.
        _ = _marshal.Handle;

        var iconSize = SystemInformation.SmallIconSize.Width;
        _normalIcon = AppIcons.GetIcon(iconSize);
        _dimmedIcon = AppIcons.GetIcon(iconSize, dimmed: true);

        // The texts are set by ApplyMenuLanguage below (and again whenever the bridge switches its language)
        _statusItem = new ToolStripMenuItem { Enabled = false, Name = "menuStatus" };
        _openServerItem = new ToolStripMenuItem { Name = "menuOpenServer" };
        _openHandbookItem = new ToolStripMenuItem { Name = "menuOpenHandbook" };
        _openLogItem = new ToolStripMenuItem { Name = "menuOpenLog" };
        _autostartItem = new ToolStripMenuItem { Name = "menuAutostart" };
        _exitItem = new ToolStripMenuItem { Name = "menuExit" };
        _uninstallItem = new ToolStripMenuItem { Name = "menuUninstall" };
        ApplyMenuLanguage();

        _menu = new ContextMenuStrip { Font = SystemFonts.MenuFont, ShowImageMargin = false };
        _menu.Items.AddRange([_statusItem, new ToolStripSeparator(), _openServerItem, _openHandbookItem, _openLogItem, _autostartItem, _exitItem,
            new ToolStripSeparator(), _uninstallItem]);

        _openServerItem.Click += (_, _) => OpenServer();
        _openHandbookItem.Click += (_, _) => OpenHandbook();
        _openLogItem.Click += (_, _) => OpenLog();
        _autostartItem.Click += (_, _) => ToggleAutostart();
        _exitItem.Click += (_, _) => _requestStop();
        _uninstallItem.Click += (_, _) => Uninstall();
        _menu.Opening += (_, _) => RefreshMenuState();

        _notify = new NotifyIcon
        {
            Icon = _dimmedIcon,
            Text = Truncate(Tooltip(status.Current), 127),
            ContextMenuStrip = _menu,
            // A test run must not put anything into the real tray.
            Visible = !env.Headless,
        };
        _dimmedShown = true;
        _notify.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowStatusWindow();
        };

        status.Changed += OnStatusChanged;
        BridgeLang.Changed += OnLanguageChanged;
        Refresh(status.Current);

        if (env.Headless)
        {
            // Test runs cannot click the real tray icon, so they drop commands into <test root>Immand.txt.
            _commandTimer.Tick += (_, _) => ProcessTestCommands();
            _commandTimer.Start();
        }

        // The balloon is only worth showing once the icon is really in the tray.
        string? balloon = cli.UpdatedTo is not null
            ? BridgeTexts.Tray.BalloonUpdated(cli.UpdatedTo)
            : cli.FirstRun ? BridgeTexts.Tray.BalloonFirstRun : null;
        if (balloon is not null)
        {
            _oneShot.Interval = 1200;
            _oneShot.Tick += (_, _) =>
            {
                _oneShot.Stop();
                if (_env.Headless)
                {
                    _lastBalloon = balloon;
                    BridgeLog.Info($"Ballon (Testlauf, nicht angezeigt): {balloon}");
                    DumpState();
                }
                else
                {
                    _notify.ShowBalloonTip(6000, BalloonTitle, balloon, ToolTipIcon.Info);
                }
            };
            _oneShot.Start();
        }

        if (cli.TestSnapshotMenu is { } snapshotPath)
        {
            var snapshot = new System.Windows.Forms.Timer { Interval = 3500 };
            snapshot.Tick += (_, _) =>
            {
                snapshot.Stop();
                UiSnapshot.CaptureTrayMenu(_menu, snapshotPath, _normalIcon, _dimmedIcon, RefreshMenuState);
            };
            snapshot.Start();
        }
    }

    // ---- called from other threads ----------------------------------------------------------------------------------

    public void PostShowStatus() => Post(ShowStatusWindow);

    public void PostExit() => Post(ExitThread);

    /// <summary>A short notice next to the tray icon (e.g. "Launching Game X …" when PlatinVault asks this PC to start a game).</summary>
    public void PostBalloon(string text, bool warning) => Post(() => ShowBalloon(text, warning));

    private void ShowBalloon(string text, bool warning)
    {
        if (_env.Headless)
        {
            _lastBalloon = text;
            BridgeLog.Info($"Ballon (Testlauf, nicht angezeigt): {text}");
            DumpState();
            return;
        }

        _notify.ShowBalloonTip(6000, BalloonTitle, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private void Post(Action action)
    {
        try
        {
            if (_marshal.IsHandleCreated && !_marshal.IsDisposed) _marshal.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The UI is already shutting down.
        }
    }

    // ---- status -----------------------------------------------------------------------------------------------------

    private void OnStatusChanged(StatusSnapshot snapshot) => Post(() => Refresh(snapshot));

    // The language changes on the polling thread (the server named another one); the menu and the windows live on the UI thread
    private void OnLanguageChanged(string language) => Post(() =>
    {
        ApplyMenuLanguage();
        Refresh(_status.Current);
        _statusForm?.ApplyLanguage();
    });

    private void ApplyMenuLanguage()
    {
        _openServerItem.Text = BridgeTexts.Tray.MenuOpenServer;
        _openHandbookItem.Text = BridgeTexts.Tray.MenuOpenHandbook;
        _openLogItem.Text = BridgeTexts.Tray.MenuOpenLog;
        _autostartItem.Text = BridgeTexts.Tray.MenuAutostart;
        _exitItem.Text = BridgeTexts.Tray.MenuExit;
        _uninstallItem.Text = BridgeTexts.Tray.MenuUninstall;
        _statusItem.Text = BridgeTexts.Tray.MenuConnecting;
    }

    private void Refresh(StatusSnapshot s)
    {
        var dimmed = !s.IsConnected;
        if (dimmed != _dimmedShown)
        {
            _notify.Icon = dimmed ? _dimmedIcon : _normalIcon;
            _dimmedShown = dimmed;
        }

        _notify.Text = Truncate(Tooltip(s), 127);
        _statusItem.Text = MenuStatus(s);
        _statusForm?.Apply(s);
        DumpState();
    }

    // The tray tooltip is limited to 127 characters.
    internal static string Tooltip(StatusSnapshot s) => BridgeEnvironment.ProductName + " – " + (s.Connection switch
    {
        ConnectionKind.Online when s.AppId != 0 => BridgeTexts.Tray.TooltipGame(s.GameName ?? string.Empty, s.Achieved, s.Total),
        ConnectionKind.Online => BridgeTexts.Tray.TooltipOnline,
        ConnectionKind.Starting => BridgeTexts.Tray.TooltipStarting,
        _ => BridgeTexts.Tray.TooltipOffline,
    }).ToString();

    internal static string MenuStatus(StatusSnapshot s) => (s.Connection switch
    {
        ConnectionKind.Online when s.AppId != 0 => BridgeTexts.Tray.MenuStatusGame(s.GameName ?? string.Empty, s.Achieved, s.Total),
        ConnectionKind.Online => BridgeTexts.Tray.MenuStatusOnline,
        ConnectionKind.Starting => BridgeTexts.Tray.MenuStatusStarting,
        ConnectionKind.Unauthorized => BridgeTexts.Tray.MenuStatusUnauthorized,
        _ => s.Detail is null ? BridgeTexts.Tray.MenuStatusOffline : BridgeTexts.Tray.MenuStatusOfflineDetail(s.Detail),
    }).ToString();

    internal static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private void RefreshMenuState()
    {
        _autostartItem.Checked = Installer.IsAutostartEnabled(_env);
        _uninstallItem.Enabled = Installer.IsInstalled(_env);
        _statusItem.Text = Truncate(MenuStatus(_status.Current), 80);
        DumpState();
    }

    // ---- test support (only active in a test run) -----------------------------------------------------------------

    /// <summary>Writes what the tray would show (tooltip, icon state, menu texts) to <c>ui	ray.txt</c> - nothing is visible in a test run.</summary>
    private void DumpState()
    {
        if (!_env.Headless) return;
        try
        {
            var s = _status.Current;
            string[] lines =
            [
                $"tooltip={_notify.Text}",
                $"iconDimmed={_dimmedShown}",
                $"connection={s.Connection}",
                $"game={s.GameName} {s.Achieved}/{s.Total}",
                $"menuStatus={_statusItem.Text}",
                $"menuItems={string.Join(" | ", _menu.Items.OfType<ToolStripItem>().Select(i => i is ToolStripSeparator ? "-" : i.Text))}",
                $"autostartEnabled={Installer.IsAutostartEnabled(_env)}",
                $"installed={Installer.IsInstalled(_env)}",
                $"lastBalloon={_lastBalloon}",
                $"statusWindowRequests={_statusWindowRequests}",
                $"trayIconVisibleOnDesktop={_notify.Visible}",
            ];
            Directory.CreateDirectory(HeadlessUi.Directory(_env));
            File.WriteAllLines(Path.Combine(HeadlessUi.Directory(_env), "tray.txt"), lines);
        }
        catch (IOException)
        {
            // Best effort, only for tests.
        }
    }

    private void ProcessTestCommands()
    {
        var file = Path.Combine(HeadlessUi.Directory(_env), "command.txt");
        string[] commands;
        try
        {
            if (!File.Exists(file)) return;
            commands = File.ReadAllLines(file);
            File.Delete(file);
        }
        catch (IOException)
        {
            return;
        }

        foreach (var command in commands.Select(c => c.Trim()).Where(c => c.Length > 0))
        {
            BridgeLog.Info($"Testbefehl: {command}");
            if (command == "status") { ShowStatusWindow(); continue; }
            var item = _menu.Items.Find(command, searchAllChildren: false).OfType<ToolStripMenuItem>().FirstOrDefault();
            if (item is null) { BridgeLog.Warn($"Testbefehl unbekannt: {command}"); continue; }
            RefreshMenuState(); // what opening the menu would do
            item.PerformClick();
        }
        DumpState();
    }

    // ---- actions ----------------------------------------------------------------------------------------------------

    public void ShowStatusWindow()
    {
        if (_env.Headless)
        {
            // Test run: the window is created and rendered into a picture, but never shown.
            _statusWindowRequests++;
            using var form = new StatusForm(OpenServer, OpenLog);
            form.Apply(_status.Current);
            HeadlessUi.Capture(_env, form, "status");
            BridgeLog.Info($"Statusfenster angefordert (Testlauf, nicht angezeigt), Nr. {_statusWindowRequests}.");
            DumpState();
            return;
        }

        if (_statusForm is null || _statusForm.IsDisposed)
        {
            _statusForm = new StatusForm(OpenServer, OpenLog);
            _statusForm.FormClosed += (_, _) => _statusForm = null;
            _statusForm.Apply(_status.Current);
            _statusForm.Show();
        }

        if (_statusForm.WindowState == FormWindowState.Minimized) _statusForm.WindowState = FormWindowState.Normal;
        // Windows only flashes the taskbar button for a window that was not started by the foreground process; toggling TopMost
        // is the usual way to really bring it to the front.
        _statusForm.TopMost = true;
        _statusForm.TopMost = false;
        _statusForm.Activate();
    }

    private void OpenServer()
    {
        if (_env.Headless)
        {
            BridgeLog.Info($"Browser (Testlauf, nicht geöffnet): {_serverUrl}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_serverUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowError(BridgeTexts.Tray.BrowserFailed(ex.Message));
        }
    }

    /// <summary>The handbook PDF next to the exe if it is there, otherwise the server's copy in the browser.</summary>
    private void OpenHandbook()
    {
        var target = Handbook.FindTarget(_env, _serverUrl);
        if (_env.Headless)
        {
            BridgeLog.Info($"Handbuch (Testlauf, nicht geöffnet): {target ?? "(nichts gefunden)"}");
            return;
        }

        try
        {
            if (target is null) { ShowError(BridgeTexts.Tray.HandbookNotFound); return; }
            Handbook.Open(target);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowError(BridgeTexts.Setup.HandbookFailed(ex.Message));
        }
    }

    private void OpenLog()
    {
        if (_env.Headless)
        {
            BridgeLog.Info($"Protokoll im Editor (Testlauf, nicht geöffnet): {BridgeLog.LogPath}");
            return;
        }

        try
        {
            if (!File.Exists(BridgeLog.LogPath)) { ShowError(BridgeTexts.Tray.NoLogYet(BridgeLog.LogPath)); return; }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{BridgeLog.LogPath}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowError(BridgeTexts.Tray.LogOpenFailed(ex.Message));
        }
    }

    private void ToggleAutostart()
    {
        var enable = !Installer.IsAutostartEnabled(_env);
        try
        {
            Installer.SetAutostart(_env, enable, BridgeEnvironment.ExePath);
            BridgeLog.Info(enable ? BridgeTexts.Tray.AutostartOn : BridgeTexts.Tray.AutostartOff);
        }
        catch (InstallException ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Uninstall()
    {
        if (UninstallFlow.Run(_env, _guard, runningInstance: true)) _requestStop();
    }

    private static void ShowError(string message) => Dialogs.Warning(message);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _status.Changed -= OnStatusChanged;
            BridgeLang.Changed -= OnLanguageChanged;
            _oneShot.Dispose();
            _commandTimer.Dispose();
            _statusForm?.Dispose();

            // Hide before disposing, otherwise Windows keeps a dead icon in the tray until the mouse moves over it.
            _notify.Visible = false;
            _notify.Dispose();
            _menu.Dispose();
            _normalIcon.Dispose();
            _dimmedIcon.Dispose();
            _marshal.Dispose();
        }
        base.Dispose(disposing);
    }
}
