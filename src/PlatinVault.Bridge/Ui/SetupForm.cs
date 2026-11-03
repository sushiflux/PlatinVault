using System.Diagnostics;

namespace PlatinVault.Bridge.Ui;

internal enum SetupMode
{
    /// <summary>First installation from an unpacked ZIP.</summary>
    Install,

    /// <summary>An installation exists; this (possibly newer) exe replaces it in place.</summary>
    Update,

    /// <summary>Running where it is (installed copy or --no-setup) but server/token are missing or need a change.</summary>
    SaveOnly,
}

internal enum SetupOutcome
{
    Cancelled,

    /// <summary>Installed (or updated) and the installed copy was started; this process should exit.</summary>
    Installed,

    /// <summary>Config saved next to the exe; this process continues as the running bridge.</summary>
    Saved,
}

/// <summary>
/// The setup window: shows server and token (from the config, or typed/pasted with live validation), tests the connection
/// right away with one real state push, and installs or saves. Every message has a fixed reserved area, so nothing moves
/// while the user types or a test result arrives.
/// </summary>
internal sealed class SetupForm : BrandedForm
{
    private const int Pad = 24;
    private const int ContentWidth = 512;

    private readonly BridgeEnvironment _env;
    private readonly CliOptions _cli;
    private readonly InstanceGuard _guard;
    private readonly StateSampler _sampler;
    private readonly SetupMode _mode;
    private readonly string _version;

    private readonly TextBox _server;
    private readonly TextBox _token;
    private readonly CheckBox _showToken;
    private readonly ScrollableText _serverMessage;
    private readonly ScrollableText _tokenMessage;
    private readonly Panel _card;
    private readonly ScrollableText _cardText;
    private readonly CheckBox _autostart;
    private readonly CheckBox _startMenu;
    private readonly Button _primary;
    private readonly Button _cancel;
    private readonly Button _handbook;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 700 };

    private CancellationTokenSource? _testCts;
    private string? _serverLanguage;
    private bool _lastTestUnauthorized;
    private bool _busy;
    private BridgeSettings? _valid;

    public SetupForm(BridgeEnvironment env, CliOptions cli, InstanceGuard guard, StateSampler sampler, SetupMode mode,
        string? prefillServer, string? prefillToken, string version, string? installedVersion)
        : base(BridgeTexts.Setup.WindowTitle, BridgeEnvironment.ProductName, SubtitleFor(mode, version, installedVersion),
            clientWidth: 560, clientHeight: 580)
    {
        _env = env;
        _cli = cli;
        _guard = guard;
        _sampler = sampler;
        _mode = mode;
        _version = version;

        var inputFont = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10f);
        var small = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.75f);
        var bold = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f, FontStyle.Bold);

        var intro = Theme.CreateLabel(BridgeTexts.Setup.Intro, R(Pad, 108, ContentWidth, 40), Theme.Muted);

        var serverLabel = Theme.CreateLabel(BridgeTexts.Setup.ServerLabel, R(Pad, 158, 300, 20), Theme.Text, bold);
        _server = new TextBox
        {
            Bounds = R(Pad, 180, ContentWidth, 28),
            Font = inputFont,
            Text = prefillServer ?? string.Empty,
            Name = "txtServer",
            AccessibleName = BridgeTexts.Setup.ServerLabel,
        };
        // The three message slots keep their size whatever they say: a text that is longer than the slot wraps and scrolls inside it (never cut)
        _serverMessage = new ScrollableText(small, Theme.PageBack) { Bounds = R(Pad, 210, ContentWidth, 34), Name = "txtServerMessage" };

        var tokenLabel = Theme.CreateLabel(BridgeTexts.Setup.TokenLabel, R(Pad, 250, 300, 20), Theme.Text, bold);
        _showToken = new CheckBox
        {
            Text = BridgeTexts.Setup.ShowToken,
            Bounds = R(Pad + ContentWidth - 150, 248, 150, 24),
            ForeColor = Theme.Muted,
            Font = small,
            CheckAlign = ContentAlignment.MiddleLeft,
            Name = "chkShowToken",
            UseMnemonic = false,
        };
        _token = new TextBox
        {
            Bounds = R(Pad, 274, ContentWidth, 28),
            Font = inputFont,
            Text = prefillToken ?? string.Empty,
            UseSystemPasswordChar = true,
            Name = "txtToken",
            AccessibleName = BridgeTexts.Setup.TokenLabel,
        };
        _tokenMessage = new ScrollableText(small, Theme.PageBack) { Bounds = R(Pad, 304, ContentWidth, 34), Name = "txtTokenMessage" };

        // Room for four lines: the longest answer a server gives (a 409 about another Steam account, with two 17-digit SteamIDs) needs four in German
        _card = new Panel { Bounds = R(Pad, 346, ContentWidth, 82), BackColor = Theme.Card, BorderStyle = BorderStyle.FixedSingle };
        _cardText = new ScrollableText(SystemFonts.MessageBoxFont!, Theme.Card) { Bounds = R(12, 6, ContentWidth - 28, 70), Name = "txtConnection" };
        _card.Controls.Add(_cardText);

        _autostart = new CheckBox
        {
            Text = BridgeTexts.Setup.Autostart,
            Bounds = R(Pad, 436, ContentWidth, 26),
            // An update keeps what the user chose earlier; a first installation starts with both on.
            Checked = mode != SetupMode.Update || Installer.IsAutostartEnabled(env),
            ForeColor = Theme.Text,
            Name = "chkAutostart",
            UseMnemonic = false,
        };
        _startMenu = new CheckBox
        {
            Text = BridgeTexts.Setup.StartMenu,
            Bounds = R(Pad, 464, ContentWidth, 26),
            Checked = mode != SetupMode.Update || File.Exists(env.StartMenuShortcutPath),
            ForeColor = Theme.Text,
            Name = "chkStartMenu",
            UseMnemonic = false,
        };

        string location = mode == SetupMode.SaveOnly
            ? BridgeTexts.Setup.LocationSave(Path.Combine(BridgeEnvironment.ExeDirectory, "bridge.json"))
            : BridgeTexts.Setup.LocationInstall(env.InstallRoot);
        var locationLabel = Theme.CreateLabel(location, R(Pad, 500, ContentWidth, 20), Theme.Muted, small);
        locationLabel.AutoEllipsis = true;
        locationLabel.Name = "lblLocation";

        string primaryText = mode switch
        {
            SetupMode.Install => BridgeTexts.Setup.PrimaryInstall,
            SetupMode.Update => BridgeTexts.Setup.PrimaryUpdate,
            _ => BridgeTexts.Setup.PrimarySave,
        };
        _primary = Theme.CreateButton(primaryText, R(Pad + ContentWidth - 210, 530, 210, 38), ButtonKind.Primary);
        _primary.Name = "btnInstall";
        _cancel = Theme.CreateButton(BridgeTexts.Dialog.Cancel, R(Pad + ContentWidth - 210 - 12 - 120, 530, 120, 38), ButtonKind.Secondary);
        _cancel.Name = "btnCancel";
        // Left of the action buttons: whoever just unpacked the ZIP can read the handbook before installing
        _handbook = Theme.CreateButton(BridgeTexts.Setup.OpenHandbook, R(Pad, 530, 150, 38), ButtonKind.Secondary);
        _handbook.Name = "btnHandbook";
        CancelButton = _cancel;
        AcceptButton = _primary;

        if (mode == SetupMode.SaveOnly)
        {
            _autostart.Visible = false;
            _startMenu.Visible = false;
        }

        Controls.AddRange([intro, serverLabel, _server, _serverMessage, tokenLabel, _showToken, _token, _tokenMessage, _card,
            _autostart, _startMenu, locationLabel, _handbook, _primary, _cancel]);

        _server.TextChanged += (_, _) => OnInputChanged();
        _token.TextChanged += (_, _) => OnInputChanged();
        _showToken.CheckedChanged += (_, _) => _token.UseSystemPasswordChar = !_showToken.Checked;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            StartConnectionTest();
        };
        _handbook.Click += (_, _) => OpenHandbook();
        _primary.Click += async (_, _) => await OnPrimaryAsync();
        _cancel.Click += (_, _) => { Outcome = SetupOutcome.Cancelled; DialogResult = DialogResult.Cancel; Close(); };

        Load += (_, _) => Begin();
        FormClosed += (_, _) =>
        {
            _debounce.Dispose();
            _testCts?.Cancel();
        };
    }

    /// <summary>Validates the prefilled values and starts the first connection test (when the window is shown, or by a headless test).</summary>
    internal void Begin()
    {
        OnInputChanged(testImmediately: true);
        (_server.Text.Length == 0 ? (Control)_server : _token.Text.Length == 0 ? _token : _primary).Select();
    }

    /// <summary>Test hook: types values into the fields as a user would.</summary>
    internal void ApplyTestInput(string server, string token)
    {
        _server.Text = server;
        _token.Text = token;
    }

    /// <summary>
    /// Test hook of <c>--test-layout</c>: every state the three message slots (under the server field, under the token field, the
    /// status card) can show, with realistic worst-case data. Add a case here when a new message can appear in one of the slots -
    /// the layout check measures them all in both languages, so a longer translation cannot silently be clipped.
    /// </summary>
    internal IEnumerable<(string Name, Action Show)> TestStates()
    {
        const string steamId = "76561198012345678";
        const string server = "http://192.168.178.10:5233";

        void ServerError(string typed)
        {
            BridgeSettings.TryParseServer(typed, out _, out var error);
            SetMessage(_serverMessage, error, BridgeTexts.Setup.ServerHint);
        }

        yield return ("server-hint", () => SetMessage(_serverMessage, null, BridgeTexts.Setup.ServerHint));
        yield return ("server-invalid", () => ServerError("meinserver.example.org:5233/platinvault/pfad/lang"));
        yield return ("server-credentials", () => ServerError("http://benutzer:passwort@192.168.178.10:5233"));
        yield return ("server-query", () => ServerError("http://192.168.178.10:5233/?x=1"));
        yield return ("token-hint", () => SetMessage(_tokenMessage, null, BridgeTexts.Setup.TokenHint));
        yield return ("token-badchars", () => SetMessage(_tokenMessage, BridgeTexts.Setup.TokenBadChars, BridgeTexts.Setup.TokenHint));

        yield return ("card-idle", () => SetCard(CardKind.Idle, BridgeTexts.Setup.CardIdle));
        yield return ("card-waiting", () => SetCard(CardKind.Idle, BridgeTexts.Setup.CardWaiting));
        yield return ("card-testing", () => SetCard(CardKind.Testing, BridgeTexts.Setup.CardTesting));
        yield return ("card-connected", () => SetCard(CardKind.Success, BridgeTexts.Setup.CardConnected(server, BridgeTexts.Setup.SteamAccount(steamId))));
        yield return ("card-connected-no-steam", () => SetCard(CardKind.Success, BridgeTexts.Setup.CardConnected(server, BridgeTexts.Setup.NoSteamAccount)));
        yield return ("card-token-invalid", () => SetCard(CardKind.Failure, BridgeTexts.Setup.CardTokenInvalid));
        yield return ("card-unreachable", () => SetCard(CardKind.Failure, BridgeTexts.Setup.CardUnreachable(BridgeTexts.Network.ConnectionError)));
        yield return ("card-rejected", () => SetCard(CardKind.Failure, BridgeTexts.Setup.CardRejected("HTTP 500")));

        // The longest answer a server really gives (409, ServerTexts.Bridge.OtherAccountDetail in the server project): the bridge
        // puts the server's "detail" into the card, in the language it asked for
        var conflict = BridgeLang.IsEnglish
            ? "HTTP 409: The bridge reports Steam account 76561198012345678, but 76561198087654321 is stored in the settings. " +
              "Sign in on the gaming PC with the stored account or change the SteamID in the settings."
            : "HTTP 409: Die Bridge meldet das Steam-Konto 76561198012345678, in den Einstellungen ist 76561198087654321 hinterlegt. " +
              "Melde dich am Spiele-PC mit dem hinterlegten Konto an oder ändere die SteamID in den Einstellungen.";
        yield return ("card-rejected-conflict", () => SetCard(CardKind.Failure, BridgeTexts.Setup.CardRejected(conflict)));
        yield return ("card-saving", () => SetCard(CardKind.Testing, BridgeTexts.Setup.Saving));
        yield return ("card-installing", () => SetCard(CardKind.Testing, BridgeTexts.Setup.Installing));
        yield return ("card-stopping", () => SetCard(CardKind.Testing, BridgeTexts.Setup.StoppingRunning));
        yield return ("card-wont-quit", () => SetCard(CardKind.Failure, BridgeTexts.Setup.Failure(BridgeTexts.Setup.MustEndFirst)));

        // Texts of unlimited length (a reason from the network stack or the server, whatever the user typed): the slots keep their size, show the
        // text COMPLETELY and scroll. States named "long-..." may scroll; the check then demands that the full text is in the slot and reachable.
        var reason = LayoutCheck.Repeat("connect ECONNREFUSED 192.168.178.10:5233 - the server did not answer within the time limit, no route to host. ", 2000);
        var unbroken = new string('x', 2000);
        var typed = LayoutCheck.Repeat("meinserver.example.org:5233/platinvault/pfad/lang ", 600);
        yield return ("long-card-rejected", () => { LayoutCheck.ExpectedFullText = reason; SetCard(CardKind.Failure, BridgeTexts.Setup.CardRejected(reason)); });
        yield return ("long-card-unreachable-unbroken", () => { LayoutCheck.ExpectedFullText = unbroken; SetCard(CardKind.Failure, BridgeTexts.Setup.CardUnreachable(unbroken)); });
        yield return ("long-card-failure", () => { LayoutCheck.ExpectedFullText = reason; SetCard(CardKind.Failure, BridgeTexts.Setup.Failure(Msg.Same(reason))); });
        yield return ("long-server-invalid", () => { LayoutCheck.ExpectedFullText = typed; ServerError(typed); });
        yield return ("long-server-invalid-unbroken", () => { LayoutCheck.ExpectedFullText = unbroken; ServerError(unbroken); });
        // Back to short texts: the slots show them again, in their normal size
        yield return ("after-long", () =>
        {
            ServerError("meinserver.example.org:5233/platinvault/pfad/lang");
            SetCard(CardKind.Success, BridgeTexts.Setup.CardConnected(server, BridgeTexts.Setup.SteamAccount(steamId)));
        });
    }

    /// <summary>The PDF next to the exe, else the handbook of the server typed in above (when that address is already valid).</summary>
    private void OpenHandbook()
    {
        var target = Handbook.FindTarget(_env, _valid?.ServerText);
        if (_env.Headless)
        {
            BridgeLog.Info($"Handbuch (Testlauf, nicht geöffnet): {target ?? "(nichts gefunden)"}");
            return;
        }

        if (target is null)
        {
            Dialogs.Info(BridgeTexts.Setup.HandbookMissing);
            return;
        }

        try
        {
            Handbook.Open(target);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Dialogs.Warning(BridgeTexts.Setup.HandbookFailed(ex.Message));
        }
    }

    /// <summary>
    /// The language written into bridge.json: the one the server named in the connection test (the installed copy then starts
    /// in the language of PlatinVault), else the one this run knows for sure (<see cref="BridgeLang.Remembered"/>, null while it is
    /// only the Windows default - the file must not freeze a guess).
    /// </summary>
    private string? LanguageToStore() => _serverLanguage ?? BridgeLang.Remembered;

    /// <summary>Test hook: the click on the primary button.</summary>
    internal Task InstallNowAsync() => OnPrimaryAsync();

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal SetupOutcome Outcome { get; private set; } = SetupOutcome.Cancelled;

    /// <summary>The settings that were installed or saved (valid when <see cref="Outcome"/> is not Cancelled).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal BridgeSettings? ChosenSettings => _valid;

    private static string SubtitleFor(SetupMode mode, string version, string? installedVersion) => (mode switch
    {
        SetupMode.Install => BridgeTexts.Setup.SubtitleInstall(version),
        SetupMode.Update => installedVersion is null
            ? BridgeTexts.Setup.SubtitleUpdate(version)
            : BridgeTexts.Setup.SubtitleUpdateFrom(installedVersion, version),
        _ => BridgeTexts.Setup.SubtitleSave(version),
    }).ToString();

    // ---- live validation: every keystroke, messages in reserved space ------------------------------------------------

    private void OnInputChanged(bool testImmediately = false)
    {
        _testCts?.Cancel();
        _lastTestUnauthorized = false;
        _valid = null;

        var serverText = _server.Text.Trim();
        var tokenText = _token.Text.Trim();

        Uri? serverUri = null;
        string? serverError = null;
        if (serverText.Length > 0 && !BridgeSettings.TryParseServer(serverText, out serverUri, out serverError)) { /* serverError set */ }

        string? tokenError = null;
        if (tokenText.Length > 0 && !BridgeSettings.IsValidToken(tokenText))
            tokenError = BridgeTexts.Setup.TokenBadChars;

        SetMessage(_serverMessage, serverError, BridgeTexts.Setup.ServerHint);
        SetMessage(_tokenMessage, tokenError, BridgeTexts.Setup.TokenHint);

        var complete = serverError is null && tokenError is null && serverUri is not null && tokenText.Length > 0;
        if (complete) _valid = new BridgeSettings(serverUri!, tokenText);

        Theme.SetEnabled(_primary, complete && !_busy, ButtonKind.Primary);

        if (!complete)
        {
            _debounce.Stop();
            SetCard(CardKind.Idle, BridgeTexts.Setup.CardIdle);
            return;
        }

        SetCard(CardKind.Idle, BridgeTexts.Setup.CardWaiting);
        if (testImmediately) StartConnectionTest();
        else
        {
            // Not on every keystroke: wait until the user stopped typing, then one request.
            _debounce.Stop();
            _debounce.Start();
        }
    }

    private static void SetMessage(ScrollableText slot, string? error, Msg hint) =>
        slot.SetText(error ?? hint, error is null ? Theme.Muted : Theme.Danger);

    // ---- connection test --------------------------------------------------------------------------------------------

    private enum CardKind { Idle, Testing, Success, Failure }

    // The reason comes from the network stack or the server and has no length limit. The card keeps its size and shows it COMPLETELY:
    // the text wraps and scrolls inside the card when it is longer than its four lines.
    private void SetCard(CardKind kind, string text) =>
        _cardText.SetText(text, kind switch
        {
            CardKind.Success => Theme.Success,
            CardKind.Failure => Theme.Danger,
            _ => Theme.Muted,
        });

    private void StartConnectionTest()
    {
        var settings = _valid;
        if (settings is null || _busy) return;

        _testCts?.Cancel();
        _testCts = new CancellationTokenSource();
        _ = RunConnectionTestAsync(settings, _testCts.Token);
    }

    private async Task RunConnectionTestAsync(BridgeSettings settings, CancellationToken ct)
    {
        SetCard(CardKind.Testing, BridgeTexts.Setup.CardTesting);

        SendResult result;
        string steamId;
        try
        {
            // Exactly what the running bridge would send first, so the test cannot confuse the server's bookkeeping.
            (result, steamId) = await Task.Run(async () =>
            {
                using var client = new BridgeClient(settings, _version);
                var observation = _sampler.Observe();
                var (payload, _) = await _sampler.BuildAsync(observation, 0, new HashSet<string>(), ct).ConfigureAwait(false);
                return (await client.SendAsync(payload, ct).ConfigureAwait(false), payload.SteamId64);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested || IsDisposed) return;

        _lastTestUnauthorized = result.Outcome == SendOutcome.Unauthorized;
        switch (result.Outcome)
        {
            case SendOutcome.Ok:
                // The server names its language in the answer: the installed copy starts in it (written into bridge.json)
                _serverLanguage = BridgeLang.Normalize(result.Reply?.UiLanguage) ?? _serverLanguage;
                var steam = steamId.Length > 0 ? BridgeTexts.Setup.SteamAccount(steamId) : BridgeTexts.Setup.NoSteamAccount;
                SetCard(CardKind.Success, BridgeTexts.Setup.CardConnected(settings.ServerText, steam));
                break;
            case SendOutcome.Unauthorized:
                SetCard(CardKind.Failure, BridgeTexts.Setup.CardTokenInvalid);
                break;
            case SendOutcome.Transient:
                SetCard(CardKind.Failure, BridgeTexts.Setup.CardUnreachable(result.Reason));
                break;
            default:
                SetCard(CardKind.Failure, BridgeTexts.Setup.CardRejected(result.Reason));
                break;
        }
    }

    // ---- install / save ---------------------------------------------------------------------------------------------

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        _server.Enabled = !busy;
        _token.Enabled = !busy;
        _autostart.Enabled = !busy;
        _startMenu.Enabled = !busy;
        Theme.SetEnabled(_primary, !busy && _valid is not null, ButtonKind.Primary);
        _cancel.Enabled = !busy;
        if (message is not null) SetCard(CardKind.Testing, message);
    }

    private async Task OnPrimaryAsync()
    {
        var settings = _valid;
        if (settings is null || _busy) return;

        if (_lastTestUnauthorized)
        {
            var answer = Dialogs.Show(this, BridgeTexts.Setup.AskTokenRejected,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, DialogResult.Yes, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        _debounce.Stop();
        _testCts?.Cancel();
        SetBusy(true, _mode == SetupMode.SaveOnly ? BridgeTexts.Setup.Saving : BridgeTexts.Setup.Installing);

        try
        {
            if (_mode == SetupMode.SaveOnly)
            {
                await Task.Run(() => SaveConfig(settings));
                Outcome = SetupOutcome.Saved;
            }
            else
            {
                if (!await StopRunningBridgeAsync()) { SetBusy(false); OnInputChanged(); return; }

                var options = new InstallOptions(settings, _autostart.Checked, _startMenu.Checked, _version, LanguageToStore());
                var installedExe = await Task.Run(() => Installer.Install(_env, options));
                StartInstalledCopy(installedExe);
                Outcome = SetupOutcome.Installed;
            }
        }
        catch (InstallException ex)
        {
            BridgeLog.Error(ex.Message);
            SetBusy(false);
            SetCard(CardKind.Failure, BridgeTexts.Setup.Failure(Msg.Same(ex.Message)));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            BridgeLog.Error(ex.Message);
            SetBusy(false);
            SetCard(CardKind.Failure, BridgeTexts.Setup.Failure(Msg.Same(ex.Message)));
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SaveConfig(BridgeSettings settings)
    {
        // Next to the exe if possible (portable and installed copies read it first); otherwise the per-user file.
        var primary = Path.Combine(BridgeEnvironment.ExeDirectory, "bridge.json");
        try
        {
            BridgeConfigFile.Save(primary, settings, LanguageToStore());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            BridgeLog.Warn(BridgeTexts.Setup.SaveFallback(primary, ex.Message, _env.UserConfigPath));
            try { BridgeConfigFile.Save(_env.UserConfigPath, settings, LanguageToStore()); }
            catch (Exception inner) when (inner is UnauthorizedAccessException or IOException)
            {
                throw new InstallException(BridgeTexts.Setup.SaveFailed(inner.Message), inner);
            }
        }
    }

    /// <summary>Ends a running bridge (new build: quit event; old build: after confirmation by force). False = user cancelled/failed.</summary>
    private async Task<bool> StopRunningBridgeAsync()
    {
        if (!_guard.IsAnotherInstanceRunning()) return true;

        SetCard(CardKind.Testing, BridgeTexts.Setup.StoppingRunning);
        if (await Task.Run(() => _guard.RequestQuitAndWait(TimeSpan.FromSeconds(8)))) return true;

        // The running bridge did not react: an old build without windows or quit handling.
        if (_env.IsTest)
        {
            SetCard(CardKind.Failure, BridgeTexts.Setup.Failure(BridgeTexts.Setup.RunningWontQuitTest));
            return false;
        }

        var answer = Dialogs.Show(this, BridgeTexts.Setup.AskKillOld,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, DialogResult.No);
        if (answer != DialogResult.Yes)
        {
            SetCard(CardKind.Failure, BridgeTexts.Setup.Failure(BridgeTexts.Setup.MustEndFirst));
            return false;
        }

        await Task.Run(() => { InstanceGuard.KillOtherInstances(); return _guard.RequestQuitAndWait(TimeSpan.FromSeconds(4)); });
        return true;
    }

    private void StartInstalledCopy(string installedExe)
    {
        var startInfo = new ProcessStartInfo(installedExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installedExe)!,
        };
        startInfo.ArgumentList.Add("--first-run");
        foreach (var argument in _env.InheritedArguments(_cli)) startInfo.ArgumentList.Add(argument);

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InstallException(BridgeTexts.Setup.StartFailed(ex.Message), ex);
        }
    }
}
