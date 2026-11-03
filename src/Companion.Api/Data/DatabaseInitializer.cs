using System.Globalization;
using Companion.Api.Configuration;
using Companion.Api.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Data;

/// <summary>
/// Brings the database schema up to date on startup via EF Core migrations (the SQLite migrations start fresh with
/// <c>InitialCreate</c> of 2026-11-02: the switch from PostgreSQL happened before any customer data existed).
/// <para>
/// Start guard (user decision 2026-11-02, betas on every platform): a database that a NEWER PlatinVault has already migrated (it
/// contains applied migrations this build does not know) is never used - a beta can be newer than the next release, and an older
/// build would run on a schema it does not understand. The server then stops with a bilingual explanation and changes nothing.
/// </para>
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Migration ids that earlier builds applied and that were later removed or renamed in code ON PURPOSE. They are not a sign of a
    /// newer PlatinVault and must not lock anybody out. Empty: the SQLite migrations began on 2026-11-02 with InitialCreate (the former
    /// PostgreSQL migrations never ran on a SQLite file). When a migration is ever deleted or renamed, add its old id here in the same change.
    /// </summary>
    internal static readonly IReadOnlySet<string> RetiredMigrations = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Readable version of the PlatinVault that last brought the schema up to date (shown when an older one refuses to start).</summary>
    public const string SchemaVersionKey = "app.schema_version";

    public static async Task MigrateAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var running = services.GetService<AppVersion>()?.Current ?? "?";

        // Before anything is written: refuse a database of a newer PlatinVault (throws, changes nothing)
        await EnsureNotNewerAsync(db, running, logger, ct);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Wende {Count} Datenbank-Migration(en) an: {Migrations}", pending.Count, string.Join(", ", pending));
        }
        await db.Database.MigrateAsync(ct);
        await RecordSchemaVersionAsync(db, running, pending.Count > 0, logger, ct);
    }

    /// <summary>The applied migrations this build does not know (neither in code nor retired on purpose), oldest first.</summary>
    public static IReadOnlyList<string> UnknownMigrations(IEnumerable<string> applied, IEnumerable<string> known, IReadOnlySet<string> retired)
    {
        var knownSet = new HashSet<string>(known, StringComparer.Ordinal);
        return applied.Where(id => !knownSet.Contains(id) && !retired.Contains(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The migrations this build knows (no database is opened).</summary>
    public static IReadOnlyList<string> KnownMigrations()
    {
        using var db = new CompanionDbContext(new DbContextOptionsBuilder<CompanionDbContext>().UseSqlite("Data Source=:memory:").Options);
        return db.Database.GetMigrations().ToList();
    }

    /// <summary>
    /// The check of the start guard on any database file - also on a backup that is about to be restored: null when this build can
    /// use it, else the bilingual explanation and the unknown migrations. Reads only.
    /// </summary>
    public static async Task<(Msg Text, IReadOnlyList<string> Unknown)?> CheckNewerAsync(SqliteConnection connection, string running, CancellationToken ct)
    {
        if (!await TableExistsAsync(connection, "__EFMigrationsHistory", ct)) return null;
        var applied = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) applied.Add(reader.GetString(0));
        }
        var unknown = UnknownMigrations(applied, KnownMigrations(), RetiredMigrations);
        if (unknown.Count == 0) return null;

        string? needed = null;
        if (await TableExistsAsync(connection, "app_settings", ct))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"Value\" FROM app_settings WHERE \"Key\" = $key";
            command.Parameters.AddWithValue("$key", SchemaVersionKey);
            var value = await command.ExecuteScalarAsync(ct) as string;
            needed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        return (ServerTexts.DatabaseGuard.Newer(needed, running, MigrationDate(unknown[^1]), string.Join(", ", unknown)), unknown);
    }

    /// <summary>Reads only (history table and the recorded schema version); throws <see cref="NewerDatabaseException"/> when the database is newer.</summary>
    private static async Task EnsureNotNewerAsync(CompanionDbContext db, string running, ILogger logger, CancellationToken ct)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            if (await CheckNewerAsync(connection, running, ct) is not { } newer) return;
            // Operators read the container log in either language: both are written
            logger.LogCritical("{German}", newer.Text.De);
            logger.LogCritical("{English}", newer.Text.En);
            throw new NewerDatabaseException(newer.Text, newer.Unknown);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    /// <summary>EF migration ids start with yyyyMMddHHmmss; the date part names "from when" the unknown change is.</summary>
    private static string MigrationDate(string migrationId) =>
        migrationId.Length >= 8 && DateTime.TryParseExact(migrationId[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : migrationId;

    internal static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Remembers which PlatinVault brought the schema up to date - when it applied migrations, or once for a database that has no
    /// record yet (this build knows its schema exactly then). Best effort: a failure here never stops the start.
    /// </summary>
    private static async Task RecordSchemaVersionAsync(CompanionDbContext db, string running, bool migrated, ILogger logger, CancellationToken ct)
    {
        try
        {
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SchemaVersionKey, ct);
            if (row is null)
            {
                db.AppSettings.Add(new AppSettingEntity { Key = SchemaVersionKey, Value = running, UpdatedAt = DateTimeOffset.UtcNow });
            }
            else if (migrated && row.Value != running)
            {
                row.Value = running;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                return;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A second server on the same database (shared development mode) wrote it at the same moment
            db.ChangeTracker.Clear();
            logger.LogDebug(ex, "Schema-Version nicht gespeichert (gleichzeitiger Start).");
        }
    }
}

/// <summary>
/// The database was already migrated by a newer PlatinVault (see <see cref="DatabaseInitializer"/>). Nothing was changed; the server
/// must not start. <see cref="DatabaseRefusedException.Text"/> explains it in both languages (the Windows program shows it in its own dialog).
/// </summary>
public sealed class NewerDatabaseException(Msg text, IReadOnlyList<string> unknownMigrations) : DatabaseRefusedException(text)
{
    public IReadOnlyList<string> UnknownMigrations { get; } = unknownMigrations;
}
