using System.Runtime.InteropServices;
using System.Windows.Forms;
using PlatinVault.Desktop;
using PlatinVault.Desktop.Ui;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Windows.Tests;

/// <summary>Geometry and hit testing of the program's own title bar (pure, no window).</summary>
public sealed class CaptionLayoutTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2.5f)]
    public void The_three_commands_sit_at_the_right_edge_over_the_full_bar_height(float scale)
    {
        var layout = CaptionLayout.Compute(1200, scale);
        Assert.Equal((int)Math.Round(40 * scale), layout.Height);
        Assert.Equal(1200, layout.Close.Right);
        Assert.Equal(layout.Close.Left, layout.Maximize.Right);
        Assert.Equal(layout.Maximize.Left, layout.Minimize.Right);
        foreach (var cell in new[] { layout.Minimize, layout.Maximize, layout.Close })
        {
            Assert.Equal(0, cell.Top);
            Assert.Equal(layout.Height, cell.Height);
            Assert.Equal((int)Math.Round(46 * scale), cell.Width);
            // The visible square lies inside its cell
            Assert.True(cell.Contains(layout.Pill(cell)));
        }
    }

    [Fact]
    public void PartAt_names_the_command_under_a_point()
    {
        var layout = CaptionLayout.Compute(1000, 1f);
        Assert.Equal(CaptionPart.Close, layout.PartAt(new Point(999, 0)));
        Assert.Equal(CaptionPart.Maximize, layout.PartAt(new Point(1000 - 46 - 1, 20)));
        Assert.Equal(CaptionPart.Minimize, layout.PartAt(new Point(1000 - 2 * 46 - 1, 39)));
        Assert.Equal(CaptionPart.None, layout.PartAt(new Point(500, 20)));
        Assert.Equal(CaptionPart.None, layout.PartAt(new Point(999, 40))); // below the bar
    }
}

public sealed class WindowFrameHitTestTests
{
    private static readonly CaptionLayout Layout = CaptionLayout.Compute(1000, 1f);
    private const int Border = 8;

    [Theory]
    [InlineData(500, 0, WindowFrame.HtTop)]
    [InlineData(500, 7, WindowFrame.HtTop)]
    [InlineData(500, 8, WindowFrame.HtCaption)]
    [InlineData(3, 2, WindowFrame.HtTopLeft)]
    [InlineData(997, 2, WindowFrame.HtTopRight)]
    [InlineData(20, 20, WindowFrame.HtCaption)] // trophy and title move the window too
    [InlineData(1000 - 23, 20, WindowFrame.HtClose)]
    [InlineData(1000 - 46 - 23, 20, WindowFrame.HtMaxButton)]
    [InlineData(1000 - 92 - 23, 20, WindowFrame.HtMinButton)]
    [InlineData(1000 - 46 - 23, 39, WindowFrame.HtMaxButton)]
    [InlineData(500, 40, WindowFrame.HtClient)] // the page below the bar
    [InlineData(1000 - 23, 40, WindowFrame.HtClient)]
    public void A_normal_window_resizes_at_the_top_and_drags_everywhere_else(int x, int y, int expected) =>
        Assert.Equal(expected, WindowFrame.HitTest(new Point(x, y), Layout, Border, maximized: false));

    [Fact]
    public void Above_the_commands_the_resize_band_ends_where_their_square_begins()
    {
        // Pill top = 5: rows 0-4 resize, from row 5 the maximise button (snap layouts) is hit
        Assert.Equal(WindowFrame.HtTop, WindowFrame.HitTest(new Point(1000 - 46 - 23, 4), Layout, Border, false));
        Assert.Equal(WindowFrame.HtMaxButton, WindowFrame.HitTest(new Point(1000 - 46 - 23, 5), Layout, Border, false));
    }

    [Theory]
    [InlineData(999, 0, WindowFrame.HtClose)] // the top-right corner of the screen closes (Fitts)
    [InlineData(500, 0, WindowFrame.HtCaption)]
    [InlineData(0, 0, WindowFrame.HtCaption)]
    [InlineData(1000 - 46 - 23, 0, WindowFrame.HtMaxButton)]
    public void A_maximised_window_has_no_top_resize_edge(int x, int y, int expected) =>
        Assert.Equal(expected, WindowFrame.HitTest(new Point(x, y), Layout, Border, maximized: true));

    [Fact]
    public void Hit_codes_map_back_to_the_commands()
    {
        Assert.Equal(CaptionPart.Minimize, WindowFrame.PartOf(WindowFrame.HtMinButton));
        Assert.Equal(CaptionPart.Maximize, WindowFrame.PartOf(WindowFrame.HtMaxButton));
        Assert.Equal(CaptionPart.Close, WindowFrame.PartOf(WindowFrame.HtClose));
        Assert.Equal(CaptionPart.None, WindowFrame.PartOf(WindowFrame.HtCaption));
        Assert.Equal(CaptionPart.None, WindowFrame.PartOf(WindowFrame.HtTop));
    }
}

public sealed class WindowFrameClientAreaTests
{
    // Windows' standard client of a window at (100, 50)-(900, 650) with an 8-px frame and a 31-px caption
    private static readonly Rectangle Standard = Rectangle.FromLTRB(108, 50 + 31 + 8, 892, 642);

    [Fact]
    public void A_normal_window_gives_only_the_caption_to_the_client()
    {
        var client = WindowFrame.ClientArea(Standard, windowTop: 50, maximized: false, resizeBorder: 8, WindowFrame.Edges.Bottom);
        Assert.Equal(Rectangle.FromLTRB(108, 50, 892, 642), client); // left/right/bottom keep the (invisible) resize borders
    }

    [Fact]
    public void A_maximised_window_loses_its_overhang_at_the_top()
    {
        // Maximised on a 1920 x 1080 monitor: the window hangs 8 px over every edge
        var standard = Rectangle.FromLTRB(0, -8 + 31 + 8, 1920, 1040);
        var client = WindowFrame.ClientArea(standard, windowTop: -8, maximized: true, resizeBorder: 8, WindowFrame.Edges.None);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 1920, 1040), client);
    }

    [Fact]
    public void An_auto_hiding_taskbar_keeps_a_small_gap_to_reveal_it()
    {
        var standard = Rectangle.FromLTRB(0, 31, 1920, 1080);
        var client = WindowFrame.ClientArea(standard, windowTop: -8, maximized: true, resizeBorder: 8, WindowFrame.Edges.Bottom | WindowFrame.Edges.Left);
        Assert.Equal(Rectangle.FromLTRB(2, 0, 1920, 1078), client);
    }
}

/// <summary>Where the main window opens and what it remembers (WindowPlacement).</summary>
public sealed class WindowPlacementTests
{
    // The user's setup (2026-11-02): a 4K primary at 250 %, a portrait screen at 100 % left of it, a second 4K at 250 % to the right
    private static readonly List<ScreenArea> Screens =
    [
        new(new Rectangle(0, 0, 3840, 2160), new Rectangle(0, 0, 3840, 2040), Primary: true, Scale: 2.5f),
        new(new Rectangle(-1080, -836, 1080, 1920), new Rectangle(-1080, -836, 1080, 1872), Primary: false, Scale: 1f),
        new(new Rectangle(3840, 0, 3840, 2160), new Rectangle(3840, 0, 3840, 2040), Primary: false, Scale: 2.5f),
    ];

    private static readonly Rectangle PrimaryWork = Screens[0].WorkingArea;

    [Fact]
    public void The_first_start_opens_maximised_on_the_primary_monitor()
    {
        var plan = WindowPlacement.Plan(new StoredWindow(null, null, null, null, false), Screens);
        Assert.True(plan.Maximized);
        Assert.Equal(0, plan.Screen);
        // Restoring gives 1440 x 900 design units, centred
        Assert.Equal(new Size(3600, 2040 - 200), plan.Normal.Size); // 900 x 2.5 = 2250 > working area - margins: clamped
        Assert.True(PrimaryWork.Contains(plan.Normal));
        Assert.Equal(PrimaryWork.Left + (PrimaryWork.Width - plan.Normal.Width) / 2, plan.Normal.Left);
    }

    [Fact]
    public void The_bounds_an_offscreen_run_stored_are_ignored_completely()
    {
        // The bug: an -Offscreen run saved its fake window; the next start kept 3600 x 2250 and the title bar was off the screen
        var plan = WindowPlacement.Plan(new StoredWindow(-32000, -32000, 3600, 2250, false), Screens);
        Assert.True(plan.Maximized);
        Assert.Equal(0, plan.Screen);
        Assert.True(PrimaryWork.Contains(plan.Normal));
    }

    [Fact]
    public void A_stored_window_larger_than_its_screen_is_shrunk_to_the_working_area()
    {
        var plan = WindowPlacement.Plan(new StoredWindow(-500, -300, 9000, 5000, false), Screens);
        Assert.False(plan.Maximized);
        Assert.Equal(PrimaryWork, plan.Normal);
    }

    [Fact]
    public void A_window_on_the_portrait_screen_stays_there_and_is_moved_fully_onto_it()
    {
        // Half above the screen: the title bar must come back into reach
        var plan = WindowPlacement.Plan(new StoredWindow(-1000, -1200, 900, 1000, false), Screens);
        Assert.Equal(1, plan.Screen);
        var work = Screens[1].WorkingArea;
        Assert.True(work.Contains(plan.Normal), $"{plan.Normal} must lie inside {work}");
        Assert.Equal(new Size(900, 1000), plan.Normal.Size);
        Assert.Equal(work.Top, plan.Normal.Top);
    }

    [Fact]
    public void A_maximised_window_on_a_monitor_that_is_gone_opens_maximised_on_the_primary()
    {
        var plan = WindowPlacement.Plan(new StoredWindow(12000, 100, 2000, 1500, true), Screens);
        Assert.True(plan.Maximized);
        Assert.Equal(0, plan.Screen);
        Assert.True(PrimaryWork.Contains(plan.Normal));
        Assert.Equal(new Size(2000, 1500), plan.Normal.Size);
    }

    [Fact]
    public void A_maximised_window_on_the_second_monitor_opens_maximised_there()
    {
        var plan = WindowPlacement.Plan(new StoredWindow(4000, 200, 2400, 1600, true), Screens);
        Assert.True(plan.Maximized);
        Assert.Equal(2, plan.Screen);
        Assert.Equal(new Rectangle(4000, 200, 2400, 1600), plan.Normal);
    }

    [Fact]
    public void A_tiny_stored_window_gets_the_minimum_size_of_its_screen()
    {
        var plan = WindowPlacement.Plan(new StoredWindow(100, 100, 50, 50, false), Screens);
        Assert.Equal(new Size(1250, 900), plan.Normal.Size); // 500 x 360 design units at 250 %
    }

    [Fact]
    public void A_screen_smaller_than_the_minimum_size_still_gets_a_window_that_fits()
    {
        List<ScreenArea> small = [new(new Rectangle(0, 0, 800, 500), new Rectangle(0, 0, 800, 460), true, 2f)];
        var plan = WindowPlacement.Plan(new StoredWindow(null, null, null, null, false), small);
        Assert.True(small[0].WorkingArea.Contains(plan.Normal));
    }

    [Fact]
    public void Test_and_offscreen_runs_never_store_their_bounds()
    {
        Assert.Null(WindowPlacement.ToStore(new Rectangle(100, 100, 1400, 900), false, Screens, persist: false));
        Assert.Null(WindowPlacement.ToStore(new Rectangle(-32000, -32000, 3600, 2250), false, Screens, persist: true));
        Assert.Null(WindowPlacement.ToStore(new Rectangle(20000, 100, 1400, 900), false, Screens, persist: true));
        Assert.Null(WindowPlacement.ToStore(new Rectangle(100, 100, 0, 900), false, Screens, persist: true));
        Assert.Equal(new StoredWindow(100, 120, 1400, 900, true), WindowPlacement.ToStore(new Rectangle(100, 120, 1400, 900), true, Screens, persist: true));
    }
}

/// <summary>WCAG contrast of the title bar in light, dark and the start screen's brand surface (rule: at least 4.5:1, also for glyphs).</summary>
public sealed class CaptionColorTests
{
    public static TheoryData<string> Palettes => ["light", "dark", "brand"];

    private static Palette PaletteOf(string name) => name switch
    {
        "light" => Palette.Light,
        "dark" => Palette.Dark,
        _ => CaptionColors.BrandPalette,
    };

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Glyphs_and_title_are_readable_on_every_background(string name)
    {
        var p = PaletteOf(name);
        var bar = CaptionColors.Bar(p);
        foreach (var close in new[] { false, true })
        {
            foreach (var state in new[] { CaptionState.Normal, CaptionState.Hover, CaptionState.Pressed })
            {
                foreach (var active in new[] { true, false })
                {
                    var back = CaptionColors.ButtonBack(p, bar, close, state);
                    var glyph = CaptionColors.Glyph(p, close, state, active);
                    var ratio = Contrast(glyph, back);
                    Assert.True(ratio >= 4.5, $"{name} close={close} {state} active={active}: {ratio:0.00}:1");
                }
            }
        }
        Assert.True(Contrast(CaptionColors.Title(p, true), bar) >= 4.5);
        Assert.True(Contrast(CaptionColors.Title(p, false), bar) >= 4.5);
        var (badgeBack, badgeFore) = CaptionColors.Badge(p);
        Assert.True(Contrast(badgeFore, badgeBack) >= 4.5, $"{name} badge: {Contrast(badgeFore, badgeBack):0.00}:1");
    }

    [Fact]
    public void Close_is_windows_red_with_a_white_cross()
    {
        Assert.Equal(Palette.Hex("#c42b1c"), CaptionColors.CloseHover);
        Assert.True(Contrast(Color.White, CaptionColors.CloseHover) >= 4.5);
        Assert.True(Contrast(Color.White, CaptionColors.ClosePressed) >= 4.5);
    }

    private static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}

/// <summary>The page reports its light/dark choice to the window (MainWindow.ThemeFromMessage).</summary>
public sealed class ThemeMessageTests
{
    [Theory]
    [InlineData("{\"pvTheme\":\"dark\"}", true)]
    [InlineData("{\"pvTheme\":\"light\"}", false)]
    [InlineData("{\"pvTheme\":\"system\"}", null)]
    [InlineData("{\"pvTheme\":1}", null)]
    [InlineData("\"dark\"", null)]
    [InlineData("{}", null)]
    [InlineData("not json", null)]
    public void Only_a_clear_light_or_dark_is_taken(string json, bool? expected) =>
        Assert.Equal(expected, MainWindow.ThemeFromMessage(json));
}

/// <summary>
/// The real window (handle created, never shown): bar and page never overlap, the bar answers WM_NCHITTEST like a caption, the client
/// starts at the window's top edge. Offscreen pictures (DrawToBitmap) cannot show a covered control, so this checks the real order.
/// </summary>
public sealed class ChromeWindowTests
{
    private sealed class TestWindow : ChromeWindow
    {
        public void Add(Control control) => AddContent(control);

        public void UseScale(float s) => ApplyScale(s);
    }

    private static void OnSta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void Content_fills_the_window_below_the_bar_and_never_covers_it()
    {
        OnSta(() =>
        {
            using var window = new TestWindow { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
            var start = new Panel { Name = "start" };
            var web = new Panel { Name = "web", Visible = false };
            window.Add(start);
            window.Add(web);
            window.Bounds = new Rectangle(-32000, -32000, 1200, 800);
            _ = window.Handle;
            // After the handle (which takes the real monitor scale): a fixed scale for the assertion
            window.UseScale(1.5f);
            web.Visible = true;
            web.BringToFront();
            start.Visible = false;
            window.PerformLayout();

            var bar = window.TitleBar;
            Assert.True(HasVisibleStyle(bar), "The title bar must be visible (WS_VISIBLE)");
            Assert.True(HasVisibleStyle(web));
            Assert.Equal(new Point(0, 0), bar.Location);
            Assert.Equal(60, bar.Height); // 40 design units at 150 %
            Assert.Equal(window.ClientSize.Width, bar.Width);
            foreach (var content in new Control[] { start, web })
            {
                Assert.Equal(bar.Bottom, content.Top);
                Assert.Equal(window.ClientSize.Height - bar.Height, content.Height);
                Assert.False(content.Bounds.IntersectsWith(bar.Bounds), $"{content.Name} overlaps the title bar");
            }
            // The page is in front of the hidden start screen; the bar is behind both (docked first) but nothing lies on it
            Assert.True(window.Controls.GetChildIndex(web) < window.Controls.GetChildIndex(start));
        });
    }

    [Fact]
    public void The_window_answers_the_hit_test_for_the_bar_and_the_client_starts_at_the_top_edge()
    {
        OnSta(() =>
        {
            using var window = new TestWindow { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
            window.Add(new Panel());
            window.Bounds = new Rectangle(200, 200, 1000, 700);
            _ = window.Handle;
            window.PerformLayout();

            // WM_NCCALCSIZE: no caption, the client begins at the window's top edge (left/right/bottom keep the resize borders)
            Assert.Equal(window.Bounds.Top, window.PointToScreen(Point.Empty).Y);
            Assert.True(window.PointToScreen(Point.Empty).X >= window.Bounds.Left);

            var bar = window.TitleBar;
            var layout = bar.Geometry;
            var origin = window.PointToScreen(Point.Empty);
            int HitAt(Point client) => (int)SendMessage(window.Handle, 0x0084, IntPtr.Zero, MakeLParam(origin.X + client.X, origin.Y + client.Y));
            Assert.Equal(WindowFrame.HtClose, HitAt(new Point(layout.Close.Left + layout.Close.Width / 2, layout.Height / 2)));
            Assert.Equal(WindowFrame.HtMaxButton, HitAt(new Point(layout.Maximize.Left + layout.Maximize.Width / 2, layout.Height / 2)));
            Assert.Equal(WindowFrame.HtMinButton, HitAt(new Point(layout.Minimize.Left + layout.Minimize.Width / 2, layout.Height / 2)));
            Assert.Equal(WindowFrame.HtCaption, HitAt(new Point(layout.Width / 2, layout.Height / 2)));
            Assert.Equal(WindowFrame.HtTop, HitAt(new Point(layout.Width / 2, 1)));
            // The bar itself is transparent for hit testing, so all of this reaches the window
            Assert.Equal(WindowFrame.HtTransparent, (int)SendMessage(bar.Handle, 0x0084, IntPtr.Zero, MakeLParam(origin.X + 10, origin.Y + 10)));
        });
    }

    [Fact]
    public void Screen_readers_see_three_named_buttons_that_can_be_invoked()
    {
        OnSta(() =>
        {
            using var bar = new PvTitleBar { Width = 900 };
            var invoked = new List<CaptionPart>();
            bar.Command += invoked.Add;
            var accessible = bar.AccessibilityObject;
            Assert.Equal(AccessibleRole.TitleBar, accessible.Role);
            Assert.Equal(3, accessible.GetChildCount());
            var names = Enumerable.Range(0, 3).Select(i => accessible.GetChild(i)!.Name).ToList();
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            accessible.GetChild(2)!.DoDefaultAction();
            Assert.Equal([CaptionPart.Close], invoked);
            bar.Maximized = true;
            Assert.NotEqual(names[1], accessible.GetChild(1)!.Name); // "Maximieren" becomes "Verkleinern"
        });
    }

    [Fact]
    public void The_main_window_puts_its_start_screen_below_the_title_bar()
    {
        OnSta(() =>
        {
            using var temp = new TempFolder();
            var env = DesktopEnvironment.Create(DesktopOptions.Parse(["--test-root", temp.Path]));
            using var window = new MainWindow(env, new DesktopSettings());
            window.Bounds = new Rectangle(-32000, -32000, 1400, 900);
            _ = window.Handle;
            window.PerformLayout();
            var bar = window.TitleBar;
            Assert.True(HasVisibleStyle(bar), "The title bar must be visible (WS_VISIBLE)");
            Assert.True(HasVisibleStyle(window.StartSurface));
            Assert.True(bar.BrandMode); // continues the night-blue start screen until the page is loaded
            Assert.Equal(bar.Bottom, window.StartSurface.Top);
            Assert.False(window.StartSurface.Bounds.IntersectsWith(bar.Bounds));
            Assert.Equal(window.S, window.StartSurface.S);
        });
    }

    // The window itself is never shown in a test, so Control.Visible is false for every child: check the child window's own style
    private static bool HasVisibleStyle(Control control) => (GetWindowLong(control.Handle, -16) & 0x10000000) != 0;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    private static IntPtr MakeLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
