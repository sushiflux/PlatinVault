namespace PlatinVault.Bridge.Ui;

/// <summary>
/// The small status window behind a double click on the tray icon (or a second start of the exe): connection, server,
/// Steam account, current game with progress, last unlock, last report and version. Refreshes live while open, and speaks
/// the bridge's current language - also when it changes while the window is open (<see cref="ApplyLanguage"/>).
/// </summary>
internal sealed class StatusForm : BrandedForm
{
    private const int Pad = 24;
    private const int LabelWidth = 130;
    private const int ValueX = Pad + LabelWidth + 6;

    private readonly Label _state;
    private readonly ScrollableText _detail;
    private readonly Label _server;
    private readonly Label _steam;
    private readonly Label _game;
    private readonly ProgressBar _progress;
    private readonly Label _unlock;
    private readonly Label _delivered;
    private readonly Label _version;

    // Every fixed text of the window with the way to get it again, so a language switch can re-apply them all
    private readonly List<(Control Control, Func<string> Text)> _fixedTexts = [];
    private StatusSnapshot? _lastSnapshot;

    public StatusForm(Action openServer, Action openLog)
        : base(BridgeTexts.Status.WindowTitle, BridgeEnvironment.ProductName, BridgeTexts.Status.Subtitle, clientWidth: 520, clientHeight: 470)
    {
        var big = new Font(SystemFonts.MessageBoxFont!.FontFamily, 13f, FontStyle.Bold);
        var small = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f);

        _state = Theme.CreateLabel(BridgeTexts.Status.StateStarting, R(Pad, 112, 472, 30), Theme.Muted, big);
        _state.Name = "lblState";
        // The reason of a failed connection comes from the network stack or the server and has no length limit: it wraps and scrolls in the
        // fixed slot instead of being cut off
        _detail = new ScrollableText(small, Theme.PageBack) { Bounds = R(Pad, 144, 472, 38), Name = "txtDetail" };
        Controls.AddRange([_state, _detail]);

        var rows = new (Msg Label, int Y)[]
        {
            (BridgeTexts.Status.RowServer, 196), (BridgeTexts.Status.RowSteam, 226), (BridgeTexts.Status.RowGame, 256),
            (BridgeTexts.Status.RowUnlock, 306), (BridgeTexts.Status.RowDelivered, 336), (BridgeTexts.Status.RowVersion, 366),
        };

        Label Row(int index, string controlName)
        {
            var (label, y) = rows[index];
            var caption = Theme.CreateLabel(label, R(Pad, y, LabelWidth, 22), Theme.Muted);
            _fixedTexts.Add((caption, () => label));
            Controls.Add(caption);
            var value = Theme.CreateLabel("–", R(ValueX, y, 520 - ValueX - Pad, 22), Theme.Text);
            value.AutoEllipsis = true;
            value.Name = controlName;
            Controls.Add(value);
            return value;
        }

        _server = Row(0, "valServer");
        _steam = Row(1, "valSteam");
        _game = Row(2, "valGame");
        _unlock = Row(3, "valUnlock");
        _delivered = Row(4, "valDelivered");
        _version = Row(5, "valVersion");

        _progress = new ProgressBar
        {
            Bounds = R(ValueX, 282, 520 - ValueX - Pad, 10),
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            Name = "barGame",
        };
        Controls.Add(_progress);

        var open = Theme.CreateButton(BridgeTexts.Status.OpenServer, R(Pad, 412, 190, 38), ButtonKind.Primary);
        open.Name = "btnOpen";
        var log = Theme.CreateButton(BridgeTexts.Status.OpenLog, R(Pad + 190 + 10, 412, 150, 38), ButtonKind.Secondary);
        log.Name = "btnLog";
        var close = Theme.CreateButton(BridgeTexts.Status.Close, R(520 - Pad - 100, 412, 100, 38), ButtonKind.Secondary);
        close.Name = "btnClose";
        _fixedTexts.Add((open, () => BridgeTexts.Status.OpenServer));
        _fixedTexts.Add((log, () => BridgeTexts.Status.OpenLog));
        _fixedTexts.Add((close, () => BridgeTexts.Status.Close));
        open.Click += (_, _) => openServer();
        log.Click += (_, _) => openLog();
        close.Click += (_, _) => Close();
        CancelButton = close;
        Controls.AddRange([open, log, close]);
    }

    /// <summary>Shows the texts again in the bridge's current language (window title, header, labels, buttons and the last snapshot). Call on the UI thread.</summary>
    public void ApplyLanguage()
    {
        Text = BridgeTexts.Status.WindowTitle;
        SetSubtitle(BridgeTexts.Status.Subtitle);
        foreach (var (control, text) in _fixedTexts) control.Text = text();
        if (_lastSnapshot is { } snapshot) Apply(snapshot);
    }

    /// <summary>Shows a snapshot. Call on the UI thread.</summary>
    public void Apply(StatusSnapshot s)
    {
        _lastSnapshot = s;

        switch (s.Connection)
        {
            case ConnectionKind.Online:
                _state.Text = BridgeTexts.Status.StateOnline;
                _state.ForeColor = Theme.Success;
                _detail.SetText(BridgeTexts.Status.DetailOnline, Theme.Muted);
                break;
            case ConnectionKind.Starting:
                _state.Text = BridgeTexts.Status.StateStarting;
                _state.ForeColor = Theme.Muted;
                _detail.SetText(BridgeTexts.Status.DetailStarting, Theme.Muted);
                break;
            case ConnectionKind.Unauthorized:
                _state.Text = BridgeTexts.Status.StateOffline;
                _state.ForeColor = Theme.Danger;
                _detail.SetText(BridgeTexts.Status.DetailUnauthorized, Theme.Muted);
                break;
            default:
                _state.Text = BridgeTexts.Status.StateOffline;
                _state.ForeColor = Theme.Danger;
                _detail.SetText(s.Detail is null ? BridgeTexts.Status.DetailOfflineRetry : BridgeTexts.Status.DetailOffline(s.Detail), Theme.Muted);
                break;
        }

        _server.Text = s.ServerText.Length > 0 ? s.ServerText : "–";
        _steam.Text = s.SteamId64 ?? (s.Connection == ConnectionKind.Online ? BridgeTexts.Status.SteamNotSignedIn : "–");

        if (s.AppId == 0)
        {
            _game.Text = BridgeTexts.Status.NoGame;
            _progress.Visible = false;
        }
        else if (s.Total > 0)
        {
            _game.Text = BridgeTexts.Status.GameProgress(s.GameName ?? string.Empty, s.Achieved, s.Total);
            _progress.Maximum = s.Total;
            _progress.Value = Math.Clamp(s.Achieved, 0, s.Total);
            _progress.Visible = true;
        }
        else
        {
            _game.Text = BridgeTexts.Status.GameNoAchievements(s.GameName ?? string.Empty);
            _progress.Visible = false;
        }

        _unlock.Text = s.LastUnlockName is null ? BridgeTexts.Status.NoUnlockYet : $"{s.LastUnlockName} ({Format(s.LastUnlockAt)})";
        _delivered.Text = Format(s.LastDeliveredAt);
        _version.Text = s.Version;
    }

    private static string Format(DateTime? time)
    {
        if (time is not { } t) return "–";
        return t.Date == DateTime.Today ? BridgeTexts.Status.Time(t) : BridgeTexts.Status.DateAndTime(t);
    }
}
