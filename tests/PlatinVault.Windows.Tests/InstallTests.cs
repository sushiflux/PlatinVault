using System.IO.Compression;
using PlatinVault.Setup;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Tests;

public sealed class PayloadTests
{
    private static string MakeZip(TempFolder temp, params (string Name, string Text)[] entries)
    {
        var zip = temp.Sub("payload.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(text);
        }
        return zip;
    }

    [Fact]
    public void Appended_payload_is_found_verified_and_extracted()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp, ("version.txt", "v2026.11.9\nv2026.11.9.31.2840"), ("release-channel.txt", "beta"), ("docs/LICENSE", "# Licence"));
        var exe = temp.Sub("setup.exe");
        File.WriteAllBytes(exe, new byte[200_000]); // stands in for the single-file apphost
        Payload.Append(exe, zip);
        // A later signature appends more bytes: the trailer must still be found
        File.AppendAllText(exe, new string('S', 9000));

        using var payload = Payload.FromExe(exe);
        Assert.NotNull(payload);
        Assert.True(payload!.Verify());
        Assert.Equal("v2026.11.9 (31.2840 β)", PlatinVault.Setup.Program.ReadVersion(payload));
        Assert.Equal("# Licence", payload.ReadText("docs/LICENSE"));

        var target = temp.Sub("out");
        payload.ExtractTo(target, null, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(target, "docs", "LICENSE")));
    }

    [Fact]
    public void The_installer_shows_a_dev_build_with_dev_inside_the_parentheses()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp, ("version.txt", "v2026.11.12\nv2026.11.12.13.557"), ("release-channel.txt", "dev"));
        var exe = temp.Sub("setup.exe");
        File.WriteAllBytes(exe, new byte[50_000]);
        Payload.Append(exe, zip);
        using var payload = Payload.FromExe(exe);
        Assert.NotNull(payload);
        Assert.Equal("v2026.11.12 (13.557-dev)", PlatinVault.Setup.Program.ReadVersion(payload!));
    }

    [Fact]
    public void A_damaged_payload_fails_verification()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp, ("version.txt", "v2026.11.9"));
        var exe = temp.Sub("setup.exe");
        File.WriteAllBytes(exe, new byte[50_000]);
        Payload.Append(exe, zip);
        var bytes = File.ReadAllBytes(exe);
        bytes[50_000 + 40] ^= 0xFF;
        File.WriteAllBytes(exe, bytes);
        using var payload = Payload.FromExe(exe);
        Assert.False(payload!.Verify());
    }

    [Fact]
    public void An_exe_without_payload_has_none()
    {
        using var temp = new TempFolder();
        var exe = temp.Sub("plain.exe");
        File.WriteAllBytes(exe, new byte[10_000]);
        Assert.Null(Payload.FromExe(exe));
    }

    [Fact]
    public void Entries_that_leave_the_target_are_refused()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp, ("../evil.txt", "x"));
        using var payload = Payload.FromZip(zip);
        Assert.Throws<InvalidDataException>(() => payload.ExtractTo(temp.Sub("out"), null, CancellationToken.None));
        Assert.False(File.Exists(temp.Sub("evil.txt")));
    }
}

public sealed class InstallLayoutTests
{
    [Fact]
    public void Dangerous_program_folders_are_refused()
    {
        Assert.False(InstallLayout.IsSafeProgramFolder(@"C:\"));
        Assert.False(InstallLayout.IsSafeProgramFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.False(InstallLayout.IsSafeProgramFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        Assert.False(InstallLayout.IsSafeProgramFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")));
        Assert.False(InstallLayout.IsSafeProgramFolder("relative\\folder"));
        Assert.True(InstallLayout.IsSafeProgramFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PlatinVault")));
        Assert.True(InstallLayout.IsSafeProgramFolder(@"D:\Games\PlatinVault"));
    }

    [Fact]
    public void A_test_layout_lives_entirely_in_the_test_root()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        Assert.True(layout.IsTest);
        Assert.IsType<FileRegistry>(layout.Registry);
        foreach (var path in new[] { layout.DefaultInstallDir, layout.DataDir, layout.StartMenuShortcut, layout.DesktopShortcut })
            Assert.StartsWith(temp.Path, path, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("-test-", layout.NameSuffix);
    }

    [Fact]
    public void Registration_writes_and_removes_its_entries()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var installDir = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, InstallLayout.ExeName), "x");

        Registration.SetAutostart(layout, true, Path.Combine(installDir, InstallLayout.ExeName));
        Registration.WriteEntries(layout, installDir, "v2026.11.9", "de");
        Assert.True(Registration.IsAutostartEnabled(layout));
        Assert.Contains("--background", layout.Registry.GetString(InstallLayout.RunKeyPath, InstallLayout.RunValueName));
        Assert.Contains("--test-root", layout.Registry.GetString(InstallLayout.UninstallKeyPath, "UninstallString"));
        Assert.Equal(installDir, layout.InstalledDir);
        Assert.Equal("v2026.11.9", layout.InstalledVersion);

        Registration.RemoveEntries(layout);
        Assert.False(Registration.IsAutostartEnabled(layout));
        Assert.Null(layout.InstalledDir);
        Assert.False(layout.Registry.KeyExists(InstallLayout.UninstallKeyPath));
    }

    [Fact]
    public void Deleting_data_removes_only_our_parts()
    {
        using var temp = new TempFolder();
        var data = new DataLayout(temp.Sub("data"));
        foreach (var folder in data.Folders) Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(data.Database, "PG_VERSION"), "18");
        File.SetAttributes(Path.Combine(data.Database, "PG_VERSION"), FileAttributes.ReadOnly);
        File.WriteAllText(data.SettingsFile, "{}");
        var bridgeLog = Path.Combine(data.Root, "bridge.log");
        File.WriteAllText(bridgeLog, "the bridge's own log");

        Uninstaller.DeleteData(data);

        Assert.All(data.Folders, f => Assert.False(Directory.Exists(f)));
        Assert.False(File.Exists(data.SettingsFile));
        Assert.True(File.Exists(bridgeLog));
    }

    [Fact]
    public void Uninstall_keeps_data_when_asked_and_removes_the_program()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var installDir = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, InstallLayout.ExeName), "x");
        Directory.CreateDirectory(layout.Data.Backups);
        Registration.WriteEntries(layout, installDir, "v1", "de");

        var done = new Uninstaller(layout, installDir) { QuitTimeout = TimeSpan.FromSeconds(1) }.Run(keepData: true);

        Assert.True(Directory.Exists(layout.Data.Backups));
        Assert.False(Directory.Exists(installDir));
        Assert.Null(layout.InstalledDir);
        Assert.Contains(InstallTexts.DoneProgramDeleted, done);
    }
}

public sealed class SetupLogicTests
{
    // The start of the official GNU AGPL v3 text, with its centred title and hard line breaks (CRLF as in a Windows checkout)
    private const string Licence =
        "                    GNU AFFERO GENERAL PUBLIC LICENSE\r\n                       Version 3, 19 November 2007\r\n\r\n" +
        " Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>\r\n Everyone is permitted to copy and distribute verbatim copies\r\n" +
        " of this license document, but changing it is not allowed.\r\n\r\n\r\n                            Preamble\r\n\r\n" +
        "  The GNU Affero General Public License is a free, copyleft license for\r\nsoftware and other kinds of works.\r\n";

    [Fact]
    public void Licence_text_is_unwrapped_into_paragraphs_with_the_title_lines_kept()
    {
        var text = SetupWindow.LicenceForDisplay(Licence);
        var lines = text.Split('\n');
        Assert.Equal("GNU AFFERO GENERAL PUBLIC LICENSE", lines[0]);
        Assert.Equal("Version 3, 19 November 2007", lines[1]);
        Assert.Contains("Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/> Everyone is permitted to copy and distribute verbatim copies of this license document, but changing it is not allowed.", lines);
        Assert.Contains("Preamble", lines);
        Assert.Contains("The GNU Affero General Public License is a free, copyleft license for software and other kinds of works.", lines);
        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain("\n\n\n", text);
    }

    [Fact]
    public void Folder_check_refuses_foreign_content()
    {
        using var temp = new TempFolder();
        var foreign = temp.Sub("foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "something.txt"), "x");
        Assert.NotNull(InstallEngine.CheckFolder(foreign));
        Assert.Null(InstallEngine.CheckFolder(temp.Sub("new-folder")));
        Assert.NotNull(InstallEngine.CheckFolder(@"C:\"));
    }

    [Theory]
    [InlineData(UiLang.German, "1,5 GB")]
    [InlineData(UiLang.English, "1.5 GB")]
    public void Sizes_use_the_language_decimal_mark(string language, string expected)
    {
        var previous = UiLang.Current;
        UiLang.Current = language;
        try
        {
            Assert.Equal(expected, InstallEngine.Size(1610612736L));
            Assert.Equal("300 MB", InstallEngine.Size(300L * 1024 * 1024));
        }
        finally
        {
            UiLang.Current = previous;
        }
    }
}
