using Companion.Api.Data;
using PlatinVault.Desktop;
using PlatinVault.Desktop.Database;
using PlatinVault.Desktop.Server;

namespace PlatinVault.Windows.Tests;

/// <summary>
/// Shared development mode of start-windows.ps1: the Windows program works on the browser stack's SQLite file and keys instead of its
/// own (<c>--shared-data-dir</c>).
/// </summary>
public sealed class SharedDevOptionsTests
{
    [Fact]
    public void Shared_option_is_parsed_and_names_key_ring_and_secrets_of_the_browser_stack()
    {
        var o = DesktopOptions.Parse(["--dev", @"C:\repo", "--shared-data-dir", @"C:\repo\.data\dev", "--backups-dir", @"C:\repo\.backups\auto"]);
        Assert.NotNull(o.Shared);
        Assert.Equal(@"C:\repo\.data\dev", o.Shared!.DataDir);
        Assert.Equal(@"C:\repo\.data\dev\dp-keys", o.Shared.KeyRingDir);
        Assert.Equal(@"C:\repo\.data\dev\secrets", o.Shared.SecretsDir);
    }

    [Fact]
    public void Without_the_shared_option_the_program_keeps_its_own_database()
    {
        Assert.Null(DesktopOptions.Parse(["--dev", @"C:\repo"]).Shared);
    }

    [Fact]
    public void The_shared_option_needs_dev()
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--shared-data-dir", @"C:\repo\.data\dev"]));
    }

    [Theory]
    [InlineData("--db-port")]
    [InlineData("--pg-dir")]
    [InlineData("--shared-db-port")]
    public void The_former_postgres_options_are_gone(string option)
    {
        // Unknown options are ignored like any other unknown argument; they set nothing any more
        var o = DesktopOptions.Parse(["--dev", @"C:\repo", option, "5235"]);
        Assert.Null(o.Shared);
    }
}

public sealed class SharedDevServerSettingsTests
{
    private static (DesktopEnvironment Env, string Repo) Environment(TempFolder temp, params string[] extra)
    {
        var repo = temp.Sub("repo");
        Directory.CreateDirectory(Path.Combine(repo, "src", "Companion.Api"));
        File.WriteAllText(Path.Combine(repo, "src", "Companion.Api", "Companion.Api.csproj"), "<Project />");
        var args = new List<string> { "--dev", repo };
        args.AddRange(extra);
        return (DesktopEnvironment.Create(DesktopOptions.Parse(args)), repo);
    }

    [Fact]
    public void The_server_gets_the_browser_stacks_database_file_backups_and_keys()
    {
        using var temp = new TempFolder();
        var (_, repo) = Environment(temp);
        var (env, _) = Environment(temp, "--shared-data-dir", Path.Combine(repo, ".data", "dev"), "--backups-dir", Path.Combine(repo, ".backups", "auto"));
        var host = new DatabaseHost(env);

        var settings = DesktopServer.DatabaseSettings(host);

        Assert.True(host.IsShared);
        Assert.Equal(Path.Combine(repo, ".data", "dev", "platinvault.db"), settings["Companion:Database:Path"]);
        Assert.Equal(Path.Combine(repo, ".backups", "auto"), settings["Companion:Backups:Dir"]);
        Assert.Equal(Path.Combine(repo, ".data", "dev", "dp-keys"), settings["Companion:KeyRingDir"]);
        Assert.Equal(Path.Combine(repo, ".data", "dev", "secrets"), settings["Companion:SecretsDir"]);
        // The caches stay in the program's own data folder: DataDir is set by the caller from env.Data.Server, not here
        Assert.DoesNotContain("Companion:DataDir", settings.Keys);
    }

    [Fact]
    public void Own_mode_uses_the_programs_database_folder_and_its_own_keys()
    {
        using var temp = new TempFolder();
        var (env, repo) = Environment(temp);
        var host = new DatabaseHost(env);

        var settings = DesktopServer.DatabaseSettings(host);

        Assert.False(host.IsShared);
        Assert.Equal(Path.Combine(repo, ".data", "desktop-dev", "database", "platinvault.db"), settings["Companion:Database:Path"]);
        Assert.Equal(Path.Combine(repo, ".backups", "desktop-dev"), settings["Companion:Backups:Dir"]);
        Assert.DoesNotContain("Companion:KeyRingDir", settings.Keys);
        Assert.DoesNotContain("Companion:SecretsDir", settings.Keys);
    }

    [Fact]
    public void An_installed_program_keeps_database_and_backups_in_its_data_folder()
    {
        using var temp = new TempFolder();
        var env = DesktopEnvironment.Create(DesktopOptions.Parse(["--test-root", temp.Sub("root")]));
        var host = new DatabaseHost(env);
        Assert.Equal(Path.Combine(env.Data.Database, "platinvault.db"), host.DatabaseFile);
        Assert.Equal(env.Data.Backups, host.BackupDir);
        Assert.StartsWith(env.Data.Root, host.DatabaseFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_cluster_of_the_former_postgres_is_left_alone()
    {
        using var temp = new TempFolder();
        var (env, _) = Environment(temp);
        DesktopLog.Init(temp.Sub("logs"));
        Directory.CreateDirectory(env.Data.Database);
        var pgVersion = Path.Combine(env.Data.Database, "PG_VERSION");
        File.WriteAllText(pgVersion, "18");

        new DatabaseHost(env).Prepare(null);

        Assert.True(File.Exists(pgVersion));
        Assert.True(Directory.Exists(env.Data.Backups));
    }
}

/// <summary>
/// <c>--backup-now</c> and <c>--restore</c> of the Windows program go through the server's backup service on the program's own file.
/// </summary>
public sealed class DesktopBackupTests
{
    [Fact]
    public async Task Backup_and_restore_work_on_the_programs_own_file()
    {
        using var temp = new TempFolder();
        DesktopLog.Init(temp.Sub("logs"));
        var env = DesktopEnvironment.Create(DesktopOptions.Parse(["--test-root", temp.Sub("root")]));
        var host = new DatabaseHost(env);
        host.Prepare(null);
        await SqliteTestDatabase.CreateAsync(host.DatabaseFile, "erste Notiz");

        var backups = host.CreateBackups();
        Assert.Equal(BackupOutcome.Created, await backups.RunAsync("Test", CancellationToken.None));
        var backup = Directory.GetFiles(backups.RecentDir, "platinvault-*.db").Single();

        await SqliteTestDatabase.SetNoteAsync(host.DatabaseFile, "geändert");
        var rows = await backups.RestoreAsync(backup, "v2026.11.12", CancellationToken.None);

        Assert.Equal("erste Notiz", await SqliteTestDatabase.NoteAsync(host.DatabaseFile));
        Assert.Contains(rows, r => r.Table == "games" && r.Rows == 0);
        // The state before the restore was kept as a backup of its own (no pruning)
        Assert.Equal(2, Directory.GetFiles(backups.RecentDir, "platinvault-*.db").Length);
    }
}
