using PlatinVault.Versioning;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Setup;

/// <summary>
/// PlatinVault-Setup-&lt;version&gt;.exe: the designed installer. It carries the program as an appended ZIP (see <see cref="Payload"/>) and
/// installs it for the current user without administrator rights. An existing installation is updated in place (data untouched).
/// Options for the build and for tests: <c>--append-payload &lt;exe&gt; &lt;zip&gt;</c> (build script), <c>--payload &lt;zip&gt;</c>,
/// <c>--test-root &lt;folder&gt;</c> (everything inside that folder, never the real profile), <c>--headless</c> (install with the defaults,
/// no window), <c>--install-dir</c>, <c>--no-webview-install</c>, <c>--test-layout &lt;folder&gt;</c> (offscreen pictures of every step).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Dialogs.Headless = args.Any(a => a.Equals("--test-root", StringComparison.OrdinalIgnoreCase));
        try
        {
            return Run(args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never Windows' crash box: the complete reason in our own window (a test run only writes it to stderr)
            Console.Error.WriteLine(ex);
            if (!Dialogs.Headless) Dialogs.Error(null, ex.ToString());
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        bool Has(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (Has("--append-payload"))
        {
            var index = Array.FindIndex(args, a => a.Equals("--append-payload", StringComparison.OrdinalIgnoreCase));
            Payload.Append(args[index + 1], args[index + 2]);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var testRoot = Arg("--test-root");
        if (testRoot is null && (Has("--headless") || Has("--test-layout") || Has("--no-webview-install")))
        {
            return 2;
        }
        var layout = InstallLayout.Create(testRoot);
        Dialogs.Headless = layout.IsTest;

        using var payload = Arg("--payload") is { } zip ? Payload.FromZip(zip) : Payload.FromExe(Environment.ProcessPath!);
        if (payload is null)
        {
            Dialogs.Error(null, SetupTexts.PayloadBroken);
            return 1;
        }

        var version = ReadVersion(payload);
        var installed = layout.InstalledVersion;
        if (layout.Registry.GetString(InstallLayout.AppKeyPath, "Language") is { } language) UiLang.Current = language;
        var state = new SetupState
        {
            Version = version,
            InstalledVersion = installed,
            LicenceText = payload.ReadText("docs/LICENSE"),
            PayloadSize = payload.UncompressedSize,
            InstallDir = Arg("--install-dir") ?? layout.InstalledDir ?? layout.DefaultInstallDir,
            DataDir = layout.DataDir,
            Autostart = installed is null || Registration.IsAutostartEnabled(layout),
            StartMenu = installed is null || File.Exists(layout.StartMenuShortcut),
            DesktopShortcut = installed is not null && File.Exists(layout.DesktopShortcut),
        };
        SetupWindow? owner = null;
        var engine = new InstallEngine(layout, payload, version)
        {
            SkipWebViewInstall = Has("--no-webview-install"),
            // A failed backup before an update: the window asks (our own dialog), a headless run cancels the update
            ConfirmWithoutBackup = Has("--headless") ? null : question =>
            {
                bool Ask() => Dialogs.Show(owner, DialogKind.Warning, question,
                    [new DialogButton(SetupTexts.CancelUpdate, DialogResult.Cancel, PvButtonKind.Primary), new DialogButton(SetupTexts.UpdateAnyway, DialogResult.OK, PvButtonKind.Danger)],
                    DialogResult.Cancel) == DialogResult.OK;
                return Task.FromResult(owner is { InvokeRequired: true } ? (bool)owner.Invoke(Ask) : Ask());
            },
        };

        if (Arg("--test-layout") is { } folder) return SetupLayoutCheck.Run(state, folder);

        if (Has("--headless"))
        {
            AttachConsole(-1);
            var log = new StringBuilder();
            try
            {
                engine.RunAsync(state.Request(), new SyncProgress(p =>
                {
                    var line = $"{p.Value * 100:0}% {p.Task}: {p.Text.De}";
                    log.AppendLine(line);
                    Console.Out.WriteLine(line);
                }), CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(layout.TestRoot!, "setup.log"), log.ToString());
                return 0;
            }
            catch (InstallException ex)
            {
                log.AppendLine("FEHLER: " + ex.Text.De);
                File.WriteAllText(Path.Combine(layout.TestRoot!, "setup.log"), log.ToString());
                Console.Out.WriteLine("FEHLER: " + ex.Text.De);
                return 1;
            }
        }

        using var window = new SetupWindow(state, (request, progress) => engine.RunAsync(request, progress, CancellationToken.None));
        owner = window;
        Application.Run(window);
        if (window.LaunchAfterFinish && !layout.IsTest)
        {
            var exe = Path.Combine(state.InstallDir, InstallLayout.ExeName);
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = state.InstallDir })?.Dispose(); }
            catch (System.ComponentModel.Win32Exception ex) { Dialogs.Error(null, ex.Message); }
        }
        return state.Succeeded ? 0 : 1;
    }

    /// <summary>
    /// The version the program will show, in the readable form of <see cref="VersionFormat"/> (e.g. "v2026.11.12 (13.557 β)" for a beta,
    /// "v2026.11.12 (13.557)" for a release). The payload carries version.txt (line 1 version, line 2 build id) and release-channel.txt.
    /// </summary>
    public static string ReadVersion(Payload payload)
    {
        var lines = (payload.ReadText("version.txt") ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return "v0.0.0";
        var kind = VersionFormat.KindFromChannel(payload.ReadText("release-channel.txt"));
        return VersionFormat.Display(lines[0], lines.Length > 1 ? lines[1] : null, kind);
    }

    /// <summary>Progress without a UI thread (the headless install reports synchronously).</summary>
    private sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
