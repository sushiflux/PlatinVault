using System.Globalization;
using System.Text;

namespace PlatinVault.Bridge;

/// <summary>
/// Log output of the bridges. Every line goes to the log file (rolling, about 1 MB at most; each host sets <see cref="LogPath"/>:
/// <c>%LOCALAPPDATA%\PlatinVault\bridge.log</c> on Windows, <c>$XDG_STATE_HOME/platinvault-bridge/bridge.log</c> on Linux). When the
/// host has a terminal (<see cref="InitConsole"/>) the same lines are also printed there. Also used, through
/// <see cref="BridgeLogger{T}"/>, by the Steam readers of the hosts.
/// </summary>
internal static class BridgeLog
{
    // The active file rolls over to bridge.log.1 at this size, so both files together stay below about 1 MB.
    private const long MaxLogBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Enables debug lines and full exception details (--verbose).</summary>
    public static bool Verbose { get; set; }

    /// <summary>True when stdout is a terminal or redirected, i.e. console output has somewhere to go.</summary>
    public static bool ConsoleAvailable { get; private set; }

    /// <summary>Log file; set from <see cref="BridgeEnvironment"/> at startup (a test run logs into its own folder).</summary>
    public static string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatinVault", "bridge.log");

    /// <summary>
    /// Tells the log whether console output has somewhere to go (a GUI-subsystem Windows exe attaches to the parent terminal first and
    /// passes the result; a Linux service always has stdout, which the service manager captures).
    /// </summary>
    public static void InitConsole(bool available)
    {
        ConsoleAvailable = available;
        if (!ConsoleAvailable) return;

        try
        {
            // German umlauts and the dash in the log lines; UTF-8 without BOM so redirected output stays clean.
            Console.OutputEncoding = Utf8NoBom;
        }
        catch (IOException)
        {
            // Encoding of an already redirected stream cannot always be changed - the bytes are UTF-8 anyway.
        }
    }

    public static void Info(string message) => Write(null, message);

    /// <summary>Positive state change (connected, game detected) - green on a terminal.</summary>
    public static void Success(string message) => Write(ConsoleColor.Green, message);

    public static void Warn(string message) => Write(ConsoleColor.Yellow, message);

    public static void Error(string message) => Write(ConsoleColor.Red, message);

    public static void Debug(string message)
    {
        if (Verbose) Write(ConsoleColor.DarkGray, message);
    }

    /// <summary>Prints text (help, version) to the terminal only; there is nothing to print without one.</summary>
    public static void Print(string text)
    {
        if (!ConsoleAvailable) return;
        lock (Gate)
        {
            try { Console.Out.WriteLine(text); }
            catch (IOException) { /* terminal went away */ }
        }
    }

    internal static void Write(ConsoleColor? color, string message)
    {
        var now = DateTime.Now;
        lock (Gate)
        {
            AppendToFile($"{now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {message}");

            if (!ConsoleAvailable) return;

            // Colors only make sense on a real console; skip them when the output is piped to a file.
            var useColor = color.HasValue && !Console.IsOutputRedirected;
            try
            {
                if (useColor) Console.ForegroundColor = color!.Value;
                Console.WriteLine($"[{now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {message}");
            }
            catch (IOException)
            {
                // The terminal was closed - logging must never crash the bridge.
            }
            finally
            {
                if (useColor)
                {
                    try { Console.ResetColor(); }
                    catch (IOException) { /* see above */ }
                }
            }
        }
    }

    private static void AppendToFile(string line)
    {
        try
        {
            var path = LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxLogBytes)
                File.Move(path, path + ".1", overwrite: true);

            // ReadWrite|Delete sharing: the freshly started replacement bridge may write while this one is exiting,
            // and the user may have the file open in an editor.
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(stream, Utf8NoBom);
            writer.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A full disk or a locked log file must not stop the bridge.
        }
    }
}

/// <summary>Bridges <see cref="ILogger{T}"/> (needed by the shared Steam stats reader) to <see cref="BridgeLog"/>.</summary>
internal sealed class BridgeLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // Debug/trace output of the shared reader is only interesting when troubleshooting.
    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && (logLevel >= LogLevel.Information || BridgeLog.Verbose);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception is not null)
            message += BridgeLog.Verbose ? Environment.NewLine + exception : $" ({exception.Message})";

        var color = logLevel switch
        {
            >= LogLevel.Error => ConsoleColor.Red,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Information => (ConsoleColor?)null,
            _ => ConsoleColor.DarkGray,
        };
        BridgeLog.Write(color, message);
    }
}

