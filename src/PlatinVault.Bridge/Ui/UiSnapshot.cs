using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PlatinVault.Bridge.Ui;

/// <summary>
/// Test aid behind the hidden <c>--test-snapshot-menu</c> option: draws the tray context menu and the tray icon variants
/// into a PNG without showing anything on screen (a tray icon itself cannot be photographed from outside).
/// </summary>
internal static class UiSnapshot
{
    public static void CaptureTrayMenu(ContextMenuStrip menu, string pngPath, Icon normal, Icon dimmed, Action refreshMenuState)
    {
        try
        {
            refreshMenuState();
            menu.PerformLayout();
            var size = menu.GetPreferredSize(Size.Empty);
            menu.Size = size;
            _ = menu.Handle;

            using var menuImage = new Bitmap(size.Width, size.Height);
            menu.DrawToBitmap(menuImage, new Rectangle(Point.Empty, size));

            const int zoom = 6;
            var canvas = new Bitmap(Math.Max(size.Width, 2 * (32 * zoom + 24)) + 40, size.Height + 32 * zoom + 120);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
                using var font = new Font("Segoe UI", 9f);
                g.DrawString("Kontextmenü (Rechtsklick auf das Tray-Symbol)", font, Brushes.Black, 20, 8);
                g.DrawImage(menuImage, 20, 30);
                g.DrawRectangle(Pens.Gray, 20, 30, size.Width, size.Height);

                var y = 30 + size.Height + 24;
                g.DrawString("Tray-Symbol: verbunden (links) und nicht verbunden (rechts), je 6-fach vergrößert", font, Brushes.Black, 20, y);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.FillRectangle(new SolidBrush(Color.FromArgb(32, 32, 32)), 20, y + 24, 2 * (32 * zoom + 24), 32 * zoom + 16);
                using var normalBitmap = normal.ToBitmap();
                using var dimmedBitmap = dimmed.ToBitmap();
                g.DrawImage(normalBitmap, new Rectangle(32, y + 32, normal.Width * zoom, normal.Height * zoom));
                g.DrawImage(dimmedBitmap, new Rectangle(32 + 32 * zoom + 24, y + 32, dimmed.Width * zoom, dimmed.Height * zoom));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
            canvas.Save(pngPath, ImageFormat.Png);
            canvas.Dispose();
            BridgeLog.Info($"Testbild des Tray-Menüs gespeichert: {pngPath}");
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException or InvalidOperationException)
        {
            BridgeLog.Warn($"Testbild des Tray-Menüs fehlgeschlagen: {ex.Message}");
        }
    }
}
