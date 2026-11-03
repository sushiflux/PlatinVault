using System.Diagnostics;
using System.Runtime.InteropServices;
using Companion.Api.Data;
using Companion.Api.Desktop;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using PlatinVault.Desktop.Database;
using PlatinVault.Desktop.Server;
using PlatinVault.Desktop.Ui;
using PlatinVault.Desktop.Updates;
using PlatinVault.Versioning;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop;

/// <summary>
/// The running Windows program: starts the server (with its SQLite database), opens the window, lives on in the tray, shows unlocks in the
/// in-game overlay (the app's own web popups, see WebOverlayWindow) and as notification, listens to the hotkey and to commands of a second start / the installer, and looks for updates once a day. The
/// server backs up its database itself (start, every 6 hours, clean stop) - quitting stops the server cleanly, which makes the last backup.
/// </summary>
internal sealed class DesktopApp : ApplicationContext
{
    private readonly DesktopEnvironment _env;
    private readonly AppInstance _instance;
    private readonly DesktopSettings _settings;
    private readonly MainWindow _main;
    private readonly TrayIcon _tray = new();
    private readonly WebOverlayWindow _overlay;
    private readonly GlobalHotkey _hotkey = new();
    private readonly DatabaseHost _database;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly CancellationTokenSource _life = new();
    private DesktopServer? _server;
    private System.Windows.Forms.Timer? _updateTimer;
    private UpdateOffer? _update;
    private bool _quitting;
    private bool _startRequested;

    public DesktopApp(DesktopEnvironment env, AppInstance instance)
    {
        _env = env;
        _instance = instance;
        _settings = DesktopSettings.Load(env.Data.SettingsFile);
        _database = new DatabaseHost(env);
        _main = new MainWindow(env, _settings);
        // The in-game overlay: created hidden now, its page is loaded once the server is up (a test run keeps it far off-screen)
        _overlay = new WebOverlayWindow(offscreen: env.Quiet);
        _overlay.AvailabilityChanged += available =>
        {
            DesktopLog.Info(available ? "Overlay bereit." : "Overlay nicht verfügbar - Trophäen-Popups erscheinen nur in der App.");
            PostHostState();
        };
        _overlay.Presented += OnOverlayPresented;
        // The page of the main window asks once it has loaded: with "only in the game" it leaves the popup to a working overlay
        _main.HostHello += PostHostState;
        _main.QuitRequested += () => _ = QuitAsync();
        _main.HiddenToTray += OnHiddenToTray;
        // Once: a window handle can be created again (style changes), the start sequence must not run twice
        _main.HandleCreated += (_, _) =>
        {
            if (_startRequested) return;
            _startRequested = true;
            _ = StartAsync();
        };
        if (env.Quiet) _main.UseOffscreenStyle();
        _ = _main.Handle; // create the handle now: the start sequence and the command channel marshal onto it

        _tray.Clicked += () => _main.Bring();
        _tray.BalloonClicked += () => _main.Bring();
        _tray.Menu = BuildMenu();
        // A test run never puts anything on the real desktop: no tray icon, no hotkey, the window off-screen (see Smoke)
        if (!env.Quiet) _tray.Show();

        _hotkey.Pressed += () => _ = ToggleGuideAsync();
        instance.Listen(env.Layout, command => _main.BeginInvoke(() => OnCommand(command)));

        if (env.Quiet) _main.ShowOffscreen();
        else if (!env.Options.Background) _main.Show();
    }

    private void OnCommand(string command)
    {
        switch (command)
        {
            case AppInstance.Show: _main.Bring(); break;
            case AppInstance.Guide: _ = ToggleGuideAsync(); break;
            case AppInstance.Quit: _ = QuitAsync(); break;
        }
    }

    /// <summary>Completes when the start sequence ended (successfully or with the error on the start screen).</summary>
    public Task Started => _started.Task;

    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DesktopServer? Server => _server;

    public DatabaseHost Database => _database;

    public MainWindow Window => _main;

    /// <summary>The in-game overlay (tests: smoke run).</summary>
    public WebOverlayWindow Overlay => _overlay;

    public DesktopSettings Settings => _settings;

    private async Task StartAsync()
    {
        var progress = new Progress<StepReport>(_main.ShowProgress);
        try
        {
            if (WebViewRuntime.InstalledVersion() is null)
            {
                _main.ShowError(DesktopTexts.Window.WebViewMissing, null);
                return;
            }

            _database.Prepare(progress);
            // Updates started from the settings use the same download/verify/run flow as the tray; never in development or test runs
            var desktopHost = new LocalDesktopHost(_env.Options.TestLaunch, null, _env.UpdateCheckAllowed ? InstallFromSettingsAsync : null);
            _server = await DesktopServer.CreateAsync(_env, _settings, _database, desktopHost, start: true, _life.Token);
            DesktopLog.Info($"Server läuft auf {_server.BaseUri}.");
            await ApplyStoredLanguageAsync();

            var feed = _server.App.Services.GetRequiredService<LiveEventFeed>();
            feed.AchievementUnlocked += unlock => _main.BeginInvoke(() => _ = OnUnlockAsync(unlock));
            feed.PlatinumUnlocked += platinum => _main.BeginInvoke(() => _ = OnPlatinum(platinum));

            // A development run can show the Vite dev server (hot reload) instead of the built UI; the server stays the API behind it
            await _main.OpenAppAsync(_env.Options.UiUrl is { } uiUrl ? new Uri(uiUrl) : _server.BaseUri, _server.BaseUri);
            await MigrateLegacyOverlaySwitchAsync();
            await StartOverlayAsync();
            _tray.Menu = BuildMenu();
            _tray.UpdateTooltip(_env.IsDev ? DesktopTexts.Dev.TrayTooltip : DesktopTexts.Tray.Tooltip);

            if (_settings.HotkeyEnabled && _env.HotkeyAllowed) _hotkey.Register(UiLang.Current);

            if (_env.UpdateCheckAllowed)
            {
                _updateTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
                _updateTimer.Tick += async (_, _) =>
                {
                    // Hourly re-read of the server's status: the server decides from the user's setting "Nach Updates suchen" when GitHub is asked again
                    _updateTimer.Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds;
                    await CheckForUpdateAsync(UpdateCheckMode.Automatic, interactive: false);
                };
                _updateTimer.Start();
            }
        }
        catch (DatabaseRefusedException ex)
        {
            // The database was used by a newer PlatinVault or is damaged: the server did not start and changed nothing. Shown on the start screen and
            // in the program's own dialog, in the language of the program.
            DesktopLog.Error($"Start abgebrochen: {ex.Text.De}");
            var text = new Msg(ex.Text.De, ex.Text.En);
            _main.Bring();
            _main.ShowError(text, null);
            if (!_env.Quiet) Dialogs.Error(_main, text);
        }
        catch (DatabaseException ex)
        {
            DesktopLog.Error($"Start fehlgeschlagen: {ex.Message}");
            _main.Bring();
            _main.ShowError(ex.Text, ex.Detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DesktopLog.Error($"Start fehlgeschlagen: {ex}");
            _main.Bring();
            _main.ShowError(DesktopTexts.Window.FailedTitle, ex.Message);
        }
        finally
        {
            _started.TrySetResult();
        }
    }

    /// <summary>The tray, the notifications and the overlay speak the language the user chose in the app (ui.language), else Windows'.</summary>
    private async Task ApplyStoredLanguageAsync()
    {
        if (_server is null) return;
        try
        {
            using var scope = _server.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var stored = await db.AppSettings.AsNoTracking().Where(s => s.Key == SettingsStore.KeyUiLanguage).Select(s => s.Value).FirstOrDefaultAsync(_life.Token);
            if (UiLang.Normalize(stored) is { } language) UiLang.Current = language;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            DesktopLog.Warn($"Sprache der App nicht lesbar: {ex.Message}");
        }
    }

    // ---- live events -----------------------------------------------------------------------------------------------------------

    // Who shows what (user decision 2026-11-03, setting "Trophäen-Popup anzeigen" = live.popup_target):
    //  - the overlay: OverlayRouting.ShowsOverlay ("overlay" always, "both" while the program window is not in front, "app" never);
    //  - the app window decides for itself with the same setting (utils/popupTarget.ts) and plays the SOUND once per unlock in every
    //    case - the overlay page is silent, so "both" never doubles it;
    //  - the Windows notification stays as it was: only while the program window is not in front.
    // Events come once from the in-process LiveEventFeed (never from SignalR): no duplicates, no catch-up replays in the overlay.

    private async Task OnUnlockAsync(LiveUnlockEvent unlock)
    {
        var foreground = _main.IsForeground;
        var (target, position) = await ReadPopupChoiceAsync();
        if (OverlayRouting.ShowsOverlay(target, foreground))
        {
            _overlay.Present(OverlayMessages.Achievement(unlock.Achievement, unlock.GameName, position, UiLang.Current));
        }
        if (_settings.NotificationsEnabled && !foreground && !_env.Silent)
        {
            var a = unlock.Achievement;
            var footer = string.Join(" · ", new[] { a.RarityTier, a.RarityLabel, unlock.GameName }.Where(p => !string.IsNullOrWhiteSpace(p)));
            _tray.Notify($"🏆 {a.DisplayName}", string.IsNullOrWhiteSpace(a.Description) ? footer : a.Description + "\n" + footer);
        }
    }

    private async Task OnPlatinum(LivePlatinumEvent platinum)
    {
        var foreground = _main.IsForeground;
        var (target, _) = await ReadPopupChoiceAsync();
        if (OverlayRouting.ShowsOverlay(target, foreground))
        {
            _overlay.Present(OverlayMessages.Platinum(platinum.GameName, platinum.Total, UiLang.Current));
        }
        if (_settings.NotificationsEnabled && !foreground && !_env.Silent)
        {
            _tray.Notify($"🏆 {DesktopTexts.Overlay.Platinum}", DesktopTexts.Overlay.PlatinumText(platinum.GameName, platinum.Total));
        }
    }

    /// <summary>
    /// Loads the overlay page (route /overlay of the same UI as the window) into the hidden overlay window, in the window's WebView2
    /// environment. A failure only costs the overlay: the main window's page is told, and with "only in the game" it shows the popup itself.
    /// </summary>
    private async Task StartOverlayAsync()
    {
        if (_main.Environment is not { } environment || _main.UiBase is not { } uiBase) return;
        var page = new Uri(uiBase, "/overlay");
        try
        {
            var development = string.Equals(_env.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);
            await _overlay.InitializeAsync(environment, page, development, _life.Token);
            DesktopLog.Info($"Overlay geladen in {_overlay.CreationMilliseconds} ms (unsichtbar bereit, Seite {page}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DesktopLog.Warn($"Overlay konnte nicht gestartet werden - Trophäen-Popups erscheinen nur in der App: {ex.Message}");
            PostHostState();
        }
    }

    private void PostHostState() => _main.PostJson(OverlayMessages.HostState(_overlay.Available));

    /// <summary>A test run (or an off-screen development run) saves what the overlay shows as a picture - nothing appears on the desktop.</summary>
    private async void OnOverlayPresented()
    {
        if (!_env.Quiet || _overlay.Core is not { } core) return;
        try
        {
            // Mid-animation: the card has slid in, the exit has not started
            await Task.Delay(1500);
            var folder = Path.Combine(_env.Layout.TestRoot ?? _env.Data.Root, "overlay");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"overlay-{DateTime.Now:HHmmssfff}.png");
            // Written under a temporary name first: whoever waits for the picture never sees a half-written file
            var partial = path + ".part";
            await using (var png = File.Create(partial))
            {
                await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, png);
            }
            File.Move(partial, path, overwrite: true);
            DesktopLog.Info($"Testlauf: Overlay als Bild gespeichert statt angezeigt: {path}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            DesktopLog.Warn($"Overlay-Bild nicht gespeichert: {ex.Message}");
        }
    }

    /// <summary>
    /// The former tray switch "OverlayEnabled" (desktop.json): switched off and no popup target in the app yet -> "app", once; then the
    /// value leaves desktop.json. From now on the app setting is the only source.
    /// </summary>
    private async Task MigrateLegacyOverlaySwitchAsync()
    {
        if (_server is null || _settings.LegacyOverlayEnabled is null) return;
        try
        {
            using var scope = _server.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var migrated = OverlayRouting.MigrateLegacySwitch(_settings.LegacyOverlayEnabled, await SettingsStore.HasPopupTargetAsync(db, _life.Token));
            if (migrated is not null)
            {
                await scope.ServiceProvider.GetRequiredService<SettingsStore>().SetPopupTargetAsync(migrated, _life.Token);
                DesktopLog.Info($"Früherer Overlay-Schalter (aus) übernommen: Trophäen-Popup anzeigen = {migrated}.");
            }
            _settings.LegacyOverlayEnabled = null;
            _settings.Save(_env.Data.SettingsFile);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or DbUpdateException)
        {
            DesktopLog.Warn($"Früherer Overlay-Schalter nicht übernommen: {ex.Message}");
        }
    }

    /// <summary>"Trophäen-Popup anzeigen" and the popup position (also "fullscreen") as the user chose them in the app; defaults when unreadable.</summary>
    private async Task<(string Target, string Position)> ReadPopupChoiceAsync()
    {
        if (_server is null) return (SettingsStore.DefaultPopupTarget, SettingsStore.DefaultPopupPosition);
        try
        {
            using var scope = _server.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var target = await SettingsStore.ReadPopupTargetAsync(db, _life.Token);
            var stored = await db.AppSettings.AsNoTracking().Where(s => s.Key == SettingsStore.KeyPopupPosition).Select(s => s.Value).FirstOrDefaultAsync(_life.Token);
            return (target, SettingsStore.NormalizePopupPosition(stored) ?? SettingsStore.DefaultPopupPosition);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return (SettingsStore.DefaultPopupTarget, SettingsStore.DefaultPopupPosition);
        }
    }

    /// <summary>
    /// Tray "Overlay testen": a gold test trophy in the overlay, at the user's popup position - whatever "Trophäen-Popup anzeigen" says
    /// (the user asked for exactly this test). Silent like every overlay popup; the app window shows nothing.
    /// </summary>
    public async Task TestOverlayAsync()
    {
        if (!_overlay.Available)
        {
            if (!_env.Quiet) Dialogs.Error(null, DesktopTexts.Overlay.Unavailable);
            return;
        }
        var (_, position) = await ReadPopupChoiceAsync();
        var test = new LiveAchievementItemDto(
            ApiName: "PV_OVERLAY_TEST", DisplayName: DesktopTexts.Overlay.TestName, Description: DesktopTexts.Overlay.TestDescription,
            OriginalDescription: null, IsTranslated: false, IconUrl: "/favicon.svg", IconGrayUrl: "/favicon.svg", Achieved: true,
            UnlockTime: DateTime.UtcNow, GlobalPercent: 2.4, RarityTier: "Gold", RarityLabel: string.Empty, IsHidden: false, UserNote: string.Empty)
        {
            HasGlobalPercent = true,
            RarityBand = "ultraRare",
        };
        _overlay.Present(OverlayMessages.Achievement(test, DesktopTexts.Overlay.TestGame, position, UiLang.Current));
    }

    /// <summary>Tray switch "Trophäen-Overlay über dem Spiel": the same app setting as Settings > Live & Sound (on = "both", off = "app").</summary>
    private async Task ToggleOverlayAsync(ToolStripMenuItem item)
    {
        if (_server is null) return;
        try
        {
            var (target, _) = await ReadPopupChoiceAsync();
            var next = OverlayRouting.TrayToggled(target);
            using (var scope = _server.App.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<SettingsStore>().SetPopupTargetAsync(next, _life.Token);
            }
            item.Checked = OverlayRouting.TrayChecked(next);
            // The open app page reloads its settings (Settings > Live & Sound and its own popup decision follow at once)
            _main.PostJson(OverlayMessages.SettingsChanged);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or DbUpdateException)
        {
            if (!_env.Quiet) Dialogs.Error(null, DesktopTexts.Overlay.SwitchFailed(ex.Message));
        }
    }

    // ---- hotkey: guide over the game ----------------------------------------------------------------------------------------------

    private async Task ToggleGuideAsync()
    {
        if (_main.IsForeground && _main.TopMost)
        {
            // Second press: back to the game
            _main.WindowState = FormWindowState.Minimized;
            return;
        }
        string? slug = null;
        if (_server is not null)
        {
            try
            {
                var status = await _server.App.Services.GetRequiredService<SteamLiveTrackerService>().GetStatusAsync(_life.Token);
                slug = status.RunningGameSlug;
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                DesktopLog.Warn($"Laufendes Spiel nicht ermittelbar: {ex.Message}");
            }
        }
        _main.ShowPath(slug is { Length: > 0 } ? $"/games/{Uri.EscapeDataString(slug)}" : null, onTop: true);
    }

    // ---- tray ------------------------------------------------------------------------------------------------------------------------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new TrayMenuRenderer(), ShowImageMargin = true, Font = Fonts.Get(9.5f, DpiScale()) };
        menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.Open, null, (_, _) => _main.Bring()) { Font = Fonts.Get(9.5f, DpiScale(), FontStyle.Bold) });
        var guideText = DesktopTexts.Tray.OpenGuide + (_hotkey.Keys is { } keys ? $"\t{keys}" : string.Empty);
        menu.Items.Add(new ToolStripMenuItem(guideText, null, (_, _) => _ = ToggleGuideAsync()));
        menu.Items.Add(new ToolStripSeparator());
        var overlaySwitch = new ToolStripMenuItem(DesktopTexts.Tray.Overlay);
        overlaySwitch.Click += (_, _) => _ = ToggleOverlayAsync(overlaySwitch);
        menu.Items.Add(overlaySwitch);
        menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.OverlayTest, null, (_, _) => _ = TestOverlayAsync()));
        menu.Items.Add(Toggle(DesktopTexts.Tray.Notifications, () => _settings.NotificationsEnabled, v => _settings.NotificationsEnabled = v));
        var installedExe = _env.Layout.InstalledDir is { } dir ? Path.Combine(dir, InstallLayout.ExeName) : null;
        if (installedExe is not null)
        {
            var autostart = new ToolStripMenuItem(DesktopTexts.Tray.Autostart) { Checked = Registration.IsAutostartEnabled(_env.Layout) };
            autostart.Click += (_, _) =>
            {
                try
                {
                    Registration.SetAutostart(_env.Layout, !autostart.Checked, installedExe);
                    autostart.Checked = !autostart.Checked;
                }
                catch (InstallException ex)
                {
                    Dialogs.Error(null, ex.Text);
                }
            };
            menu.Items.Add(autostart);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.Handbook, null, (_, _) => OpenHandbook()));
        menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.Backups, null, (_, _) => MainWindow.OpenFolder(_env.Data.Backups)));
        menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.Logs, null, (_, _) => MainWindow.OpenFolder(_env.Data.Logs)));
        if (_env.UpdateCheckAllowed || _update is not null) menu.Items.Add(new ToolStripSeparator());
        if (_env.UpdateCheckAllowed)
        {
            menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.CheckUpdates, null, (_, _) => _ = CheckForUpdateAsync(UpdateCheckMode.Manual, interactive: true)));
        }
        if (_update is { } offer)
        {
            menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.Update(offer.Version.Display), null, (_, _) => _ = InstallUpdateAsync(offer)));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(DesktopTexts.Tray.Quit, null, (_, _) => _ = QuitAsync()));
        menu.Opening += async (_, _) =>
        {
            // The menu opens at the tray icon: its font follows that monitor's DPI, not the main window's (per-monitor DPI)
            var scale = DpiScale();
            menu.Font = Fonts.Get(9.5f, scale);
            if (menu.Items.Count > 0) menu.Items[0].Font = Fonts.Get(9.5f, scale, FontStyle.Bold);
            // The tray switch shows the app setting as it is now (it may have been changed in the app meanwhile)
            overlaySwitch.Checked = OverlayRouting.TrayChecked((await ReadPopupChoiceAsync()).Target);
            await ApplyStoredLanguageAsync();
        };
        return menu;
    }

    private ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { Checked = get() };
        item.Click += (_, _) =>
        {
            set(!get());
            item.Checked = get();
            _settings.Save(_env.Data.SettingsFile);
        };
        return item;
    }

    /// <summary>Scale of the monitor under the mouse: the tray menu opens there.</summary>
    private static float DpiScale() => ScreenDpi.ScaleAt(Cursor.Position);

    private void OnHiddenToTray()
    {
        if (_settings.TrayHintShown) return;
        _settings.TrayHintShown = true;
        _settings.Save(_env.Data.SettingsFile);
        var hotkey = _hotkey.Keys is { } keys ? " " + DesktopTexts.Tray.HotkeyReady(keys) : string.Empty;
        _tray.Notify(DesktopTexts.Tray.StillRunningTitle, DesktopTexts.Tray.StillRunningText + hotkey);
    }

    private void OpenHandbook()
    {
        var folder = Path.Combine(_env.InstallDir, "docs", "handbuch", UiLang.Current == UiLang.English ? "en" : "de");
        var pdf = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.pdf").FirstOrDefault() : null;
        if (pdf is null && _server is not null)
        {
            _main.ShowPath(null, false);
            pdf = new Uri(_server.BaseUri, $"api/docs/handbook?lang={UiLang.Current}").ToString();
        }
        if (pdf is null) return;
        try { Process.Start(new ProcessStartInfo(pdf) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { Dialogs.Error(null, ex.Message); }
    }

    // ---- updates ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Asks the server's update check (the same for every host) what to offer: betas only while "Beta-Versionen erhalten" is on in the
    /// app (Settings > System > Updates), never a version older than this one, only versions with a Windows installer. The periodic check
    /// is quiet; the tray's "Nach Updates suchen" (<paramref name="interactive"/>) always says what it found.
    /// </summary>
    private async Task CheckForUpdateAsync(UpdateCheckMode mode, bool interactive)
    {
        if (_server is null || !_env.UpdateCheckAllowed) return;
        UpdateStatus status;
        try
        {
            status = await UpdateStatusBuilder.GetAsync(_server.App.Services, mode, _life.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or OperationCanceledException)
        {
            DesktopLog.Info($"Update-Prüfung nicht möglich: {ex.Message}");
            if (interactive && !_env.Quiet) Dialogs.Error(null, DesktopTexts.Update.CheckFailed(ex.Message));
            return;
        }

        _settings.LastUpdateCheck = DateTimeOffset.Now;
        _settings.Save(_env.Data.SettingsFile);
        if (status.Error is { } error)
        {
            DesktopLog.Info($"Update-Prüfung nicht möglich: {error.De}");
            if (interactive && !_env.Quiet) Dialogs.Error(null, DesktopTexts.Update.CheckFailed(UiLang.Current == UiLang.English ? error.En : error.De));
            return;
        }

        var offer = UpdateOffer.From(status.Available);
        var changed = offer?.Version.Text != _update?.Version.Text;
        _update = offer;
        if (changed) _tray.Menu = BuildMenu();
        if (offer is null)
        {
            if (interactive && !_env.Quiet)
            {
                Dialogs.Info(null, status.State == UpdateState.Unchecked ? DesktopTexts.Update.NotChecked : DesktopTexts.Update.UpToDate(_env.Version, status.BetaUpdatesEnabled));
            }
            return;
        }
        if (interactive) await InstallUpdateAsync(offer);
        else if (changed && _settings.NotificationsEnabled) _tray.Notify("PlatinVault", DesktopTexts.Update.Available(offer.Version.Display));
    }

    /// <summary>The tray's update entry: asks first (own dialog), then downloads, verifies and runs the installer.</summary>
    private async Task InstallUpdateAsync(UpdateOffer offer)
    {
        if (_env.Quiet) return;
        var answer = Dialogs.Show(null, DialogKind.Question, DesktopTexts.Update.Confirm(_env.Version, offer.Version.Display),
            [new DialogButton(DesktopTexts.Update.Later, DialogResult.Cancel), new DialogButton(DesktopTexts.Update.Install, DialogResult.OK, PvButtonKind.Primary)],
            DialogResult.Cancel, focusIndex: 1);
        if (answer != DialogResult.OK) return;
        var result = await DownloadAndRunAsync(offer, _life.Token);
        if (result.Outcome == DesktopUpdateOutcome.ChecksumMismatch) Dialogs.Error(null, DesktopTexts.Update.ChecksumMismatch);
        else if (result.Outcome == DesktopUpdateOutcome.Failed) Dialogs.Error(null, DesktopTexts.Update.Failed(result.Detail ?? "?"));
    }

    /// <summary>
    /// "Installieren" in the app's settings: the user confirmed in the app's own dialog already, so this only downloads, verifies and
    /// runs the installer; the outcome goes back to the settings page, which shows any error in full.
    /// </summary>
    private Task<DesktopUpdateStart> InstallFromSettingsAsync(PublishedRelease release, CancellationToken ct) =>
        UpdateOffer.From(release) is { } offer
            ? DownloadAndRunAsync(offer, ct)
            : Task.FromResult(new DesktopUpdateStart(DesktopUpdateOutcome.Failed, "no installer"));

    private async Task<DesktopUpdateStart> DownloadAndRunAsync(UpdateOffer offer, CancellationToken ct)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
            var installer = await new UpdateChecker(_http).DownloadAsync(offer, _env.Data.Updates, linked.Token);
            if (installer is null) return new DesktopUpdateStart(DesktopUpdateOutcome.ChecksumMismatch, null);
            DesktopLog.Info($"Update {offer.Version.Text}: Installer geprüft, wird gestartet.");
            // The installer asks this program to quit (the server makes its last backup) and starts it again at the end
            Process.Start(new ProcessStartInfo(installer, "--update") { UseShellExecute = false })?.Dispose();
            return new DesktopUpdateStart(DesktopUpdateOutcome.Started, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            DesktopLog.Warn($"Update {offer.Version.Text} nicht gestartet: {ex.Message}");
            return new DesktopUpdateStart(DesktopUpdateOutcome.Failed, ex.Message);
        }
    }

    // ---- quit ------------------------------------------------------------------------------------------------------------------------

    public async Task QuitAsync()
    {
        if (_quitting) return;
        _quitting = true;
        DesktopLog.Info("PlatinVault wird beendet.");
        _updateTimer?.Stop();
        _hotkey.Dispose();
        _main.AllowClose();
        _main.Hide();
        _overlay.HideOverlay();
        _tray.Dispose();

        if (_server is not null)
        {
            // Stopping the server makes its last backup ("Beenden") - also the "backup before every update" (the installer quits the program first)
            await _server.DisposeAsync();
            _server = null;
        }
        await _life.CancelAsync();
        _main.Close();
        _overlay.Dispose();
        _instance.Dispose();
        ExitThread();
    }
}
