using Companion.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Services;

/// <summary>
/// Applies <see cref="GuideLinker.Reconcile"/> to a game: updates the chapters' sections and creates/removes the tick marks. Called after every change of
/// the chapters of a game (new guide, completing, researching again, pasting answers, importing, removing a chapter). Idempotent.
/// </summary>
internal static class GuideLinkMaintenance
{
    /// <summary>
    /// Works on the tracked entities of <paramref name="game"/> (its <c>Tabs</c> and <c>ProgressChecks</c> must be loaded) and does NOT save - the caller's
    /// SaveChanges writes it together with its own change. Returns true when anything changed. Call it BEFORE deleting the tick marks of removed item ids:
    /// a row whose link target is gone takes over the target's tick from <c>ProgressChecks</c>.
    /// </summary>
    public static bool Apply(CompanionDbContext db, GameEntity game)
    {
        var checks = game.ProgressChecks.Select(p => p.ItemKey).ToHashSet(StringComparer.Ordinal);
        var result = GuideLinker.Reconcile(game.Tabs.ToList(), checks);
        if (result.IsEmpty)
        {
            return false;
        }

        foreach (var tab in game.Tabs)
        {
            if (result.ChangedSections.TryGetValue(tab.Id, out var json))
            {
                tab.SectionsJson = json;
            }
        }

        foreach (var key in result.AddChecks.Where(k => !checks.Contains(k)))
        {
            var check = new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = key, CheckedAt = DateTimeOffset.UtcNow };
            db.ProgressChecks.Add(check);
            if (!game.ProgressChecks.Contains(check))
            {
                game.ProgressChecks.Add(check);
            }
        }

        var obsolete = game.ProgressChecks.Where(p => result.RemoveChecks.Contains(p.ItemKey)).ToList();
        if (obsolete.Count > 0)
        {
            db.ProgressChecks.RemoveRange(obsolete);
            foreach (var check in obsolete)
            {
                game.ProgressChecks.Remove(check);
            }
        }
        return true;
    }

    /// <summary>
    /// SaveChanges for a change that went through <see cref="Apply"/> (or moved ticks otherwise). Tick marks are shared with the player's clicks and with a
    /// second server on the same database: a tick the linker wants to add may have been added in the meantime (unique index GameId + ItemKey), one it wants to
    /// remove may be gone already. Such a conflict is no failure - the wanted state is already there: the conflicting tick changes are dropped, the rest
    /// is saved again (one retry), and ticks to remove are then removed by key (idempotent). Only when even that fails, the tick changes are given up with
    /// a log line and the chapters are saved without them - a job whose chapters are done never fails because of a tick.
    /// </summary>
    public static async Task SaveAsync(CompanionDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return;
        }
        catch (DbUpdateException) when (TickEntries(db).Count > 0)
        {
            // fall through: settle the tick changes and try once more
        }

        var logger = Logger(db);
        logger.LogInformation("Häkchen wurden gleichzeitig geändert (zweites Fenster/zweiter Server) – die Verknüpfungen werden ohne die doppelten Änderungen gespeichert");
        var pendingDeletes = await SettleTickEntriesAsync(db, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (TickEntries(db).Count > 0)
        {
            logger.LogWarning(ex, "Häkchen-Änderungen der Verknüpfungen konnten nicht gespeichert werden und werden übersprungen; die Kapitel werden gespeichert");
            foreach (var entry in TickEntries(db))
            {
                entry.State = EntityState.Detached;
            }
            pendingDeletes.Clear();
            await db.SaveChangesAsync(ct);
        }
        foreach (var (gameId, key) in pendingDeletes)
        {
            await db.ProgressChecks.Where(p => p.GameId == gameId && p.ItemKey == key).ExecuteDeleteAsync(ct);
        }
    }

    private static List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ProgressCheckEntity>> TickEntries(CompanionDbContext db) =>
        db.ChangeTracker.Entries<ProgressCheckEntity>().Where(e => e.State is EntityState.Added or EntityState.Deleted).ToList();

    /// <summary>
    /// Compares the pending tick changes with what is stored NOW: an added tick that exists already and a removed tick are detached; the removals are
    /// returned to be done by key after the save (the stored row may be another one with the same key).
    /// </summary>
    private static async Task<List<(Guid GameId, string Key)>> SettleTickEntriesAsync(CompanionDbContext db, CancellationToken ct)
    {
        var deletes = new List<(Guid, string)>();
        foreach (var entry in TickEntries(db))
        {
            var tick = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                var stored = await db.ProgressChecks.AsNoTracking().AnyAsync(p => p.GameId == tick.GameId && p.ItemKey == tick.ItemKey, ct);
                if (stored)
                {
                    entry.State = EntityState.Detached;
                }
            }
            else
            {
                entry.State = EntityState.Detached;
                deletes.Add((tick.GameId, tick.ItemKey));
            }
        }
        return deletes;
    }

    private static ILogger Logger(CompanionDbContext db)
    {
        try
        {
            return db.GetService<ILoggerFactory>().CreateLogger("Companion.Api.GuideLinks");
        }
        catch (InvalidOperationException)
        {
            return NullLogger.Instance;
        }
    }

    /// <summary>For chapters that are not stored yet (an import): applies the link rules to the sections of the given tabs. There are no tick marks yet, so nothing else changes.</summary>
    public static bool ApplyToTabs(IReadOnlyCollection<GuideTabEntity> tabs)
    {
        var result = GuideLinker.Reconcile(tabs, new HashSet<string>(StringComparer.Ordinal));
        foreach (var tab in tabs.Where(t => result.ChangedSections.ContainsKey(t.Id)))
        {
            tab.SectionsJson = result.ChangedSections[tab.Id];
        }
        return result.ChangedSections.Count > 0;
    }

    /// <summary>
    /// For the flows that saved their chapters through other contexts (the research jobs): loads the game fresh, applies the links and saves. The context's
    /// tracked entities are cleared first so the answer built afterwards reads what is stored.
    /// </summary>
    public static async Task<bool> RelinkAsync(CompanionDbContext db, Guid gameId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null || !Apply(db, game))
        {
            db.ChangeTracker.Clear();
            return false;
        }
        try
        {
            await SaveAsync(db, ct);
        }
        catch (DbUpdateException ex)
        {
            // The chapters of the job are saved already; links are made again by the next change of the guide
            Logger(db).LogWarning(ex, "Die Verknüpfungen zwischen den Kapiteln konnten nicht gespeichert werden; die Kapitel selbst sind gespeichert");
            db.ChangeTracker.Clear();
            return false;
        }
        db.ChangeTracker.Clear();
        return true;
    }
}
