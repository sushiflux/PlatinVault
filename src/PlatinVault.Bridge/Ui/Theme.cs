namespace PlatinVault.Bridge.Ui;

/// <summary>Switches shared by all windows; set once at startup.</summary>
internal static class UiEnvironment
{
    /// <summary>
    /// True in test runs: nothing may become visible on the real desktop. Windows are created but never shown (they are rendered
    /// into PNG files), message boxes are answered automatically, and there is no tray icon or balloon.
    /// </summary>
    public static bool Headless { get; set; }
}

/// <summary>
/// Colors and small control factories in the look of the PlatinVault app (dark navy header, gold accent, crystal blue
/// primary action). All text/background pairs reach WCAG AA (4.5:1) on the surfaces they are used on.
/// </summary>
internal static class Theme
{
    public static readonly Color HeaderBack = ColorTranslator.FromHtml("#171f30");
    public static readonly Color HeaderTitle = ColorTranslator.FromHtml("#f8fafc");
    public static readonly Color HeaderSubtitle = ColorTranslator.FromHtml("#cbd5e1");
    public static readonly Color Gold = ColorTranslator.FromHtml("#fbbf24");

    public static readonly Color PageBack = ColorTranslator.FromHtml("#f4f6fb");
    public static readonly Color Card = Color.White;
    public static readonly Color Border = ColorTranslator.FromHtml("#64748b");

    public static readonly Color Text = ColorTranslator.FromHtml("#1e293b");
    public static readonly Color Muted = ColorTranslator.FromHtml("#475569");

    /// <summary>Primary action (white text on it: 5.9:1).</summary>
    public static readonly Color Accent = ColorTranslator.FromHtml("#0369a1");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#075985");
    public static readonly Color Success = ColorTranslator.FromHtml("#047857");
    public static readonly Color Danger = ColorTranslator.FromHtml("#be123c");
    public static readonly Color DangerHover = ColorTranslator.FromHtml("#9f1239");

    public static Label CreateLabel(string text, Rectangle bounds, Color? color = null, Font? font = null)
    {
        return new Label
        {
            Text = text,
            Bounds = bounds,
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
            Font = font ?? SystemFonts.MessageBoxFont!,
            UseMnemonic = false,
            AutoSize = false,
        };
    }

    public static Button CreateButton(string text, Rectangle bounds, ButtonKind kind)
    {
        var button = new Button
        {
            Text = text,
            Bounds = bounds,
            UseMnemonic = false,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Font = SystemFonts.MessageBoxFont!,
        };

        switch (kind)
        {
            case ButtonKind.Primary:
            case ButtonKind.Danger:
                var back = kind == ButtonKind.Primary ? Accent : Danger;
                var hover = kind == ButtonKind.Primary ? AccentHover : DangerHover;
                button.BackColor = back;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = hover;
                button.FlatAppearance.MouseDownBackColor = hover;
                break;
            default:
                button.BackColor = Card;
                button.ForeColor = Text;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = ColorTranslator.FromHtml("#e2e8f0");
                break;
        }
        return button;
    }

    /// <summary>Two copies of the disabled look: grey text on a light grey fill, still readable.</summary>
    public static void SetEnabled(Button button, bool enabled, ButtonKind kind)
    {
        button.Enabled = enabled;
        if (kind is ButtonKind.Primary or ButtonKind.Danger)
        {
            var normal = kind == ButtonKind.Primary ? Accent : Danger;
            button.BackColor = enabled ? normal : ColorTranslator.FromHtml("#cbd5e1");
            button.ForeColor = enabled ? Color.White : ColorTranslator.FromHtml("#475569");
        }
    }
}

internal enum ButtonKind
{
    Primary,
    Secondary,
    Danger,
}

/// <summary>
/// Common frame of all bridge windows: fixed dialog, the app icon in a dark header with a gold line, light page below.
/// Controls are laid out with fixed positions in 96-dpi design units, converted with <see cref="R"/> to the real pixel size
/// of the screen, so that a message appearing never moves anything else. WinForms' own auto scaling is not used: it
/// scaled the controls but not the window on screens with a different DPI than the primary one. The process is
/// system-DPI aware (see the csproj), so the factor is the same for the whole lifetime of the process.
/// </summary>
internal abstract class BrandedForm : Form
{
    public const int HeaderHeight = 92;

    /// <summary>Pixels per design unit: 1.0 at 96 dpi, 2.5 on a 250 % screen.</summary>
    protected readonly float DpiScale;

    private readonly Label _subtitle;

    protected BrandedForm(string windowTitle, string title, string subtitle, int clientWidth, int clientHeight)
    {
        DpiScale = DeviceDpi / 96f;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.PageBack;
        Text = windowTitle;
        ClientSize = new Size(Px(clientWidth), Px(clientHeight));
        Icon = AppIcons.GetIcon(32);
        ShowInTaskbar = !UiEnvironment.Headless;
        StartPosition = FormStartPosition.CenterScreen;


        var header = new Panel { Bounds = R(0, 0, clientWidth, HeaderHeight), BackColor = Theme.HeaderBack };
        var stripe = new Panel { Bounds = R(0, HeaderHeight, clientWidth, 3), BackColor = Theme.Gold };
        var logo = new PictureBox
        {
            Bounds = R(24, 18, 56, 56),
            Image = AppIcons.GetBitmap(Px(56)),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };
        var titleFont = new Font(SystemFonts.MessageBoxFont!.FontFamily, 17f, FontStyle.Bold);
        var titleLabel = Theme.CreateLabel(title, R(96, 16, clientWidth - 120, 34), Theme.HeaderTitle, titleFont);
        _subtitle = Theme.CreateLabel(subtitle, R(97, 52, clientWidth - 120, 24), Theme.HeaderSubtitle);

        header.Controls.AddRange([logo, titleLabel, _subtitle]);
        Controls.AddRange([header, stripe]);
    }

    /// <summary>Design units to pixels.</summary>
    protected int Px(int designUnits) => (int)Math.Round(designUnits * DpiScale);

    /// <summary>A rectangle given in design units, as pixels.</summary>
    protected Rectangle R(int x, int y, int width, int height) => new(Px(x), Px(y), Px(width), Px(height));

    protected void SetSubtitle(string text) => _subtitle.Text = text;
}
