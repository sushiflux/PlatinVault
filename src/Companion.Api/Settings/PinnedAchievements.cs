using System.Text.Json;
using Companion.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Settings;

/// <summary>The outcome of a pin or unpin call.</summary>
/// <param name="ApiNames">The pinned achievements afterwards, in display order.</param>
/// <param name="Changed">False when nothing had to be written (already pinned / not pinned).</param>
/// <param name="LimitReached">True when the achievement could not be pinned because <see cref="SettingsStore.MaxPinnedAchievements"/> are pinned already.</param>
public readonly record struct PinChange(IReadOnlyList<string> ApiNames, bool Changed, bool LimitReached = false);

/// <summary>The pin storage could not be written because other writers kept changing the same row.</summary>
public sealed class PinConflictException(Exception? inner = null) : Exception("The pinned achievements are being changed concurrently.", inner);

/// <summary>
/// Pinned achievements ("Im Blick"): per game an ordered list of API names in the <c>app_settings</c> row <c>pins.{appId}</c>
/// (JSON array, first = shown first, at most <see cref="SettingsStore.MaxPinnedAchievements"/>). An unreadable value counts as an empty list.
/// Two server processes can share one SQLite file (browser stack + Windows program), so every change is a compare-and-swap: the row is
/// read, the new list is computed, and it is written only if the row still holds the value that was read (one <c>UPDATE ... WHERE
/// Value = old</c> / <c>DELETE</c>, or an INSERT that the primary key refuses when another writer was first); on a lost race the whole
/// step is repeated. A parallel change can therefore never be overwritten.
/// </summary>
public static class PinnedAchievements
{
    public const int MaxNameLength = 200;
    private const int MaxAttempts = 12;

    public static string KeyFor(uint appId) => $"pins.{appId}";

    /// <summary>Parses a stored value: distinct (case-insensitive), non-blank names in stored order, at most the limit. Anything unreadable gives an empty list.</summary>
    public static List<string> Parse(string? json)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var names = JsonSerializer.Deserialize<List<string?>>(json);
            if (names is null) return result;
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength) continue;
                if (result.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                result.Add(name);
                if (result.Count == SettingsStore.MaxPinnedAchievements) break;
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }
        return result;
    }

    public static string Serialize(IReadOnlyList<string> names) => JsonSerializer.Serialize(names);

    /// <summary>The pinned API names of a game, in display order.</summary>
    public static async Task<IReadOnlyList<string>> GetAsync(CompanionDbContext db, uint appId, CancellationToken ct = default)
    {
        var key = KeyFor(appId);
        var stored = await db.AppSettings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync(ct);
        return Parse(stored);
    }

    /// <summary>Appends an achievement at the end. Pinning an already pinned one changes nothing; with the maximum reached it is refused.</summary>
    public static Task<PinChange> AddAsync(CompanionDbContext db, uint appId, string apiName, CancellationToken ct = default)
    {
        var name = apiName.Trim();
        return MutateAsync(db, appId, current =>
        {
            if (current.Contains(name, StringComparer.OrdinalIgnoreCase)) return (null, false);
            if (current.Count >= SettingsStore.MaxPinnedAchievements) return (null, true);
            return ([.. current, name], false);
        }, ct);
    }

    /// <summary>Removes the given achievements; names that are not pinned are ignored.</summary>
    public static Task<PinChange> RemoveAsync(CompanionDbContext db, uint appId, IEnumerable<string> apiNames, CancellationToken ct = default)
    {
        var names = apiNames.Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        return MutateAsync(db, appId, current =>
        {
            var next = current.Where(c => !names.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            return (next.Count == current.Count ? null : next, false);
        }, ct);
    }

    private static async Task<PinChange> MutateAsync(
        CompanionDbContext db,
        uint appId,
        Func<List<string>, (List<string>? Next, bool LimitReached)> mutate,
        CancellationToken ct)
    {
        var key = KeyFor(appId);
        Exception? lastConflict = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var oldValue = await db.AppSettings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync(ct);
            var current = Parse(oldValue);
            var (next, limitReached) = mutate(current);
            if (next is null)
            {
                return new PinChange(current, Changed: false, LimitReached: limitReached);
            }

            var now = DateTimeOffset.UtcNow;
            bool written;
            if (oldValue is null)
            {
                // No row yet: the primary key makes a parallel first insert fail, and that attempt then starts over with the new row
                try
                {
                    db.AppSettings.Add(new AppSettingEntity { Key = key, Value = Serialize(next), UpdatedAt = now });
                    await db.SaveChangesAsync(ct);
                    written = true;
                }
                catch (DbUpdateException ex)
                {
                    db.ChangeTracker.Clear();
                    lastConflict = ex;
                    written = false;
                }
            }
            else if (next.Count == 0)
            {
                written = await db.AppSettings.Where(x => x.Key == key && x.Value == oldValue).ExecuteDeleteAsync(ct) == 1;
            }
            else
            {
                var serialized = Serialize(next);
                written = await db.AppSettings.Where(x => x.Key == key && x.Value == oldValue)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Value, serialized).SetProperty(x => x.UpdatedAt, now), ct) == 1;
            }

            if (written)
            {
                return new PinChange(next, Changed: true);
            }

            // Lost the race against another writer: read again and redo the change on top of theirs (a short pause avoids lock-step retries)
            await Task.Delay(Random.Shared.Next(2, 25), ct);
        }
        throw new PinConflictException(lastConflict);
    }
}
