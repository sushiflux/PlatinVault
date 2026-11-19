using System.Text.Json;
using Companion.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Settings;

/// <summary>
/// The set of completed games the trophy cabinet has already presented (animated into their slot), as the API shows it: Steam games by AppID,
/// games added by hand (no AppID) by game id. A missing game list means "none yet" (so a new platinum of a game added by hand still animates),
/// Initialized only says whether the cabinet was ever opened (the AppIDs row exists).
/// </summary>
public sealed record CabinetPresentedDto(bool Initialized, List<int> AppIds, List<Guid>? GameIds = null);

public sealed record CabinetPresentedRequest(List<int>? AppIds, bool? Baseline, List<Guid>? GameIds = null);

/// <summary>
/// Which games of the trophy cabinet have already been presented. Stored once in <c>app_settings</c> (single user, so the
/// Windows window and the browser agree). A missing row means "never opened the cabinet": the client then records the whole
/// current collection as the baseline WITHOUT animating it, so a fresh install does not play the animation for old games.
/// Writes only ever add ids (union), so two windows cannot take each other's entries away.
/// </summary>
public static class CabinetPresented
{
    public const string Key = "cabinet.presented";

    /// <summary>The games added by hand that were presented (a JSON array of game ids); a separate row, so the Steam row keeps its old shape.</summary>
    public const string GamesKey = "cabinet.presented_games";
    // Far above any real library; guards the row and the request size
    public const int MaxIds = 5000;

    /// <summary>Reads the stored JSON array; anything unreadable counts as "no state" (null), not as an empty set.</summary>
    public static HashSet<int>? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            var ids = JsonSerializer.Deserialize<List<int>>(stored);
            return ids is null ? null : ids.Where(id => id > 0).ToHashSet();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Adds the submitted ids (positive, distinct, capped) to what is stored; returns the new set.</summary>
    public static HashSet<int> Merge(HashSet<int>? existing, IEnumerable<int>? submitted)
    {
        var result = existing is null ? [] : new HashSet<int>(existing);
        if (submitted is not null)
        {
            foreach (var id in submitted.Where(id => id > 0).Take(MaxIds))
            {
                if (result.Count >= MaxIds) break;
                result.Add(id);
            }
        }
        return result;
    }

    public static string Serialize(IEnumerable<int> ids) => JsonSerializer.Serialize(ids.Order().ToList());

    /// <summary>Reads the stored game ids; anything unreadable or missing is the empty set (nothing presented yet).</summary>
    public static HashSet<Guid> ParseGames(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];
        try
        {
            var ids = JsonSerializer.Deserialize<List<Guid>>(stored);
            return ids is null ? [] : ids.Where(id => id != Guid.Empty).ToHashSet();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Adds the submitted game ids (not empty, distinct, capped) to what is stored; returns the new set.</summary>
    public static HashSet<Guid> MergeGames(HashSet<Guid>? existing, IEnumerable<Guid>? submitted)
    {
        var result = existing is null ? [] : new HashSet<Guid>(existing);
        if (submitted is not null)
        {
            foreach (var id in submitted.Where(id => id != Guid.Empty).Take(MaxIds))
            {
                if (result.Count >= MaxIds) break;
                result.Add(id);
            }
        }
        return result;
    }

    public static string SerializeGames(IEnumerable<Guid> ids) => JsonSerializer.Serialize(ids.Order().ToList());

    public static async Task<CabinetPresentedDto> GetAsync(CompanionDbContext db, CancellationToken ct)
    {
        var rows = await db.AppSettings.AsNoTracking().Where(x => x.Key == Key || x.Key == GamesKey).ToListAsync(ct);
        var set = Parse(rows.FirstOrDefault(r => r.Key == Key)?.Value);
        var games = ParseGames(rows.FirstOrDefault(r => r.Key == GamesKey)?.Value);
        return new CabinetPresentedDto(set is not null, set?.Order().ToList() ?? [], games.Order().ToList());
    }

    /// <summary>
    /// Adds the ids to the stored set (creating it, i.e. marking the state as initialised) and returns the result. <paramref name="gameIds"/> = games
    /// added by hand; they are stored in their own row, only when some are submitted.
    /// </summary>
    public static async Task<CabinetPresentedDto> AddAsync(CompanionDbContext db, IEnumerable<int>? ids, CancellationToken ct, IEnumerable<Guid>? gameIds = null)
    {
        var gameList = gameIds?.ToList();
        if (gameList is { Count: > 0 })
        {
            await AddGamesAsync(db, gameList, ct);
        }

        for (var attempt = 0; ; attempt++)
        {
            var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == Key, ct);
            var merged = Merge(Parse(row?.Value), ids);
            var json = Serialize(merged);
            if (row is null)
            {
                db.AppSettings.Add(new AppSettingEntity { Key = Key, Value = json, UpdatedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                row.Value = json;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            try
            {
                await db.SaveChangesAsync(ct);
                var games = await db.AppSettings.AsNoTracking().Where(x => x.Key == GamesKey).Select(x => x.Value).FirstOrDefaultAsync(ct);
                return new CabinetPresentedDto(true, merged.Order().ToList(), ParseGames(games).Order().ToList());
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Another window created the row at the same moment: read it again and add to it
                db.ChangeTracker.Clear();
            }
        }
    }

    // Union into the games row, same retry rule as the Steam row (two windows, or two servers on one file, never take each other's entries away)
    private static async Task AddGamesAsync(CompanionDbContext db, List<Guid> submitted, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == GamesKey, ct);
            var json = SerializeGames(MergeGames(ParseGames(row?.Value), submitted));
            if (row is null)
            {
                db.AppSettings.Add(new AppSettingEntity { Key = GamesKey, Value = json, UpdatedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                row.Value = json;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
