using System.Drawing.Drawing2D;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// The window-command glyphs (minimise, maximise, restore, close, light/dark) of every PlatinVault window: the installer's title strip
/// and the program's own title bar draw the same shapes with the same stroke, so "close" or "minimise" looks alike everywhere.
/// Round caps like the Tabler icons of the web app; the stroke is a whole number of device pixels and the lines sit on the pixel grid,
/// so the small shapes stay crisp at 100-250 % instead of turning into soft anti-aliased smudges.
/// </summary>
public static class CaptionGlyphs
{
    /// <summary>Stroke width in device pixels for a scale (1.0 = 100 %): 2 px at 100/150 %, 3 px at 200 %, 4 px at 250 %.</summary>
    public static float StrokeWidth(float scale) => Math.Max(1f, MathF.Round(1.5f * scale, MidpointRounding.AwayFromZero));

    public static void Minimize(Graphics g, RectangleF r, Color c) => Minimize(g, r, c, Scale(r));

    public static void Maximize(Graphics g, RectangleF r, Color c) => Maximize(g, r, c, Scale(r));

    public static void Restore(Graphics g, RectangleF r, Color c) => Restore(g, r, c, Scale(r));

    public static void Close(Graphics g, RectangleF r, Color c) => Close(g, r, c, Scale(r));

    // Minimise and maximise use 9 of the 16 design units of the glyph box, close 8 (its diagonals look larger): one optical size
    private const float Inset = 0.22f;

    // Every glyph box is 16 design units: the scale follows from the box the caller passes
    private static float Scale(RectangleF r) => r.Width / 16f;

    public static void Minimize(Graphics g, RectangleF r, Color c, float scale)
    {
        using var pen = Pen(c, scale);
        var y = Snap(r.Top + r.Height * 0.5f, pen.Width);
        g.DrawLine(pen, Snap(r.Left + r.Width * Inset, pen.Width), y, Snap(r.Right - r.Width * Inset, pen.Width), y);
    }

    public static void Maximize(Graphics g, RectangleF r, Color c, float scale)
    {
        using var pen = Pen(c, scale);
        var box = SnapRect(r.Left + r.Width * Inset, r.Top + r.Height * Inset, r.Width * (1 - 2 * Inset), r.Height * (1 - 2 * Inset), pen.Width);
        using var path = Draw.Rounded(box, r.Width * 0.11f);
        g.DrawPath(pen, path);
    }

    /// <summary>Two overlapping squares: the front one bottom left, the edge of the one behind it top right.</summary>
    public static void Restore(Graphics g, RectangleF r, Color c, float scale)
    {
        using var pen = Pen(c, scale);
        var shift = r.Width * 0.125f;
        var size = r.Width * (1 - 2 * Inset) - shift;
        var front = SnapRect(r.Left + r.Width * Inset, r.Top + r.Height * Inset + shift, size, size, pen.Width);
        using (var path = Draw.Rounded(front, r.Width * 0.09f)) g.DrawPath(pen, path);
        var backLeft = front.Left + Snap(shift, 0);
        var backTop = front.Top - Snap(shift, 0);
        var backRight = front.Right + Snap(shift, 0);
        var backBottom = front.Bottom - Snap(shift, 0);
        g.DrawLines(pen, [
            new PointF(backLeft, front.Top),
            new PointF(backLeft, backTop),
            new PointF(backRight, backTop),
            new PointF(backRight, backBottom),
            new PointF(front.Right, backBottom),
        ]);
    }

    public static void Close(Graphics g, RectangleF r, Color c, float scale)
    {
        using var pen = Pen(c, scale);
        var left = r.Left + r.Width * 0.25f;
        var top = r.Top + r.Height * 0.25f;
        var right = r.Right - r.Width * 0.25f;
        var bottom = r.Bottom - r.Height * 0.25f;
        g.DrawLine(pen, left, top, right, bottom);
        g.DrawLine(pen, right, top, left, bottom);
    }

    /// <summary>Sun of the light/dark switch (shown while the window is dark: "switch to light").</summary>
    public static void Sun(Graphics g, RectangleF r, Color c)
    {
        using var pen = new Pen(c, Math.Max(1.5f, r.Width / 11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var center = new PointF(r.Left + r.Width / 2, r.Top + r.Height / 2);
        var core = r.Width * 0.22f;
        g.DrawEllipse(pen, center.X - core, center.Y - core, core * 2, core * 2);
        for (var i = 0; i < 8; i++)
        {
            var a = Math.PI / 4 * i;
            var (sin, cos) = ((float)Math.Sin(a), (float)Math.Cos(a));
            g.DrawLine(pen, center.X + cos * r.Width * 0.36f, center.Y + sin * r.Width * 0.36f, center.X + cos * r.Width * 0.47f, center.Y + sin * r.Width * 0.47f);
        }
    }

    /// <summary>Moon of the light/dark switch (shown while the window is light: "switch to dark").</summary>
    public static void Moon(Graphics g, RectangleF r, Color c)
    {
        using var outer = new GraphicsPath();
        outer.AddEllipse(RectangleF.Inflate(r, -r.Width * 0.12f, -r.Height * 0.12f));
        using var cut = new GraphicsPath();
        cut.AddEllipse(new RectangleF(r.Left + r.Width * 0.38f, r.Top - r.Height * 0.05f, r.Width * 0.7f, r.Height * 0.7f));
        using var region = new Region(outer);
        region.Exclude(cut);
        using var brush = new SolidBrush(c);
        g.FillRegion(brush, region);
    }

    private static Pen Pen(Color c, float scale) =>
        new(c, StrokeWidth(scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    // With PixelOffsetMode.Half (Draw.Smooth) a pixel's centre lies at n + 0.5: an odd stroke is crisp on .5, an even one on whole numbers
    private static float Snap(float value, float stroke) =>
        (int)stroke % 2 == 1 ? MathF.Floor(value) + 0.5f : MathF.Round(value);

    private static RectangleF SnapRect(float x, float y, float width, float height, float stroke)
    {
        var left = Snap(x, stroke);
        var top = Snap(y, stroke);
        var size = MathF.Round(Math.Min(width, height));
        return new RectangleF(left, top, size, size);
    }
}
