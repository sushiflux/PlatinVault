using System.Runtime.InteropServices;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Install;

/// <summary>A failed install/uninstall step whose message can be shown to the user as it is (complete, in the UI language).</summary>
public class InstallException(Msg message, Exception? inner = null) : Exception(message.De, inner)
{
    public Msg Text { get; } = message;
}

/// <summary>What the installer registers for the user: autostart (Run value), Start-menu and desktop shortcut, the "Apps" entry and the program's own key.</summary>
public static class Registration
{
    public static bool IsAutostartEnabled(InstallLayout layout) => layout.Registry.GetString(InstallLayout.RunKeyPath, InstallLayout.RunValueName) is { Length: > 0 };

    /// <summary>Autostart at sign-in: the Run value starts the program in the background (tray only), see Program.cs "--background".</summary>
    public static void SetAutostart(InstallLayout layout, bool enabled, string exePath)
    {
        try
        {
            if (enabled)
            {
                var args = string.Join(" ", layout.InheritedArguments().Select(Quote));
                layout.Registry.SetString(InstallLayout.RunKeyPath, InstallLayout.RunValueName, $"{Quote(exePath)} --background{(args.Length > 0 ? " " + args : string.Empty)}");
            }
            else
            {
                layout.Registry.DeleteValue(InstallLayout.RunKeyPath, InstallLayout.RunValueName);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InstallException(InstallTexts.AutostartFailed(ex.Message), ex);
        }
    }

    public static void WriteEntries(InstallLayout layout, string installDir, string version, string language)
    {
        var exe = Path.Combine(installDir, InstallLayout.ExeName);
        try
        {
            var reg = layout.Registry;
            reg.SetString(InstallLayout.AppKeyPath, "InstallDir", installDir);
            reg.SetString(InstallLayout.AppKeyPath, "DataDir", layout.DataDir);
            reg.SetString(InstallLayout.AppKeyPath, "Version", version);
            reg.SetString(InstallLayout.AppKeyPath, "Language", language);

            var key = InstallLayout.UninstallKeyPath;
            reg.SetString(key, "DisplayName", InstallLayout.ProductName);
            reg.SetString(key, "DisplayIcon", exe);
            reg.SetString(key, "Publisher", "SushiFlux");
            reg.SetString(key, "DisplayVersion", version);
            reg.SetString(key, "InstallLocation", installDir);
            reg.SetString(key, "InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
            var args = string.Join(" ", layout.InheritedArguments().Select(Quote));
            reg.SetString(key, "UninstallString", $"{Quote(exe)} --uninstall{(args.Length > 0 ? " " + args : string.Empty)}");
            reg.SetString(key, "URLInfoAbout", "https://github.com/sushiflux/PlatinVault");
            reg.SetDword(key, "EstimatedSize", (int)Math.Min(int.MaxValue, FolderSizeKb(installDir)));
            reg.SetDword(key, "NoModify", 1);
            reg.SetDword(key, "NoRepair", 1);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InstallException(InstallTexts.AppsEntryFailed(ex.Message), ex);
        }
    }

    public static void RemoveEntries(InstallLayout layout)
    {
        layout.Registry.DeleteValue(InstallLayout.RunKeyPath, InstallLayout.RunValueName);
        layout.Registry.DeleteKey(InstallLayout.UninstallKeyPath);
        layout.Registry.DeleteKey(InstallLayout.AppKeyPath);
    }

    public static void SetShortcut(string shortcutPath, bool enabled, string exePath, string description)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            CreateShortcut(shortcutPath, exePath, Path.GetDirectoryName(exePath)!, description);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or COMException or InvalidOperationException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            throw new InstallException(InstallTexts.ShortcutFailed(shortcutPath, ex.Message), ex);
        }
    }

    private static void CreateShortcut(string shortcutPath, string target, string workingDirectory, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = target;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.Description = description;
                shortcut.IconLocation = target + ",0";
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public static long FolderSizeKb(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try { bytes += new FileInfo(file).Length; }
            catch (IOException) { /* file vanished meanwhile */ }
        }
        return Math.Max(1, bytes / 1024);
    }

    public static string Quote(string value) => value.Contains(' ') || value.Contains('\t') ? $"\"{value}\"" : value;
}
