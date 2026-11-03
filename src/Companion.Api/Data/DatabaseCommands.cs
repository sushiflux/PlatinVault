using Companion.Api.Configuration;
using Companion.Api.Localization;
using Microsoft.Extensions.Options;

namespace Companion.Api.Data;

/// <summary>
/// The server's database commands for operators, run INSTEAD of the server (nothing else starts):
/// <list type="bullet">
/// <item><c>--backup-now</c>: one backup right now (also while a server runs on the same file - backups are online copies).
/// Exit code 0 = created or unchanged, 1 = failed.</item>
/// <item><c>--restore &lt;file&gt;</c>: replaces the database with a backup (see <see cref="DatabaseBackupService.RestoreAsync"/>); the
/// server must be stopped. Docker: <c>docker compose run --rm platinvault --restore /data/backups/recent/&lt;file&gt;</c>
/// (deploy/restore-db.sh does all of it), development: <c>scripts/restore-db.ps1</c>. Exit code 0 = restored, 1 = not.</item>
/// </list>
/// Output: one line per step in German and English (operators of a container read either).
/// </summary>
public static class DatabaseCommands
{
    /// <summary>Splits the arguments: the command (if any) and the remaining arguments for the host's configuration.</summary>
    public static (string? Command, string? File, string[] HostArgs) Parse(string[] args)
    {
        var rest = new List<string>();
        string? command = null, file = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--backup-now":
                    command = "backup";
                    break;
                case "--restore":
                    command = "restore";
                    file = i + 1 < args.Length ? args[++i] : null;
                    break;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }
        return (command, file, rest.ToArray());
    }

    /// <summary>Runs a command with the configuration the server would use (data folder, database file, backup folder).</summary>
    public static async Task<int> RunAsync(string command, string? file, IServiceProvider services, TextWriter output, CancellationToken ct = default)
    {
        var backups = services.GetRequiredService<DatabaseBackupService>();
        var version = services.GetRequiredService<AppVersion>().Current;
        void Say(Msg text) => output.WriteLine($"{text.De} / {text.En}");

        if (command == "backup")
        {
            var outcome = await backups.RunAsync("von Hand", ct, force: true);
            Say(outcome == BackupOutcome.Failed ? ServerTexts.Backup.ManualFailed(backups.Root) : ServerTexts.Backup.ManualDone(backups.RecentDir));
            return outcome == BackupOutcome.Failed ? 1 : 0;
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            Say(ServerTexts.Backup.RestoreNoFile);
            return 2;
        }
        Say(ServerTexts.Backup.RestoreStarting(Path.GetFullPath(file), backups.DatabaseFile));
        try
        {
            var rows = await backups.RestoreAsync(file, version, ct);
            Say(ServerTexts.Backup.RestoreDone);
            foreach (var (table, count) in rows) output.WriteLine($"  {table}: {count}");
            return 0;
        }
        catch (DatabaseRestoreException ex)
        {
            Say(ex.Detail is null ? ex.Text : ex.Text.Append(" (" + ex.Detail + ")"));
            return 1;
        }
    }

    /// <summary>The backup service of a configuration (the server registers the same).</summary>
    public static DatabaseBackupService CreateBackupService(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CompanionOptions>>().Value;
        return new DatabaseBackupService(options.DatabaseFile, options.BackupDir, services.GetRequiredService<ILogger<DatabaseBackupService>>());
    }
}
