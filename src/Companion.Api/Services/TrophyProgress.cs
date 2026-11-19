using Companion.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>The trophy state of a manual game after <see cref="TrophyProgress.RecomputeAsync"/>.</summary>
/// <param name="PlatinumReached">True only for the ONE call that set <see cref="GameEntity.PlatinumAt"/> (announce the platinum moment).</param>
/// <param name="PlatinumLost">True when this call cleared <see cref="GameEntity.PlatinumAt"/> (silent).</param>
public sealed record TrophyProgressState(
    int Total,
    int Unlocked,
    DateTimeOffset? PlatinumAt,
    bool PlatinumReached,
    bool PlatinumLost);

public enum TrophyTickOutcome
{
    GameNotFound,
    TrophyNotFound,
    /// <summary>The trophy already had the requested state (another request or another server was first): nothing to announce.</summary>
    Unchanged,
    /// <summary>This request changed the tick.</summary>
    Changed,
}

/// <summary>What one tick / un-tick did (<see cref="TrophyProgress.SetUnlockedAsync"/>).</summary>
/// <param name="AutoTickedPlatinumKey">D3: the platinum trophy this request ticked because every other base-game trophy is ticked now.</param>
/// <param name="AutoUntickedPlatinumKey">D3: the platinum trophy this request un-ticked silently because a base-game trophy was un-ticked.</param>
/// <param name="Progress">The state after the change (null for GameNotFound / TrophyNotFound).</param>
public sealed record TrophyTickResult(
    TrophyTickOutcome Outcome,
    string TrophyKey,
    bool Unlocked,
    bool IsPlatinumTrophy,
    string? AutoTickedPlatinumKey,
    string? AutoUntickedPlatinumKey,
    TrophyProgressState? Progress)
{
    public bool Changed => Outcome == TrophyTickOutcome.Changed;

    /// <summary>
    /// Show the normal popup for the ticked trophy: only when THIS request ticked it, and never for the platinum trophy itself (that one is
    /// announced as the platinum moment only). Un-ticking is always silent.
    /// </summary>
    public bool AnnounceTrophy => Changed && Unlocked && !IsPlatinumTrophy;

    /// <summary>Show the platinum moment: this request is the one that reached platinum.</summary>
    public bool AnnouncePlatinum => Progress?.PlatinumReached == true;
}

/// <summary>
/// Ticks and progress of the trophy lists of manual games (<c>game_trophies</c>). The only writer of <see cref="GameTrophyEntity.UnlockedAt"/>
/// and of the trophy columns of a manual game (<see cref="GameEntity.TrophiesUnlocked"/>, <see cref="GameEntity.TrophiesTotal"/>,
/// <see cref="GameEntity.TrophiesUpdatedAt"/>, <see cref="GameEntity.PlatinumAt"/>).
/// <para>
/// Two servers may work on one SQLite file (browser stack + Windows program). Every change is therefore a conditional UPDATE inside one write
/// transaction (Microsoft.Data.Sqlite starts it with BEGIN IMMEDIATE, so it waits for the other writer instead of reading a stale snapshot):
/// <c>SET UnlockedAt = now WHERE ... AND UnlockedAt IS NULL</c> changes one row for exactly one request, and only that request announces the
/// popup; the same holds for <c>PlatinumAt</c> and the platinum moment ("first wins").
/// </para>
/// <para>
/// Platinum (coordinator decision D3): on PlayStation, when the list has a platinum trophy, platinum = that trophy is ticked. It ticks itself as
/// soon as every other base-game trophy (<see cref="GameTrophyEntity.GroupName"/> null) is ticked, and un-ticking any base-game trophy un-ticks it
/// silently. Everywhere else (and on a PlayStation list without a platinum trophy, as small games have) platinum = 100 % of all trophies.
/// </para>
/// </summary>
public static class TrophyProgress
{
    /// <summary>
    /// Sets or clears the tick of one trophy and keeps platinum and the counts of the game in step. Joins a transaction the caller already
    /// started, else runs in its own. Pass <paramref name="now"/> as UTC.
    /// </summary>
    public static async Task<TrophyTickResult> SetUnlockedAsync(
        CompanionDbContext db, Guid gameId, string trophyKey, bool unlocked, DateTimeOffset now, CancellationToken ct)
    {
        now = now.ToUniversalTime();
        await using var ownTransaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        var platform = await db.Games.AsNoTracking().Where(g => g.Id == gameId).Select(g => g.Platform).FirstOrDefaultAsync(ct);
        if (platform is null)
        {
            return new(TrophyTickOutcome.GameNotFound, trophyKey, unlocked, false, null, null, null);
        }
        var trophy = await db.GameTrophies.AsNoTracking()
            .Where(t => t.GameId == gameId && t.TrophyKey == trophyKey)
            .Select(t => new { t.Grade, t.GroupName })
            .FirstOrDefaultAsync(ct);
        if (trophy is null)
        {
            return new(TrophyTickOutcome.TrophyNotFound, trophyKey, unlocked, false, null, null, null);
        }

        var gradedPlatinum = GamePlatforms.HasPlatinumGrade(platform);
        var isPlatinumTrophy = gradedPlatinum && TrophyTiers.NormalizeGrade(trophy.Grade) == TrophyTiers.Platinum;

        var rows = unlocked
            ? await db.GameTrophies
                .Where(t => t.GameId == gameId && t.TrophyKey == trophyKey && t.UnlockedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UnlockedAt, now).SetProperty(t => t.UpdatedAt, now), ct)
            : await db.GameTrophies
                .Where(t => t.GameId == gameId && t.TrophyKey == trophyKey && t.UnlockedAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UnlockedAt, (DateTimeOffset?)null).SetProperty(t => t.UpdatedAt, now), ct);

        string? autoTicked = null;
        string? autoUnticked = null;
        // Only a change of a base-game trophy moves the platinum trophy; a DLC trophy never does
        if (rows == 1 && gradedPlatinum && !isPlatinumTrophy && trophy.GroupName is null)
        {
            if (unlocked)
            {
                autoTicked = await TickPlatinumWhenBaseCompleteAsync(db, gameId, now, ct);
            }
            else
            {
                autoUnticked = await UntickPlatinumAsync(db, gameId, now, ct);
            }
        }

        var progress = await RecomputeCoreAsync(db, gameId, platform, now, ct);
        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(ct);
        }
        return new(rows == 1 ? TrophyTickOutcome.Changed : TrophyTickOutcome.Unchanged, trophyKey, unlocked, isPlatinumTrophy, autoTicked, autoUnticked, progress);
    }

    /// <summary>
    /// Recounts the trophies of a game from <c>game_trophies</c> and sets or clears <see cref="GameEntity.PlatinumAt"/> (conditional, see the class).
    /// Call it after saving a (re-)researched list. Joins a transaction the caller already started, else runs in its own. Returns null when the
    /// game does not exist. Ticks are not changed here (the D3 auto-tick belongs to a tick, see <see cref="SetUnlockedAsync"/>).
    /// </summary>
    public static async Task<TrophyProgressState?> RecomputeAsync(CompanionDbContext db, Guid gameId, DateTimeOffset now, CancellationToken ct)
    {
        now = now.ToUniversalTime();
        await using var ownTransaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var platform = await db.Games.AsNoTracking().Where(g => g.Id == gameId).Select(g => g.Platform).FirstOrDefaultAsync(ct);
        if (platform is null)
        {
            return null;
        }
        var progress = await RecomputeCoreAsync(db, gameId, platform, now, ct);
        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(ct);
        }
        return progress;
    }

    /// <summary>The platinum rule on loaded trophies (same rule as the SQL of <see cref="RecomputeAsync"/>; for read-only callers).</summary>
    public static bool IsPlatinumReached(string? platform, IReadOnlyCollection<GameTrophyEntity> trophies)
    {
        if (trophies.Count == 0) return false;
        if (GamePlatforms.HasPlatinumGrade(platform))
        {
            var platinum = trophies.Where(t => TrophyTiers.NormalizeGrade(t.Grade) == TrophyTiers.Platinum).ToList();
            if (platinum.Count > 0) return platinum.Any(t => t.UnlockedAt is not null);
        }
        return trophies.All(t => t.UnlockedAt is not null);
    }

    // D3: ticks the (first) unticked platinum trophy when no other base-game trophy is left unticked. Returns its key, null when nothing changed.
    private static async Task<string?> TickPlatinumWhenBaseCompleteAsync(CompanionDbContext db, Guid gameId, DateTimeOffset now, CancellationToken ct)
    {
        var baseOpen = await db.GameTrophies.AnyAsync(t =>
            t.GameId == gameId && t.GroupName == null && t.UnlockedAt == null && (t.Grade == null || t.Grade.ToLower() != TrophyTiers.Platinum), ct);
        if (baseOpen) return null;

        var key = await db.GameTrophies
            .Where(t => t.GameId == gameId && t.UnlockedAt == null && t.Grade != null && t.Grade.ToLower() == TrophyTiers.Platinum)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey)
            .Select(t => t.TrophyKey)
            .FirstOrDefaultAsync(ct);
        if (key is null) return null;

        var rows = await db.GameTrophies
            .Where(t => t.GameId == gameId && t.TrophyKey == key && t.UnlockedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UnlockedAt, now).SetProperty(t => t.UpdatedAt, now), ct);
        return rows == 1 ? key : null;
    }

    // D3: a base-game trophy was un-ticked, so every platinum trophy goes back to unticked (silent). Returns the first key, null when none was ticked.
    private static async Task<string?> UntickPlatinumAsync(CompanionDbContext db, Guid gameId, DateTimeOffset now, CancellationToken ct)
    {
        var key = await db.GameTrophies
            .Where(t => t.GameId == gameId && t.UnlockedAt != null && t.Grade != null && t.Grade.ToLower() == TrophyTiers.Platinum)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey)
            .Select(t => t.TrophyKey)
            .FirstOrDefaultAsync(ct);
        if (key is null) return null;

        await db.GameTrophies
            .Where(t => t.GameId == gameId && t.UnlockedAt != null && t.Grade != null && t.Grade.ToLower() == TrophyTiers.Platinum)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UnlockedAt, (DateTimeOffset?)null).SetProperty(t => t.UpdatedAt, now), ct);
        return key;
    }

    private static async Task<TrophyProgressState> RecomputeCoreAsync(CompanionDbContext db, Guid gameId, string platform, DateTimeOffset now, CancellationToken ct)
    {
        if (GamePlatforms.IsSteam(platform))
        {
            // A Steam game's counts and platinum date come from Steam (SteamLiveTrackerService): never overwrite them from an empty table
            var steam = await db.Games.AsNoTracking().Where(g => g.Id == gameId)
                .Select(g => new { g.TrophiesTotal, g.TrophiesUnlocked, g.PlatinumAt }).FirstAsync(ct);
            return new TrophyProgressState(steam.TrophiesTotal, steam.TrophiesUnlocked, steam.PlatinumAt, false, false);
        }

        // Counts in one statement from the table itself, so a concurrent writer can never leave a count that does not match the rows
        await db.Games
            .Where(g => g.Id == gameId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.TrophiesTotal, g => db.GameTrophies.Count(t => t.GameId == g.Id))
                .SetProperty(g => g.TrophiesUnlocked, g => db.GameTrophies.Count(t => t.GameId == g.Id && t.UnlockedAt != null))
                .SetProperty(g => g.TrophiesUpdatedAt, now), ct);

        var counts = await db.GameTrophies.AsNoTracking()
            .Where(t => t.GameId == gameId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Unlocked = group.Count(t => t.UnlockedAt != null),
                Platinum = group.Count(t => t.Grade != null && t.Grade.ToLower() == TrophyTiers.Platinum),
                PlatinumTicked = group.Count(t => t.UnlockedAt != null && t.Grade != null && t.Grade.ToLower() == TrophyTiers.Platinum),
            })
            .FirstOrDefaultAsync(ct);
        var total = counts?.Total ?? 0;
        var unlockedCount = counts?.Unlocked ?? 0;
        var reached = GamePlatforms.HasPlatinumGrade(platform) && counts is { Platinum: > 0 }
            ? counts.PlatinumTicked > 0
            : total > 0 && unlockedCount >= total;

        var platinumReached = false;
        var platinumLost = false;
        if (reached)
        {
            // Exactly one request gets the row: that one announces the platinum moment
            platinumReached = await db.Games
                .Where(g => g.Id == gameId && g.PlatinumAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.PlatinumAt, now), ct) == 1;
        }
        else
        {
            platinumLost = await db.Games
                .Where(g => g.Id == gameId && g.PlatinumAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.PlatinumAt, (DateTimeOffset?)null), ct) == 1;
        }

        var platinumAt = await db.Games.AsNoTracking().Where(g => g.Id == gameId).Select(g => g.PlatinumAt).FirstOrDefaultAsync(ct);
        return new TrophyProgressState(total, unlockedCount, platinumAt, platinumReached, platinumLost);
    }
}
