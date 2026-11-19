using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>What saving a researched list did: whether facts were written, ticks carried over, ticked old trophies kept.</summary>
public sealed record TrophyListSaveResult(bool FactsUpdated, int KeptTicks, int RetainedOldTicked, int Count);

/// <summary>
/// Saves a checked trophy list (and the facts of a game without IGDB) - ONE place for the AI job and the answer of the own subscription. In one transaction:
/// the old rows are read inside it, merged by <see cref="TrophyMerge"/> (ticks are kept; a continued row is updated in place, so its tick time is never
/// written here), the research time and sources are set, the facts are written only when they do not come from IGDB (D10), and the progress is recounted
/// (<see cref="TrophyProgress.RecomputeAsync"/>, which never ticks anything).
/// </summary>
public static class TrophyListStore
{
    public static async Task<TrophyListSaveResult> SaveAsync(CompanionDbContext db, Guid gameId, TrophyListRead read, TrophyResearchInclude include, DateTimeOffset now, CancellationToken ct)
    {
        if (!read.Ok)
        {
            throw new InvalidOperationException("Only a checked list without errors is saved.");
        }
        now = now.ToUniversalTime();
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(gameId.ToString("N")));
        var kept = 0;
        var retained = 0;
        var count = 0;

        if (include.Trophies && read.Trophies.Count > 0)
        {
            var old = await db.GameTrophies.Where(t => t.GameId == gameId).OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey).ToListAsync(ct);
            var plan = TrophyMerge.Plan(old, read.Trophies);
            kept = plan.KeptTicks;
            retained = plan.RetainedOldTicked;

            db.GameTrophies.RemoveRange(plan.Removed);
            var order = 0;
            foreach (var entry in plan.Entries)
            {
                var row = entry.Existing;
                if (row is null)
                {
                    row = new GameTrophyEntity { Id = Guid.NewGuid(), GameId = gameId, TrophyKey = entry.Key, CreatedAt = now };
                    db.GameTrophies.Add(row);
                }
                Apply(row, entry.Item, order++, now);
            }
            // Ticked trophies the new list does not have stay, after the new list (a tick is never lost by a re-research)
            foreach (var row in plan.Retained)
            {
                row.SortOrder = order++;
                row.UpdatedAt = now;
            }
            count = order;

            game.TrophyListResearchedAt = now;
            game.TrophyListSourcesJson = ManualTrophyLists.WriteSources(read.Sources);
        }

        var factsUpdated = false;
        if (include.Facts && read.Facts is { } facts && game.FactsSource != "igdb")
        {
            ApplyFacts(game, facts, AiGuideGeneratorService.GuideLanguage(game), now);
            factsUpdated = true;
        }
        game.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await TrophyProgress.RecomputeAsync(db, gameId, now, ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
        return new TrophyListSaveResult(factsUpdated, kept, retained, count);
    }

    /// <summary>The researched texts and values on a row; the tick (<see cref="GameTrophyEntity.UnlockedAt"/>) is never touched.</summary>
    private static void Apply(GameTrophyEntity row, TrophyListItem item, int order, DateTimeOffset now)
    {
        row.SortOrder = order;
        row.Name = item.Name;
        row.OriginalName = item.OriginalName;
        row.Description = item.Description;
        row.Grade = item.Grade;
        row.Points = item.Points;
        row.RarityPercent = item.RarityPercent;
        row.IsHidden = item.Hidden;
        row.IsMissable = item.Missable;
        row.GroupName = item.Group;
        row.Category = item.Category;
        row.Tip = item.Tip;
        row.UpdatedAt = now;
    }

    /// <summary>The facts of the answer on the game (only called when they do not come from IGDB); a value the answer lacks keeps the stored one.</summary>
    internal static void ApplyFacts(GameEntity game, TrophyListFacts facts, string language, DateTimeOffset now)
    {
        game.FactsSource = "ai";
        if (facts.FirstReleaseAt is { } release)
        {
            game.FirstReleaseAt = release;
            // A full date reads like the IGDB one ("12. Okt. 2023"); only a year stays the year
            game.ReleaseDate = facts.ReleaseDate is { Length: 4 } year ? year : ManualGameFacts.FormatReleaseDate(release, language);
        }
        if (facts.Developers.Count > 0) game.Developer = Clip(string.Join(", ", facts.Developers), 240);
        if (facts.Publishers.Count > 0) game.Publisher = Clip(string.Join(", ", facts.Publishers), 240);
        if (facts.Genres.Count > 0)
        {
            game.SteamGenres = facts.Genres;
            // The prompt asks for the genres in English (like IGDB's), so the genre profiles and the card read them the same way
            game.SteamGenresLanguage = Lang.English;
        }
        if (facts.Summary is not null) game.Summary = facts.Summary;
        if (facts.HoursComplete is { } hours) game.CompletionHours = hours;
        game.FactsUpdatedAt = now;
    }

    private static string Clip(string text, int max) => text.Length > max ? text[..max].TrimEnd() : text;
}
