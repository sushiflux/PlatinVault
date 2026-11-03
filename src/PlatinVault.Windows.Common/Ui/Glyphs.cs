using System.Drawing.Drawing2D;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// Small line icons for the designed windows (2-px strokes, round caps - the look of the Tabler set the web app uses), drawn as
/// vectors so they stay sharp at every scale. Each takes the box to draw into and the colour.
/// </summary>
public static class Glyphs
{
    private static Pen Stroke(Color c, RectangleF r) =>
        new(c, Math.Max(1.4f, r.Width / 13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    public static void Database(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        var w = r.Width * 0.62f;
        var x = r.Left + (r.Width - w) / 2;
        var h = r.Height * 0.2f;
        var top = r.Top + r.Height * 0.14f;
        var bottom = r.Bottom - r.Height * 0.14f - h;
        g.DrawEllipse(pen, x, top, w, h);
        g.DrawLine(pen, x, top + h / 2, x, bottom + h / 2);
        g.DrawLine(pen, x + w, top + h / 2, x + w, bottom + h / 2);
        g.DrawArc(pen, x, (top + bottom) / 2, w, h, 0, 180);
        g.DrawArc(pen, x, bottom, w, h, 0, 180);
    }

    public static void Lock(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        var body = new RectangleF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.45f, r.Width * 0.56f, r.Height * 0.4f);
        using (var path = Draw.Rounded(body, r.Width * 0.08f)) g.DrawPath(pen, path);
        g.DrawArc(pen, r.Left + r.Width * 0.32f, r.Top + r.Height * 0.15f, r.Width * 0.36f, r.Height * 0.5f, 180, 180);
        g.DrawLine(pen, r.Left + r.Width * 0.32f, r.Top + r.Height * 0.4f, r.Left + r.Width * 0.32f, body.Top);
        g.DrawLine(pen, r.Right - r.Width * 0.32f, r.Top + r.Height * 0.4f, r.Right - r.Width * 0.32f, body.Top);
        g.DrawLine(pen, r.Left + r.Width / 2, body.Top + body.Height * 0.35f, r.Left + r.Width / 2, body.Top + body.Height * 0.65f);
    }

    /// <summary>Two overlapping sheets (the "copy" icon).</summary>
    public static void Copy(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        PointF At(float x, float y) => new(r.Left + r.Width * x, r.Top + r.Height * y);
        // The back sheet is only drawn where the front one does not cover it (an open frame), like the Tabler "copy" icon
        g.DrawLines(pen, [At(0.70f, 0.38f), At(0.70f, 0.14f), At(0.14f, 0.14f), At(0.14f, 0.70f), At(0.38f, 0.70f)]);
        var front = new RectangleF(r.Left + r.Width * 0.38f, r.Top + r.Height * 0.38f, r.Width * 0.48f, r.Height * 0.48f);
        using var path = Draw.Rounded(front, r.Width * 0.08f);
        g.DrawPath(pen, path);
    }

    public static void Clock(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        var circle = RectangleF.Inflate(r, -r.Width * 0.16f, -r.Height * 0.16f);
        g.DrawEllipse(pen, circle);
        var cx = r.Left + r.Width / 2;
        var cy = r.Top + r.Height / 2;
        g.DrawLines(pen, [new PointF(cx, cy - circle.Height * 0.3f), new PointF(cx, cy), new PointF(cx + circle.Width * 0.22f, cy + circle.Height * 0.14f)]);
    }

    public static void Bolt(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        g.DrawPolygon(pen, [
            new PointF(r.Left + r.Width * 0.56f, r.Top + r.Height * 0.12f),
            new PointF(r.Left + r.Width * 0.26f, r.Top + r.Height * 0.56f),
            new PointF(r.Left + r.Width * 0.5f, r.Top + r.Height * 0.56f),
            new PointF(r.Left + r.Width * 0.44f, r.Bottom - r.Height * 0.12f),
            new PointF(r.Right - r.Width * 0.26f, r.Top + r.Height * 0.44f),
            new PointF(r.Left + r.Width * 0.5f, r.Top + r.Height * 0.44f),
        ]);
    }

    public static void Book(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        var cx = r.Left + r.Width / 2;
        var top = r.Top + r.Height * 0.22f;
        var bottom = r.Bottom - r.Height * 0.2f;
        g.DrawLine(pen, cx, top, cx, bottom);
        g.DrawLines(pen, [new PointF(cx, top), new PointF(r.Left + r.Width * 0.14f, top - r.Height * 0.04f), new PointF(r.Left + r.Width * 0.14f, bottom - r.Height * 0.04f), new PointF(cx, bottom)]);
        g.DrawLines(pen, [new PointF(cx, top), new PointF(r.Right - r.Width * 0.14f, top - r.Height * 0.04f), new PointF(r.Right - r.Width * 0.14f, bottom - r.Height * 0.04f), new PointF(cx, bottom)]);
    }

    public static void Home(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        g.DrawLines(pen, [
            new PointF(r.Left + r.Width * 0.16f, r.Top + r.Height * 0.48f),
            new PointF(r.Left + r.Width * 0.5f, r.Top + r.Height * 0.18f),
            new PointF(r.Right - r.Width * 0.16f, r.Top + r.Height * 0.48f),
        ]);
        g.DrawLines(pen, [
            new PointF(r.Left + r.Width * 0.26f, r.Top + r.Height * 0.4f),
            new PointF(r.Left + r.Width * 0.26f, r.Bottom - r.Height * 0.16f),
            new PointF(r.Right - r.Width * 0.26f, r.Bottom - r.Height * 0.16f),
            new PointF(r.Right - r.Width * 0.26f, r.Top + r.Height * 0.4f),
        ]);
    }

    public static void Folder(Graphics g, RectangleF r, Color c)
    {
        using var pen = Stroke(c, r);
        g.DrawLines(pen, [
            new PointF(r.Left + r.Width * 0.14f, r.Bottom - r.Height * 0.2f),
            new PointF(r.Left + r.Width * 0.14f, r.Top + r.Height * 0.24f),
            new PointF(r.Left + r.Width * 0.4f, r.Top + r.Height * 0.24f),
            new PointF(r.Left + r.Width * 0.5f, r.Top + r.Height * 0.34f),
            new PointF(r.Right - r.Width * 0.14f, r.Top + r.Height * 0.34f),
            new PointF(r.Right - r.Width * 0.14f, r.Bottom - r.Height * 0.2f),
            new PointF(r.Left + r.Width * 0.14f, r.Bottom - r.Height * 0.2f),
        ]);
    }
}

/// <summary>A feature line: icon in a soft tinted square, bold title, muted explanation (wraps inside its fixed box).</summary>
public sealed class PvFeature : PvControl
{
    public Action<Graphics, RectangleF, Color>? Icon { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Tint of the icon square: accent (default), gold or emerald.</summary>
    public Func<Palette, (Color Back, Color Fore)> Tint { get; set; } = p => (p.AccentSoft, p.Accent);

    public bool Compact { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var (back, fore) = Tint(P);
        var size = U(Compact ? 32 : 42);
        var square = new RectangleF(U(0.5f), U(Compact ? 0 : 2), size, size);
        Draw.FillRounded(g, square, U(8), back);
        Icon?.Invoke(g, RectangleF.Inflate(square, -size * 0.2f, -size * 0.2f), fore);
        var left = (int)(square.Right + U(14));
        var width = Width - left;
        if (Compact)
        {
            Draw.Text(g, Text, Fonts.Get(10f, S, FontStyle.Bold), P.TextSecondary, new Rectangle(left, 0, width, (int)size), TextFormatFlags.VerticalCenter);
            return;
        }
        var title = Fonts.Get(10.5f, S, FontStyle.Bold);
        var titleH = TextRenderer.MeasureText(g, "Ag", title, Size.Empty, TextFormatFlags.NoPadding).Height;
        Draw.Text(g, Text, title, P.Text, new Rectangle(left, (int)U(1), width, titleH + 2));
        Draw.Text(g, Description, Fonts.Get(9.5f, S), P.Muted, new Rectangle(left, (int)U(1) + titleH + (int)U(3), width, Height - titleH - (int)U(4)));
    }

    /// <summary>Height the texts need at the control's width (layout check).</summary>
    public int NeededHeight
    {
        get
        {
            var left = (int)(U(Compact ? 32 : 42) + U(14));
            if (Compact) return (int)U(32);
            var title = Fonts.Get(10.5f, S, FontStyle.Bold);
            var titleH = TextRenderer.MeasureText("Ag", title, Size.Empty, TextFormatFlags.NoPadding).Height;
            return (int)U(1) + titleH + (int)U(3) + Draw.Measure(Description, Fonts.Get(9.5f, S), Width - left).Height;
        }
    }
}
