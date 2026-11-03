namespace PlatinVault.Desktop.Ui;

/// <summary>A monitor as the placement sees it: whole area, working area (without the taskbar), primary or not, its scale (1.0 = 100 %).</summary>
internal readonly record struct ScreenArea(Rectangle Bounds, Rectangle WorkingArea, bool Primary, float Scale);

/// <summary>What desktop.json remembers of the window: the normal (not maximised) bounds in device pixels and whether it was maximised.</summary>
internal readonly record struct StoredWindow(int? X, int? Y, int? Width, int? Height, bool Maximized);

/// <summary>Where the window opens: its normal bounds, maximised or not, and the index of the monitor it belongs to.</summary>
internal readonly record struct PlacementPlan(Rectangle Normal, bool Maximized, int Screen);

/// <summary>
/// Where the main window opens and what of it is remembered - pure, so every case is unit-tested without a window:
/// <list type="bullet">
/// <item>First start (nothing stored yet): maximised on the primary monitor; "restore" then gives a centred window of 1440 x 900
/// design units (smaller on a small screen).</item>
/// <item>Later starts: what the user left - maximised or the normal bounds - on the monitor those bounds belong to.</item>
/// <item>The window is never larger than the working area of its monitor and lies completely inside it, so the title bar with its
/// buttons is always reachable. A monitor that is gone: the window opens centred (or maximised) on the primary monitor.</item>
/// <item>Values that are no real window position (Windows' -32000 for minimised/off-screen windows, a size of 0) are ignored completely,
/// as if nothing had been stored.</item>
/// </list>
/// </summary>
internal static class WindowPlacement
{
    /// <summary>Default normal size in design units (96-dpi pixels).</summary>
    public static readonly Size DefaultSize = new(1440, 900);

    /// <summary>
    /// Smallest window in design units. Small on purpose: Windows 11's snap layouts need at most 500 effective pixels of width
    /// (learn.microsoft.com, "Support snap layouts"), and two windows stacked on a 1080p screen at 250 % leave about 400 in height.
    /// </summary>
    public static readonly Size MinimumSize = new(500, 360);

    /// <summary>Free edge around a default-size window on a small screen, in design units.</summary>
    public const int Margin = 40;

    /// <summary>Windows parks minimised (and our test runs' off-screen) windows at -32000; anything that far out is no position.</summary>
    public const int OffscreenLimit = -30000;

    public static PlacementPlan Plan(StoredWindow stored, IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0) throw new ArgumentException("No screen.", nameof(screens));
        var primary = PrimaryIndex(screens);

        if (!IsUsable(stored))
        {
            // First start (or garbage): maximised on the primary monitor, the normal bounds centred there
            return new PlacementPlan(Centered(DefaultFor(screens[primary]), screens[primary]), true, primary);
        }

        var rect = new Rectangle(stored.X!.Value, stored.Y!.Value, stored.Width!.Value, stored.Height!.Value);
        var index = BestScreen(rect, screens);
        if (index < 0)
        {
            // The monitor of the stored bounds is gone: the same size (as far as it fits), centred on the primary monitor
            var area = screens[primary];
            return new PlacementPlan(Centered(Clamp(rect.Size, area), area), stored.Maximized, primary);
        }

        var screen = screens[index];
        var size = Clamp(rect.Size, screen);
        var work = screen.WorkingArea;
        var x = Math.Clamp(rect.X, work.Left, work.Right - size.Width);
        var y = Math.Clamp(rect.Y, work.Top, work.Bottom - size.Height);
        return new PlacementPlan(new Rectangle(x, y, size.Width, size.Height), stored.Maximized, index);
    }

    /// <summary>
    /// What to write into desktop.json when the window closes, or null to write nothing: a test or off-screen run never remembers
    /// its bounds, and bounds that are no real position (off every monitor, -32000, empty) are never stored.
    /// </summary>
    public static StoredWindow? ToStore(Rectangle normal, bool maximized, IReadOnlyList<ScreenArea> screens, bool persist)
    {
        if (!persist) return null;
        var candidate = new StoredWindow(normal.X, normal.Y, normal.Width, normal.Height, maximized);
        if (!IsUsable(candidate) || BestScreen(normal, screens) < 0) return null;
        return candidate;
    }

    public static bool IsUsable(StoredWindow stored) =>
        stored is { X: { } x, Y: { } y, Width: { } width, Height: { } height } &&
        width > 0 && height > 0 && x > OffscreenLimit && y > OffscreenLimit;

    /// <summary>The monitor whose working area overlaps the rectangle most; -1 when it overlaps none.</summary>
    public static int BestScreen(Rectangle rect, IReadOnlyList<ScreenArea> screens)
    {
        var best = -1;
        long bestArea = 0;
        for (var i = 0; i < screens.Count; i++)
        {
            var overlap = Rectangle.Intersect(rect, screens[i].WorkingArea);
            long area = (long)Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);
            if (area > bestArea)
            {
                best = i;
                bestArea = area;
            }
        }
        return best;
    }

    private static int PrimaryIndex(IReadOnlyList<ScreenArea> screens)
    {
        for (var i = 0; i < screens.Count; i++)
        {
            if (screens[i].Primary) return i;
        }
        return 0;
    }

    /// <summary>The default size on a monitor: 1440 x 900 design units, at most the working area minus a margin.</summary>
    public static Size DefaultFor(ScreenArea screen)
    {
        var work = screen.WorkingArea;
        var margin = Px(Margin, screen.Scale);
        var width = Math.Min(Px(DefaultSize.Width, screen.Scale), work.Width - 2 * margin);
        var height = Math.Min(Px(DefaultSize.Height, screen.Scale), work.Height - 2 * margin);
        return Clamp(new Size(width, height), screen);
    }

    /// <summary>At least the minimum size (as far as the screen allows), never more than the working area.</summary>
    public static Size Clamp(Size size, ScreenArea screen)
    {
        var work = screen.WorkingArea;
        var minWidth = Math.Min(Px(MinimumSize.Width, screen.Scale), work.Width);
        var minHeight = Math.Min(Px(MinimumSize.Height, screen.Scale), work.Height);
        return new Size(Math.Clamp(size.Width, minWidth, work.Width), Math.Clamp(size.Height, minHeight, work.Height));
    }

    private static Rectangle Centered(Size size, ScreenArea screen)
    {
        var work = screen.WorkingArea;
        return new Rectangle(work.Left + (work.Width - size.Width) / 2, work.Top + (work.Height - size.Height) / 2, size.Width, size.Height);
    }

    private static int Px(int units, float scale) => (int)Math.Round(units * scale);
}
