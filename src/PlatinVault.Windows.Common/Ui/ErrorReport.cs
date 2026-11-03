using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// Rule "error messages are always copyable": builds the text a "Copy" button puts on the clipboard (the complete message, then one
/// line with the app version and the time) and writes it there. A failing clipboard (another program holds it) is reported to the caller,
/// which shows "Copy not possible" - it never throws into the window.
/// </summary>
public static class ErrorReport
{
    /// <summary>The readable version of this program ("v2026.11.14 (13.557)"), set once at startup by each program; null = unknown.</summary>
    public static string? AppVersion { get; set; }

    /// <summary>The text that was copied last (test aid; also set in a headless run, where the real clipboard is never touched).</summary>
    public static string? LastCopied { get; private set; }

    /// <summary>The complete text for the clipboard: the message as shown, a blank line, then "PlatinVault &lt;version&gt; · &lt;time&gt;".</summary>
    public static string Build(string message, string? version = null, DateTimeOffset? at = null)
    {
        var text = message.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n', ' ').Replace("\n", Environment.NewLine);
        var when = (at ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        var stamp = new StringBuilder("PlatinVault");
        var v = version ?? AppVersion;
        if (!string.IsNullOrWhiteSpace(v)) stamp.Append(' ').Append(v);
        stamp.Append(" · ").Append(when);
        return text + Environment.NewLine + Environment.NewLine + stamp;
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard (retrying a few times while another program holds it). False when that fails.</summary>
    public static bool TryCopy(string text)
    {
        if (Dialogs.Headless)
        {
            // A test run never touches the real clipboard of the person running it
            LastCopied = text;
            return true;
        }
        try
        {
            Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 100);
            LastCopied = text;
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException or ArgumentException)
        {
            return false;
        }
    }
}
