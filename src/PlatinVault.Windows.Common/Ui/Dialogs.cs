using System.Drawing.Imaging;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Ui;

public enum DialogKind
{
    Info,
    Question,
    Warning,
    Error,
}

/// <summary>A button of <see cref="Dialogs.Show"/>: its text names the action ("Löschen", not "OK").</summary>
public sealed record DialogButton(string Text, DialogResult Result, PvButtonKind Kind = PvButtonKind.Secondary);

/// <summary>
/// The only place that opens message boxes - always our own <see cref="MessageWindow"/>, never Windows' MessageBox (rule "Dialogs:
/// always our own"). In a test run (<see cref="Headless"/>) nothing may appear on the desktop: the question is logged and answered
/// with the given test answer.
/// </summary>
public static class Dialogs
{
    public static bool Headless { get; set; }

    /// <summary>Where a headless run writes what would have been shown.</summary>
    public static Action<string>? Log { get; set; }

    public static DialogResult Show(IWin32Window? owner, DialogKind kind, string text, IReadOnlyList<DialogButton> buttons,
        DialogResult testAnswer, int focusIndex = 0)
    {
        if (Headless)
        {
            Log?.Invoke($"Dialog ({kind}, test answer {testAnswer}): {text.Replace('\n', ' ')}");
            return testAnswer;
        }

        using var window = new MessageWindow(kind, text, buttons, focusIndex, null, owner);
        if (owner is null)
        {
            window.StartPosition = FormStartPosition.CenterScreen;
            window.TopMost = true;
            return window.ShowDialog();
        }
        window.StartPosition = FormStartPosition.CenterParent;
        return window.ShowDialog(owner);
    }

    public static void Info(IWin32Window? owner, string text) =>
        Show(owner, DialogKind.Info, text, [new DialogButton(CommonTexts.Ok, DialogResult.OK, PvButtonKind.Primary)], DialogResult.OK);

    public static void Error(IWin32Window? owner, string text) =>
        Show(owner, DialogKind.Error, text, [new DialogButton(CommonTexts.Close, DialogResult.OK, PvButtonKind.Primary)], DialogResult.OK);
}

/// <summary>
/// Message and question window in the PlatinVault look: night-blue band with the trophy at the left, title by kind, the message in a
/// scrolling text area (always the complete text, however long), the buttons right-aligned with the positive action outermost right.
/// </summary>
public sealed class MessageWindow : Form
{
    private const int W = 560;
    private const int Band = 92;
    private const int TextMax = 240;

    private readonly float _s;
    private readonly Palette _p;
    private readonly DialogKind _kind;

    public MessageWindow(DialogKind kind, string text, IReadOnlyList<DialogButton> buttons, int focusIndex, float? forcedScale, IWin32Window? owner = null)
    {
        _kind = kind;
        // The scale of the monitor the dialog opens on: its owner's, else the one under the mouse (CenterScreen); DeviceDpi is not
        // known before the handle exists
        _s = forcedScale ?? (owner is not null ? ScreenDpi.ScaleOfWindow(owner.Handle) : ScreenDpi.ScaleAt(Cursor.Position));
        _p = ThemeMode.Current;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = forcedScale is null;
        Text = "PlatinVault";
        Icon = AppIcons.GetIcon(32);
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = _p.Page;

        var textWidth = W - Band - 32 - 28;
        var font = Fonts.Get(10f, _s);
        var needed = (int)Math.Ceiling(Draw.Measure(text, font, Px(textWidth) - Px(10)).Height / _s) + 4;
        var textHeight = Math.Clamp(needed, 24, TextMax);
        var height = 28 + 26 + 10 + textHeight + 24 + 42 + 22;
        ClientSize = new Size(Px(W), Px(height));

        var title = new PvText { Text = TitleFor(kind), Points = 13f, Style = FontStyle.Bold, Display = true, S = _s, P = _p, Surface = _p.Page, Name = "lblTitle" };
        title.Bounds = R(Band + 32, 26, textWidth, 26);
        var body = new PvScrollText { S = _s, P = _p, Surface = _p.Page, Points = 10f, Name = "txtMessage" };
        body.Bounds = R(Band + 32, 26 + 26 + 10, textWidth, textHeight);
        body.SetText(text, p => p.TextSecondary);
        Controls.AddRange([title, body]);

        var right = W - 28;
        var created = new List<PvButton>();
        for (var i = buttons.Count - 1; i >= 0; i--)
        {
            var b = buttons[i];
            var width = Math.Max(120, TextRenderer.MeasureText(b.Text, Fonts.Get(10f, 1f, FontStyle.Bold)).Width + 40);
            var button = new PvButton { Text = b.Text, Kind = b.Kind, DialogResult = b.Result, S = _s, P = _p, Surface = _p.Page, Name = "btn" + i };
            button.Bounds = R(right - width, height - 22 - 42, width, 42);
            right -= width + 12;
            created.Insert(0, button);
        }
        Controls.AddRange([.. created]);
        var cancel = created.FirstOrDefault(b => b.DialogResult is DialogResult.Cancel or DialogResult.No) ?? created[0];
        CancelButton = cancel;
        ActiveControl = created[Math.Clamp(focusIndex, 0, created.Count - 1)];
    }

    private int Px(float v) => (int)Math.Round(v * _s);

    private Rectangle R(int x, int y, int w, int h) => new(Px(x), Px(y), Px(w), Px(h));

    private static string TitleFor(DialogKind kind) => kind switch
    {
        DialogKind.Error => CommonTexts.DialogError,
        DialogKind.Warning => CommonTexts.DialogWarning,
        DialogKind.Question => CommonTexts.DialogQuestion,
        _ => CommonTexts.DialogInfo,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var band = new Rectangle(0, 0, Px(Band), ClientSize.Height);
        Brand.PaintNight(g, band);
        var accent = _kind switch
        {
            DialogKind.Error => _p.IsDark ? _p.Danger : Palette.Hex("#fda4af"),
            DialogKind.Warning => Brand.GoldGlow,
            _ => Brand.Crystal,
        };
        Brand.PaintGlow(g, new RectangleF(Px(Band / 2f - 40), Px(28 - 22), Px(80), Px(80)), accent, 110);
        using (var logo = AppIcons.GetBitmap(Px(44))) g.DrawImage(logo, Px(Band / 2f - 22), Px(24), Px(44), Px(44));
        using (var pen = new Pen(_p.Border, Math.Max(1f, _s))) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var round = 2;
            _ = NativeDwm.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows: square corners
        }
    }

    /// <summary>The layout is fixed at the scale the dialog opened with: a move to a monitor with another DPI keeps the pixel size.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;
        base.OnDpiChanged(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    /// <summary>Offscreen rendering for the layout check.</summary>
    public void SavePicture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _ = Handle;
        foreach (Control c in Controls) _ = c.Handle;
        using var bitmap = ControlSnapshot.Capture(this, ClientSize);
        bitmap.Save(path, ImageFormat.Png);
    }
}

internal static class NativeDwm
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
