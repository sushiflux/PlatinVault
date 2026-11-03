using Companion.Api.Data;
using Companion.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Tests;

/// <summary>A temporary folder with a real, migrated PlatinVault SQLite file (never the dev database).</summary>
internal sealed class SqliteScratch : IDisposable
{
    public SqliteScratch()
    {
        Root = Path.Combine(Path.GetTempPath(), "pv-sqlite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DatabaseFile = Path.Combine(Root, "data", "platinvault.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
    }

    public string Root { get; }

    public string DatabaseFile { get; }

    public string BackupDir => Path.Combine(Root, "backups");

    public CompanionDbContext Context(string? file = null) =>
        new(new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlite(SqliteDatabase.ConnectionString(file ?? DatabaseFile))
            .AddInterceptors(new SqliteConnectionSetup())
            .Options);

    public async Task<CompanionDbContext> MigratedAsync()
    {
        await SqliteDatabase.PrepareAsync(DatabaseFile, NullLogger.Instance);
        var db = Context();
        await db.Database.MigrateAsync();
        return db;
    }

    public DatabaseBackupService Backups(TimeProvider? time = null) => new(DatabaseFile, BackupDir, NullLogger.Instance, time);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* a handle of the test runner may still be closing */ }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class SqliteStartTests
{
    [Fact]
    public async Task A_new_file_is_created_in_wal_mode_with_foreign_keys_on()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();

        await using var connection = new SqliteConnection(SqliteDatabase.ConnectionString(scratch.DatabaseFile, pooling: false));
        await connection.OpenAsync();
        Assert.Equal("wal", (string)(await SqliteDatabase.ScalarAsync(connection, "PRAGMA journal_mode;", default))!);
        Assert.Equal(1L, (long)(await SqliteDatabase.ScalarAsync(connection, "PRAGMA foreign_keys;", default))!);
    }

    [Fact]
    public async Task Error_14_cannot_open_is_recognised_and_explained_plainly_with_the_original_text_kept()
    {
        using var scratch = new SqliteScratch();
        // A folder where the database file should be: SQLite cannot open it (SQLITE_CANTOPEN, "unable to open database file")
        var file = Path.Combine(scratch.Root, "data", "platinvault.db");
        Directory.CreateDirectory(file);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => SqliteDatabase.PrepareAsync(file, NullLogger.Instance));
        Assert.True(SqliteDatabase.IsCannotOpen(failure), failure.ToString());
        // Also when another exception wraps it (EF Core / the host)
        Assert.True(SqliteDatabase.IsCannotOpen(new InvalidOperationException("wrapped", failure)));
        Assert.False(SqliteDatabase.IsCannotOpen(new IOException("something else")));

        var explained = SqliteDatabase.CannotOpen(file, failure, NullLogger.Instance);
        Assert.IsAssignableFrom<DatabaseRefusedException>(explained);
        // Plain hint (both languages), no antivirus brand, and SQLite's own words at the end
        foreach (var text in new[] { explained.Text.De, explained.Text.En })
        {
            Assert.Contains("platinvault.db-wal", text);
            Assert.Contains("platinvault.db-shm", text);
            Assert.Contains("unable to open database file", text);
            Assert.DoesNotContain("Defender", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("Virenschutz", explained.Text.De);
        Assert.Contains("virus scanner", explained.Text.En);
        Assert.Contains("Quarantäne", explained.Text.De);
        Assert.Contains("quarantine", explained.Text.En);
    }

    [Fact]
    public async Task A_file_that_is_no_database_stops_the_start_and_is_left_as_it_is()
    {
        using var scratch = new SqliteScratch();
        var junk = new byte[8192];
        new Random(7).NextBytes(junk);
        await File.WriteAllBytesAsync(scratch.DatabaseFile, junk);

        var error = await Assert.ThrowsAsync<DamagedDatabaseException>(() => SqliteDatabase.PrepareAsync(scratch.DatabaseFile, NullLogger.Instance));

        Assert.Contains(scratch.DatabaseFile, error.Text.De);
        Assert.Contains("beschädigt", error.Text.De);
        Assert.Contains("damaged", error.Text.En);
        Assert.Equal(junk, await File.ReadAllBytesAsync(scratch.DatabaseFile));
    }

    [Fact]
    public async Task A_damaged_page_fails_the_quick_check()
    {
        using var scratch = new SqliteScratch();
        await using (var db = await scratch.MigratedAsync())
        {
            for (var i = 0; i < 400; i++) db.AppSettings.Add(new AppSettingEntity { Key = $"k{i}", Value = new string('x', 200) });
            await db.SaveChangesAsync();
        }
        SqliteConnection.ClearAllPools();
        // Overwrite a page in the middle (not the header): the file still opens, but SQLite's own check reports the damage
        var bytes = await File.ReadAllBytesAsync(scratch.DatabaseFile);
        Assert.True(bytes.Length > 8 * 4096);
        for (var i = 5 * 4096; i < 6 * 4096; i++) bytes[i] = 0x55;
        await File.WriteAllBytesAsync(scratch.DatabaseFile, bytes);

        await Assert.ThrowsAsync<DamagedDatabaseException>(() => SqliteDatabase.PrepareAsync(scratch.DatabaseFile, NullLogger.Instance));
    }
}

public sealed class SqliteStartGuardTests
{
    private static ServiceProvider Services(SqliteScratch scratch)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CompanionDbContext>(o => o.UseSqlite(SqliteDatabase.ConnectionString(scratch.DatabaseFile)));
        return services.BuildServiceProvider();
    }

    private static async Task<string> SnapshotAsync(string file)
    {
        await using var c = new SqliteConnection(SqliteDatabase.ConnectionString(file, pooling: false));
        await c.OpenAsync();
        var lines = new List<string>();
        foreach (var sql in new[]
                 {
                     "SELECT 'history:' || \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1",
                     "SELECT 'setting:' || \"Key\" || '=' || \"Value\" || '@' || \"UpdatedAt\" FROM app_settings ORDER BY 1",
                     "SELECT 'schema:' || name || ':' || coalesce(sql, '') FROM sqlite_master ORDER BY name",
                 })
        {
            await using var command = c.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        }
        return string.Join("\n", lines);
    }

    [Fact]
    public async Task A_fresh_start_migrates_and_a_newer_database_is_refused_without_changes()
    {
        using var scratch = new SqliteScratch();
        await SqliteDatabase.PrepareAsync(scratch.DatabaseFile, NullLogger.Instance);
        await using var services = Services(scratch);

        await DatabaseInitializer.MigrateAsync(services, NullLogger.Instance);
        await DatabaseInitializer.MigrateAsync(services, NullLogger.Instance); // a second start is a no-op
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            Assert.True(await db.AppSettings.AnyAsync(s => s.Key == DatabaseInitializer.SchemaVersionKey));
        }

        // A newer PlatinVault used the file: it applied a migration this build does not know and recorded its version
        await using (var c = new SqliteConnection(SqliteDatabase.ConnectionString(scratch.DatabaseFile, pooling: false)))
        {
            await c.OpenAsync();
            await SqliteDatabase.ExecuteAsync(c,
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20991231000000_FromTheFuture', '10.0.0');" +
                "UPDATE app_settings SET \"Value\" = 'v2099.1.5 (2.40 β)' WHERE \"Key\" = 'app.schema_version';", default);
        }
        var before = await SnapshotAsync(scratch.DatabaseFile);

        var refused = await Assert.ThrowsAsync<NewerDatabaseException>(() => DatabaseInitializer.MigrateAsync(services, NullLogger.Instance));
        Assert.Equal(["20991231000000_FromTheFuture"], refused.UnknownMigrations);
        Assert.Contains("v2099.1.5 (2.40 β)", refused.Text.De);
        Assert.Contains("unverändert", refused.Text.De);
        Assert.Contains("untouched", refused.Text.En);
        Assert.Equal(before, await SnapshotAsync(scratch.DatabaseFile));
    }

    [Fact]
    public void The_model_has_no_pending_changes_against_its_migrations()
    {
        using var scratch = new SqliteScratch();
        using var db = scratch.Context();
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

public sealed class SqliteDateTests
{
    [Fact]
    public async Task Instants_are_compared_ordered_and_aggregated_in_the_database()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var t0 = new DateTimeOffset(2026, 11, 2, 23, 30, 0, TimeSpan.Zero);
        // Stored from different offsets: all become UTC, the order is the order of the instants
        db.PlayerCountSamples.AddRange(
            new PlayerCountSampleEntity { AppId = 1, SampledAt = t0.ToOffset(TimeSpan.FromHours(2)), Count = 1 },
            new PlayerCountSampleEntity { AppId = 1, SampledAt = t0.AddMinutes(30), Count = 2 },
            new PlayerCountSampleEntity { AppId = 1, SampledAt = t0.AddMinutes(-90).ToOffset(TimeSpan.FromHours(-5)), Count = 3 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var ordered = await db.PlayerCountSamples.OrderBy(s => s.SampledAt).Select(s => s.Count).ToListAsync();
        Assert.Equal([3, 1, 2], ordered);
        Assert.Equal(2, await db.PlayerCountSamples.CountAsync(s => s.SampledAt >= t0.AddMinutes(-1)));
        Assert.Equal(t0.AddMinutes(-90), await db.PlayerCountSamples.MinAsync(s => (DateTimeOffset?)s.SampledAt));
        var latest = await db.PlayerCountSamples.MaxAsync(s => (DateTimeOffset?)s.SampledAt);
        Assert.Equal(t0.AddMinutes(30), latest);
        Assert.Equal(TimeSpan.Zero, latest!.Value.Offset);
    }

    [Fact]
    public async Task Old_samples_are_condensed_per_utc_day()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var day = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        db.PlayerCountSamples.AddRange(
            new PlayerCountSampleEntity { AppId = 7, SampledAt = day.AddHours(1), Count = 10 },
            new PlayerCountSampleEntity { AppId = 7, SampledAt = day.AddHours(23).AddMinutes(30), Count = 30 },
            new PlayerCountSampleEntity { AppId = 7, SampledAt = day.AddDays(1).AddHours(2), Count = 5 },
            new PlayerCountSampleEntity { AppId = 7, SampledAt = day.AddDays(40), Count = 99 });
        // An existing day row keeps its lower minimum
        db.PlayerCountDaily.Add(new PlayerCountDailyEntity { AppId = 7, Date = new DateOnly(2026, 9, 1), MinCount = 4, AvgCount = 4, MaxCount = 4 });
        db.PlayerCountDaily.Add(new PlayerCountDailyEntity { AppId = 7, Date = new DateOnly(2020, 1, 1), MinCount = 1, AvgCount = 1, MaxCount = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (condensed, samples, daily) = await HistoryRecorderWorker.CondenseAsync(db, day.AddDays(30), new DateOnly(2024, 1, 1), CancellationToken.None);

        Assert.Equal((2, 3, 1), (condensed, samples, daily));
        db.ChangeTracker.Clear();
        var first = await db.PlayerCountDaily.SingleAsync(d => d.Date == new DateOnly(2026, 9, 1));
        Assert.Equal((4, 20.0, 30), (first.MinCount, first.AvgCount, first.MaxCount));
        var second = await db.PlayerCountDaily.SingleAsync(d => d.Date == new DateOnly(2026, 9, 2));
        Assert.Equal((5, 5.0, 5), (second.MinCount, second.AvgCount, second.MaxCount));
        Assert.Equal(99, (await db.PlayerCountSamples.SingleAsync()).Count);
    }
}

public sealed class DatabaseLeaseTests
{
    [Fact]
    public async Task Only_one_server_holds_the_lease_and_it_is_free_again_after_release()
    {
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        await using var serverA = scratch.Context();
        await using var serverB = scratch.Context();
        var duration = TimeSpan.FromMinutes(30);

        await using (var held = await DatabaseLease.TryAcquireAsync(serverA, DatabaseLease.HistoryRecorder, duration, default, owner: "A"))
        {
            Assert.NotNull(held);
            Assert.Null(await DatabaseLease.TryAcquireAsync(serverB, DatabaseLease.HistoryRecorder, duration, default, owner: "B"));
            // Other names are independent
            await using var other = await DatabaseLease.TryAcquireAsync(serverB, "something-else", duration, default, owner: "B");
            Assert.NotNull(other);
        }

        await using (var again = await DatabaseLease.TryAcquireAsync(serverB, DatabaseLease.HistoryRecorder, duration, default, owner: "B"))
        {
            Assert.NotNull(again);
            Assert.Null(await DatabaseLease.TryAcquireAsync(serverA, DatabaseLease.HistoryRecorder, duration, default, owner: "A"));
        }
    }

    [Fact]
    public async Task A_lease_of_a_server_that_died_runs_out()
    {
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        await using var dead = scratch.Context();
        await using var alive = scratch.Context();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero));

        // Taken and never released (the process died)
        Assert.NotNull(await DatabaseLease.TryAcquireAsync(dead, DatabaseLease.HistoryRecorder, TimeSpan.FromMinutes(30), default, owner: "dead", time: clock));
        clock.Now = clock.Now.AddMinutes(29);
        Assert.Null(await DatabaseLease.TryAcquireAsync(alive, DatabaseLease.HistoryRecorder, TimeSpan.FromMinutes(30), default, owner: "alive", time: clock));
        clock.Now = clock.Now.AddMinutes(2);
        Assert.NotNull(await DatabaseLease.TryAcquireAsync(alive, DatabaseLease.HistoryRecorder, TimeSpan.FromMinutes(30), default, owner: "alive", time: clock));
    }

    [Fact]
    public async Task Two_servers_racing_for_the_lease_never_both_win()
    {
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        for (var round = 0; round < 20; round++)
        {
            var name = $"race-{round}";
            var attempts = Enumerable.Range(0, 4).Select(async i =>
            {
                await using var db = scratch.Context();
                return await DatabaseLease.TryAcquireAsync(db, name, TimeSpan.FromMinutes(5), default, owner: $"server-{i}") is not null;
            });
            Assert.Equal(1, (await Task.WhenAll(attempts)).Count(won => won));
        }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public sealed class DatabaseBackupTests
{
    private static async Task SetNoteAsync(SqliteScratch scratch, string value)
    {
        await using var db = scratch.Context();
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "test.note");
        if (row is null) db.AppSettings.Add(new AppSettingEntity { Key = "test.note", Value = value });
        else row.Value = value;
        await db.SaveChangesAsync();
    }

    private static async Task<string?> NoteAsync(string file)
    {
        await using var c = new SqliteConnection(SqliteDatabase.ConnectionString(file, pooling: false));
        await c.OpenAsync();
        await using var command = c.CreateCommand();
        command.CommandText = "SELECT \"Value\" FROM app_settings WHERE \"Key\" = 'test.note'";
        return await command.ExecuteScalarAsync() as string;
    }

    [Fact]
    public async Task A_backup_is_kept_only_when_the_content_changed_and_is_a_verified_single_file()
    {
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        await SetNoteAsync(scratch, "eins");
        var backups = scratch.Backups();

        Assert.Equal(BackupOutcome.Created, await backups.RunAsync("Test", default));
        Assert.Equal(BackupOutcome.Unchanged, await backups.RunAsync("Test", default));
        // The lease table does not count as a change
        await using (var db = scratch.Context())
        {
            await using var lease = await DatabaseLease.TryAcquireAsync(db, "x", TimeSpan.FromMinutes(1), default);
            Assert.Equal(BackupOutcome.Unchanged, await backups.RunAsync("Test", default));
        }

        await SetNoteAsync(scratch, "zwei");
        Assert.Equal(BackupOutcome.Created, await backups.RunAsync("Test", default));

        var files = Directory.GetFiles(backups.RecentDir, "platinvault-*.db");
        Assert.Equal(2, files.Length);
        Assert.Single(Directory.GetFiles(backups.DailyDir, "platinvault-*.db"));
        Assert.Empty(Directory.GetFiles(backups.RecentDir, "*-wal"));
        Assert.Empty(Directory.GetFiles(backups.RecentDir, ".tmp-*"));
        foreach (var file in files) Assert.Null(await DatabaseBackupService.VerifyAsync(file, default));
        // "By hand" always keeps a copy
        Assert.Equal(BackupOutcome.Created, await backups.RunAsync("von Hand", default, force: true));
    }

    [Fact]
    public async Task The_content_hash_ignores_the_page_layout()
    {
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        await SetNoteAsync(scratch, "gleich");
        var a = Path.Combine(scratch.Root, "a.db");
        var b = Path.Combine(scratch.Root, "b.db");
        await DatabaseBackupService.SnapshotAsync(scratch.DatabaseFile, a, default);
        // Same content, different history: rows added and removed again
        await using (var db = scratch.Context())
        {
            for (var i = 0; i < 200; i++) db.AppSettings.Add(new AppSettingEntity { Key = $"tmp{i}", Value = new string('y', 300) });
            await db.SaveChangesAsync();
            await db.AppSettings.Where(s => s.Key.StartsWith("tmp")).ExecuteDeleteAsync();
        }
        await DatabaseBackupService.SnapshotAsync(scratch.DatabaseFile, b, default);

        Assert.Equal(await DatabaseBackupService.ContentHashAsync(a, default), await DatabaseBackupService.ContentHashAsync(b, default));
        await SetNoteAsync(scratch, "anders");
        var c = Path.Combine(scratch.Root, "c.db");
        await DatabaseBackupService.SnapshotAsync(scratch.DatabaseFile, c, default);
        Assert.NotEqual(await DatabaseBackupService.ContentHashAsync(a, default), await DatabaseBackupService.ContentHashAsync(c, default));
    }

    [Fact]
    public async Task Restore_makes_a_safety_copy_first_and_replaces_the_file()
    {
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        await SetNoteAsync(scratch, "alt");
        var backups = scratch.Backups();
        await backups.RunAsync("Test", default);
        var chosen = Directory.GetFiles(backups.RecentDir, "platinvault-*.db").Single();
        await SetNoteAsync(scratch, "neu");
        SqliteConnection.ClearAllPools();

        var rows = await backups.RestoreAsync(chosen, "v2026.11.12", default);

        Assert.Equal("alt", await NoteAsync(scratch.DatabaseFile));
        Assert.Contains(rows, r => r.Table == "app_settings" && r.Rows >= 1);
        Assert.True(File.Exists(chosen), "the chosen backup is never touched");
        // The safety copy holds the state before the restore
        var safety = Directory.GetFiles(backups.RecentDir, "platinvault-*.db").Where(f => f != chosen).Single();
        Assert.Equal("neu", await NoteAsync(safety));
        Assert.Empty(Directory.GetFiles(backups.RestoreDir, "restore-source-*"));
    }

    [Fact]
    public async Task Restore_refuses_unusable_backups_and_changes_nothing()
    {
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        await SetNoteAsync(scratch, "bleibt");
        SqliteConnection.ClearAllPools();
        var backups = scratch.Backups();
        var original = await File.ReadAllBytesAsync(scratch.DatabaseFile);

        var junk = Path.Combine(scratch.Root, "junk.db");
        await File.WriteAllTextAsync(junk, "not a database");
        await Assert.ThrowsAsync<DatabaseRestoreException>(() => backups.RestoreAsync(junk, "v1", default));

        var foreign = Path.Combine(scratch.Root, "foreign.db");
        await using (var c = new SqliteConnection(SqliteDatabase.ConnectionString(foreign, pooling: false)))
        {
            await c.OpenAsync();
            await SqliteDatabase.ExecuteAsync(c, "CREATE TABLE notes (id INTEGER PRIMARY KEY)", default);
        }
        var notOurs = await Assert.ThrowsAsync<DatabaseRestoreException>(() => backups.RestoreAsync(foreign, "v1", default));
        Assert.Contains("keine Sicherung von PlatinVault", notOurs.Text.De);

        var newer = Path.Combine(scratch.Root, "newer.db");
        await DatabaseBackupService.SnapshotAsync(scratch.DatabaseFile, newer, default);
        await using (var c = new SqliteConnection(SqliteDatabase.ConnectionString(newer, pooling: false)))
        {
            await c.OpenAsync();
            await SqliteDatabase.ExecuteAsync(c, "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20991231000000_FromTheFuture', '10.0.0')", default);
        }
        await Assert.ThrowsAsync<DatabaseRestoreException>(() => backups.RestoreAsync(newer, "v1", default));

        await Assert.ThrowsAsync<DatabaseRestoreException>(() => backups.RestoreAsync(Path.Combine(scratch.Root, "missing.db"), "v1", default));

        SqliteConnection.ClearAllPools();
        Assert.Equal(original, await File.ReadAllBytesAsync(scratch.DatabaseFile));
        Assert.Equal("bleibt", await NoteAsync(scratch.DatabaseFile));
    }

    [Fact]
    public async Task Restore_stops_while_another_process_has_the_database_open()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Only Windows refuses to open a file that another handle uses.");
        using var scratch = new SqliteScratch();
        (await scratch.MigratedAsync()).Dispose();
        var backups = scratch.Backups();
        await backups.RunAsync("Test", default, force: true);
        var chosen = Directory.GetFiles(backups.RecentDir, "platinvault-*.db").Single();

        await using var running = new SqliteConnection(SqliteDatabase.ConnectionString(scratch.DatabaseFile, pooling: false));
        await running.OpenAsync();
        var error = await Assert.ThrowsAsync<DatabaseRestoreException>(() => backups.RestoreAsync(chosen, "v1", default));
        Assert.Contains("wird gerade benutzt", error.Text.De);
    }
}

public sealed class BackupRetentionTests
{
    // Built with Path.Combine on purpose: a hard-coded Windows path with backslashes is one single file name on Linux (the backslash is
    // no separator there), so the retention rules saw no parseable name at all and the tests failed for a reason unrelated to production.
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "pv-retention-test");

    private static string Recent(string name) => Path.Combine(Dir, "recent", name);

    private static string Daily(string name) => Path.Combine(Dir, "daily", name);

    [Fact]
    public void Recent_keeps_the_newest_twenty()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0);
        var recent = Enumerable.Range(0, 25).Select(i => Recent($"platinvault-{now.AddHours(-i):yyyyMMdd-HHmmss}.db")).ToList();
        var deleted = DatabaseBackupService.Retention.ToDelete(recent, [], now).ToList();
        Assert.Equal(5, deleted.Count);
        Assert.All(deleted, path => Assert.Contains(path, recent.Skip(20)));
    }

    [Fact]
    public void Daily_keeps_sixty_days()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0);
        var daily = Enumerable.Range(0, 70).Select(i => Daily($"platinvault-{now.AddDays(-i):yyyyMMdd}.db")).ToList();
        var deleted = DatabaseBackupService.Retention.ToDelete([], daily, now).ToList();
        Assert.Equal(9, deleted.Count);
        Assert.DoesNotContain(Daily($"platinvault-{now.AddDays(-60):yyyyMMdd}.db"), deleted);
    }

    [Fact]
    public void Foreign_files_are_never_touched()
    {
        var now = new DateTime(2026, 11, 1);
        var deleted = DatabaseBackupService.Retention.ToDelete(
            [Recent("manual-copy.db"), Recent("platinvault-garbage.db"), Recent("platinvault-20200101-000000.dump")],
            [Daily("notes.db")], now);
        Assert.Empty(deleted);
    }
}
