using PlatinVault.Versioning;
using System.Text;
using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Setup;

/// <summary>
/// <c>--test-layout &lt;folder&gt; --test-root &lt;folder&gt;</c>: renders every step of the installer - new installation and update - in
/// German and English, light and dark, at 100, 150 and 200 % into PNG files (never shown) and checks that every owner-drawn text fits
/// its fixed box, every control lies inside its parent and the window frame never moves between states.
/// </summary>
internal static class SetupLayoutCheck
{
    public static int Run(SetupState template, string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        var failures = 0;
        var pictures = 0;

        foreach (var update in new[] { false, true })
        foreach (var language in new[] { UiLang.German, UiLang.English })
        foreach (var dark in new[] { false, true })
        foreach (var scale in new[] { 1f, 1.5f, 2f, 2.5f })
        {
            UiLang.Current = language;
            ThemeMode.Set(dark);
            var state = new SetupState
            {
                Version = $"v2026.11.9 (31.2840 {VersionFormat.BetaMark})",
                InstalledVersion = update ? $"v2026.11.1 (12.345 {VersionFormat.BetaMark})" : null,
                LicenceText = template.LicenceText,
                PayloadSize = template.PayloadSize,
                InstallDir = @"C:\Users\Beispiel\AppData\Local\Programs\PlatinVault",
                DataDir = @"C:\Users\Beispiel\AppData\Local\PlatinVault",
            };
            using var window = new SetupWindow(state, (_, _) => Task.CompletedTask, scale);
            var tag = $"{(update ? "update" : "install")}-{language}-{(dark ? "dark" : "light")}-{scale * 100:0}";
            List<Rectangle>? frame = null;
            List<(string Name, Rectangle Bounds)>? failedPage = null;
            foreach (var (name, show) in window.TestStates())
            {
                show();
                window.SavePicture(Path.Combine(folder, $"{tag}-{name}.png"));
                pictures++;
                // "Copied" / "Copy not possible" appearing under the error text must not move or resize anything on the page
                if (name == "failed")
                {
                    failedPage = Descendants(window).Select(c => (c.Name, c.Bounds)).ToList();
                }
                else if (name.StartsWith("failed-copy", StringComparison.Ordinal) && failedPage is not null
                         && !Descendants(window).Select(c => (c.Name, c.Bounds)).SequenceEqual(failedPage))
                {
                    failures++;
                    report.AppendLine($"FAIL {tag}-{name}: the copy status moved or resized a control");
                }
                foreach (var finding in Measure(window))
                {
                    failures++;
                    report.AppendLine($"FAIL {tag}-{name}: {finding}");
                }
                var fixedParts = window.Controls.Cast<Control>().Where(c => c.Name is "btnNext" or "content" or "btnClose").Select(c => c.Bounds).ToList();
                frame ??= fixedParts;
                if (!fixedParts.SequenceEqual(frame))
                {
                    failures++;
                    report.AppendLine($"FAIL {tag}-{name}: frame moved");
                }
            }
        }

        var summary = $"Layout check (installer): {pictures} pictures, {failures} failure(s). Folder: {folder}";
        report.AppendLine(summary);
        File.WriteAllText(Path.Combine(folder, "layout-report.txt"), report.ToString(), new UTF8Encoding(false));
        return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<string> Measure(Control root)
    {
        foreach (var control in Descendants(root))
        {
            if (control.Parent is { } parent && parent is not Form && !parent.ClientRectangle.Contains(control.Bounds))
                yield return $"{control.Name} sticks out of {parent.Name}";
            if (control is PvButton button && button.Visible && button.NeededWidth > button.Width)
                yield return $"{button.Name} \"{Shorten(button.Text)}\" needs {button.NeededWidth}px, has {button.Width}px";
            if (control is PvText text && text.Text.Length > 0 && text.NeededHeight > text.Height)
                yield return $"{text.Name} \"{Shorten(text.Text)}\" needs {text.NeededHeight}px, has {text.Height}px";
            if (control is PvFeature feature && feature.NeededHeight > feature.Height)
                yield return $"{feature.Name} \"{Shorten(feature.Text)}\" needs {feature.NeededHeight}px, has {feature.Height}px";
            if (control is PvScrollText scroll && scroll.Name is "txtFolderProblem" && scroll.Overflows)
                yield return $"{scroll.Name} scrolls in a normal state";
        }
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..59] + "…";

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }
}
