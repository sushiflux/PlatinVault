using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Data;

/// <summary>
/// A named lease in the database for work that must not run in two server processes at once on the same file (development: the browser
/// stack's backend and the Windows program share <c>.data/dev/platinvault.db</c>). Replaces PostgreSQL's advisory lock.
/// <para>
/// One row per name in <c>app_leases</c> (owner, expiry in Unix milliseconds). Taking it is ONE upsert statement, and SQLite runs write
/// statements one after another for the whole file - so two processes can never both win: the row is inserted, or taken over only when it
/// has expired or already belongs to the caller. Releasing deletes the row (only the owner's). If a process dies while holding it, the
/// lease simply runs out after its duration; nothing waits for it - <see cref="TryAcquireAsync"/> never blocks.
/// </para>
/// </summary>
public sealed class DatabaseLease : IAsyncDisposable
{
    /// <summary>Recording of player counts and playtime (<see cref="Services.HistoryRecorderWorker"/>).</summary>
    public const string HistoryRecorder = "history-recorder";

    /// <summary>The owner name of this process: two servers on one file always differ, even with the same process id on two machines.</summary>
    public static readonly string ProcessOwner = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid():N}";

    private readonly CompanionDbContext _db;
    private readonly string _name;
    private readonly string _owner;
    private bool _released;

    private DatabaseLease(CompanionDbContext db, string name, string owner)
    {
        _db = db;
        _name = name;
        _owner = owner;
    }

    /// <summary>The lease for <paramref name="duration"/>, or null when another owner holds an unexpired one (never waits).</summary>
    public static async Task<DatabaseLease?> TryAcquireAsync(CompanionDbContext db, string name, TimeSpan duration, CancellationToken ct,
        string? owner = null, TimeProvider? time = null)
    {
        owner ??= ProcessOwner;
        var now = (time ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();
        var expires = now + (long)duration.TotalMilliseconds;
        var changed = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO app_leases ("Name", "Owner", "ExpiresAt") VALUES ({0}, {1}, {2})
            ON CONFLICT ("Name") DO UPDATE SET "Owner" = excluded."Owner", "ExpiresAt" = excluded."ExpiresAt"
            WHERE app_leases."ExpiresAt" <= {3} OR app_leases."Owner" = excluded."Owner"
            """,
            [name, owner, expires, now],
            ct);
        return changed > 0 ? new DatabaseLease(db, name, owner) : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_released) return;
        _released = true;
        try
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM app_leases WHERE \"Name\" = {0} AND \"Owner\" = {1}", [_name, _owner]);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ObjectDisposedException)
        {
            // Not released: the lease runs out by itself after its duration
        }
    }
}
