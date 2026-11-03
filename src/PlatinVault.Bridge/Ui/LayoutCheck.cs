using System.Text;

namespace PlatinVault.Bridge.Ui;

/// <summary>
/// Test aid behind the hidden <c>--test-layout &lt;folder&gt;</c> option (needs <c>--test-root</c>, so nothing ever appears on the
/// real desktop): builds every window of the bridge in German and in English, puts each of them through all the states that show
/// different texts (<see cref="SetupForm.TestStates"/>, status snapshots, message boxes), renders each state to a PNG in the given
/// folder and measures that every text still fits its control.
/// The windows use fixed positions in 96-dpi design units (see <see cref="BrandedForm"/>), so the question is whether a text
/// needs more room than its control has. The picture shows the real result at the PC's own scaling; the measurement repeats the
/// check at 100, 125, 150, 200 and 250 percent: a control's size scales with the Windows scaling, and so does the font - the
/// check builds the font at the simulated scale and lets WinForms/GDI measure the text the way it would draw it. A text that needs
/// more room than the control has (wrapped to too many lines, wider than a button or checkbox) is a failure; so is a control that
/// sticks out of its parent or overlaps a sibling. Exit code 0 = no failure, 1 = failures (listed in the report file).
/// </summary>
internal static class LayoutCheck
{
    private static readonly float[] Scales = [1.0f, 1.25f, 1.5f, 2.0f, 2.5f];

    /// <summary>
    /// Set by a "long-..." test state to the text that must be shown COMPLETELY in a <see cref="ScrollableText"/> of the window (a reason of
    /// unlimited length, a typed address). The check then demands that the slot holds all of it, that it really scrolls (a scroll bar is
    /// there) and that nothing else on the window moved or changed size.
    /// </summary>
    internal static string? ExpectedFullText { get; set; }

    /// <summary><paramref name="seed"/> repeated and cut to exactly <paramref name="length"/> characters (worst-case test data).</summary>
    internal static string Repeat(string seed, int length)
    {
        var text = new StringBuilder();
        while (text.Length < length) text.Append(seed);
        return text.ToString(0, length);
    }

    private sealed record Scenario(string Name, Func<Form> Create, Func<Form, IEnumerable<(string State, Action Show)>> States);

    public static int Run(BridgeEnvironment env, CliOptions cli, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        float systemScale;
        using (var probe = new Form())
            systemScale = probe.DeviceDpi / 96f;

        var report = new StringBuilder();
        var checks = 0;
        var failures = 0;
        var pictures = 0;
        var seen = new HashSet<string>();

        using var guard = new InstanceGuard(env);
        var sampler = new StateSampler(new SteamLocalSource(), "1.0.0", null);

        var scenarios = new List<Scenario>
        {
            new("setup-install", () => NewSetup(SetupMode.Install), SetupStates),
            new("setup-update", () => NewSetup(SetupMode.Update), SetupStates),
            new("setup-save", () => NewSetup(SetupMode.SaveOnly), SetupStates),
            new("status", () => new StatusForm(() => { }, () => { }), StatusStates),
            new("uninstall", () => new UninstallForm(env), form => [("dialog", () => { })]),
        };
        scenarios.AddRange(MessageScenarios());

        SetupForm NewSetup(SetupMode mode) => new(env, cli, guard, sampler, mode, "http://192.168.1.20:5233",
            "Rk3fVx9aQzLmP2sTuYh7WbN4cDeJgA8o", "1.0.123", mode == SetupMode.Update ? "1.0.100" : null);

        foreach (var language in BridgeLang.Supported)
        {
            BridgeLang.Pin(language);
            report.AppendLine($"=== Language {language} (system scale {systemScale:0.00}) ===");

            foreach (var scenario in scenarios)
            {
                using var form = scenario.Create();
                // Where every control sits in the first state: a long text must never move or resize anything (the window is rigid)
                List<Rectangle>? baseline = null;
                foreach (var (state, show) in scenario.States(form))
                {
                    ExpectedFullText = null;
                    show();
                    var name = $"{language}-{scenario.Name}-{state}";
                    var mayScroll = state.StartsWith("long-", StringComparison.Ordinal);

                    HeadlessUi.SaveWindowPicture(form, Path.Combine(outputDirectory, name + ".png"));
                    pictures++;

                    var current = Descendants(form).Select(c => c.Bounds).Append(new Rectangle(Point.Empty, form.Size)).ToList();
                    baseline ??= current;
                    checks++;
                    if (!current.SequenceEqual(baseline) && seen.Add($"{language}|{scenario.Name}|moved|{state}"))
                    {
                        failures++;
                        report.AppendLine($"FAIL {name}: the state moved or resized something on the window");
                    }

                    foreach (var finding in CheckFullText(form, mayScroll, ref checks))
                    {
                        if (!seen.Add($"{language}|{scenario.Name}|{state}|{finding}")) continue;
                        failures++;
                        report.AppendLine($"FAIL {name}: {finding}");
                    }

                    foreach (var finding in Measure(form, scenario.Name, systemScale, mayScroll, ref checks))
                    {
                        // The same static control fails in every state of its window: report it once per window
                        if (!seen.Add($"{language}|{scenario.Name}|{finding}")) continue;
                        failures++;
                        report.AppendLine($"FAIL {name}: {finding}");
                    }
                }
            }
        }

        foreach (var finding in CheckCopy())
        {
            failures++;
            checks++;
            report.AppendLine($"FAIL copy: {finding}");
        }

        // The status window is open while the bridge learns a new language from the server: it must switch with it
        foreach (var finding in CheckLiveSwitch())
        {
            failures++;
            checks++;
            report.AppendLine($"FAIL live-switch: {finding}");
        }

        var summary = $"Layout check: {checks} measurements in {pictures} window pictures, {failures} failure(s). Pictures: {outputDirectory}";
        report.AppendLine(summary);
        File.WriteAllText(Path.Combine(outputDirectory, "layout-report.txt"), report.ToString(), new UTF8Encoding(false));

        BridgeLog.Print(summary);
        foreach (var line in report.ToString().Split('\n').Where(l => l.StartsWith("FAIL", StringComparison.Ordinal)))
            BridgeLog.Print("  " + line.TrimEnd());
        return failures == 0 ? 0 : 1;
    }

    // ---- states -------------------------------------------------------------------------------------------------------------

    private static IEnumerable<(string State, Action Show)> SetupStates(Form form) =>
        ((SetupForm)form).TestStates();

    private static IEnumerable<(string State, Action Show)> StatusStates(Form form)
    {
        var status = (StatusForm)form;
        var now = DateTime.Now;
        var baseline = new StatusSnapshot { ServerText = "http://192.168.1.20:5233", Version = "1.0.123" };

        const string steamId = "76561198012345678";
        yield return ("starting", () => status.Apply(baseline));
        yield return ("online-no-game", () => status.Apply(baseline with
        {
            Connection = ConnectionKind.Online, SteamId64 = steamId, LastDeliveredAt = now,
        }));
        yield return ("online-game", () => status.Apply(baseline with
        {
            Connection = ConnectionKind.Online, SteamId64 = steamId, AppId = 1091500, GameName = "Cyberpunk 2077", Achieved = 12, Total = 44,
            LastUnlockName = "Hidden Gem Collector", LastUnlockAt = now, LastDeliveredAt = now,
        }));
        yield return ("online-game-older-report", () => status.Apply(baseline with
        {
            Connection = ConnectionKind.Online, SteamId64 = steamId, AppId = 1091500, GameName = "Cyberpunk 2077", Achieved = 12, Total = 44,
            LastUnlockName = "Hidden Gem Collector", LastUnlockAt = now.AddDays(-3), LastDeliveredAt = now.AddDays(-3),
        }));
        yield return ("online-game-no-achievements", () => status.Apply(baseline with
        {
            Connection = ConnectionKind.Online, SteamId64 = steamId, AppId = 440, GameName = "Team Fortress 2", LastDeliveredAt = now,
        }));
        yield return ("online-no-steam", () => status.Apply(baseline with { Connection = ConnectionKind.Online, LastDeliveredAt = now }));
        yield return ("unauthorized", () => status.Apply(baseline with { Connection = ConnectionKind.Unauthorized }));
        yield return ("offline", () => status.Apply(baseline with
        {
            Connection = ConnectionKind.Offline, Detail = CoreTexts.Network.ConnectionError,
        }));
        yield return ("rejected", () => status.Apply(baseline with { Connection = ConnectionKind.Rejected, Detail = "HTTP 500" }));
        // "Copy" next to the error: Copied / Copy not possible fill their reserved slot, nothing moves (the check compares every state with the first)
        yield return ("offline-copied", () =>
        {
            status.Apply(baseline with { Connection = ConnectionKind.Offline, Detail = CoreTexts.Network.ConnectionError });
            status.Copy.CopyNow();
        });
        yield return ("offline-copy-failed", () => status.Copy.ShowOutcome(false));

        // The reason has no length limit: the slot shows all of it and scrolls (see ScrollableText)
        var reason = Repeat("connect ECONNREFUSED 192.168.1.20:5233 - the server did not answer within the time limit, no route to host. ", 2000);
        var unbroken = new string('x', 2000);
        yield return ("long-offline-detail", () => { ExpectedFullText = reason; status.Apply(baseline with { Connection = ConnectionKind.Offline, Detail = reason }); });
        yield return ("long-rejected-detail-unbroken", () => { ExpectedFullText = unbroken; status.Apply(baseline with { Connection = ConnectionKind.Rejected, Detail = unbroken }); });
        yield return ("offline-after-long", () => status.Apply(baseline with { Connection = ConnectionKind.Offline, Detail = CoreTexts.Network.ConnectionError }));
    }

    /// <summary>The message boxes the bridge can open, with their real texts (they size themselves to the text, so the check is about buttons and the text area).</summary>
    private static IEnumerable<Scenario> MessageScenarios()
    {
        Scenario Box(string name, Func<Msg> text, MessageBoxButtons buttons, MessageBoxIcon icon) => new(
            "message-" + name,
            () => new MessageForm(text(), buttons, icon, MessageBoxDefaultButton.Button1),
            _ => [("box", () => { })]);

        yield return Box("ask-token-rejected", () => BridgeTexts.Setup.AskTokenRejected, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        yield return Box("ask-kill-old", () => BridgeTexts.Setup.AskKillOld, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        yield return Box("ask-replace-old", () => BridgeTexts.Program.AskReplaceOld, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        yield return Box("old-wont-quit", () => BridgeTexts.Program.OldWontQuit, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        yield return Box("cannot-quit", () => BridgeTexts.Uninstall.CannotQuit, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        yield return Box("handbook-missing", () => BridgeTexts.Setup.HandbookMissing, MessageBoxButtons.OK, MessageBoxIcon.Information);
        yield return Box("uninstall-done", () => BridgeTexts.Uninstall.DoneDialog(
        [
            BridgeTexts.Installer.AutostartRemoved, BridgeTexts.Installer.StartMenuRemoved, BridgeTexts.Installer.AppsEntryRemoved,
            BridgeTexts.Installer.SettingsDeleted, BridgeTexts.Installer.LogDeleted, BridgeTexts.Installer.FolderDeleted,
        ]), MessageBoxButtons.OK, MessageBoxIcon.Information);
        yield return Box("error", () => BridgeTexts.Tray.NoLogYet(@"C:\Users\Beispiel\AppData\Local\PlatinVault\bridge.log"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        yield return Box("ok-cancel", () => BridgeTexts.Setup.AskTokenRejected, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        yield return Box("yes-no-cancel", () => BridgeTexts.Setup.AskTokenRejected, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        // The copy button of an error: after "Copied" / "Copy not possible" appear in their reserved slot nothing may have moved
        yield return new Scenario("message-error-copy",
            () => new MessageForm(BridgeTexts.Tray.NoLogYet(@"C:\Users\Beispiel\AppData\Local\PlatinVault\bridge.log"), MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1),
            form =>
            {
                var message = (MessageForm)form;
                return [("box", () => { }), ("copied", message.CopyNow), ("copy-failed", () => message.ShowCopyOutcome(false))];
            });

        // A very long error: the slot scrolls, the window keeps its size, everything is still in the text (and in the copy)
        var longError = Repeat("connect ECONNREFUSED 192.168.1.20:5233 - the server did not answer within the time limit, no route to host. ", 3000);
        yield return new Scenario("message-error-long",
            () => new MessageForm(longError, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1),
            _ => [("long-box", () => ExpectedFullText = longError)]);
    }

    /// <summary>"Copy" puts the COMPLETE text on the clipboard (nothing shortened), followed by the version and the time.</summary>
    private static List<string> CheckCopy()
    {
        var findings = new List<string>();
        var text = Repeat("connect ECONNREFUSED 192.168.1.20:5233 - the server did not answer. ", 3000);
        using var form = new MessageForm(text, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
        form.CopyNow();
        var copied = ErrorCopy.LastCopied;
        if (copied is null) findings.Add("the Copy button did not copy anything");
        else
        {
            if (!copied.StartsWith(text.TrimEnd(), StringComparison.Ordinal)) findings.Add("the copied text does not start with the complete message");
            if (!copied.Contains("PlatinVault Bridge", StringComparison.Ordinal) || !copied.Contains(" · ", StringComparison.Ordinal))
                findings.Add("the copied text lacks the version / time line");
        }
        return findings;
    }

    /// <summary>
    /// Opens the status window in German with a game running, switches the bridge to English the way a server answer does and
    /// calls <see cref="StatusForm.ApplyLanguage"/> like the tray does: everything on the window must be English afterwards
    /// (nothing left of the German texts), and the other way round.
    /// </summary>
    private static List<string> CheckLiveSwitch()
    {
        var findings = new List<string>();
        var snapshot = new StatusSnapshot
        {
            Connection = ConnectionKind.Online, ServerText = "http://192.168.1.20:5233", Version = "1.0.123", SteamId64 = "76561198012345678",
            AppId = 1091500, GameName = "Cyberpunk 2077", Achieved = 12, Total = 44, LastDeliveredAt = DateTime.Now,
        };

        foreach (var (from, to) in new[] { (BridgeLang.German, BridgeLang.English), (BridgeLang.English, BridgeLang.German) })
        {
            BridgeLang.Pin(from);
            using var form = new StatusForm(() => { }, () => { });
            form.Apply(snapshot);

            BridgeLang.Pin(to);
            form.ApplyLanguage();

            var shown = Descendants(form).Select(c => c.Text).Append(form.Text).ToHashSet();
            Msg[] expected =
            [
                BridgeTexts.Status.StateOnline, BridgeTexts.Status.DetailOnline, BridgeTexts.Status.RowSteam, BridgeTexts.Status.RowGame,
                BridgeTexts.Status.RowUnlock, BridgeTexts.Status.RowDelivered, BridgeTexts.Status.OpenServer, BridgeTexts.Status.OpenLog,
                BridgeTexts.Status.Close, BridgeTexts.Status.NoUnlockYet, BridgeTexts.Status.GameProgress("Cyberpunk 2077", 12, 44),
            ];
            foreach (var message in expected)
            {
                if (!shown.Contains(message.Resolve(to)))
                    findings.Add($"{from} -> {to}: \"{message.Resolve(to)}\" is not on the window after the switch");
                if (message.De != message.En && shown.Contains(message.Resolve(from)))
                    findings.Add($"{from} -> {to}: \"{message.Resolve(from)}\" is still on the window after the switch");
            }
        }
        return findings;
    }

    // ---- measuring ----------------------------------------------------------------------------------------------------------

    /// <summary>All findings of one window in its current state, over all scales; <paramref name="checks"/> counts the measurements.</summary>
    private static List<string> Measure(Form form, string scenario, float systemScale, bool mayScroll, ref int checks)
    {
        var findings = new List<string>();
        foreach (var control in Descendants(form))
        {
            // A window that is not shown reports every control as invisible, so a control the window hides on purpose is named here:
            // the save-only setup has no autostart / Start menu choice
            if (scenario == "setup-save" && control.Name is "chkAutostart" or "chkStartMenu") continue;
            // The folder path is data of this PC and is cut with an ellipsis when it is long, in every language
            if (control.Name is "lblLocation" or "lblFolder") continue;

            CheckGeometry(control, findings, ref checks);

            // A message slot that holds a text of unlimited length scrolls on purpose; the text of a normal state must fit without scrolling -
            // measured on the real control at the PC's own scaling here, and by lines at the other scalings below
            if (control is ScrollableText slot)
            {
                if (mayScroll) continue;
                checks++;
                if (slot.Overflows) findings.Add($"{Describe(slot)} needs more than the {slot.VisibleLines} line(s) of its slot at the PC's own scaling and would scroll");
            }

            if (control.Text.Length == 0 || control is not (Label or Button or CheckBox or ScrollableText)) continue;

            // One finding per control, naming every scaling at which it fails (the first one in detail)
            var failing = new List<string>();
            string? firstDetail = null;
            foreach (var scale in Scales)
            {
                checks++;
                if (Fits(control, scale, systemScale, out var detail)) continue;
                failing.Add($"{scale * 100:0}%");
                firstDetail ??= $"at {scale * 100:0}%: {detail}";
            }
            if (failing.Count > 0)
                findings.Add($"{Describe(control)} does not fit at {string.Join(", ", failing)} - {firstDetail}");
        }
        return findings;
    }

    /// <summary>
    /// A "long-..." state: the slot that was given <see cref="ExpectedFullText"/> must hold ALL of it (nothing cut, no "..."), it must really be
    /// taller than the slot (otherwise the test proves nothing) and then show its scroll bar, so the rest of the text can be reached.
    /// </summary>
    private static List<string> CheckFullText(Form form, bool mayScroll, ref int checks)
    {
        var findings = new List<string>();
        if (ExpectedFullText is not { } expected) return findings;

        checks += 3;
        var slot = Descendants(form).OfType<ScrollableText>().FirstOrDefault(box => box.Text.Contains(expected, StringComparison.Ordinal));
        if (slot is null)
        {
            findings.Add($"the text ({expected.Length} characters) is not shown completely in any message slot of the window");
            return findings;
        }
        if (!mayScroll) findings.Add($"{Describe(slot)} got a long text in a state that is not named long-...");
        if (!slot.Overflows) findings.Add($"{Describe(slot)} holds the long text without scrolling - the test text is too short to prove anything");
        else if (!slot.HasVerticalScrollBar) findings.Add($"{Describe(slot)} does not fit the long text and has no scroll bar - the rest cannot be reached");
        return findings;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }

    private static string Describe(Control control) =>
        $"{(string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name)} \"{Shorten(control.Text.Replace('\n', ' '), 60)}\"";

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>A control must lie inside its parent and must not overlap a sibling that is next to it in the design.</summary>
    private static void CheckGeometry(Control control, List<string> findings, ref int checks)
    {
        checks++;
        if (control.Parent is { } parent && parent is not Form)
        {
            if (!parent.ClientRectangle.Contains(control.Bounds))
                findings.Add($"{Describe(control)} sticks out of its parent {parent.Name}");
        }
        else if (control.Parent is Form form && !form.ClientRectangle.Contains(control.Bounds))
        {
            findings.Add($"{Describe(control)} sticks out of the window");
        }

        if (control.Parent is null) return;
        foreach (Control sibling in control.Parent.Controls)
        {
            // Each pair once; decoration (header, stripe, the marker bar of a message) is laid out on purpose
            if (sibling == control || control.Parent.Controls.GetChildIndex(sibling) < control.Parent.Controls.GetChildIndex(control)) continue;
            if (sibling is Panel || control is Panel) continue;
            checks++;
            if (control.Bounds.IntersectsWith(sibling.Bounds))
                findings.Add($"{Describe(control)} overlaps {Describe(sibling)}");
        }
    }

    /// <summary>
    /// Does the text fit the control at this Windows scaling? The control's design size comes from its real size at the PC's own
    /// scaling; the font is rebuilt at the simulated scaling (same typeface and style, the point size scaled by the ratio of the
    /// two scalings, because a point converts to pixels with the PC's own DPI) and the text is measured like WinForms draws it.
    /// </summary>
    private static bool Fits(Control control, float scale, float systemScale, out string detail)
    {
        var width = (int)Math.Round(control.Width / systemScale * scale);
        var height = (int)Math.Round(control.Height / systemScale * scale);
        using var font = new Font(control.Font.FontFamily, control.Font.SizeInPoints * scale / systemScale, control.Font.Style, GraphicsUnit.Point);

        switch (control)
        {
            case Label label when label.AutoEllipsis:
            {
                // Data such as the server address or a game name may be cut with "…" when it is long; fixed texts may not
                var needed = TextRenderer.MeasureText(label.Text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                detail = $"single line needs {needed.Width}x{needed.Height}px, has {width}x{height}px";
                return needed.Width <= width && needed.Height <= height;
            }
            case ScrollableText box:
            {
                // A slot shows whole lines (it is snapped to them): the text may need as many lines as the slot shows, no more. The text is indented
                // like a label's (3 px) and the rich text box keeps a few pixels of its own margin: it wraps as if the slot were that much narrower.
                var textWidth = width - (int)Math.Ceiling(8 * scale);
                using var probe = new Label { AutoSize = true, UseMnemonic = false, Font = font, Text = box.Text, MaximumSize = new Size(textWidth, 0) };
                var needed = probe.GetPreferredSize(new Size(textWidth, 0));
                var oneLine = TextRenderer.MeasureText("Ag", font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height;
                var neededLines = (int)Math.Round(needed.Height / (double)oneLine);
                detail = $"needs {neededLines} line(s) of {textWidth}px, the slot shows {box.VisibleLines}";
                return needed.Width <= textWidth && neededLines <= box.VisibleLines;
            }
            case Label label:
            {
                using var probe = new Label { AutoSize = true, UseMnemonic = false, Font = font, Text = label.Text, MaximumSize = new Size(width, 0) };
                var needed = probe.GetPreferredSize(new Size(width, 0));
                detail = $"needs {needed.Width}x{needed.Height}px, has {width}x{height}px";
                return needed.Width <= width && needed.Height <= height;
            }
            case CheckBox:
            {
                // 13 px glyph plus the gap to the text, scaled like the window
                var glyph = (int)Math.Ceiling(20 * scale);
                var needed = TextRenderer.MeasureText(control.Text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                detail = $"needs {needed.Width + glyph}x{needed.Height}px, has {width}x{height}px";
                return needed.Width + glyph <= width && needed.Height <= height;
            }
            default:
            {
                // Button: text on one line with some air at both sides (a longer text would wrap and be clipped in the 38-unit height)
                var needed = TextRenderer.MeasureText(control.Text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                var air = (int)Math.Ceiling(16 * scale);
                detail = $"needs {needed.Width + air}x{needed.Height}px, has {width}x{height}px";
                return needed.Width + air <= width && needed.Height <= height;
            }
        }
    }
}
