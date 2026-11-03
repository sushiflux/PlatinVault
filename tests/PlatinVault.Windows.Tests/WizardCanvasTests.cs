using System.Windows.Forms;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Windows.Tests;

/// <summary>
/// The z-order of the installer pages. Offscreen screenshots (DrawToBitmap) paint child controls in reverse order, so a control
/// that is hidden behind another in the real window still looks fine there - this test checks the real order instead.
/// </summary>
public sealed class WizardCanvasTests
{
    [Fact]
    public void A_control_added_later_lies_on_top_of_the_card_it_was_placed_on()
    {
        using var host = new Panel();
        var canvas = new PageCanvas(host, 1f, Palette.Light);

        // The location step: a folder card first, then the path and the "Change" button on top of it
        var card = canvas.Add(new PvText(), 0, 0, PageCanvas.Width, 64, "cardFolder");
        var path = canvas.Text("C:\\Users\\Test\\AppData\\Local\\Programs\\PlatinVault", 58, 30, 300, 26, name: "lblFolder");
        var change = canvas.Add(new PvButton { Text = "Ändern" }, PageCanvas.Width - 122, 14, 110, 36, "btnChangeFolder");

        // Child index 0 is the topmost control in WinForms
        Assert.True(host.Controls.GetChildIndex(change) < host.Controls.GetChildIndex(card), "The button must lie on top of the card.");
        Assert.True(host.Controls.GetChildIndex(path) < host.Controls.GetChildIndex(card), "The path must lie on top of the card.");
        Assert.True(host.Controls.GetChildIndex(change) < host.Controls.GetChildIndex(path), "Later controls lie on top of earlier ones.");
    }

    [Fact]
    public void The_snapshot_paints_children_in_their_real_stacking_order()
    {
        // A card with a label on it, the label topmost - like the folder card of the location step
        using var host = new Panel { Size = new Size(200, 100), BackColor = Color.White };
        using var card = new Panel { Bounds = new Rectangle(10, 10, 180, 80), BackColor = Color.Red };
        using var label = new Panel { Bounds = new Rectangle(40, 30, 60, 30), BackColor = Color.Blue };
        host.Controls.Add(card);
        host.Controls.Add(label);
        label.BringToFront();
        _ = host.Handle;

        using var picture = ControlSnapshot.Capture(host);

        Assert.Equal(Color.Blue.ToArgb(), picture.GetPixel(70, 45).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), picture.GetPixel(20, 20).ToArgb());
        Assert.Equal(Color.White.ToArgb(), picture.GetPixel(2, 2).ToArgb());
    }
}
