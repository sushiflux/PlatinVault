using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// The designed uninstaller (started from Windows "Apps" with <c>PlatinVault.exe --uninstall</c>): asks "keep data?" with two cards
/// (keep is preselected), removes with real progress and lists at the end what was done. Never a Windows message box.
/// </summary>
public sealed class UninstallWindow : WizardWindow
{
    private readonly ConfirmPage _confirm;
    private readonly RemovePage _remove;
    private readonly DonePage _done;
    private readonly Func<bool, IProgress<StepProgress>, IReadOnlyList<Msg>> _run;

    public UninstallWindow(InstallLayout layout, string version, Func<bool, IProgress<StepProgress>, IReadOnlyList<Msg>> run, float? forcedScale = null)
        : this(new ConfirmPage(layout.DataDir), new RemovePage(), new DonePage(), version, run, forcedScale)
    {
    }

    private UninstallWindow(ConfirmPage confirm, RemovePage remove, DonePage done, string version,
        Func<bool, IProgress<StepProgress>, IReadOnlyList<Msg>> run, float? forcedScale)
        : base(UninstallTexts.WindowTitle, UninstallTexts.SideSubtitle, version, [confirm, remove, done], forcedScale)
    {
        _confirm = confirm;
        _remove = remove;
        _done = done;
        _run = run;
        _confirm.Changed = UpdateButtons;
        Next.Click += async (_, _) => await OnNextAsync();
        Cancel.Click += (_, _) => Close();
        Back.Visible = false;
        GoTo(0);
        UpdateButtons();
    }

    public bool Finished { get; private set; }

    protected override void LanguageChanged() => UpdateButtons();

    private void UpdateButtons()
    {
        Cancel.Text = CommonTexts.Cancel;
        switch (StepIndex)
        {
            case 0:
                Next.Text = _confirm.KeepData ? UninstallTexts.ButtonUninstall : UninstallTexts.ButtonDeleteAll;
                Next.Kind = _confirm.KeepData ? PvButtonKind.Primary : PvButtonKind.Danger;
                Next.Enabled = true;
                Cancel.Visible = true;
                break;
            case 1:
                Next.Text = UninstallTexts.ButtonUninstall;
                Next.Enabled = false;
                Cancel.Visible = false;
                break;
            default:
                Next.Text = CommonTexts.Close;
                Next.Kind = PvButtonKind.Primary;
                Next.Enabled = true;
                Cancel.Visible = false;
                break;
        }
        Next.Invalidate();
    }

    private async Task OnNextAsync()
    {
        if (StepIndex == 0) await RunUninstallAsync(_confirm.KeepData);
        else if (StepIndex == 2) Close();
    }

    /// <summary>Runs the removal (also used by the headless test path).</summary>
    public async Task RunUninstallAsync(bool keepData)
    {
        GoTo(1);
        UpdateButtons();
        var progress = new Progress<StepProgress>(p => _remove.Report(p));
        try
        {
            var done = await Task.Run(() => _run(keepData, progress));
            _done.SetResult(done, null);
            Finished = true;
        }
        catch (InstallException ex)
        {
            _done.SetResult([], ex.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _done.SetResult([], new Msg(ex.Message, ex.Message));
        }
        GoTo(2);
        UpdateButtons();
    }

    /// <summary>Test states for the offscreen pictures.</summary>
    public IEnumerable<(string Name, Action Show)> TestStates()
    {
        yield return ("confirm-keep", () => { GoTo(0); _confirm.Select(true); UpdateButtons(); });
        yield return ("confirm-delete", () => { GoTo(0); _confirm.Select(false); UpdateButtons(); });
        yield return ("removing", () => { GoTo(1); _remove.Report(new StepProgress(0.55f, UninstallTexts.ProgressDeleteData)); UpdateButtons(); });
        yield return ("done", () =>
        {
            _done.SetResult([InstallTexts.DoneStopped, InstallTexts.DoneEntries, InstallTexts.DoneDataKept(@"C:\Users\Beispiel\AppData\Local\PlatinVault"), InstallTexts.DoneProgramScheduled], null);
            GoTo(2);
            UpdateButtons();
        });
        yield return ("failed", () =>
        {
            _done.SetResult([], InstallTexts.DataNotDeleted(@"C:\Users\Beispiel\AppData\Local\PlatinVault\database",
                string.Concat(Enumerable.Repeat("The process cannot access the file 'base\\16384\\2619' because it is being used by another process. ", 8))));
            GoTo(2);
            UpdateButtons();
        });
    }

    private sealed class ConfirmPage(string dataDir) : WizardPage
    {
        private PvChoice? _keep;
        private PvChoice? _delete;

        public bool KeepData { get; private set; } = true;

        public Action? Changed { get; set; }

        public override Msg StepLabel => UninstallTexts.StepConfirm;
        public override Msg Kicker => UninstallTexts.ConfirmKicker;
        public override Msg Title => UninstallTexts.ConfirmTitle;
        public override Msg Subtitle => UninstallTexts.ConfirmSubtitle;

        public void Select(bool keep)
        {
            KeepData = keep;
            if (_keep is not null) _keep.Selected = keep;
            if (_delete is not null) _delete.Selected = !keep;
            Changed?.Invoke();
        }

        public override void Build(PageCanvas c)
        {
            _keep = c.Add(new PvChoice
            {
                Text = UninstallTexts.KeepTitle, Description = UninstallTexts.KeepText, Badge = "✓",
                BadgeColors = p => (p.EmeraldSoft, p.Emerald), Selected = KeepData,
            }, 0, 8, PageCanvas.Width, 96, "choiceKeep");
            _delete = c.Add(new PvChoice
            {
                Text = UninstallTexts.DeleteTitle, Description = UninstallTexts.DeleteText, Badge = "✕",
                BadgeColors = p => (p.DangerSoft, p.Danger), Selected = !KeepData,
            }, 0, 118, PageCanvas.Width, 96, "choiceDelete");
            _keep.Click += (_, _) => Select(true);
            _delete.Click += (_, _) => Select(false);
            c.Text(UninstallTexts.DataLocation(dataDir), 2, 228, PageCanvas.Width - 4, 72, 9f, color: p => p.Muted, name: "lblDataDir");
        }
    }

    private sealed class RemovePage : WizardPage
    {
        private PvProgress? _bar;
        private PvText? _status;
        private StepProgress _last = new(0f, UninstallTexts.ProgressStopping);

        public override Msg StepLabel => UninstallTexts.StepRemove;
        public override Msg Kicker => UninstallTexts.RemoveKicker;
        public override Msg Title => UninstallTexts.RemoveTitle;
        public override Msg Subtitle => UninstallTexts.RemoveSubtitle;

        public void Report(StepProgress progress)
        {
            _last = progress;
            if (_bar is not null) _bar.Value = progress.Value;
            if (_status is not null) _status.Text = progress.Text;
        }

        public override void Build(PageCanvas c)
        {
            _bar = c.Add(new PvProgress { Value = _last.Value, Running = true }, 0, 40, PageCanvas.Width, 10, "progress");
            _status = c.Text(_last.Text, 0, 66, PageCanvas.Width, 48, 10f, color: p => p.TextSecondary, name: "lblStatus");
        }
    }

    private sealed class DonePage : WizardPage
    {
        private IReadOnlyList<Msg> _done = [];
        private Msg? _error;

        public override Msg StepLabel => UninstallTexts.StepDone;
        public override Msg Kicker => UninstallTexts.DoneKicker;
        public override Msg Title => _error is null ? UninstallTexts.DoneTitle : UninstallTexts.FailedTitle;
        public override Msg Subtitle => _error is null ? UninstallTexts.DoneSubtitle : UninstallTexts.FailedSubtitle;

        public void SetResult(IReadOnlyList<Msg> done, Msg? error)
        {
            _done = done;
            _error = error;
        }

        public override void Build(PageCanvas c)
        {
            if (_error is { } error)
            {
                var box = c.Add(new PvScrollText { Framed = true, Points = 9.5f }, 0, 8, PageCanvas.Width, 220, "txtError");
                box.SetText(error, p => p.Danger);
                return;
            }
            var y = 8;
            foreach (var line in _done.Take(7))
            {
                c.Add(new CheckRow { Text = line }, 0, y, PageCanvas.Width, 30);
                y += 34;
            }
        }
    }

    /// <summary>A done-list row: green check in a soft circle, text beside it.</summary>
    private sealed class CheckRow : PvControl
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var dot = new RectangleF(U(1), (Height - U(22)) / 2f, U(22), U(22));
            using (var brush = new SolidBrush(P.EmeraldSoft)) g.FillEllipse(brush, dot);
            Draw.Check(g, RectangleF.Inflate(dot, -U(5), -U(5)), P.Emerald, Math.Max(1.5f, U(2)));
            Draw.Text(g, Text, Fonts.Get(10f, S), P.TextSecondary, new Rectangle((int)U(34), 0, Width - (int)U(34), Height), TextFormatFlags.VerticalCenter);
        }
    }
}
