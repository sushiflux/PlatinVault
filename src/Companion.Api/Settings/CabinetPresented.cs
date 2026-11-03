using System.Text.Json;
using Companion.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Settings;

/// <summary>The set of completed games the trophy cabinet has already presented (animated into their slot), as the API shows it.</summary>
public sealed record CabinetPresentedDto(bool Initialized, List<int> AppIds);

public sealed record CabinetPresentedRequest(List<int>? AppIds, bool? Baseline);

/// <summary>
/// Which games of the trophy cabinet have already been presented. Stored once in <c>app_settings</c> (single user, so the
/// Windows window and the browser agree). A missing row means "never opened the cabinet": the client then records the whole
/// current collection as the baseline WITHOUT animating it, so a fresh install does not play the animation for old games.
/// Writes only ever add ids (union), so two windows cannot take each other's entries away.
/// </summary>
public static class CabinetPresented
{
    public const string Key = "cabinet.presented";
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

    public static async Task<CabinetPresentedDto> GetAsync(CompanionDbContext db, CancellationToken ct)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == Key, ct);
        var set = Parse(row?.Value);
        return new CabinetPresentedDto(set is not null, set?.Order().ToList() ?? []);
    }

    /// <summary>Adds the ids to the stored set (creating it, i.e. marking the state as initialised) and returns the result.</summary>
    public static async Task<CabinetPresentedDto> AddAsync(CompanionDbContext db, IEnumerable<int>? ids, CancellationToken ct)
    {
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
                return new CabinetPresentedDto(true, merged.Order().ToList());
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Another window created the row at the same moment: read it again and add to it
                db.ChangeTracker.Clear();
            }
        }
    }
}
