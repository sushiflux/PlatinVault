using System.Drawing.Imaging;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// Renders a control with its children in their real stacking order. <see cref="Control.DrawToBitmap"/> paints child controls in
/// reverse z-order (the topmost child first, the bottommost last), so a label lying on a card looks covered in the picture while it
/// is visible in the window - and the other way round. That broke both the page transition (a snapshot of the page) and the
/// offscreen layout pictures. Here every child is painted again from the bottom of the z-order to the top, recursively, so the
/// topmost one ends up on top like on the screen.
/// </summary>
public static class ControlSnapshot
{
    /// <summary>A new bitmap of the control (the caller disposes it).</summary>
    public static Bitmap Capture(Control control) => Capture(control, control.Size);

    /// <summary>A new bitmap of the given size with the control drawn at its top left corner.</summary>
    public static Bitmap Capture(Control control, Size size)
    {
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        Paint(control, g, Point.Empty);
        return bitmap;
    }

    private static void Paint(Control control, Graphics g, Point origin)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        using (var own = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb))
        {
            // The control itself (its children come out in the wrong order here and are painted over right below)
            control.DrawToBitmap(own, new Rectangle(Point.Empty, control.Size));
            g.DrawImageUnscaled(own, origin);
        }

        // Child index 0 is the topmost: paint from the last (bottom) to the first (top)
        for (var i = control.Controls.Count - 1; i >= 0; i--)
        {
            var child = control.Controls[i];
            if (!IsShownInParent(child)) continue;
            Paint(child, g, new Point(origin.X + child.Left, origin.Y + child.Top));
        }
    }

    // Control.Visible is false for every child of a window that was never shown (the offscreen pictures), so the child's own
    // WS_VISIBLE style decides whether it would be visible once its window is
    private static bool IsShownInParent(Control control) =>
        control.IsHandleCreated ? (GetWindowLong(control.Handle, GwlStyle) & WsVisible) != 0 : control.Visible;

    private const int GwlStyle = -16;
    private const int WsVisible = 0x10000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
}
