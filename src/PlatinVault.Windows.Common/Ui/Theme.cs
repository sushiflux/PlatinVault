using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// The colours of the PlatinVault web app (frontend/src/index.css, light-dark tokens) for native windows, light and dark.
/// Every text/background pair reaches WCAG AA (4.5:1) on the surface it is used on; the values are the app's own tokens.
/// </summary>
public sealed record Palette(
    bool IsDark,
    Color Page,
    Color Surface,
    Color Elevated,
    Color Text,
    Color TextSecondary,
    Color Muted,
    Color Border,
    Color BorderStrong,
    Color Accent,
    Color AccentHover,
    Color OnAccent,
    Color AccentSoft,
    Color Gold,
    Color GoldSoft,
    Color Emerald,
    Color EmeraldSoft,
    Color Danger,
    Color DangerHover,
    Color DangerSoft,
    Color ToggleOn,
    Color ToggleOff,
    Color Knob,
    Color Focus)
{
    public static readonly Palette Light = new(
        IsDark: false,
        Page: Hex("#f4f6fb"), Surface: Hex("#ffffff"), Elevated: Hex("#edf1f9"),
        Text: Hex("#1e293b"), TextSecondary: Hex("#334155"), Muted: Hex("#475569"),
        Border: Hex("#cbd5e1"), BorderStrong: Hex("#64748b"),
        Accent: Hex("#0369a1"), AccentHover: Hex("#075985"), OnAccent: Color.White, AccentSoft: Hex("#e0f2fe"),
        Gold: Hex("#b45309"), GoldSoft: Hex("#fef3c7"),
        Emerald: Hex("#047857"), EmeraldSoft: Hex("#d1fae5"),
        Danger: Hex("#be123c"), DangerHover: Hex("#9f1239"), DangerSoft: Hex("#ffe4e6"),
        ToggleOn: Hex("#15803d"), ToggleOff: Hex("#dc2626"), Knob: Color.White, Focus: Hex("#0369a1"));

    public static readonly Palette Dark = new(
        IsDark: true,
        Page: Hex("#171f30"), Surface: Hex("#1f293d"), Elevated: Hex("#28354e"),
        Text: Hex("#f8fafc"), TextSecondary: Hex("#dbe4f0"), Muted: Hex("#a3b3c9"),
        Border: Hex("#384966"), BorderStrong: Hex("#64748b"),
        Accent: Hex("#7dd3fc"), AccentHover: Hex("#bae6fd"), OnAccent: Hex("#0b1220"), AccentSoft: Hex("#1c3550"),
        Gold: Hex("#fcd34d"), GoldSoft: Hex("#3a3320"),
        Emerald: Hex("#6ee7b7"), EmeraldSoft: Hex("#1b3a36"),
        Danger: Hex("#fda4af"), DangerHover: Hex("#fecdd3"), DangerSoft: Hex("#43273a"),
        ToggleOn: Hex("#4ade80"), ToggleOff: Hex("#f87171"), Knob: Hex("#0b1220"), Focus: Hex("#7dd3fc"));

    public static Color Hex(string value) => ColorTranslator.FromHtml(value);
}

/// <summary>
/// The brand side panel (same look as the cover of the PlatinVault documents: deep night blue, crystal glow top right, warm gold
/// glow bottom left). Fixed dark in both themes, so the white texts on it always reach 4.5:1.
/// </summary>
public static class Brand
{
    public static readonly Color NightTop = Palette.Hex("#0c1734");
    public static readonly Color NightBottom = Palette.Hex("#060a16");
    public static readonly Color Crystal = Palette.Hex("#38bdf8");
    public static readonly Color GoldGlow = Palette.Hex("#fbbf24");
    public static readonly Color Title = Color.White;
    public static readonly Color Body = Palette.Hex("#cbd5e1");
    public static readonly Color Faint = Palette.Hex("#94a3b8");
    public static readonly Color Line = Color.FromArgb(56, 148, 163, 184);
    public static readonly Color Heart = Palette.Hex("#4ade80");

    /// <summary>Paints the night-blue panel with its two soft glows into <paramref name="bounds"/>.</summary>
    public static void PaintNight(Graphics g, Rectangle bounds)
    {
        using (var back = new LinearGradientBrush(bounds, NightTop, NightBottom, 75f))
        {
            g.FillRectangle(back, bounds);
        }

        var state = g.Save();
        g.SetClip(bounds);
        PaintGlow(g, new RectangleF(bounds.Right - bounds.Width * 0.95f, bounds.Top - bounds.Height * 0.35f, bounds.Width * 1.5f, bounds.Height * 0.8f), Crystal, 105);
        PaintGlow(g, new RectangleF(bounds.Left - bounds.Width * 0.6f, bounds.Bottom - bounds.Height * 0.42f, bounds.Width * 1.3f, bounds.Height * 0.7f), GoldGlow, 60);
        g.Restore(state);
    }

    /// <summary>A soft round glow: full colour in the middle, fading to nothing at the edge (no visible rim).</summary>
    public static void PaintGlow(Graphics g, RectangleF ellipse, Color color, int alpha)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(ellipse);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, color),
            SurroundColors = [Color.FromArgb(0, color)],
        };
        // Pull the colour towards the centre so the fade is long and smooth
        brush.Blend = new Blend
        {
            Factors = [0f, 0.18f, 0.55f, 1f],
            Positions = [0f, 0.35f, 0.7f, 1f],
        };
        g.FillPath(brush, path);
    }
}

/// <summary>Light/dark choice of the native windows: follows Windows' app mode until the user picks one in a window.</summary>
public static class ThemeMode
{
    private static Palette? _override;

    public static event Action? Changed;

    public static Palette Current => _override ?? (SystemPrefersDark() ? Palette.Dark : Palette.Light);

    public static void Set(bool dark)
    {
        _override = dark ? Palette.Dark : Palette.Light;
        Changed?.Invoke();
    }

    /// <summary>Windows' "app mode" (Settings > Personalisation > Colours): AppsUseLightTheme = 0 means dark.</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

/// <summary>Fonts in pixels for a given scale (design units are 96-dpi pixels), so a window looks the same at any DPI and offscreen.</summary>
public static class Fonts
{
    private static readonly Dictionary<(float, FontStyle, bool), Font> Cache = [];
    private static readonly string Family = PickFamily();

    /// <summary>A font of <paramref name="points"/> at 100 % (as the web app's rem sizes), scaled by <paramref name="scale"/>.</summary>
    public static Font Get(float points, float scale, FontStyle style = FontStyle.Regular, bool display = false)
    {
        var pixels = (float)Math.Round(points * 96f / 72f * scale, 2);
        var key = (pixels, style, display);
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var font))
            {
                font = new Font(display ? DisplayFamily : Family, pixels, style, GraphicsUnit.Pixel);
                Cache[key] = font;
            }
            return font;
        }
    }

    private static readonly string DisplayFamily = Installed("Segoe UI Variable Display") ? "Segoe UI Variable Display" : Family;

    private static string PickFamily() => Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    private static bool Installed(string name)
    {
        try
        {
            using var family = new FontFamily(name);
            return string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Small drawing helpers shared by all owner-drawn controls.</summary>
public static class Draw
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }

    public static void FillRounded(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void FillRoundedBrush(Graphics g, RectangleF r, float radius, Brush brush)
    {
        using var path = Rounded(r, radius);
        g.FillPath(brush, path);
    }

    public static void StrokeRounded(Graphics g, RectangleF r, float radius, Color color, float width)
    {
        using var path = Rounded(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.A + (b.A - a.A) * t),
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Wrapped text, top-left aligned, never with an ellipsis (a text that does not fit is a layout bug the layout check reports).</summary>
    public static void Text(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags extra = 0) =>
        TextRenderer.DrawText(g, text, font, bounds, color,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding | extra);

    public static Size Measure(string text, Font font, int width) =>
        TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);

    /// <summary>A check mark inside <paramref name="box"/>.</summary>
    public static void Check(Graphics g, RectangleF box, Color color, float width)
    {
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [
            new PointF(box.Left + box.Width * 0.18f, box.Top + box.Height * 0.53f),
            new PointF(box.Left + box.Width * 0.42f, box.Top + box.Height * 0.76f),
            new PointF(box.Left + box.Width * 0.84f, box.Top + box.Height * 0.26f),
        ]);
    }

    /// <summary>A cross (x) inside <paramref name="box"/>.</summary>
    public static void Cross(Graphics g, RectangleF box, Color color, float width)
    {
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, box.Left + box.Width * 0.25f, box.Top + box.Height * 0.25f, box.Right - box.Width * 0.25f, box.Bottom - box.Height * 0.25f);
        g.DrawLine(pen, box.Right - box.Width * 0.25f, box.Top + box.Height * 0.25f, box.Left + box.Width * 0.25f, box.Bottom - box.Height * 0.25f);
    }
}
