using System.Data.Common;
using Companion.Api.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Companion.Api.Data;

/// <summary>
/// How PlatinVault opens its SQLite file (user decision 2026-11-02: one file per installation instead of a PostgreSQL server).
/// <list type="bullet">
/// <item>WAL journal: readers never block the writer and the other way round; set once, it is stored in the file itself.</item>
/// <item>A busy timeout on every connection: in development two server processes (browser stack + Windows program) share one file,
/// and a write that meets the other process's write waits instead of failing at once.</item>
/// <item>Foreign keys on (SQLite has them off per connection by default) - the cascades of the model rely on them.</item>
/// <item>A <c>PRAGMA quick_check</c> at every start: a damaged file stops the start with a clear bilingual message. Nothing is ever
/// deleted, repaired or recreated automatically - the user restores a backup (see <see cref="DatabaseBackupService"/>).</item>
/// </list>
/// </summary>
public static class SqliteDatabase
{
    /// <summary>How long a statement waits for a lock held by another connection (milliseconds).</summary>
    public const int BusyTimeoutMilliseconds = 15_000;

    /// <summary>The connection string of the database file. Pooling off for one-off connections that must release the file at once (backups, restore).</summary>
    public static string ConnectionString(string file, bool pooling = true, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = mode,
            Pooling = pooling,
            ForeignKeys = true,
            // Microsoft.Data.Sqlite retries a busy statement until this many seconds have passed
            DefaultTimeout = 60,
        }.ToString();

    /// <summary>
    /// Opens the file once before the server uses it: creates its folder, refuses a damaged file (<see cref="DamagedDatabaseException"/>,
    /// nothing changed) and switches a new file to WAL. A missing file is fine - the migrations create it.
    /// </summary>
    public static async Task PrepareAsync(string file, ILogger logger, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var existed = File.Exists(file) && new FileInfo(file).Length > 0;
        await using var connection = new SqliteConnection(ConnectionString(file, pooling: false));
        try
        {
            await connection.OpenAsync(ct);
            await ExecuteAsync(connection, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};", ct);
            if (existed)
            {
                var result = await QuickCheckAsync(connection, ct);
                if (result is not null) throw Damaged(file, result, logger, null);
            }
            var mode = Convert.ToString(await ScalarAsync(connection, "PRAGMA journal_mode = WAL;", ct), System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Datenbank {File}: WAL-Modus nicht aktiv (Modus {Mode}) - läuft weiter, aber Lesen und Schreiben können sich gegenseitig warten lassen.", file, mode);
            }
            logger.LogInformation(existed ? "Datenbank {File} geöffnet (Prüfung ok)." : "Neue Datenbank {File} angelegt.", file);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SQLitePCL.raw.SQLITE_CORRUPT or SQLitePCL.raw.SQLITE_NOTADB)
        {
            throw Damaged(file, ex.Message, logger, ex);
        }
        finally
        {
            SqliteConnection.ClearPool(connection);
        }
    }

    /// <summary><c>PRAGMA quick_check</c>: null when the file is fine, else what SQLite reported (first lines).</summary>
    public static async Task<string?> QuickCheckAsync(SqliteConnection connection, CancellationToken ct = default) =>
        await CheckAsync(connection, "PRAGMA quick_check;", ct);

    /// <summary><c>PRAGMA integrity_check</c> (the thorough one, used on every backup): null when fine, else what SQLite reported.</summary>
    public static async Task<string?> IntegrityCheckAsync(SqliteConnection connection, CancellationToken ct = default) =>
        await CheckAsync(connection, "PRAGMA integrity_check;", ct);

    private static async Task<string?> CheckAsync(SqliteConnection connection, string pragma, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = pragma;
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct) && lines.Count < 5) lines.Add(reader.GetString(0));
        return lines is ["ok"] ? null : string.Join("; ", lines);
    }

    /// <summary>True when <paramref name="exception"/> (or an inner one) is SQLite's "unable to open database file" (error code 14, SQLITE_CANTOPEN).</summary>
    public static bool IsCannotOpen(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is SqliteException { SqliteErrorCode: SQLitePCL.raw.SQLITE_CANTOPEN }) return true;
        }
        return false;
    }

    /// <summary>The explained version of error code 14: hint about a blocking virus scanner and what to do, the original SQLite text kept at the end.</summary>
    public static CannotOpenDatabaseException CannotOpen(string file, Exception inner, ILogger logger)
    {
        var technical = inner is SqliteException ? inner.Message : (inner.InnerException?.Message ?? inner.Message);
        var text = ServerTexts.DatabaseGuard.CannotOpen(file, technical);
        logger.LogCritical("{German}", text.De);
        logger.LogCritical("{English}", text.En);
        return new CannotOpenDatabaseException(text, inner);
    }

    private static DamagedDatabaseException Damaged(string file, string detail, ILogger logger, Exception? inner)
    {
        var text = ServerTexts.DatabaseGuard.Damaged(file, detail);
        logger.LogCritical("{German}", text.De);
        logger.LogCritical("{English}", text.En);
        return new DamagedDatabaseException(text, inner);
    }

    internal static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct);
    }
}

/// <summary>Sets the per-connection settings SQLite does not keep in the file (busy timeout) on every connection EF Core opens.</summary>
public sealed class SqliteConnectionSetup : DbConnectionInterceptor
{
    private static readonly string Pragmas = $"PRAGMA busy_timeout = {SqliteDatabase.BusyTimeoutMilliseconds};";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>The server must not start on this database; <see cref="Text"/> explains why in both languages. Nothing was changed.</summary>
public abstract class DatabaseRefusedException(Msg text, Exception? inner = null) : Exception(text.De, inner)
{
    public Msg Text { get; } = text;

    public override string Message => Text.ToString();
}

/// <summary>
/// SQLite could not open the file or create its helper files (-wal, -shm): error code 14. Usually a virus scanner blocked or quarantined
/// files in the data folder. <see cref="DatabaseRefusedException.Text"/> explains it and keeps SQLite's own text at the end; nothing was changed.
/// </summary>
public sealed class CannotOpenDatabaseException(Msg text, Exception? inner = null) : DatabaseRefusedException(text, inner);

/// <summary>The database file failed SQLite's own check (<c>quick_check</c>) or is no SQLite file at all.</summary>
public sealed class DamagedDatabaseException(Msg text, Exception? inner = null) : DatabaseRefusedException(text, inner);
