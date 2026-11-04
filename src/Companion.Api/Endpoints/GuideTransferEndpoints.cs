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

            var document = new GuideExportDocument(
                Format: GuideImport.FormatName,
                FormatVersion: GuideImport.SupportedFormatVersion,
                ExportedAt: DateTimeOffset.UtcNow,
                ExportedBy: $"PlatinVault {version.Current}",
                Game: new GuideExportGame(
                    Title: game.Title,
                    Subtitle: game.Subtitle,
                    SteamAppId: game.SteamAppId,
                    CoverUrl: game.CoverUrl,
                    HeroBannerUrl: game.HeroBannerUrl,
                    AccentColor: game.AccentColor,
                    EstimatedHours: game.EstimatedHours,
                    MissablesInfo: game.MissablesInfo,
                    DifficultyInfo: game.DifficultyInfo,
                    GenreTags: game.GenreTags,
                    ContentLanguage: AiGuideGeneratorService.GuideLanguage(game)),
                Tabs: WalkthroughTab.InGuideOrder(game.Tabs)
                    .Select(t => new GuideExportTab(t.Slug, t.Title, t.Icon, t.SortOrder, ApiEndpoints.DeserializeSections(t.SectionsJson), t.GroupKey, t.TopicId))
                    .ToList());

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

            if (tabs.Count == 0)
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
            await db.SaveChangesAsync(ct);

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
