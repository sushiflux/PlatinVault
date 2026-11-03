using PlatinVault.Versioning;
using System.Text;
using PlatinVault.Desktop.Ui;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop;

/// <summary>
/// <c>--test-layout &lt;folder&gt; --test-root &lt;folder&gt;</c>: renders every native surface of the program into PNG files without
/// showing anything - the uninstall window in every state, German/English, light/dark, at 100/150/200 % - the start screen (progress and a very long error) and the main window's
/// title bar in every state (light/dark, 100-250 %, see RenderTitleBars). The in-game overlay is the app's own web page (/overlay) and is
/// checked by the frontend tests and the smoke test instead. Checks on the way
/// that no owner-drawn text needs more room than its control has (PvText) and that nothing moves between the states of a window.
/// </summary>
internal static class TestLayout
{
    private static readonly float[] Scales = [1f, 1.5f, 2f, 2.5f];

    public static int Run(DesktopEnvironment env, string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        var failures = 0;
        var pictures = 0;

        foreach (var language in new[] { UiLang.German, UiLang.English })
        {
            UiLang.Current = language;
            foreach (var dark in new[] { false, true })
            {
                ThemeMode.Set(dark);
                foreach (var scale in Scales)
                {
                    var tag = $"{language}-{(dark ? "dark" : "light")}-{scale * 100:0}";
                    using var window = new UninstallWindow(env.Layout, $"v2026.11.1 (12.345 {VersionFormat.BetaMark})", (_, _) => [], scale);
                    List<Rectangle>? baseline = null;
                    List<(string Name, Rectangle Bounds)>? failedPage = null;
                    foreach (var (state, show) in window.TestStates())
                    {
                        show();
                        window.SavePicture(Path.Combine(folder, $"uninstall-{state}-{tag}.png"));
                        pictures++;
                        // "Copied" appearing next to the Copy button must not move or resize anything on the error page
                        if (state == "failed") failedPage = Descendants(window).Select(c => (c.Name, c.Bounds)).ToList();
                        else if (state == "failed-copied" && failedPage is not null && !Descendants(window).Select(c => (c.Name, c.Bounds)).SequenceEqual(failedPage))
                        {
                            failures++;
                            report.AppendLine($"FAIL uninstall-{state}-{tag}: the copy status moved or resized a control");
                        }
                        failures += Measure(window, $"uninstall-{state}-{tag}", report);
                        // Window chrome and action bar stay where they are in every state
                        var fixedParts = window.Controls.Cast<Control>().Where(c => c.Name is "btnNext" or "btnCancel" or "btnClose" or "content").Select(c => c.Bounds).ToList();
                        baseline ??= fixedParts;
                        if (!fixedParts.SequenceEqual(baseline))
                        {
                            failures++;
                            report.AppendLine($"FAIL uninstall-{state}-{tag}: frame moved");
                        }
                    }
                }
            }

            // Start screen: progress, and an error with a very long technical detail (complete, scrolls in its slot)
            foreach (var errorState in new[] { false, true })
            {
                using var host = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, ClientSize = new Size(1280, 800) };
                var screen = new StartScreen { Bounds = new Rectangle(0, 0, 1280, 800), S = 1f };
                host.Controls.Add(screen);
                _ = host.Handle;
                _ = screen.Handle;
                if (errorState)
                {
                    screen.SetError(DesktopTexts.Database.StartFailed,
                        string.Concat(Enumerable.Repeat(@"SQLite Error 14: 'unable to open database file' (C:\Users\Beispiel\AppData\Local\PlatinVault\database\platinvault.db). ", 30)), @"C:\Users\Beispiel\AppData\Local\PlatinVault\logs");
                }
                else
                {
                    screen.SetProgress(0.45f, DesktopTexts.Window.StartingDatabase);
                }
                foreach (Control c in screen.Controls) _ = c.Handle;
                using var bitmap = ControlSnapshot.Capture(screen, host.ClientSize);
                bitmap.Save(Path.Combine(folder, $"start-{(errorState ? "error" : "progress")}-{language}.png"), System.Drawing.Imaging.ImageFormat.Png);
                pictures++;
                if (errorState)
                {
                    // After a click on "Copy" the status fills its slot; nothing on the screen may move
                    var before = Descendants(screen).Select(c => (c.Name, c.Bounds)).ToList();
                    screen.Controls.Find("btnCopyStartError", true).OfType<PvButton>().Single().PerformClick();
                    using var copied = ControlSnapshot.Capture(screen, host.ClientSize);
                    copied.Save(Path.Combine(folder, $"start-error-copied-{language}.png"), System.Drawing.Imaging.ImageFormat.Png);
                    pictures++;
                    if (!Descendants(screen).Select(c => (c.Name, c.Bounds)).SequenceEqual(before))
                    {
                        failures++;
                        report.AppendLine($"FAIL start-error-copied-{language}: the copy status moved or resized a control");
                    }
                }
            }

            // Our own error message window with the copy button: before, after "Copied" and after "Copy not possible", light/dark, 100-250 %
            foreach (var dark in new[] { false, true })
            {
                ThemeMode.Set(dark);
                foreach (var scale in Scales)
                {
                    var tag = $"{language}-{(dark ? "dark" : "light")}-{scale * 100:0}";
                    var buttons = new[] { new DialogButton(CommonTexts.Close, DialogResult.OK, PvButtonKind.Primary) };
                    using var error = new MessageWindow(DialogKind.Error, DesktopTexts.Update.Failed(
                        "Access to the path 'PlatinVault.exe' is denied. " + string.Concat(Enumerable.Repeat("The database could not be set up: SQLite Error 14: 'unable to open database file'. ", 6))), buttons, 0, scale);
                    error.SavePicture(Path.Combine(folder, $"dialog-error-{tag}.png"));
                    pictures++;
                    failures += Measure(error, $"dialog-error-{tag}", report);
                    var plain = Descendants(error).Select(c => (c.Name, c.Bounds)).ToList();
                    foreach (var (name, ok) in new[] { ("copied", true), ("copy-failed", false) })
                    {
                        error.Copy!.ShowOutcome(ok);
                        error.SavePicture(Path.Combine(folder, $"dialog-error-{name}-{tag}.png"));
                        pictures++;
                        failures += Measure(error, $"dialog-error-{name}-{tag}", report);
                        if (!Descendants(error).Select(c => (c.Name, c.Bounds)).SequenceEqual(plain))
                        {
                            failures++;
                            report.AppendLine($"FAIL dialog-error-{name}-{tag}: the copy status moved or resized a control");
                        }
                    }
                    // The copy really contains the complete text, the version and the time
                    error.Copy!.Copy();
                    if (ErrorReport.LastCopied is not { } copiedText || !copiedText.Contains("unable to open database file", StringComparison.Ordinal) || !copiedText.Contains(" · ", StringComparison.Ordinal))
                    {
                        failures++;
                        report.AppendLine($"FAIL dialog-error-{tag}: the copied text is incomplete");
                    }
                }
            }

            // Our own message window (update question) instead of a Windows message box
            using (var message = new MessageWindow(DialogKind.Question, DesktopTexts.Update.Confirm("v2026.11.1", "v2026.11.9"),
                       [new DialogButton(DesktopTexts.Update.Later, DialogResult.Cancel), new DialogButton(DesktopTexts.Update.Install, DialogResult.OK, PvButtonKind.Primary)], 1, 1f))
            {
                message.SavePicture(Path.Combine(folder, $"dialog-update-{language}.png"));
                pictures++;
                failures += Measure(message, $"dialog-update-{language}", report);
            }

            // The program window's own title bar: every state, light/dark, 100-250 %
            var (barPictures, barFailures) = RenderTitleBars(folder, language, report);
            pictures += barPictures;
            failures += barFailures;
        }

        var summary = $"Layout check (Windows program): {pictures} pictures, {failures} failure(s). Folder: {folder}";
        report.AppendLine(summary);
        File.WriteAllText(Path.Combine(folder, "layout-report.txt"), report.ToString(), new UTF8Encoding(false));
        Console.Out.WriteLine(report.ToString());
        return failures == 0 ? 0 : 1;
    }

    private static readonly float[] BarScales = [1f, 1.5f, 2f, 2.5f];

    /// <summary>
    /// The states of the title bar, top to bottom in each picture: idle, minimise hovered, maximise hovered, close hovered, close
    /// pressed, maximised (restore glyph) with restore hovered, window inactive, development badge, start screen (brand surface).
    /// </summary>
    private static readonly (string Name, Action<PvTitleBar> Setup)[] BarStates =
    [
        ("idle", _ => { }),
        ("hover-minimize", bar => bar.SetHover(CaptionPart.Minimize)),
        ("hover-maximize", bar => bar.SetHover(CaptionPart.Maximize)),
        ("hover-close", bar => bar.SetHover(CaptionPart.Close)),
        ("pressed-close", bar => { bar.SetHover(CaptionPart.Close); bar.SetPressed(CaptionPart.Close); }),
        ("maximized-hover-restore", bar => { bar.Maximized = true; bar.SetHover(CaptionPart.Maximize); }),
        ("inactive", bar => bar.Active = false),
        ("dev-badge", bar => bar.Badge = DesktopTexts.Dev.Badge),
        ("start-screen", bar => { bar.BrandMode = true; bar.Badge = DesktopTexts.Dev.Badge; bar.SetHover(CaptionPart.Minimize); }),
    ];

    /// <summary>
    /// One picture per theme and scale with every bar state stacked (titlebar-&lt;lang&gt;-&lt;theme&gt;-&lt;scale&gt;.png, 900 design units
    /// wide), plus the check that title and badge end before the window commands even at the smallest window width.
    /// </summary>
    private static (int Pictures, int Failures) RenderTitleBars(string folder, string language, StringBuilder report)
    {
        var pictures = 0;
        var failures = 0;
        foreach (var dark in new[] { false, true })
        {
            ThemeMode.Set(dark);
            foreach (var scale in BarScales)
            {
                var tag = $"{language}-{(dark ? "dark" : "light")}-{scale * 100:0}";
                var width = (int)Math.Round(900 * scale);
                var gap = (int)Math.Round(6 * scale);
                var rows = new List<Bitmap>();
                try
                {
                    foreach (var (_, setup) in BarStates)
                    {
                        using var bar = NewBar(width, scale);
                        setup(bar);
                        var row = ControlSnapshot.Capture(bar);
                        rows.Add(row);
                    }
                    using var sheet = new Bitmap(width, rows.Sum(r => r.Height + gap) + gap);
                    using (var g = Graphics.FromImage(sheet))
                    {
                        g.Clear(dark ? Color.FromArgb(8, 12, 22) : Color.FromArgb(200, 208, 220));
                        var y = gap;
                        foreach (var row in rows)
                        {
                            g.DrawImageUnscaled(row, 0, y);
                            y += row.Height + gap;
                        }
                    }
                    sheet.Save(Path.Combine(folder, $"titlebar-{tag}.png"), System.Drawing.Imaging.ImageFormat.Png);
                    pictures++;
                }
                finally
                {
                    foreach (var row in rows) row.Dispose();
                }

                // Smallest window: 500 design units minus the invisible resize borders left and right
                using var narrow = NewBar((int)Math.Round(484 * scale), scale);
                narrow.Badge = DesktopTexts.Dev.Badge;
                var (title, badge) = narrow.TextAreas();
                var textRight = (badge ?? title).Right;
                if (textRight + (int)Math.Round(12 * scale) > narrow.Geometry.Minimize.Left)
                {
                    failures++;
                    report.AppendLine($"FAIL titlebar-{tag}: title/badge ends at {textRight}px, the window commands start at {narrow.Geometry.Minimize.Left}px");
                }
            }
        }
        return (pictures, failures);

        static PvTitleBar NewBar(int width, float scale)
        {
            var bar = new PvTitleBar { S = scale, P = ThemeMode.Current, Title = "PlatinVault", PassHitTestToWindow = false, DrawTopBorder = false };
            bar.Bounds = new Rectangle(0, 0, width, bar.Geometry.Height);
            _ = bar.Handle;
            return bar;
        }
    }

    /// <summary>Every owner-drawn text must fit its fixed box (no clipped line); every control must lie inside its parent.</summary>
    public static int Measure(Control root, string name, StringBuilder report)
    {
        var failures = 0;
        foreach (var control in Descendants(root))
        {
            if (!control.Visible && control.Parent?.Visible == true) continue;
            if (control.Parent is { } parent && parent is not Form && !parent.ClientRectangle.Contains(control.Bounds))
            {
                failures++;
                report.AppendLine($"FAIL {name}: {control.Name} sticks out of {parent.Name}");
            }
            if (control is PvButton button && button.Visible && button.NeededWidth > button.Width)
            {
                failures++;
                report.AppendLine($"FAIL {name}: {button.Name} \"{Shorten(button.Text)}\" needs {button.NeededWidth}px, has {button.Width}px");
            }
            if (control is PvText text && text.Text.Length > 0 && text.NeededHeight > text.Height)
            {
                failures++;
                report.AppendLine($"FAIL {name}: {text.Name} \"{Shorten(text.Text)}\" needs {text.NeededHeight}px, has {text.Height}px");
            }
        }
        return failures;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..59] + "…";

    public static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }
}
