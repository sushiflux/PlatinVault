using System.Windows.Forms;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Windows.Tests;

/// <summary>
/// Rule "error messages are always copyable" for the native windows: the text a Copy button puts on the clipboard, the selectable
/// message text and the copy bar. The real clipboard is never touched (headless mode records the copied text instead).
/// </summary>
[Collection("Headless dialogs")]
public sealed class ErrorCopyTests
{
    [Fact]
    public void The_copied_text_is_the_complete_message_then_a_blank_line_then_version_and_time()
    {
        var at = new DateTimeOffset(2026, 11, 3, 14, 5, 12, TimeSpan.FromHours(1));
        var text = ErrorReport.Build("Das Update ist fehlgeschlagen.\r\nGrund: Access to the path 'PlatinVault.exe' is denied.\n", "v2026.11.14 (13.557)", at);

        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Das Update ist fehlgeschlagen.", lines[0]);
        Assert.Equal("Grund: Access to the path 'PlatinVault.exe' is denied.", lines[1]);
        Assert.Equal(string.Empty, lines[2]);
        Assert.Equal("PlatinVault v2026.11.14 (13.557) · 2026-11-03 14:05:12 +01:00", lines[3]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void Without_a_known_version_only_the_time_follows_the_message()
    {
        var saved = ErrorReport.AppVersion;
        ErrorReport.AppVersion = null;
        try
        {
            var text = ErrorReport.Build("Fehler", null, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
            Assert.EndsWith("PlatinVault · 2026-01-02 03:04:05 +00:00", text, StringComparison.Ordinal);
        }
        finally
        {
            ErrorReport.AppVersion = saved;
        }
    }

    [Fact]
    public void A_headless_run_records_the_copy_and_never_touches_the_real_clipboard()
    {
        var before = Dialogs.Headless;
        Dialogs.Headless = true;
        try
        {
            Assert.True(ErrorReport.TryCopy("abc"));
            Assert.Equal("abc", ErrorReport.LastCopied);
        }
        finally
        {
            Dialogs.Headless = before;
        }
    }

    [Fact]
    public void The_message_text_shows_all_of_a_long_text_and_selects_exactly_what_is_there()
    {
        var text = string.Concat(Enumerable.Repeat("The database could not be set up: SQLite Error 14: 'unable to open database file'. ", 40)).TrimEnd();
        using var box = new PvScrollText { S = 1f, P = Palette.Light, Surface = Palette.Light.Page, Bounds = new Rectangle(0, 0, 420, 90) };
        box.SetText(text);

        Assert.True(box.Overflows);
        Assert.True(box.ContentHeight > 90);
        box.SelectAll();
        Assert.Equal(text, box.SelectedText);

        box.Select(4, 8);
        Assert.Equal(text.Substring(4, 8), box.SelectedText);
    }

    [Fact]
    public void The_line_breaker_loses_no_character_and_breaks_a_word_wider_than_the_line()
    {
        var font = Fonts.Get(9.5f, 1f);
        var text = "short words and one " + new string('x', 400) + " more words after it";
        var pieces = PvScrollText.Wrap(text, font, 200).ToList();

        Assert.True(pieces.Count > 3);
        Assert.All(pieces, p => Assert.True(p.Length > 0));
        // Everything but the spaces at the breaks is on some line, in order
        var joined = string.Concat(pieces.Select(p => text.Substring(p.Start, p.Length)));
        Assert.Equal(text.Replace(" ", string.Empty), joined.Replace(" ", string.Empty));
        // The pieces follow each other without overlap
        for (var i = 1; i < pieces.Count; i++) Assert.True(pieces[i].Start >= pieces[i - 1].Start + pieces[i - 1].Length);
    }

    [Fact]
    public void Ctrl_C_in_the_message_text_copies_the_selection_or_everything()
    {
        var before = Dialogs.Headless;
        Dialogs.Headless = true;
        try
        {
            using var box = new PvScrollText { S = 1f, P = Palette.Light, Surface = Palette.Light.Page, Bounds = new Rectangle(0, 0, 300, 60) };
            box.SetText("alpha beta gamma");
            box.Select(6, 4);
            Assert.True(box.CopyToClipboard());
            Assert.Equal("beta", ErrorReport.LastCopied);

            box.Select(0, 0);
            Assert.True(box.CopyToClipboard());
            Assert.Equal("alpha beta gamma", ErrorReport.LastCopied);
        }
        finally
        {
            Dialogs.Headless = before;
        }
    }

    [Fact]
    public void The_copy_bar_copies_the_message_with_version_and_time_and_shows_a_short_confirmation()
    {
        var before = Dialogs.Headless;
        Dialogs.Headless = true;
        try
        {
            var message = "Die Datenbank konnte nicht eingerichtet werden: SQLite Error 14: 'unable to open database file'.";
            var bar = new CopyBar(() => message, 1f, Palette.Light, Palette.Light.Page, "Test");
            bar.Place(new Rectangle(0, 0, 400, 36), 12);
            using var host = new Panel { Size = new Size(400, 36) };
            host.Controls.AddRange([.. bar.Controls]);
            var buttonBounds = bar.Button.Bounds;
            var statusBounds = bar.Status.Bounds;

            Assert.Equal(string.Empty, bar.Status.Text);
            Assert.True(bar.Copy());

            Assert.StartsWith(message, ErrorReport.LastCopied, StringComparison.Ordinal);
            Assert.Contains(" · ", ErrorReport.LastCopied, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(bar.Status.Text));
            // The confirmation fills its reserved slot; nothing moves or changes size
            Assert.Equal(buttonBounds, bar.Button.Bounds);
            Assert.Equal(statusBounds, bar.Status.Bounds);

            bar.ShowOutcome(false);
            Assert.Equal(buttonBounds, bar.Button.Bounds);
            Assert.Equal(statusBounds, bar.Status.Bounds);
            Assert.True(bar.Status.NeededHeight <= bar.Status.Height, "The failure text must fit its slot");
        }
        finally
        {
            Dialogs.Headless = before;
        }
    }

    [Fact]
    public void An_error_dialog_has_a_copy_button_and_the_other_dialogs_do_not()
    {
        var before = Dialogs.Headless;
        Dialogs.Headless = true;
        try
        {
            var ok = new[] { new DialogButton("OK", DialogResult.OK, PvButtonKind.Primary) };
            using var error = new MessageWindow(DialogKind.Error, "Fehler: Zugriff verweigert.", ok, 0, 1f);
            using var info = new MessageWindow(DialogKind.Info, "Hinweis.", ok, 0, 1f);
            Assert.NotNull(error.Copy);
            Assert.Null(info.Copy);

            error.Copy!.Copy();
            Assert.StartsWith("Fehler: Zugriff verweigert.", ErrorReport.LastCopied, StringComparison.Ordinal);
        }
        finally
        {
            Dialogs.Headless = before;
        }
    }
}

/// <summary>The tests above switch the process-wide headless flag: they must not run in parallel with each other.</summary>
[CollectionDefinition("Headless dialogs", DisableParallelization = true)]
public sealed class HeadlessDialogsCollection;
