using System.Text;

namespace PlatinVault.Desktop;

/// <summary>
/// The program's own log file (<c>logsdesktop.log</c> in the data folder, the server writes <c>logsserver.log</c>).
/// German lines like the server's operator logs; never a password or key. Rotates at 5 MB.
/// </summary>
internal static class DesktopLog
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>Also writes every line to the standard output (installer mode: the setup reads the lines).</summary>
    public static bool Echo { get; set; }

    public static void Init(string logFolder)
    {
        Directory.CreateDirectory(logFolder);
        _path = Path.Combine(logFolder, "desktop.log");
    }

    public static string? FilePath => _path;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("FEHLER", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        if (Echo)
        {
            try { Console.Out.WriteLine(line); Console.Out.Flush(); }
            catch (IOException) { /* no reader any more */ }
        }
        if (_path is null) return;
        lock (Gate)
        {
            try
            {
                RotateIfNeeded(_path);
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the program
            }
        }
    }

    public static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes) return;
        var old = path + ".1";
        if (File.Exists(old)) File.Delete(old);
        File.Move(path, old);
    }
}
