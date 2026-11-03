using Companion.Api.Configuration;
using Companion.Api.Data;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Desktop.Database;

/// <summary>
/// Where the Windows program keeps its database and backups, and what it tells the server about them. Since 2026-11-02 the database
/// is ONE SQLite file (<c>%LOCALAPPDATA%\PlatinVault\database\platinvault.db</c>); there is no database process, no port and no
/// credentials any more - the server opens the file itself, checks it, migrates it and backs it up (start, every 6 hours, clean stop).
/// <para>
/// Shared development mode (<c>--shared-data-dir</c>, start-windows.ps1's default): the program uses the browser stack's file
/// <c>.data\dev\platinvault.db</c> and its key ring/API keys; both servers back up into the same folder (<c>.backups\auto</c>, passed as
/// <c>--backups-dir</c>) - the backup lock and the change check keep that free of duplicates.
/// </para>
/// </summary>
internal sealed class DatabaseHost(DesktopEnvironment env)
{
    /// <summary>The database file this run uses.</summary>
    public string DatabaseFile => env.Shared is { } shared
        ? Path.Combine(shared.DataDir, DatabaseOptions.FileName)
        : Path.Combine(env.Data.Database, DatabaseOptions.FileName);

    public string BackupDir => env.Data.Backups;

    /// <summary>Development only: the browser stack's database file and keys are used.</summary>
    public bool IsShared => env.Shared is not null;

    /// <summary>The server's backup service for the helper modes (<c>--backup-now</c>, <c>--restore</c>), which run without a server.</summary>
    public DatabaseBackupService CreateBackups() => new(DatabaseFile, BackupDir, new DesktopLogLogger("Sicherung"));

    /// <summary>
    /// Before the server starts: the folders exist. A cluster of the former bundled PostgreSQL (betas before 2026-11-02) in the database
    /// folder is left alone - it is not used any more and never deleted automatically; the log says so once per start.
    /// </summary>
    public void Prepare(IProgress<StepReport>? progress)
    {
        progress?.Report(new StepReport(0.2f, DesktopTexts.Window.StartingDatabase));
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        Directory.CreateDirectory(BackupDir);
        if (!IsShared && File.Exists(Path.Combine(env.Data.Database, "PG_VERSION")))
        {
            DesktopLog.Info($"Im Datenbankordner {env.Data.Database} liegen noch Dateien der früheren PostgreSQL-Datenbank. PlatinVault nutzt sie nicht mehr " +
                            "(neue Datenbank: platinvault.db) und löscht sie nicht von selbst.");
        }
        progress?.Report(new StepReport(0.5f, DesktopTexts.Window.StartingServer));
    }

    /// <summary>
    /// The server's database settings: the file, the backup folder and - in shared development mode - the browser stack's key ring and
    /// encrypted API keys, so the same keys decrypt. The data folder with the caches stays the program's own (set by the caller).
    /// </summary>
    public Dictionary<string, string?> ServerSettings()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Companion:Database:Path"] = DatabaseFile,
            ["Companion:Backups:Dir"] = BackupDir,
        };
        if (env.Shared is { } shared)
        {
            settings["Companion:KeyRingDir"] = shared.KeyRingDir;
            settings["Companion:SecretsDir"] = shared.SecretsDir;
        }
        return settings;
    }
}

/// <summary>Start progress: 0..1 and what happens now.</summary>
internal readonly record struct StepReport(float Value, Msg Text);

/// <summary>A problem of the program's own start (not of the server); <see cref="Text"/> in both languages, <see cref="Detail"/> technical.</summary>
internal sealed class DatabaseException(Msg message, string? detail = null, Exception? inner = null)
    : Exception(message.De + (detail is null ? string.Empty : " " + detail), inner)
{
    public Msg Text { get; } = message;

    public string? Detail { get; } = detail;
}

/// <summary>Writes the server's backup lines (German) into desktop.log when no server runs (helper modes).</summary>
internal sealed class DesktopLogLogger(string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var text = $"{category}: {formatter(state, exception)}";
        if (logLevel >= LogLevel.Error) DesktopLog.Error(text);
        else if (logLevel == LogLevel.Warning) DesktopLog.Warn(text);
        else DesktopLog.Info(text);
    }
}
