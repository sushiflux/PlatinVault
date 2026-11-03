using System.Runtime.InteropServices;

namespace PlatinVault.Bridge;

/// <summary>
/// Win32 glue for a GUI-subsystem exe: attaching to the parent terminal and a message box for start-up errors that
/// would otherwise be invisible (no console window when launched by double click or Task Scheduler).
/// </summary>
internal static class NativeConsole
{
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint MbOk = 0x0;
    private const uint MbIconError = 0x10;

    /// <summary>
    /// A GUI-subsystem process starts without std handles unless the parent redirected them. When started from a terminal
    /// the parent's console is attached and the handles are reopened on it. Returns false when there is no terminal.
    /// </summary>
    public static bool TryAttach()
    {
        var outValid = IsValid(GetStdHandle(StdOutputHandle));
        var errValid = IsValid(GetStdHandle(StdErrorHandle));
        if (outValid && errValid) return true;

        if (!AttachConsole(AttachParentProcess)) return outValid;

        // Reopen the console screen buffer for whichever std handle is still missing.
        var console = CreateFileW("CONOUT$", GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!IsValid(console)) return outValid;

        if (!outValid) SetStdHandle(StdOutputHandle, console);
        if (!errValid) SetStdHandle(StdErrorHandle, console);
        return true;
    }

    public static void ShowError(string caption, string text) =>
        MessageBoxW(IntPtr.Zero, text, caption, MbOk | MbIconError);

    private static bool IsValid(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
