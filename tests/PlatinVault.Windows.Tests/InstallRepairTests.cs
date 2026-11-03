using System.IO.Compression;
using PlatinVault.Setup;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Tests;

/// <summary>
/// A damaged installation (an antivirus removed PlatinVault.exe, the registry entries were rolled back) must be repairable by the setup instead of
/// refused with "folder already contains other files" - and the user's database is still backed up before anything touches it.
/// </summary>
public sealed class InstallRepairTests
{
    private static void MakeDamagedFolder(string target, bool withExe = false)
    {
        Directory.CreateDirectory(target);
        if (withExe) File.WriteAllText(Path.Combine(target, InstallLayout.ExeName), "old program");
        File.WriteAllText(Path.Combine(target, "version.txt"), "v-old");
        File.WriteAllText(Path.Combine(target, "release-channel.txt"), "release");
        File.WriteAllText(Path.Combine(target, "Companion.Api.dll"), "old server");
        File.WriteAllText(Path.Combine(target, "OldOnly.dll"), "old");
    }

    private static string MakePayload(TempFolder temp)
    {
        var zip = temp.Sub("payload.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(text);
        }
        Add(InstallLayout.ExeName, "new program");
        Add("version.txt", "v-new");
        Add("release-channel.txt", "release");
        return zip;
    }

    private static void MakeDatabase(InstallLayout layout)
    {
        Directory.CreateDirectory(layout.Data.Database);
        File.WriteAllText(InstallEngine.DatabaseFile(layout), "sqlite");
    }

    /// <summary>A stand-in for PlatinVault.exe: records "exe-content|arguments" and answers with the given exit codes.</summary>
    private sealed class FakeTool(int backupExit = 0, int prepareExit = 0)
    {
        public List<string> Calls { get; } = [];

        public Task<int> Run(string exe, IEnumerable<string> args, Action<string> onLine, TimeSpan timeout, CancellationToken ct)
        {
            var first = args.First();
            Calls.Add($"{File.ReadAllText(exe)}|{first}");
            if (first == "--prepare") onLine("PV-PROGRESS 100 Fertig|Done");
            return Task.FromResult(first == "--backup-now" ? backupExit : prepareExit);
        }
    }

    private static InstallEngine Engine(InstallLayout layout, string zip, FakeTool tool, out Payload payload,
        Func<Msg, Task<bool>>? confirm = null)
    {
        payload = Payload.FromZip(zip);
        return new InstallEngine(layout, payload, "v-new") { SkipWebViewInstall = true, Tool = tool.Run, ConfirmWithoutBackup = confirm };
    }

    [Fact]
    public void Folder_check_accepts_a_program_folder_whose_exe_is_gone_and_still_refuses_foreign_folders()
    {
        using var temp = new TempFolder();
        var damaged = temp.Sub("damaged");
        MakeDamagedFolder(damaged);
        Assert.False(File.Exists(Path.Combine(damaged, InstallLayout.ExeName)));
        Assert.Null(InstallEngine.CheckFolder(damaged));

        var withExeOnly = temp.Sub("exe-only");
        Directory.CreateDirectory(withExeOnly);
        File.WriteAllText(Path.Combine(withExeOnly, InstallLayout.ExeName), "x");
        Assert.Null(InstallEngine.CheckFolder(withExeOnly));

        // Only some of the markers, or a version.txt of somebody else's project: not ours
        var partial = temp.Sub("partial");
        Directory.CreateDirectory(partial);
        File.WriteAllText(Path.Combine(partial, "version.txt"), "1.0");
        File.WriteAllText(Path.Combine(partial, "other.txt"), "x");
        Assert.NotNull(InstallEngine.CheckFolder(partial));

        var noProgramPart = temp.Sub("no-program-part");
        Directory.CreateDirectory(noProgramPart);
        File.WriteAllText(Path.Combine(noProgramPart, "version.txt"), "1.0");
        File.WriteAllText(Path.Combine(noProgramPart, "release-channel.txt"), "x");
        Assert.NotNull(InstallEngine.CheckFolder(noProgramPart));
    }

    [Fact]
    public void Program_folder_markers_work_with_the_ui_folder_too()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("ui");
        Directory.CreateDirectory(Path.Combine(folder, "wwwroot"));
        File.WriteAllText(Path.Combine(folder, "version.txt"), "v");
        File.WriteAllText(Path.Combine(folder, "release-channel.txt"), "release");
        Assert.True(InstallLayout.IsProgramFolder(folder));
        Assert.False(InstallLayout.IsProgramFolder(temp.Sub("missing")));
    }

    [Fact]
    public async Task Repair_without_exe_and_without_registry_installs_over_the_folder_and_backs_up_with_the_new_program_first()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = Path.Combine(temp.Path, "install");
        MakeDamagedFolder(target);
        MakeDatabase(layout);
        Assert.Null(layout.InstalledVersion); // registry entries are gone as well
        var tool = new FakeTool();
        var engine = Engine(layout, MakePayload(temp), tool, out var payload);
        using (payload)
        {
            await engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None);
        }

        // The old exe was missing, so the NEW program makes the backup, before it sets up (migrates) the database
        Assert.Equal(["new program|--backup-now", "new program|--prepare"], tool.Calls);
        Assert.Equal("new program", File.ReadAllText(Path.Combine(target, InstallLayout.ExeName)));
        Assert.Equal("v-new", File.ReadAllText(Path.Combine(target, "version.txt")));
        Assert.False(File.Exists(Path.Combine(target, "OldOnly.dll")));
        Assert.Equal("v-new", layout.InstalledVersion);
        Assert.Empty(Directory.GetDirectories(temp.Path, ".platinvault-*"));
    }

    [Fact]
    public async Task Update_with_the_old_exe_present_backs_up_with_the_old_program_before_the_files_are_replaced()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = Path.Combine(temp.Path, "install");
        MakeDamagedFolder(target, withExe: true);
        MakeDatabase(layout);
        Registration.WriteEntries(layout, target, "v-old", "de");
        var tool = new FakeTool();
        var engine = Engine(layout, MakePayload(temp), tool, out var payload);
        using (payload)
        {
            await engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None);
        }

        Assert.Equal(["old program|--backup-now", "new program|--prepare"], tool.Calls);
    }

    [Fact]
    public async Task Without_a_database_there_is_nothing_to_back_up()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = Path.Combine(temp.Path, "install");
        MakeDamagedFolder(target);
        var tool = new FakeTool();
        var engine = Engine(layout, MakePayload(temp), tool, out var payload);
        using (payload)
        {
            await engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None);
        }

        Assert.Equal(["new program|--prepare"], tool.Calls);
    }

    [Fact]
    public async Task A_failed_backup_in_a_repair_cancels_it_and_leaves_the_damaged_folder_as_it_was()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = Path.Combine(temp.Path, "install");
        MakeDamagedFolder(target);
        MakeDatabase(layout);
        var tool = new FakeTool(backupExit: 1);
        var engine = Engine(layout, MakePayload(temp), tool, out var payload); // headless: no confirmation, so it cancels
        using (payload)
        {
            var ex = await Assert.ThrowsAsync<InstallException>(() =>
                engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None));
            Assert.Contains("Sicherung", ex.Text.De);
        }

        Assert.Equal(["new program|--backup-now"], tool.Calls);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "OldOnly.dll")));
        Assert.Empty(Directory.GetDirectories(temp.Path, ".platinvault-*"));
    }

    [Fact]
    public async Task A_failed_backup_can_be_waived_by_the_user_in_a_repair()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = Path.Combine(temp.Path, "install");
        MakeDamagedFolder(target);
        MakeDatabase(layout);
        var tool = new FakeTool(backupExit: 1);
        var engine = Engine(layout, MakePayload(temp), tool, out var payload, confirm: _ => Task.FromResult(true));
        using (payload)
        {
            await engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None);
        }

        Assert.Equal(["new program|--backup-now", "new program|--prepare"], tool.Calls);
    }
}
