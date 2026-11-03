using System.Diagnostics;

namespace PlatinVault.Desktop.Server;

/// <summary>
/// Opens a folder in Windows Explorer - the one place for the window, the tray menu and the app's "open folder" buttons. The path is
/// always one the program itself computed (never text from a request); the folder is created first when it does not exist yet (data,
/// backups and logs can be empty on a fresh installation).
/// </summary>
internal static class FolderOpener
{
    /// <summary>Opens <paramref name="folder"/>; null on success, else the system's own error text.</summary>
    public static string? TryOpen(string folder, bool createIfMissing = true)
    {
        try
        {
            if (createIfMissing) Directory.CreateDirectory(folder);
            else if (!Directory.Exists(folder)) return $"The folder does not exist: {folder}";
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DesktopLog.Warn($"Ordner konnte nicht geöffnet werden: {ex.Message}");
            return ex.Message;
        }
    }
}
