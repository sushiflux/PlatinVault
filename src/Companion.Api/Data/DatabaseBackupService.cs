using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Companion.Api.Localization;
using Microsoft.Data.Sqlite;

namespace Companion.Api.Data;

/// <summary>What a backup run did.</summary>
public enum BackupOutcome
{
    Created,
    Unchanged,
    Failed,
}

/// <summary>
/// Automatic backups of the SQLite file - ONE implementation for every host (Docker/Unraid/Linux/NAS, development, Windows program),
/// following the backup rules of the former PostgreSQL sidecar:
/// <list type="bullet">
/// <item><b>How:</b> SQLite's own online copy <c>VACUUM INTO</c> (a consistent snapshot while the server keeps running, compacted) -
/// never a copy of the live file, which could catch half a write.</item>
/// <item><b>When:</b> at start, every 6 hours and at a clean stop (<see cref="DatabaseBackupWorker"/>); the Windows program's stop before
/// an update is such a clean stop. <c>--backup-now</c> makes one by hand.</item>
/// <item><b>Only when something changed:</b> the content of each new copy is hashed - schema plus every row of every table in a fixed
/// order, so the page layout of the file does not matter - and compared with the hash of the last kept backup (<c>.last-hash</c>).
/// Otherwise an empty database after a data loss would keep producing identical copies that push the last GOOD ones out of the
/// rotation. The lease table is left out of the hash (it only says which process records history right now).</item>
/// <item><b>Verified:</b> <c>PRAGMA integrity_check</c> on the copy must answer "ok", else the copy is discarded and logged.</item>
/// <item><b>Kept:</b> <c>recent/</c> the newest 20, <c>daily/</c> the first backup of each day for 60 days. Only files named
/// <c>platinvault-&lt;date&gt;[-&lt;time&gt;].db</c> are ever deleted; anything else in the folder stays.</item>
/// <item><b>Two processes, one folder</b> (development: browser stack and Windows program share the file and <c>.backups/auto</c>): a
/// lock file serialises the runs across processes, and the hash makes the second process's copy "unchanged".</item>
/// </list>
/// </summary>
public sealed class DatabaseBackupService(string databaseFile, string backupRoot, ILogger logger, TimeProvider? time = null)
{
    public const string Prefix = "platinvault";
    public const string Extension = ".db";
    public const int KeepRecent = 20;
    public const int KeepDailyDays = 60;

    /// <summary>Tables that are not part of "did the content change" (the lease only says which process records history right now).</summary>
    private static readonly HashSet<string> VolatileTables = new(StringComparer.Ordinal) { "app_leases", "__EFMigrationsLock" };

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(90);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string DatabaseFile { get; } = Path.GetFullPath(databaseFile);

    public string Root { get; } = Path.GetFullPath(backupRoot);

    public string RecentDir => Path.Combine(Root, "recent");

    public string DailyDir => Path.Combine(Root, "daily");

    /// <summary>Work folder of a restore (the copy of the chosen backup, safety copies when the online backup is impossible).</summary>
    public string RestoreDir => Path.Combine(Root, "restore");

    private string HashFile => Path.Combine(Root, ".last-hash");

    private string LockFile => Path.Combine(Root, ".backup.lock");

    /// <summary>The newest kept backup (null when there is none) - for the system check.</summary>
    public FileInfo? Latest() =>
        Directory.Exists(RecentDir)
            ? new DirectoryInfo(RecentDir).GetFiles(Prefix + "-*" + Extension).Where(f => Retention.Stamp(f.FullName) is not null)
                .OrderByDescending(f => Retention.Stamp(f.FullName)).FirstOrDefault()
            : null;

    /// <param name="reason">For the log ("Start", "alle 6 Stunden", "Beenden", "von Hand").</param>
    /// <param name="force">Keep the copy even when nothing changed (by hand, safety copy before a restore).</param>
    /// <param name="prune">False for the safety backup before a restore: the retention rules must not delete anything then (the chosen
    /// backup itself could be the oldest one).</param>
    public async Task<BackupOutcome> RunAsync(string reason, CancellationToken ct, bool force = false, bool prune = true)
    {
        await _gate.WaitAsync(ct);
        string? temp = null;
        try
        {
            if (!File.Exists(DatabaseFile))
            {
                logger.LogWarning("Sicherung ({Reason}): keine Datenbank unter {File} - nichts zu sichern.", reason, DatabaseFile);
                return BackupOutcome.Failed;
            }
            Directory.CreateDirectory(RecentDir);
            Directory.CreateDirectory(DailyDir);
            await using var crossProcess = await AcquireFolderLockAsync(ct);
            if (crossProcess is null)
            {
                logger.LogWarning("Sicherung ({Reason}): der Sicherungsordner {Dir} ist seit {Seconds} Sekunden von einem anderen Prozess belegt - übersprungen.",
                    reason, Root, (int)LockWait.TotalSeconds);
                return BackupOutcome.Failed;
            }

            var now = _time.GetLocalNow().DateTime;
            // Two backups within one second (e.g. the safety backup right before a restore) must never overwrite each other: the name
            // moves on by a second until it is free (the name stays parseable for the retention rules)
            while (File.Exists(Path.Combine(RecentDir, $"{Prefix}-{now:yyyyMMdd-HHmmss}{Extension}"))) now = now.AddSeconds(1);
            var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            temp = Path.Combine(RecentDir, $".tmp-{stamp}-{Guid.NewGuid():N}{Extension}");

            await SnapshotAsync(DatabaseFile, temp, ct);
            if (await VerifyAsync(temp, ct) is { } problem)
            {
                logger.LogError("Sicherung ({Reason}): die Kopie ist nicht in Ordnung ({Problem}) - verworfen.", reason, problem);
                return BackupOutcome.Failed;
            }

            var hash = await ContentHashAsync(temp, ct);
            if (!force && File.Exists(HashFile) && (await File.ReadAllTextAsync(HashFile, ct)).Trim() == hash)
            {
                logger.LogInformation("Sicherung ({Reason}): keine Änderung seit der letzten Sicherung - übersprungen.", reason);
                return BackupOutcome.Unchanged;
            }

            var final = Path.Combine(RecentDir, $"{Prefix}-{stamp}{Extension}");
            File.Move(temp, final);
            temp = null;
            await File.WriteAllTextAsync(HashFile, hash, ct);
            var daily = Path.Combine(DailyDir, $"{Prefix}-{now:yyyyMMdd}{Extension}");
            if (!File.Exists(daily)) File.Copy(final, daily);
            if (prune)
            {
                foreach (var old in Retention.ToDelete(Directory.GetFiles(RecentDir, Prefix + "-*" + Extension), Directory.GetFiles(DailyDir, Prefix + "-*" + Extension), now))
                {
                    File.Delete(old);
                }
            }
            logger.LogInformation("Sicherung ({Reason}) angelegt: {File} ({Bytes} Bytes).", reason, Path.GetFileName(final), new FileInfo(final).Length);
            return BackupOutcome.Created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            logger.LogError("Sicherung ({Reason}) fehlgeschlagen: {Message}", reason, ex.Message);
            return BackupOutcome.Failed;
        }
        finally
        {
            if (temp is not null) TryDelete(temp);
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces the database with a backup. The server must NOT be running on the file (Windows refuses while any process has it open;
    /// in a container the scripts stop it first). Order - the rule of the former PostgreSQL restore, never weakened:
    /// <list type="number">
    /// <item>The chosen backup is copied into <c>restore/</c> first: nothing below (safety backup, retention) can change or delete the source.</item>
    /// <item>The copy is checked (SQLite integrity, a PlatinVault database, not from a newer PlatinVault) before the database is touched.</item>
    /// <item>A safety backup of the current state, without the retention rules. When the file cannot be copied online (e.g. damaged), its
    /// files are copied as they are into <c>restore/before-restore-&lt;time&gt;/</c> - the server is stopped, so that copy is exact.</item>
    /// <item>Only then the database file is replaced (old WAL/SHM files removed first, they belong to the old file).</item>
    /// </list>
    /// Missing migrations are applied by the server on its next start, so an older backup is lifted to the current schema.
    /// </summary>
    /// <returns>The rows per table of the restored database.</returns>
    public async Task<IReadOnlyList<(string Table, long Rows)>> RestoreAsync(string backupFile, string runningVersion, CancellationToken ct)
    {
        var source = Path.GetFullPath(backupFile);
        if (!File.Exists(source)) throw new DatabaseRestoreException(ServerTexts.Backup.RestoreMissing(source));

        // 1. Copy away before anything else happens
        Directory.CreateDirectory(RestoreDir);
        var work = Path.Combine(RestoreDir, $"restore-source-{Guid.NewGuid():N}{Extension}");
        try
        {
            File.Copy(source, work);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseRestoreException(ServerTexts.Backup.RestoreUnreadable(source), ex.Message, ex);
        }

        try
        {
            // 2. Usable at all? Before the database is touched in any way
            if (await VerifyAsync(work, ct) is { } problem) throw new DatabaseRestoreException(ServerTexts.Backup.RestoreUnreadable(source), problem);
            await using (var check = new SqliteConnection(SqliteDatabase.ConnectionString(work, pooling: false, SqliteOpenMode.ReadWrite)))
            {
                await check.OpenAsync(ct);
                if (!await DatabaseInitializer.TableExistsAsync(check, "games", ct) || !await DatabaseInitializer.TableExistsAsync(check, "__EFMigrationsHistory", ct))
                {
                    throw new DatabaseRestoreException(ServerTexts.Backup.RestoreNotPlatinVault(source));
                }
                if (await DatabaseInitializer.CheckNewerAsync(check, runningVersion, ct) is { } newer)
                {
                    throw new DatabaseRestoreException(ServerTexts.Backup.RestoreNewer(source), string.Join(", ", newer.Unknown));
                }
            }

            // Nobody may have the database open while it is replaced (effective on Windows; containers are stopped by the scripts)
            EnsureNotInUse();

            // 3. Safety copy of the current state, without pruning
            if (File.Exists(DatabaseFile))
            {
                if (await RunAsync("vor Wiederherstellung", ct, force: true, prune: false) == BackupOutcome.Failed)
                {
                    var raw = CopyFilesAsTheyAre();
                    logger.LogWarning("Online-Sicherung vor der Wiederherstellung nicht möglich - Datenbankdateien unverändert nach {Dir} kopiert.", raw);
                }
            }

            // 4. Only now: replace the file
            SqliteConnection.ClearAllPools();
            foreach (var sidecar in new[] { DatabaseFile + "-wal", DatabaseFile + "-shm", DatabaseFile + "-journal" })
            {
                try
                {
                    if (File.Exists(sidecar)) File.Delete(sidecar);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new DatabaseRestoreException(ServerTexts.Backup.RestoreInUse(DatabaseFile), ex.Message, ex);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
            var incoming = DatabaseFile + ".restore-tmp";
            File.Copy(work, incoming, overwrite: true);
            File.Move(incoming, DatabaseFile, overwrite: true);
            // The restored content is the new reference for "changed since the last backup"
            TryDelete(HashFile);

            var rows = await CountRowsAsync(DatabaseFile, ct);
            logger.LogInformation("Sicherung wiederhergestellt: {File}", source);
            return rows;
        }
        catch (SqliteException ex)
        {
            throw new DatabaseRestoreException(ServerTexts.Backup.RestoreUnreadable(source), ex.Message, ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(work);
        }
    }

    /// <summary>
    /// A consistent copy of a (possibly running) database: SQLite's <c>VACUUM INTO</c>. The copy is switched to the plain rollback
    /// journal, so a backup is always ONE self-contained file (no -wal/-shm next to it) that can be copied, mailed or restored as it is.
    /// </summary>
    public static async Task SnapshotAsync(string databaseFile, string target, CancellationToken ct)
    {
        await using (var connection = new SqliteConnection(SqliteDatabase.ConnectionString(databaseFile, pooling: false, SqliteOpenMode.ReadWrite)))
        {
            await connection.OpenAsync(ct);
            await SqliteDatabase.ExecuteAsync(connection, $"PRAGMA busy_timeout = {SqliteDatabase.BusyTimeoutMilliseconds};", ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $target";
            command.Parameters.AddWithValue("$target", target);
            await command.ExecuteNonQueryAsync(ct);
        }
        await using (var copy = new SqliteConnection(SqliteDatabase.ConnectionString(target, pooling: false, SqliteOpenMode.ReadWrite)))
        {
            await copy.OpenAsync(ct);
            await SqliteDatabase.ScalarAsync(copy, "PRAGMA journal_mode = DELETE;", ct);
        }
    }

    /// <summary><c>PRAGMA integrity_check</c> of a file that nobody else uses: null when fine, else what is wrong.</summary>
    public static async Task<string?> VerifyAsync(string file, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqliteConnection(SqliteDatabase.ConnectionString(file, pooling: false, SqliteOpenMode.ReadWrite));
            await connection.OpenAsync(ct);
            return await SqliteDatabase.IntegrityCheckAsync(connection, ct);
        }
        catch (SqliteException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Hash of the CONTENT of a database file: the schema (sorted by name) and every row of every table, sorted by all columns, with
    /// type tags. Independent of the file's page layout, so two backups with the same data hash the same.
    /// </summary>
    public static async Task<string> ContentHashAsync(string file, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(SqliteDatabase.ConnectionString(file, pooling: false, SqliteOpenMode.ReadWrite));
        await connection.OpenAsync(ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var tables = new List<string>();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "SELECT type, name, coalesce(sql, '') FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name";
            await using var reader = await schema.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var (type, name) = (reader.GetString(0), reader.GetString(1));
                if (VolatileTables.Contains(name)) continue;
                Append(hash, $"S|{type}|{name}|{reader.GetString(2)}\n");
                if (type == "table") tables.Add(name);
            }
        }
        foreach (var table in tables)
        {
            await using var rows = connection.CreateCommand();
            var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
            // Column count first, so ORDER BY can list every column position
            int columns;
            await using (var probe = connection.CreateCommand())
            {
                probe.CommandText = $"SELECT * FROM {quoted} LIMIT 0";
                await using var probeReader = await probe.ExecuteReaderAsync(ct);
                columns = probeReader.FieldCount;
            }
            rows.CommandText = $"SELECT * FROM {quoted} ORDER BY {string.Join(", ", Enumerable.Range(1, columns))}";
            Append(hash, $"T|{table}\n");
            await using var reader = await rows.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    switch (value)
                    {
                        case DBNull:
                            Append(hash, "N;");
                            break;
                        case byte[] bytes:
                            Append(hash, $"B{bytes.Length}:");
                            hash.AppendData(bytes);
                            Append(hash, ";");
                            break;
                        case string text:
                            Append(hash, $"S{text.Length}:{text};");
                            break;
                        default:
                            Append(hash, $"V:{Convert.ToString(value, CultureInfo.InvariantCulture)};");
                            break;
                    }
                }
                Append(hash, "\n");
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Rows per table (for the restore report).</summary>
    public static async Task<IReadOnlyList<(string Table, long Rows)>> CountRowsAsync(string file, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(SqliteDatabase.ConnectionString(file, pooling: false, SqliteOpenMode.ReadWrite));
        await connection.OpenAsync(ct);
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
        }
        var result = new List<(string, long)>();
        foreach (var table in tables)
        {
            await using var count = connection.CreateCommand();
            count.CommandText = $"SELECT count(*) FROM \"{table.Replace("\"", "\"\"")}\"";
            result.Add((table, Convert.ToInt64(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static void Append(IncrementalHash hash, string text) => hash.AppendData(Encoding.UTF8.GetBytes(text));

    /// <summary>On Windows a file that another process (another server) has open cannot be opened exclusively: then the restore stops.</summary>
    private void EnsureNotInUse()
    {
        if (!File.Exists(DatabaseFile)) return;
        SqliteConnection.ClearAllPools();
        try
        {
            using var exclusive = new FileStream(DatabaseFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new DatabaseRestoreException(ServerTexts.Backup.RestoreInUse(DatabaseFile), ex.Message, ex);
        }
    }

    /// <summary>The database files as they are (db, -wal, -shm) into a new folder under restore/; returns the folder.</summary>
    private string CopyFilesAsTheyAre()
    {
        var folder = Path.Combine(RestoreDir, $"before-restore-{_time.GetLocalNow():yyyyMMdd-HHmmss}");
        for (var n = 2; Directory.Exists(folder); n++) folder = Path.Combine(RestoreDir, $"before-restore-{_time.GetLocalNow():yyyyMMdd-HHmmss}-{n}");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var file in new[] { DatabaseFile, DatabaseFile + "-wal", DatabaseFile + "-shm" })
            {
                if (File.Exists(file)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseRestoreException(ServerTexts.Backup.RestoreNoSafetyBackup, ex.Message, ex);
        }
        return folder;
    }

    /// <summary>The lock file that serialises backups of several processes on one folder; null when it stays busy.</summary>
    private async Task<FileStream?> AcquireFolderLockAsync(CancellationToken ct)
    {
        var deadline = _time.GetUtcNow() + LockWait;
        while (true)
        {
            try
            {
                return new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (_time.GetUtcNow() < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next run; the name never matches the retention pattern
        }
    }

    /// <summary>The retention rules on file names (no clock or disk access, so they are unit-tested).</summary>
    public static class Retention
    {
        public static IEnumerable<string> ToDelete(IEnumerable<string> recent, IEnumerable<string> daily, DateTime now)
        {
            var sortedRecent = recent
                .Select(path => (path, stamp: Stamp(path)))
                .Where(e => e.stamp is not null)
                .OrderByDescending(e => e.stamp)
                .ToList();
            foreach (var (path, _) in sortedRecent.Skip(KeepRecent)) yield return path;

            var cutoff = now.Date.AddDays(-KeepDailyDays);
            foreach (var path in daily)
            {
                if (Stamp(path) is { } day && day < cutoff) yield return path;
            }
        }

        /// <summary>The time in a name "platinvault-20261101-213000.db" or the day in "platinvault-20261101.db"; null for any other name.</summary>
        public static DateTime? Stamp(string path)
        {
            if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase)) return null;
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(Prefix + "-", StringComparison.Ordinal)) return null;
            var rest = name[(Prefix.Length + 1)..];
            if (DateTime.TryParseExact(rest, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return at;
            if (DateTime.TryParseExact(rest, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day;
            return null;
        }
    }
}

/// <summary>A restore did not happen (or stopped before the database was replaced); <see cref="Text"/> says why in both languages.</summary>
public sealed class DatabaseRestoreException(Msg text, string? detail = null, Exception? inner = null)
    : Exception(text.De + (detail is null ? string.Empty : " " + detail), inner)
{
    public Msg Text { get; } = text;

    public string? Detail { get; } = detail;
}
