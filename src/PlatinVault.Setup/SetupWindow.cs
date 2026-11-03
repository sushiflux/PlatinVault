using System.Text.RegularExpressions;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Setup;

/// <summary>
/// The designed installer: Sprache/Language → Lizenz/Licence → Datenbank/Database → Ort &amp; Autostart/Location &amp; autostart →
/// Installieren/Installing (real progress, database setup included) → Fertig/Done. An update (an installation exists) skips database
/// and location. Every message has its room from the start (rigid layout); errors are shown completely in a scrolling slot.
/// </summary>
internal sealed partial class SetupWindow : WizardWindow
{
    private readonly SetupState _state;
    private readonly Func<InstallRequest, IProgress<InstallProgress>, Task> _install;
    private readonly InstallingPage _installing;
    private readonly DonePage _done;
    private readonly LocationPage? _location;
    private bool _running;

    public SetupWindow(SetupState state, Func<InstallRequest, IProgress<InstallProgress>, Task> install, float? forcedScale = null)
        : this(state, install, BuildPages(state), forcedScale)
    {
    }

    private SetupWindow(SetupState state, Func<InstallRequest, IProgress<InstallProgress>, Task> install, IReadOnlyList<WizardPage> pages, float? forcedScale)
        : base(SetupTexts.WindowTitle, state.IsUpdate ? SetupTexts.SideUpdate(state.InstalledVersion!) : SetupTexts.SideInstall, state.Version, pages, forcedScale)
    {
        _state = state;
        _install = install;
        _location = pages.OfType<LocationPage>().SingleOrDefault();
        _installing = pages.OfType<InstallingPage>().Single();
        _done = pages.OfType<DonePage>().Single();
        if (_location is not null) _location.Changed = UpdateButtons;
        foreach (var page in pages.OfType<LanguagePage>()) page.Changed = UpdateButtons;

        Next.Click += async (_, _) => await OnNextAsync();
        Back.Click += (_, _) => GoTo(StepIndex - 1);
        Cancel.Click += (_, _) => OnCloseRequested();
        StepChanged += (_, _) => UpdateButtons();
        GoTo(0);
    }

    private static IReadOnlyList<WizardPage> BuildPages(SetupState state)
    {
        var pages = new List<WizardPage> { new LanguagePage(state), new LicencePage(state) };
        if (!state.IsUpdate)
        {
            pages.Add(new DatabasePage());
            pages.Add(new LocationPage(state));
        }
        pages.Add(new InstallingPage(state));
        pages.Add(new DonePage(state));
        return pages;
    }

    /// <summary>The user wants to launch PlatinVault after "Finish".</summary>
    public bool LaunchAfterFinish => _state.Succeeded && _state.LaunchNow;

    protected override void LanguageChanged() => UpdateButtons();

    private void UpdateButtons()
    {
        var page = CurrentPage;
        Back.Text = CommonTexts.Back;
        Cancel.Text = CommonTexts.Cancel;
        Back.Visible = StepIndex > 0 && page is not InstallingPage and not DonePage;
        Cancel.Visible = page is not InstallingPage and not DonePage;
        Next.Kind = PvButtonKind.Primary;
        Next.Visible = true;
        switch (page)
        {
            case LicencePage:
                Next.Text = _state.IsUpdate ? SetupTexts.Update : CommonTexts.Next;
                // The AGPL is not a contract to accept (using PlatinVault needs no consent); only a broken package stops here
                Next.Enabled = _state.LicenceText is not null;
                break;
            case LocationPage:
                Next.Text = SetupTexts.Install;
                Next.Enabled = _location!.Problem is null;
                break;
            case InstallingPage:
                Next.Text = _state.Failed ? CommonTexts.Retry : SetupTexts.Install;
                // While it runs there is nothing to press; after a failure "Try again"
                Next.Visible = _state.Failed;
                Next.Enabled = _state.Failed && !_running;
                Cancel.Visible = _state.Failed;
                Cancel.Text = CommonTexts.Close;
                break;
            case DonePage:
                Next.Text = SetupTexts.Finish;
                Next.Enabled = true;
                break;
            default:
                Next.Text = CommonTexts.Next;
                Next.Enabled = true;
                break;
        }
        Back.Invalidate();
        Next.Invalidate();
        Cancel.Invalidate();
    }

    private async Task OnNextAsync()
    {
        switch (CurrentPage)
        {
            case DonePage:
                Close();
                return;
            case InstallingPage when _state.Failed:
                await RunInstallAsync();
                return;
            default:
                if (StepIndex + 1 == Steps - 2)
                {
                    GoTo(StepIndex + 1);
                    await RunInstallAsync();
                }
                else
                {
                    GoTo(StepIndex + 1);
                }
                return;
        }
    }

    private int Steps => _state.IsUpdate ? 4 : 6;

    /// <summary>Runs the installation with the window's choices (also the headless/test path).</summary>
    public async Task RunInstallAsync()
    {
        _running = true;
        _state.Failed = false;
        _state.Error = null;
        _installing.Reset();
        RebuildPage();
        UpdateButtons();
        var progress = new Progress<InstallProgress>(p => _installing.Report(p));
        try
        {
            await _install(_state.Request(), progress);
            _state.Succeeded = true;
            _running = false;
            GoTo(Steps - 1);
        }
        catch (InstallException ex)
        {
            _state.Failed = true;
            _state.Error = ex.Text;
            _running = false;
            RebuildPage();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            _state.Failed = true;
            _state.Error = new Msg(ex.Message, ex.Message);
            _running = false;
            RebuildPage();
        }
        UpdateButtons();
    }

    protected override void OnCloseRequested()
    {
        if (_running) return; // copying and the database setup are not interrupted halfway
        if (_state.Succeeded || _state.Failed || Headless)
        {
            Close();
            return;
        }
        var answer = Dialogs.Show(this, DialogKind.Question, SetupTexts.CancelQuestion,
            [new DialogButton(SetupTexts.CancelYes, DialogResult.Yes), new DialogButton(SetupTexts.CancelNo, DialogResult.No, PvButtonKind.Primary)],
            DialogResult.No, focusIndex: 1);
        if (answer == DialogResult.Yes) Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }

    /// <summary>Every state of every page, for the offscreen pictures of the layout check.</summary>
    public IEnumerable<(string Name, Action Show)> TestStates()
    {
        var installing = Steps - 2;
        var original = _state.InstallDir;
        for (var i = 0; i < installing; i++)
        {
            var index = i;
            yield return ($"step{index + 1}", () => { GoTo(index); UpdateButtons(); });
        }
        if (!_state.IsUpdate)
        {
            yield return ("step4-unsafe-folder", () => { _location!.SetFolder(@"C:\"); GoTo(3); UpdateButtons(); });
            yield return ("step4-ok", () => { _location!.SetFolder(original); GoTo(3); UpdateButtons(); });
        }
        yield return ("installing", () =>
        {
            GoTo(installing);
            _running = true;
            _installing.Reset();
            _installing.Report(new InstallProgress(0.72f, InstallTask.Database, new Msg("Tabellen werden angelegt …", "Creating the tables …")));
            UpdateButtons();
        });
        yield return ("failed", () =>
        {
            _running = false;
            _state.Failed = true;
            _state.Error = SetupTexts.DatabaseFailed(string.Concat(Enumerable.Repeat(
                @"SQLite Error 13: 'database or disk is full' while writing C:\Users\Beispiel\AppData\Local\PlatinVault\database\platinvault.db. ", 12)));
            GoTo(installing);
            UpdateButtons();
        });
        // The same page after a click on "Copy": the status slot fills, nothing else may move (compared by the layout check)
        yield return ("failed-copied", () => Controls.Find("btnCopyInstallError", true).OfType<PvButton>().Single().PerformClick());
        yield return ("failed-copy-not-possible", () =>
        {
            var status = FindCopyStatus();
            status.Color = p => p.Danger;
            status.Text = CommonTexts.CopyFailed;
        });
        yield return ("done", () =>
        {
            _state.Failed = false;
            _state.Succeeded = true;
            GoTo(installing + 1);
            UpdateButtons();
        });
    }

    private PvText FindCopyStatus() => Controls.Find("lblCopyStatusInstallError", true).OfType<PvText>().Single();

    // ---- pages -------------------------------------------------------------------------------------------------------------------

    private sealed class LanguagePage(SetupState state) : WizardPage
    {
        public Action? Changed { get; set; }

        public override Msg StepLabel => SetupTexts.StepLanguage;
        public override Msg Kicker => state.IsUpdate ? SetupTexts.Update : SetupTexts.LanguageKicker;
        public override Msg Title => state.IsUpdate ? SetupTexts.LanguageUpdateTitle : SetupTexts.LanguageTitle;
        public override Msg Subtitle => state.IsUpdate ? SetupTexts.LanguageUpdateSubtitle(state.InstalledVersion!, state.Version) : SetupTexts.LanguageSubtitle;

        public override void Build(PageCanvas c)
        {
            var german = c.Add(new PvChoice { Text = "Deutsch", Description = SetupTexts.GermanText.De, Badge = "DE", Selected = UiLang.Current == UiLang.German }, 0, 6, PageCanvas.Width, 76, "choiceGerman");
            var english = c.Add(new PvChoice { Text = "English", Description = SetupTexts.EnglishText.En, Badge = "EN", Selected = UiLang.Current == UiLang.English }, 0, 92, PageCanvas.Width, 76, "choiceEnglish");
            // Switching re-renders the whole window in the chosen language at once
            german.Click += (_, _) => { UiLang.Current = UiLang.German; Changed?.Invoke(); };
            english.Click += (_, _) => { UiLang.Current = UiLang.English; Changed?.Invoke(); };

            var y = 196;
            c.Add(new PvFeature { Text = SetupTexts.FeatureLive, Icon = Glyphs.Bolt, Compact = true, Tint = p => (p.GoldSoft, p.Gold) }, 0, y, PageCanvas.Width, 32, "featureLive");
            c.Add(new PvFeature { Text = SetupTexts.FeatureGuides, Icon = Glyphs.Book, Compact = true }, 0, y + 44, PageCanvas.Width, 32, "featureGuides");
            c.Add(new PvFeature { Text = SetupTexts.FeatureLocal, Icon = Glyphs.Home, Compact = true, Tint = p => (p.EmeraldSoft, p.Emerald) }, 0, y + 88, PageCanvas.Width, 32, "featureLocal");
        }
    }

    private sealed class LicencePage(SetupState state) : WizardPage
    {
        public override Msg StepLabel => SetupTexts.StepLicence;
        public override Msg Kicker => SetupTexts.LicenceKicker;
        public override Msg Title => SetupTexts.LicenceTitle;
        public override Msg Subtitle => SetupTexts.LicenceSubtitle;

        public override void Build(PageCanvas c)
        {
            // The official GNU AGPL v3 text (English only, like every official GNU licence text). Nothing to tick: the licence grants
            // rights and needs no consent to use the program
            var box = c.Add(new PvScrollText { Framed = true, Points = 9f }, 0, 0, PageCanvas.Width, 118, "txtLicence");
            if (state.LicenceText is { } text) box.SetText(LicenceForDisplay(text), p => p.TextSecondary);
            else box.SetText(SetupTexts.LicenceMissing, p => p.Danger);
            c.Text(SetupTexts.LicenceSource, 0, 128, PageCanvas.Width, 34, 9f, color: p => p.Muted, name: "lblLicenceSource");
            // Which connections PlatinVault makes by itself and how to switch them off - shown during every installation and update
            c.Text(SetupTexts.PrivacyTitle, 0, 174, PageCanvas.Width, 20, 9.5f, FontStyle.Bold, color: p => p.Text, name: "lblPrivacyTitle");
            c.Text(SetupTexts.PrivacyText, 0, 196, PageCanvas.Width, 142, 9f, color: p => p.TextSecondary, name: "lblPrivacy");
        }
    }

    private sealed class DatabasePage : WizardPage
    {
        public override Msg StepLabel => SetupTexts.StepDatabase;
        public override Msg Kicker => SetupTexts.DatabaseKicker;
        public override Msg Title => SetupTexts.DatabaseTitle;
        public override Msg Subtitle => SetupTexts.DatabaseSubtitle;

        public override void Build(PageCanvas c)
        {
            c.Add(new PvFeature { Text = SetupTexts.DbLocalTitle, Description = SetupTexts.DbLocalText, Icon = Glyphs.Database }, 0, 6, PageCanvas.Width, 82, "featureDbLocal");
            c.Add(new PvFeature { Text = SetupTexts.DbSecretTitle, Description = SetupTexts.DbSecretText, Icon = Glyphs.Lock, Tint = p => (p.GoldSoft, p.Gold) }, 0, 102, PageCanvas.Width, 82, "featureDbSecret");
            c.Add(new PvFeature { Text = SetupTexts.DbBackupTitle, Description = SetupTexts.DbBackupText, Icon = Glyphs.Clock, Tint = p => (p.EmeraldSoft, p.Emerald) }, 0, 198, PageCanvas.Width, 82, "featureDbBackup");
        }
    }

    private sealed class LocationPage(SetupState state) : WizardPage
    {
        private PvText? _path;
        private PvScrollText? _problemText;

        public Action? Changed { get; set; }

        public Msg? Problem { get; private set; } = InstallEngine.CheckFolder(state.InstallDir);

        public override Msg StepLabel => SetupTexts.StepLocation;
        public override Msg Kicker => SetupTexts.LocationKicker;
        public override Msg Title => SetupTexts.LocationTitle;
        public override Msg Subtitle => SetupTexts.LocationSubtitle;

        public void SetFolder(string folder)
        {
            state.InstallDir = folder;
            Problem = InstallEngine.CheckFolder(folder);
            if (_path is not null) _path.Text = folder;
            _problemText?.SetText(ProblemText(), p => Problem is null ? p.Muted : p.Danger);
            Changed?.Invoke();
        }

        private string ProblemText()
        {
            if (Problem is { } problem) return problem;
            var needed = state.PayloadSize * 2 + 300L * 1024 * 1024;
            var free = InstallEngine.FreeSpace(state.InstallDir);
            return SetupTexts.FolderSpace(InstallEngine.Size(needed), free is { } f ? InstallEngine.Size(f) : "?");
        }

        public override void Build(PageCanvas c)
        {
            c.Add(new FolderCard(), 0, 0, PageCanvas.Width, 64, "cardFolder");
            c.Text(SetupTexts.ProgramFolder, 58, 10, 300, 20, 9f, FontStyle.Bold, p => p.Muted, "lblFolderTitle").Surface = c.P.Surface;
            _path = c.Text(state.InstallDir, 58, 30, PageCanvas.Width - 58 - 132, 26, 9.5f, color: p => p.Text, name: "lblFolder");
            _path.Surface = c.P.Surface;
            var change = c.Add(new PvButton { Text = SetupTexts.ChangeFolder, Kind = PvButtonKind.Secondary }, PageCanvas.Width - 122, 14, 110, 36, "btnChangeFolder");
            change.Surface = c.P.Surface;
            change.Click += (_, _) => ChooseFolder(c.Host);
            // One reserved slot below the card: free space or what is wrong with the folder (scrolls if a path makes it long)
            _problemText = c.Add(new PvScrollText { Points = 9f }, 4, 70, PageCanvas.Width - 4, 36, "txtFolderProblem");
            _problemText.SetText(ProblemText(), p => Problem is null ? p.Muted : p.Danger);

            var auto = c.Add(new PvToggle { Text = SetupTexts.AutostartTitle, Description = SetupTexts.AutostartText, Checked = state.Autostart }, 0, 112, PageCanvas.Width, 72, "tglAutostart");
            auto.CheckedChanged += (_, _) => state.Autostart = auto.Checked;
            var menu = c.Add(new PvToggle { Text = SetupTexts.StartMenuTitle, Checked = state.StartMenu }, 0, 194, (PageCanvas.Width - 12) / 2, 48, "tglStartMenu");
            menu.CheckedChanged += (_, _) => state.StartMenu = menu.Checked;
            var desktop = c.Add(new PvToggle { Text = SetupTexts.DesktopTitle, Checked = state.DesktopShortcut }, (PageCanvas.Width + 12) / 2, 194, (PageCanvas.Width - 12) / 2, 48, "tglDesktop");
            desktop.CheckedChanged += (_, _) => state.DesktopShortcut = desktop.Checked;
            c.Text(SetupTexts.DataFolder(state.DataDir), 2, 256, PageCanvas.Width - 4, 52, 8.5f, color: p => p.Muted, name: "lblDataFolder");
        }

        private void ChooseFolder(Control owner)
        {
            // The Windows folder picker is the one system dialog that stays (users expect it)
            using var dialog = new FolderBrowserDialog { InitialDirectory = Path.GetDirectoryName(state.InstallDir) ?? state.InstallDir, ShowNewFolderButton = true, UseDescriptionForTitle = true, Description = SetupTexts.ProgramFolder };
            if (dialog.ShowDialog(owner.FindForm()) != DialogResult.OK) return;
            var chosen = dialog.SelectedPath;
            // A chosen parent folder gets the program's own subfolder, so nothing is ever installed straight into "Documents" or a drive
            if (!string.Equals(Path.GetFileName(chosen.TrimEnd('\\')), "PlatinVault", StringComparison.OrdinalIgnoreCase))
                chosen = Path.Combine(chosen, "PlatinVault");
            SetFolder(chosen);
        }

        private sealed class FolderCard : PvControl
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Draw.Smooth(g);
                var card = new RectangleF(U(0.5f), U(0.5f), Width - U(1), Height - U(1));
                Draw.FillRounded(g, card, U(8), P.Surface);
                Draw.StrokeRounded(g, card, U(8), P.Border, Math.Max(1f, U(1)));
                var square = new RectangleF(U(12), (Height - U(34)) / 2f, U(34), U(34));
                Draw.FillRounded(g, square, U(7), P.AccentSoft);
                Glyphs.Folder(g, RectangleF.Inflate(square, -U(7), -U(7)), P.Accent);
            }
        }
    }

    private sealed class InstallingPage(SetupState state) : WizardPage
    {
        private PvProgress? _bar;
        private PvText? _status;
        private PvText? _percent;
        private readonly Dictionary<InstallTask, TaskRow> _rows = [];
        private InstallProgress _last = new(0f, InstallTask.Prepare, SetupTexts.TaskPrepare);

        public override Msg StepLabel => SetupTexts.StepInstall;
        public override Msg Kicker => SetupTexts.InstallKicker;
        public override Msg Title => state.Failed ? SetupTexts.FailedTitle : state.IsUpdate ? SetupTexts.UpdateTitle : SetupTexts.InstallTitle;
        public override Msg Subtitle => state.Failed ? SetupTexts.FailedSubtitle : SetupTexts.InstallSubtitle;

        public void Reset() => _last = new InstallProgress(0f, InstallTask.Prepare, SetupTexts.TaskPrepare);

        public void Report(InstallProgress p)
        {
            _last = p;
            if (_bar is not null) _bar.Value = p.Value;
            if (_status is not null) _status.Text = p.Text;
            if (_percent is not null) _percent.Text = $"{p.Value * 100:0} %";
            foreach (var (task, row) in _rows)
            {
                row.State = task < p.Task || p.Value >= 1f ? RowState.Done : task == p.Task ? RowState.Active : RowState.Pending;
                row.Invalidate();
            }
        }

        public override void Build(PageCanvas c)
        {
            _rows.Clear();
            _bar = c.Add(new PvProgress { Value = _last.Value, Running = !state.Failed, Failed = state.Failed }, 0, 6, PageCanvas.Width, 10, "progress");
            _status = c.Text(_last.Text, 0, 26, PageCanvas.Width - 80, 24, 9.5f, color: p => p.TextSecondary, name: "lblStatus");
            _percent = c.Text($"{_last.Value * 100:0} %", PageCanvas.Width - 72, 26, 72, 24, 9.5f, FontStyle.Bold, p => p.Text, "lblPercent");
            _percent.Align = TextFormatFlags.Right;

            if (state.Failed && state.Error is { } error)
            {
                // The complete reason, scrolling inside its slot; it replaces the step list (a different page state, not a message)
                // (selectable; the Copy button copies all of it plus version and time)
                c.AddErrorBox(() => error, p => p.Danger, 0, 62, PageCanvas.Width, 176, "txtInstallError");
                return;
            }

            var tasks = new List<(InstallTask Task, Msg Text)> { (InstallTask.Prepare, SetupTexts.TaskPrepare) };
            if (state.IsUpdate) tasks.Add((InstallTask.Stop, SetupTexts.TaskStop));
            tasks.Add((InstallTask.Files, SetupTexts.TaskFiles));
            tasks.Add((InstallTask.WebView, SetupTexts.TaskWebView));
            tasks.Add((InstallTask.Database, SetupTexts.TaskDatabase));
            tasks.Add((InstallTask.Shortcuts, SetupTexts.TaskShortcuts));
            var y = 64;
            foreach (var (task, text) in tasks)
            {
                _rows[task] = c.Add(new TaskRow { Text = text }, 0, y, PageCanvas.Width, 30, "row" + task);
                y += 36;
            }
            Report(_last);
        }
    }

    private enum RowState { Pending, Active, Done }

    private sealed class TaskRow : PvControl
    {
        public RowState State { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var dot = new RectangleF(U(1), (Height - U(22)) / 2f, U(22), U(22));
            switch (State)
            {
                case RowState.Done:
                    using (var b = new SolidBrush(P.EmeraldSoft)) g.FillEllipse(b, dot);
                    Draw.Check(g, RectangleF.Inflate(dot, -U(5), -U(5)), P.Emerald, Math.Max(1.5f, U(2)));
                    break;
                case RowState.Active:
                    Brand.PaintGlow(g, RectangleF.Inflate(dot, U(6), U(6)), P.Accent, 70);
                    using (var b = new SolidBrush(P.Accent)) g.FillEllipse(b, RectangleF.Inflate(dot, -U(6), -U(6)));
                    using (var pen = new Pen(P.Accent, Math.Max(1f, U(1.5f)))) g.DrawEllipse(pen, RectangleF.Inflate(dot, -U(1), -U(1)));
                    break;
                default:
                    using (var pen = new Pen(P.Border, Math.Max(1f, U(1.5f)))) g.DrawEllipse(pen, RectangleF.Inflate(dot, -U(1), -U(1)));
                    break;
            }
            Draw.Text(g, Text, Fonts.Get(10f, S, State == RowState.Active ? FontStyle.Bold : FontStyle.Regular),
                State == RowState.Pending ? P.Muted : P.Text, new Rectangle((int)U(34), 0, Width - (int)U(34), Height), TextFormatFlags.VerticalCenter);
        }
    }

    private sealed class DonePage(SetupState state) : WizardPage
    {
        public override Msg StepLabel => SetupTexts.StepDone;
        public override Msg Kicker => SetupTexts.DoneKicker;
        public override Msg Title => state.IsUpdate ? SetupTexts.DoneUpdatedTitle : SetupTexts.DoneTitle;
        public override Msg Subtitle => SetupTexts.DoneSubtitle;

        public override void Build(PageCanvas c)
        {
            c.Add(new PvFeature { Text = SetupTexts.TipTrayTitle, Description = SetupTexts.TipTrayText, Icon = Glyphs.Home, Tint = p => (p.AccentSoft, p.Accent) }, 0, 6, PageCanvas.Width, 70, "tipTray");
            c.Add(new PvFeature { Text = SetupTexts.TipHotkeyTitle, Description = SetupTexts.TipHotkeyText, Icon = Glyphs.Bolt, Tint = p => (p.GoldSoft, p.Gold) }, 0, 90, PageCanvas.Width, 70, "tipHotkey");
            var launch = c.Add(new PvToggle { Text = SetupTexts.LaunchNow, Checked = state.LaunchNow }, 0, 186, PageCanvas.Width, 52, "tglLaunch");
            launch.CheckedChanged += (_, _) => state.LaunchNow = launch.Checked;
        }
    }

    /// <summary>
    /// The plain licence text for the scrolling box: the hard line breaks of the official text (about 70 characters) are joined within a
    /// paragraph, so the box wraps it to its own width. Blank lines stay paragraph breaks (one at most); a line indented by eight or more
    /// spaces (the centred title lines) stays a line of its own.
    /// </summary>
    public static string LicenceForDisplay(string text)
    {
        var paragraphs = new List<string>();
        var current = new List<string>();
        void Flush()
        {
            if (current.Count > 0) paragraphs.Add(string.Join(' ', current));
            current.Clear();
        }
        foreach (var raw in text.Replace("\r\n", "\n").TrimStart('﻿').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                Flush();
                if (paragraphs.Count > 0 && paragraphs[^1].Length > 0) paragraphs.Add(string.Empty);
                continue;
            }
            var centred = CentredLine().IsMatch(line);
            if (centred) Flush();
            current.Add(line.Trim());
            if (centred) Flush();
        }
        Flush();
        return string.Join('\n', paragraphs).Trim();
    }

    [GeneratedRegex(@"^ {8,}\S")]
    private static partial Regex CentredLine();
}

/// <summary>What the installer knows and what the user chose (shared by the pages).</summary>
internal sealed class SetupState
{
    public required string Version { get; init; }
    public string? InstalledVersion { get; init; }
    public bool IsUpdate => InstalledVersion is not null;
    public string? LicenceText { get; init; }
    public long PayloadSize { get; init; }
    public required string InstallDir { get; set; }
    public required string DataDir { get; init; }
    public bool Autostart { get; set; } = true;
    public bool StartMenu { get; set; } = true;
    public bool DesktopShortcut { get; set; }
    public bool LaunchNow { get; set; } = true;
    public bool Failed { get; set; }
    public bool Succeeded { get; set; }
    public Msg? Error { get; set; }

    public InstallRequest Request() => new(InstallDir, Autostart, StartMenu, DesktopShortcut, UiLang.Current);
}
