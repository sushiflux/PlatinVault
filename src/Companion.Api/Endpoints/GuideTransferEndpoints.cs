using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// Guide export / import: a game plus its guide tabs as one shareable JSON file ("platinvault-guide", format version 1).
/// Progress, notes and settings are never part of it.
/// </summary>
public static class GuideTransferEndpoints
{
    private const long MaxImportBytes = 5 * 1024 * 1024;
    private const int MaxSlugLength = 150;
    private const int MaxTabSlugLength = 120;
    private const int MaxItemKeyLength = 200;

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The file is read and edited by people, never embedded in HTML: keep umlauts and quotes readable instead of XXXX escapes
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapGuideTransfer(this RouteGroupBuilder api)
    {
        api.MapGet("/games/{slug}/export", async (string slug, CompanionDbContext db, AppVersion version, CancellationToken ct) =>
        {
            var game = await db.Games
                .AsNoTracking()
                .Include(g => g.Tabs)
                .FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GuideNotFound(slug));
            }

            var manual = GamePlatforms.IsManual(game);
            var trophies = manual
                ? await db.GameTrophies.AsNoTracking().Where(t => t.GameId == game.Id).OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey).ToListAsync(ct)
                : [];

            var trophySources = manual && trophies.Count > 0 ? ManualTrophyLists.ReadSources(game.TrophyListSourcesJson) : [];

            var document = new GuideExportDocument(
                Format: GuideImport.FormatName,
                FormatVersion: GuideImport.SupportedFormatVersion,
                ExportedAt: DateTimeOffset.UtcNow,
                ExportedBy: $"PlatinVault {version.Current}",
                Game: new GuideExportGame(
                    Title: game.Title,
                    Subtitle: game.Subtitle,
                    SteamAppId: game.SteamAppId,
                    // An uploaded cover lives in this installation's database, its address means nothing elsewhere: left out
                    CoverUrl: CoverStore.IsLocalCoverUrl(game.CoverUrl) ? string.Empty : game.CoverUrl,
                    HeroBannerUrl: game.HeroBannerUrl,
                    AccentColor: game.AccentColor,
                    EstimatedHours: game.EstimatedHours,
                    MissablesInfo: game.MissablesInfo,
                    DifficultyInfo: game.DifficultyInfo,
                    GenreTags: game.GenreTags,
                    ContentLanguage: AiGuideGeneratorService.GuideLanguage(game),
                    // The fields of a game added by hand stay null for a Steam game: its file looks exactly as before
                    Platform: manual ? game.Platform : null,
                    PlatformVariant: manual ? game.PlatformVariant : null,
                    IgdbId: manual ? game.IgdbId : null,
                    FactsSource: manual ? game.FactsSource : null,
                    Developer: manual ? game.Developer : null,
                    Publisher: manual ? game.Publisher : null,
                    Summary: manual ? game.Summary : null,
                    FirstReleaseAt: manual ? game.FirstReleaseAt : null,
                    ReleaseDate: manual ? game.ReleaseDate : null,
                    TrophyListResearchedAt: manual ? game.TrophyListResearchedAt : null),
                Tabs: WalkthroughTab.InGuideOrder(game.Tabs)
                    .Select(t => new GuideExportTab(t.Slug, t.Title, t.Icon, t.SortOrder, ApiEndpoints.DeserializeSections(t.SectionsJson), t.GroupKey, t.TopicId))
                    .ToList(),
                Trophies: trophies.Count == 0 ? null : trophies.Select(t => new GuideExportTrophy(
                    t.TrophyKey, t.Name, t.OriginalName, t.Description, GamePlatforms.HasPlatinumGrade(game.Platform) ? TrophyTiers.NormalizeGrade(t.Grade) : null,
                    t.Points, t.RarityPercent, t.IsHidden ? true : null, t.IsMissable ? true : null, t.GroupName,
                    t.Category.Length == 0 ? null : t.Category, t.Tip.Length == 0 ? null : t.Tip)).ToList(),
                TrophySources: trophySources.Count > 0 ? trophySources : null);

            return Results.File(
                JsonSerializer.SerializeToUtf8Bytes(document, ExportOptions),
                "application/json",
                $"{game.Slug}.platinvault.json");
        });

        api.MapPost("/games/import", async (
            HttpContext http,
            CompanionDbContext db,
            SteamClient steamClient,
            ILogger<GuideImportLogCategory> logger,
            CancellationToken ct) =>
        {
            if (http.Request.ContentLength == 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Import.NoDataTitle, ServerTexts.Import.NoDataDetail);
            }
            if (http.Request.ContentLength > MaxImportBytes)
            {
                return TooLarge();
            }
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = MaxImportBytes;
            }

            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(http.Request.Body, new JsonDocumentOptions { MaxDepth = 32 }, ct);
            }
            catch (JsonException ex)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Import.InvalidFileTitle, ServerTexts.Import.NotJson(ex.LineNumber, ex.BytePositionInLine));
            }
            catch (BadHttpRequestException)
            {
                return TooLarge();
            }

            ParsedGuide parsed;
            using (document)
            {
                try
                {
                    parsed = GuideImport.Parse(document.RootElement);
                }
                catch (GuideImportException ex)
                {
                    return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Import.NotPossibleTitle, ex.Message);
                }
            }

            if (parsed.SteamAppId is { } appId)
            {
                var existing = await db.Games.AsNoTracking()
                    .Where(g => g.SteamAppId == appId)
                    .Select(g => g.Title)
                    .FirstOrDefaultAsync(ct);
                if (existing is not null)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ServerTexts.Import.AlreadyExistsTitle,
                        ServerTexts.Import.AlreadyExistsDetail(existing, appId));
                }
            }

            // A game of another platform than Steam: the same IGDB game (or, without one, the same title) on the same platform is a duplicate
            if (parsed.Platform is { } importPlatform)
            {
                var importVariant = parsed.PlatformVariant;
                var sameGame = db.Games.AsNoTracking().Where(g => g.Platform == importPlatform && g.PlatformVariant == importVariant);
                // The title is compared in C#, not with lower() in SQL: SQLite folds ASCII letters only ("Ökoland" vs "ÖKOLAND")
                var dupe = parsed.IgdbId is { } importIgdbId
                    ? await sameGame.Where(g => g.IgdbId == importIgdbId).Select(g => new { g.Slug, g.Title }).FirstOrDefaultAsync(ct)
                    : (await sameGame.Select(g => new { g.Slug, g.Title }).ToListAsync(ct))
                        .FirstOrDefault(g => string.Equals(g.Title, parsed.Title, StringComparison.OrdinalIgnoreCase));
                if (dupe is not null)
                {
                    return Results.Problem(
                        title: ServerTexts.ManualGames.AlreadyAddedTitle,
                        detail: ServerTexts.ManualGames.AlreadyAdded(dupe.Title, GamePlatforms.DisplayName(importPlatform, parsed.PlatformVariant), dupe.Slug),
                        statusCode: StatusCodes.Status409Conflict,
                        extensions: new Dictionary<string, object?> { ["existingSlug"] = dupe.Slug });
                }
            }

            var coverUrl = parsed.CoverUrl;
            var heroUrl = parsed.HeroBannerUrl;
            if (parsed.SteamAppId is { } steamAppId && (string.IsNullOrEmpty(coverUrl) || string.IsNullOrEmpty(heroUrl)))
            {
                var resolved = await steamClient.ResolveSingleAppAssetsAsync(steamAppId, ct);
                coverUrl = string.IsNullOrEmpty(coverUrl) ? resolved.CoverUrl : coverUrl;
                heroUrl = string.IsNullOrEmpty(heroUrl) ? resolved.HeroUrl : heroUrl;
            }

            var baseSlug = AiGuideGeneratorService.Slugify(parsed.Title);
            baseSlug = baseSlug.Length > MaxSlugLength ? baseSlug[..MaxSlugLength].Trim('-') : baseSlug;
            var gameId = Guid.NewGuid();
            var game = new GameEntity
            {
                Id = gameId,
                Slug = await ApiEndpoints.EnsureUniqueSlugAsync(db, baseSlug, ct),
                Title = parsed.Title,
                Subtitle = parsed.Subtitle,
                SteamAppId = parsed.SteamAppId,
                CoverUrl = coverUrl,
                HeroBannerUrl = heroUrl,
                AccentColor = parsed.AccentColor,
                EstimatedHours = parsed.EstimatedHours,
                MissablesInfo = parsed.MissablesInfo,
                DifficultyInfo = parsed.DifficultyInfo,
                GenreTags = parsed.GenreTags,
                ContentLanguage = parsed.ContentLanguage,
                SortOrder = (await db.Games.MaxAsync(g => (int?)g.SortOrder, ct) ?? 0) + 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            if (parsed.Platform is { } gamePlatform)
            {
                // A game added by hand: platform, facts and (below) its trophy list, but never a Steam AppID (Parse refused one)
                game.Platform = gamePlatform;
                game.PlatformVariant = parsed.PlatformVariant;
                game.IgdbId = parsed.IgdbId;
                game.FactsSource = parsed.FactsSource;
                game.Developer = parsed.Developer;
                game.Publisher = parsed.Publisher;
                game.Summary = parsed.Summary;
                game.FirstReleaseAt = parsed.FirstReleaseAt;
                game.ReleaseDate = parsed.ReleaseDate;
                game.IsFavorite = true;
                if (parsed.Trophies.Count > 0)
                {
                    game.TrophyListResearchedAt = parsed.TrophyListResearchedAt ?? game.CreatedAt;
                    game.TrophyListSourcesJson = ManualTrophyLists.WriteSources(parsed.TrophySources);
                }
            }
            var trophyRows = parsed.Trophies.Select((t, i) => new GameTrophyEntity
            {
                Id = Guid.NewGuid(),
                GameId = gameId,
                TrophyKey = t.Key,
                SortOrder = i,
                Name = t.Name,
                OriginalName = t.OriginalName,
                Description = t.Description,
                Grade = t.Grade,
                Points = t.Points,
                RarityPercent = t.RarityPercent,
                IsHidden = t.Hidden,
                IsMissable = t.Missable,
                GroupName = t.Group,
                Category = t.Category,
                Tip = t.Tip,
                // No tick: a file holds no progress
                UnlockedAt = null,
                CreatedAt = game.CreatedAt,
                UpdatedAt = game.CreatedAt,
            }).ToList();

            // Regular tabs are numbered 1..n in file order; the optional lore tab keeps its slug and always sorts last
            var tabNumber = 0;
            var tabs = parsed.Tabs
                .Select(tab =>
                {
                    var number = tab.IsLore ? 0 : ++tabNumber;
                    return new GuideTabEntity
                    {
                        Id = Guid.NewGuid(),
                        GameId = gameId,
                        Slug = tab.IsLore ? LoreTab.Slug : tab.IsWalkthrough ? WalkthroughTab.Slug : TabSlug(number, tab.Title),
                        Title = tab.Title,
                        Icon = tab.Icon,
                        SortOrder = tab.IsLore ? LoreTab.SortOrder : number,
                        SectionsJson = tab.SectionsJson,
                        GroupKey = tab.IsWalkthrough ? tab.GroupKey ?? GuideGroups.Start : tab.GroupKey,
                        TopicId = tab.IsWalkthrough ? WalkthroughTab.TopicId : tab.TopicId
                    };
                })
                .ToList();

            // A game added by hand starts without a starter tab (its page offers "research the trophy list" / "create the guide")
            if (tabs.Count == 0 && parsed.Platform is null)
            {
                tabs.Add(ApiEndpoints.CreateStarterTab(game));
            }

            // Progress is stored per item id and game, so an id may occur only once per game
            ApiEndpoints.MakeItemIdsUnique(tabs, []);
            // Links in the file: refs that point at nothing are dropped, walkthrough rows / side quests get linked by name (a new game has no ticks to merge)
            GuideLinkMaintenance.ApplyToTabs(tabs);
            var overlong = ApiEndpoints.CollectTakenItemIds(tabs).FirstOrDefault(id => id.Length > MaxItemKeyLength);
            if (overlong is not null)
            {
                return Problems.Create(
                    StatusCodes.Status400BadRequest,
                    ServerTexts.Import.NotPossibleTitle,
                    ServerTexts.Import.ItemIdTooLong(MaxItemKeyLength, overlong[..40]));
            }

            db.Games.Add(game);
            db.GuideTabs.AddRange(tabs);
            db.GameTrophies.AddRange(trophyRows);
            await db.SaveChangesAsync(ct);

            if (trophyRows.Count > 0)
            {
                // Counts (and the platinum state) are written by the one place that owns them
                var state = await TrophyProgress.RecomputeAsync(db, game.Id, DateTimeOffset.UtcNow, ct);
                game.TrophiesTotal = state?.Total ?? 0;
                game.TrophiesUnlocked = state?.Unlocked ?? 0;
                game.PlatinumAt = state?.PlatinumAt;
            }

            game.Tabs = tabs;
            logger.LogInformation("Guide importiert: {Title} ({Tabs} Tabs).", game.Title, tabs.Count);
            return Results.Ok(ApiEndpoints.MapToSummaryDto(game));
        });
    }

    private static string TabSlug(int number, string title)
    {
        var slug = AiGuideGeneratorService.Slugify(title);
        var full = slug.Length == 0 ? $"tab-{number}" : $"tab-{number}-{slug}";
        return full.Length > MaxTabSlugLength ? full[..MaxTabSlugLength].TrimEnd('-') : full;
    }

    private static IResult TooLarge() =>
        Problems.Create(StatusCodes.Status413PayloadTooLarge, ServerTexts.Import.FileTooLargeTitle, ServerTexts.Import.FileTooLargeDetail(MaxImportBytes / (1024 * 1024)));
}

/// <summary>Logger category for the guide import (static classes cannot be used as ILogger type arguments).</summary>
public sealed class GuideImportLogCategory;
