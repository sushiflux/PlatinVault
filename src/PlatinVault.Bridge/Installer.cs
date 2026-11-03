using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PlatinVault.Bridge;

/// <summary>A failed install/uninstall step with a message that can be shown to the user as is.</summary>
internal sealed class InstallException(string message, Exception? inner = null) : Exception(message, inner);

/// <param name="Language">The language to write into bridge.json: the server's as learned by the connection test, else the current one (null: leave the key out).</param>
internal sealed record InstallOptions(BridgeSettings Settings, bool Autostart, bool StartMenu, string Version, string? Language);

/// <summary>
/// A per-user installation without an MSI: the exe and its <c>bridge.json</c> are copied to
/// <c>%LOCALAPPDATA%\Programs\PlatinVault Bridge</c>, autostart is a value in <c>HKCU\...\Run</c>, the Start-menu entry is a
/// shortcut and an uninstall entry makes the program show up under Windows "Apps". Nothing needs administrator rights.
/// </summary>
internal static class Installer
{
    /// <summary>Version of the installed copy as recorded in the uninstall entry; null when nothing is installed.</summary>
    public static string? ReadInstalledVersion(BridgeEnvironment env)
    {
        using var key = Registry.CurrentUser.OpenSubKey(env.UninstallKeyPath);
        return key?.GetValue("DisplayVersion") as string;
    }

    /// <summary>True when an uninstall entry exists or the installed exe is there.</summary>
    public static bool IsInstalled(BridgeEnvironment env)
    {
        if (File.Exists(env.InstalledExePath)) return true;
        using var key = Registry.CurrentUser.OpenSubKey(env.UninstallKeyPath);
        return key is not null;
    }

    public static bool IsAutostartEnabled(BridgeEnvironment env)
    {
        using var key = Registry.CurrentUser.OpenSubKey(env.RunKeyPath);
        return key?.GetValue(BridgeEnvironment.RegistryName) is string;
    }

    public static void SetAutostart(BridgeEnvironment env, bool enabled, string exePath)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(env.RunKeyPath);
                // Quoted: the install path contains spaces.
                key.SetValue(BridgeEnvironment.RegistryName, $"\"{exePath}\"", RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(env.RunKeyPath, writable: true);
                key?.DeleteValue(BridgeEnvironment.RegistryName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InstallException(BridgeTexts.Installer.AutostartFailed(enabled, ex.Message), ex);
        }
    }

    /// <summary>
    /// Copies the running exe and the config to the install folder and registers autostart, Start menu and the "Apps" entry.
    /// A running bridge must be stopped before (the target exe would be locked). Returns the path of the installed exe.
    /// </summary>
    public static string Install(BridgeEnvironment env, InstallOptions options)
    {
        var target = env.InstalledExePath;

        try
        {
            Directory.CreateDirectory(env.InstallRoot);

            // Copy next to the target and move over it, so a failed copy never leaves a half-written exe behind.
            var staging = target + ".tmp";
            File.Copy(BridgeEnvironment.ExePath, staging, overwrite: true);
            File.Move(staging, target, overwrite: true);

            // Leftovers of the self-update in the install folder are obsolete now.
            foreach (var leftover in new[] { target + ".old", target + ".new" })
                if (File.Exists(leftover)) File.Delete(leftover);

            BridgeConfigFile.Save(Path.Combine(env.InstallRoot, "bridge.json"), options.Settings, options.Language);

            // The handbook PDFs of the ZIP go along, so the tray menu finds them next to the installed exe
            Handbook.CopyToInstallFolder(BridgeEnvironment.ExeDirectory, env.InstallRoot);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InstallException(BridgeTexts.Installer.CopyFailed(env.InstallRoot, ex.Message), ex);
        }

        SetAutostart(env, options.Autostart, target);
        SetStartMenuShortcut(env, options.StartMenu, target);
        WriteUninstallEntry(env, target, options.Version);
        RemoveLegacyScheduledTask(env);

        return target;
    }

    /// <summary>Keeps the "Apps" entry honest after a self-update replaced the installed exe with a newer version.</summary>
    public static void RefreshUninstallVersion(BridgeEnvironment env, string version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(env.UninstallKeyPath, writable: true);
            if (key is null || string.Equals(key.GetValue("DisplayVersion") as string, version, StringComparison.Ordinal)) return;
            key.SetValue("DisplayVersion", version);
            if (File.Exists(env.InstalledExePath))
                key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(env.InstalledExePath).Length / 1024), RegistryValueKind.DWord);
            BridgeLog.Info(BridgeTexts.Installer.AppsVersionUpdated(version));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            BridgeLog.Debug(BridgeTexts.Installer.AppsVersionNotUpdatedDebug(ex.Message));
        }
    }

    public static void SetStartMenuShortcut(BridgeEnvironment env, bool enabled, string exePath)
    {
        var path = env.StartMenuShortcutPath;
        try
        {
            if (!enabled)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            CreateShortcut(path, exePath, Path.GetDirectoryName(exePath)!, BridgeTexts.Installer.ShortcutDescription);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or COMException or InvalidOperationException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            throw new InstallException(BridgeTexts.Installer.StartMenuFailed(enabled, ex.Message), ex);
        }
    }

    private static void WriteUninstallEntry(BridgeEnvironment env, string exePath, string version)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(env.UninstallKeyPath);
            key.SetValue("DisplayName", BridgeEnvironment.ProductName);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("Publisher", "SushiFlux");
            key.SetValue("DisplayVersion", version);
            key.SetValue("InstallLocation", env.InstallRoot);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall" + TestArguments(env), RegistryValueKind.String);
            key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exePath).Length / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InstallException(BridgeTexts.Installer.AppsEntryFailed(ex.Message), ex);
        }
    }

    // A test installation must find its own (test) locations again when its uninstall entry is run.
    private static string TestArguments(BridgeEnvironment env) =>
        env.TestRoot is null ? string.Empty : $" --test-root \"{env.TestRoot}\" --install-root \"{env.InstallRoot}\"";

    /// <summary>An older package registered a Task Scheduler task; the Run key replaces it, two autostarts are pointless.</summary>
    private static void RemoveLegacyScheduledTask(BridgeEnvironment env)
    {
        if (env.IsTest) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Delete /TN \"PlatinVault Bridge\" /F")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            BridgeLog.Debug(BridgeTexts.Installer.LegacyTaskDebug(ex.Message));
        }
    }

    /// <summary>
    /// Removes autostart, Start-menu entry, the "Apps" entry and the per-user config. The install folder is deleted too:
    /// at once when this process does not run from it, otherwise by a helper script after this process has exited.
    /// Returns what was done, for the final message.
    /// </summary>
    public static IReadOnlyList<Msg> Uninstall(BridgeEnvironment env, bool deleteLog)
    {
        var done = new List<Msg>();

        try
        {
            SetAutostart(env, false, env.InstalledExePath);
            done.Add(BridgeTexts.Installer.AutostartRemoved);

            SetStartMenuShortcut(env, false, env.InstalledExePath);
            done.Add(BridgeTexts.Installer.StartMenuRemoved);

            Registry.CurrentUser.DeleteSubKeyTree(env.UninstallKeyPath, throwOnMissingSubKey: false);
            done.Add(BridgeTexts.Installer.AppsEntryRemoved);

            if (File.Exists(env.UserConfigPath))
            {
                File.Delete(env.UserConfigPath);
                done.Add(BridgeTexts.Installer.SettingsDeleted);
            }

            if (deleteLog)
            {
                foreach (var path in new[] { env.LogPath, env.LogPath + ".1" })
                    if (File.Exists(path)) File.Delete(path);
                done.Add(BridgeTexts.Installer.LogDeleted);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InstallException(BridgeTexts.Installer.UninstallInterrupted(ex.Message), ex);
        }

        if (Directory.Exists(env.InstallRoot))
        {
            if (!IsSafeToDelete(env.InstallRoot))
            {
                done.Add(BridgeTexts.Installer.FolderNotDeleted(env.InstallRoot));
            }
            else if (env.IsRunningFromInstallRoot)
            {
                ScheduleFolderDeletion(env);
                done.Add(BridgeTexts.Installer.FolderDeleteScheduled);
            }
            else
            {
                try
                {
                    Directory.Delete(env.InstallRoot, recursive: true);
                    done.Add(BridgeTexts.Installer.FolderDeleted);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    throw new InstallException(BridgeTexts.Installer.FolderDeleteFailed(env.InstallRoot, ex.Message), ex);
                }
            }
        }

        return done;
    }

    /// <summary>Only ever delete a folder that clearly is a bridge install: not a drive root, and it must hold the exe.</summary>
    private static bool IsSafeToDelete(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd('\\', '/');
        if (full.Length < 10 || Path.GetPathRoot(full)?.TrimEnd('\\', '/').Length == full.Length) return false;
        return File.Exists(Path.Combine(full, BridgeEnvironment.ExeFileName));
    }

    /// <summary>
    /// A running exe cannot delete its own folder. A tiny hidden batch file retries the deletion until the process has
    /// exited, then removes itself.
    /// </summary>
    private static void ScheduleFolderDeletion(BridgeEnvironment env)
    {
        var scriptDirectory = env.TestRoot is null ? Path.GetTempPath() : Path.Combine(env.TestRoot, "data");
        Directory.CreateDirectory(scriptDirectory);
        var script = Path.Combine(scriptDirectory, $"platinvault-bridge-cleanup-{Guid.NewGuid():N}.cmd");

        var text = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine($"set \"TARGET={env.InstallRoot}\"")
            .AppendLine("for /l %%i in (1,1,60) do (")
            .AppendLine("  rmdir /s /q \"%TARGET%\" >nul 2>&1")
            .AppendLine("  if not exist \"%TARGET%\" goto :done")
            .AppendLine("  ping -n 2 127.0.0.1 >nul")
            .AppendLine(")")
            .AppendLine(":done")
            .AppendLine("(goto) 2>nul & del \"%~f0\"")
            .ToString();
        File.WriteAllText(script, text, Encoding.ASCII);

        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c \"\"{script}\"\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = scriptDirectory,
        })?.Dispose();
    }

    private static void CreateShortcut(string shortcutPath, string target, string workingDirectory, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")
                   ?? throw new InvalidOperationException(BridgeTexts.Installer.NoScriptHost);
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
}
