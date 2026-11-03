using System.Drawing.Imaging;

namespace PlatinVault.Bridge.Ui;

/// <summary>
/// Test support: lets the whole UI be exercised without ever showing anything on the real desktop. Windows are created
/// (they get a window handle, so they lay out and paint like real ones) but never shown, and are rendered into PNG files
/// under <c>&lt;test root&gt;\ui</c> with <see cref="Control.DrawToBitmap"/>. Only active in a test run (<c>--test-root</c>).
/// </summary>
internal static class HeadlessUi
{
    public static string Directory(BridgeEnvironment env) => Path.Combine(env.TestRoot ?? Path.GetTempPath(), "ui");

    /// <summary>Renders a window that is not shown to <c>&lt;test root&gt;\ui\&lt;name&gt;.png</c>.</summary>
    public static string Capture(BridgeEnvironment env, Control window, string name)
    {
        var path = Path.Combine(Directory(env), name + ".png");
        SaveWindowPicture(window, path);
        BridgeLog.Info($"Testbild gespeichert (Fenster wurde nicht angezeigt): {path}");
        return path;
    }

    /// <summary>Renders a window that is not shown to the given PNG file (used by the layout check, which writes many pictures).</summary>
    public static void SaveWindowPicture(Control window, string path)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        CreateHandles(window);
        var size = window.Size;
        using var bitmap = new Bitmap(size.Width, size.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        bitmap.Save(path, ImageFormat.Png);
    }

    // Child controls get their window handle only when they become visible; force it so they can paint without being shown.
    private static void CreateHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls) CreateHandles(child);
    }

    /// <summary>
    /// Runs one scripted setup scenario on the UI thread and ends: renders the window after the connection test, and for
    /// <c>setup-install</c> presses the install button. Scenarios: <c>setup</c> (default), <c>setup-invalid</c>, <c>setup-install</c>.
    /// </summary>
    public static void RunSetupScenario(BridgeEnvironment env, SetupForm form, string? scenario)
    {
        var context = new ApplicationContext();
        var starter = new System.Windows.Forms.Timer { Interval = 50 };
        starter.Tick += async (_, _) =>
        {
            starter.Stop();
            try
            {
                form.Begin();
                await Task.Delay(2500); // connection test

                if (scenario == "setup-invalid")
                {
                    form.ApplyTestInput("192.168.1.5:5233", "token mit leerzeichen");
                    await Task.Delay(300);
                    Capture(env, form, "setup-invalid");
                }
                else
                {
                    Capture(env, form, "setup-initial");
                }

                if (scenario == "setup-install")
                {
                    await form.InstallNowAsync();
                    BridgeLog.Info($"Einrichtung im Testlauf beendet: {form.Outcome}.");
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Error($"Testszenario fehlgeschlagen: {ex}");
            }
            finally
            {
                starter.Dispose();
                context.ExitThread();
            }
        };
        starter.Start();
        Application.Run(context);
    }
}
