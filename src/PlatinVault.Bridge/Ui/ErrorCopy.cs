using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PlatinVault.Bridge.Ui;

/// <summary>
/// Rule "error messages are always copyable": the text a "Copy" button puts on the clipboard (the complete message, a blank line, then
/// the bridge version and the time) and the write itself. A busy clipboard is reported to the caller ("Copy not possible"), never thrown
/// into the window. A test run (<see cref="UiEnvironment.Headless"/>) never touches the real clipboard.
/// </summary>
internal static class ErrorCopy
{
    /// <summary>The text that was copied last (test aid).</summary>
    public static string? LastCopied { get; private set; }

    public static string Build(string message, DateTimeOffset? at = null)
    {
        var version = typeof(ErrorCopy).Assembly.GetName().Version?.ToString(3);
        var text = message.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n', ' ').Replace("\n", Environment.NewLine);
        var stamp = new StringBuilder("PlatinVault Bridge");
        if (!string.IsNullOrEmpty(version)) stamp.Append(' ').Append(version);
        stamp.Append(" · ").Append((at ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        return text + Environment.NewLine + Environment.NewLine + stamp;
    }

    public static bool TryCopy(string text)
    {
        if (UiEnvironment.Headless)
        {
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
