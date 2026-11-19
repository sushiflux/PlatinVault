using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Companion.Api.Endpoints;

/// <summary>
/// Games added by hand (with or without IGDB), the IGDB search of the add dialog and the uploaded cover. Contract (routes, requests, answers):
/// <c>ManualGameDtos.cs</c>. Rules: a manual game is never "steam" and never gets a Steam AppID; the platform (and PlayStation variant) is fixed
/// after adding; IGDB is never required (without Twitch credentials the title + platform are typed, the cover is uploaded).
/// </summary>
public static class ManualGameEndpoints
{
    private const int MaxTitleLength = 240;

    public static void MapManualGames(this RouteGroupBuilder api)
    {
        // The add dialog's search: an IGDB number, an igdb.com/games/<slug> address or a name. 204 = no Twitch credentials, 502 = IGDB cannot be reached
        api.MapGet("/igdb/search", async (string? q, IgdbService igdb, CompanionDbContext db, CancellationToken ct) =>
        {
            var query = IgdbService.ParseSearch(q);
            if (query is null)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.ManualGames.SearchInvalidTitle, ServerTexts.ManualGames.SearchInvalidDetail(IgdbService.MaxSearchLength));
            }
            if (!igdb.IsConfigured)
            {
                return Results.NoContent();
            }

            IgdbSearchResponseDto? found;
            try
            {
                found = await igdb.SearchAsync(query, ct);
            }
            catch (TwitchApiException ex)
            {
                return Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Twitch.IgdbUnavailableTitle, ex.Message);
            }
            if (found is null)
            {
                return Results.NoContent();
            }

            // "Already added": the vault's games with this IGDB game, on whichever platform (the same game may be added once per platform)
            var ids = found.Results.Select(h => h.IgdbId).ToList();
            var existing = (await db.Games.AsNoTracking()
                    .Where(g => g.IgdbId != null && ids.Contains(g.IgdbId.Value))
                    .Select(g => new { IgdbId = g.IgdbId!.Value, g.Slug, g.Platform, g.PlatformVariant })
                    .ToListAsync(ct))
                .ToLookup(g => g.IgdbId, g => new ManualGameRefDto(g.Slug, g.Platform, g.PlatformVariant));
            return Results.Ok(new IgdbSearchResponseDto(found.Results.Select(h => h with { Existing = [.. existing[h.IgdbId]] }).ToList()));
        });

        // All facts of one IGDB game (cached for a week): 204 = no credentials, 404 = IGDB has no such game, 502 = IGDB cannot be reached
        api.MapGet("/igdb/games/{igdbId:int}", async (int igdbId, IgdbService igdb, CancellationToken ct) =>
        {
            if (igdbId <= 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.ManualGames.InvalidIgdbIdDetail);
            }
            if (!igdb.IsConfigured)
            {
                return Results.NoContent();
            }
            try
            {
                var facts = await igdb.GetByIgdbIdAsync(igdbId, ct);
                return facts is null
                    ? Problems.Create(StatusCodes.Status404NotFound, ServerTexts.ManualGames.IgdbGameNotFoundTitle, ServerTexts.ManualGames.IgdbGameNotFound(igdbId))
                    : Results.Ok(facts);
            }
            catch (TwitchApiException ex)
            {
                return Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Twitch.IgdbUnavailableTitle, ex.Message);
            }
        });

        api.MapPost("/games/manual", async (
            CreateManualGameRequest? req,
            CompanionDbContext db,
            IgdbService igdb,
            LibraryMetaService libraryMeta,
            ILogger<ManualGameLog> logger,
            CancellationToken ct) =>
        {
            if (req is null)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.ManualGames.TitleMissingDetail);
            }

            var platform = GamePlatforms.Validate(req.Platform, req.PlatformVariant);
            if (platform.Choice is not { } choice)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Platforms.InvalidTitle, platform.Message!.Value);
            }

            var title = req.Title?.Trim() ?? string.Empty;
            if (title.Length > MaxTitleLength)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.ManualGames.TitleTooLong(MaxTitleLength));
            }

            IgdbGameDto? facts = null;
            if (req.IgdbId is { } igdbId)
            {
                if (igdbId <= 0)
                {
                    return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.ManualGames.InvalidIgdbIdDetail);
                }
                if (!igdb.IsConfigured)
                {
                    return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.ManualGames.IgdbNeedsCredentialsTitle, ServerTexts.ManualGames.IgdbNeedsCredentialsDetail);
                }

                // Same IGDB game on the same platform (+ variant) twice is a mistake; another platform is a game of its own
                var dupe = await db.Games.AsNoTracking()
                    .Where(g => g.IgdbId == igdbId && g.Platform == choice.Platform && g.PlatformVariant == choice.Variant)
                    .Select(g => new { g.Slug, g.Title })
                    .FirstOrDefaultAsync(ct);
                if (dupe is not null)
                {
                    return AlreadyAdded(dupe.Title, choice, dupe.Slug);
                }

                try
                {
                    facts = await igdb.GetByIgdbIdAsync(igdbId, ct);
                }
                catch (TwitchApiException ex)
                {
                    return Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Twitch.IgdbUnavailableTitle, ex.Message);
                }
                if (facts is null)
                {
                    return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.ManualGames.IgdbGameNotFoundTitle, ServerTexts.ManualGames.IgdbGameNotFound(igdbId));
                }
                // A title typed by the player wins over IGDB's name
                if (title.Length == 0)
                {
                    title = facts.Name.Length > MaxTitleLength ? facts.Name[..MaxTitleLength] : facts.Name;
                }
            }
            else
            {
                if (title.Length == 0)
                {
                    return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.ManualGames.TitleMissingTitle, ServerTexts.ManualGames.TitleMissingDetail);
                }
                // The title is compared in C#, not with lower() in SQL: SQLite folds ASCII letters only, so "Ökoland" and "ÖKOLAND" would both pass.
                // Only the games of this platform (+ variant) are candidates - a short list.
                var candidates = await db.Games.AsNoTracking()
                    .Where(g => g.Platform == choice.Platform && g.PlatformVariant == choice.Variant)
                    .Select(g => new { g.Slug, g.Title })
                    .ToListAsync(ct);
                var dupe = candidates.FirstOrDefault(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase));
                if (dupe is not null)
                {
                    return AlreadyAdded(dupe.Title, choice, dupe.Slug);
                }
            }

            var language = Lang.Current;
            var defaults = AiGuideGeneratorService.DefaultsFor(language);
            var now = DateTimeOffset.UtcNow;
            var game = new GameEntity
            {
                Id = Guid.NewGuid(),
                Slug = await ApiEndpoints.EnsureUniqueSlugAsync(db, AiGuideGeneratorService.Slugify(title), ct),
                Title = title,
                Subtitle = defaults.Subtitle,
                SteamAppId = null,
                CoverUrl = string.Empty,
                HeroBannerUrl = string.Empty,
                AccentColor = "emerald",
                EstimatedHours = defaults.EstimatedHours,
                MissablesInfo = defaults.MissablesInfo,
                DifficultyInfo = defaults.DifficultyInfo,
                GenreTags = defaults.GenreTags,
                ContentLanguage = language,
                IsFavorite = true,
                SortOrder = await ApiEndpoints.NextGameSortOrderAsync(db, ct),
                Platform = choice.Platform,
                PlatformVariant = choice.Variant,
                CreatedAt = now,
                UpdatedAt = now,
            };
            if (facts is not null)
            {
                game.CoverUrl = facts.CoverUrl ?? string.Empty;
                game.HeroBannerUrl = ManualGameFacts.HeroUrl(facts);
                ManualGameFacts.Apply(game, facts, language, now);
            }

            // No starter tab: the page of a manual game starts with "research the trophy list" / "create the guide"
            db.Games.Add(game);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Spiel von Hand hinzugefügt: {Title} ({Platform}{Variant}, IGDB {IgdbId}).",
                game.Title, game.Platform, game.PlatformVariant is null ? "" : "/" + game.PlatformVariant, game.IgdbId);

            return Results.Created($"/api/games/{game.Slug}", SummaryOf(game, libraryMeta));
        });

        // Own cover (raw image body). Only for games added by hand: a Steam game gets its picture from Steam. Replaces an earlier upload.
        api.MapPut("/games/{slug}/cover", async (string slug, HttpContext http, CompanionDbContext db, CoverStore covers, LibraryMetaService libraryMeta, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (!GamePlatforms.IsManual(game))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.ManualGames.CoverTitle, ServerTexts.ManualGames.CoverOnlyManual);
            }

            var request = http.Request;
            var contentType = request.ContentType;
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = CoverStore.MaxBytes + 1024;
            }

            byte[] data;
            bool tooLarge;
            if (request.ContentLength > CoverStore.MaxBytes)
            {
                (data, tooLarge) = ([], true);
            }
            else
            {
                try
                {
                    (data, tooLarge) = await CoverStore.ReadBodyAsync(request.Body, ct);
                }
                catch (BadHttpRequestException)
                {
                    (data, tooLarge) = ([], true);
                }
            }

            var check = CoverStore.Check(contentType, data, tooLarge);
            if (!check.Ok)
            {
                return Problems.Create(check.Status, ServerTexts.ManualGames.CoverTitle, check.Error!.Value);
            }

            var now = DateTimeOffset.UtcNow;
            var saved = await covers.SaveAsync(game.Id, check.ContentType!, data, now, ct);
            var url = CoverStore.UrlFor(game.Id, saved.UpdatedAt);
            await db.Games.Where(g => g.Id == game.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.CoverUrl, url).SetProperty(g => g.UpdatedAt, now), ct);
            return Results.Ok(SummaryOf(await LoadAsync(db, game.Id, ct), libraryMeta));
        });

        // Removes the uploaded cover; the IGDB cover comes back when the game has an IGDB id (from the cache, else asked), otherwise the tile has none
        api.MapDelete("/games/{slug}/cover", async (string slug, CompanionDbContext db, CoverStore covers, IgdbService igdb, LibraryMetaService libraryMeta, ILogger<ManualGameLog> logger, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (!GamePlatforms.IsManual(game))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.ManualGames.CoverTitle, ServerTexts.ManualGames.CoverOnlyManual);
            }

            if (await covers.DeleteAsync(game.Id, ct) || CoverStore.IsLocalCoverUrl(game.CoverUrl))
            {
                var restored = string.Empty;
                if (game.IgdbId is > 0 && igdb.IsConfigured)
                {
                    try
                    {
                        restored = (await igdb.GetByIgdbIdAsync(game.IgdbId.Value, ct))?.CoverUrl ?? string.Empty;
                    }
                    catch (TwitchApiException ex)
                    {
                        logger.LogDebug("IGDB-Cover für {Title} nicht abrufbar: {Message}", game.Title, ex.Message);
                    }
                }
                await db.Games.Where(g => g.Id == game.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.CoverUrl, restored).SetProperty(g => g.UpdatedAt, DateTimeOffset.UtcNow), ct);
            }
            return Results.Ok(SummaryOf(await LoadAsync(db, game.Id, ct), libraryMeta));
        });

        // The uploaded image. The address carries ?v=<ticks of the upload>, so the browser may cache it for good
        api.MapGet("/covers/{gameId:guid}", async (Guid gameId, HttpContext http, CoverStore covers, CancellationToken ct) =>
        {
            var cover = await covers.FindAsync(gameId, ct);
            if (cover is null || !CoverStore.ContentTypes.Contains(cover.ContentType))
            {
                return Results.NotFound();
            }
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
            return Results.File(cover.Data, cover.ContentType, entityTag: new EntityTagHeaderValue($"\"{cover.Sha256}\""));
        });
    }

    private static IResult AlreadyAdded(string title, GamePlatformChoice choice, string slug) =>
        Results.Problem(
            title: ServerTexts.ManualGames.AlreadyAddedTitle,
            detail: ServerTexts.ManualGames.AlreadyAdded(title, GamePlatforms.DisplayName(choice.Platform, choice.Variant), slug),
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["existingSlug"] = slug });

    private static Task<GameEntity> LoadAsync(CompanionDbContext db, Guid id, CancellationToken ct) =>
        db.Games.AsNoTracking().Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstAsync(g => g.Id == id, ct);

    private static GameSummaryDto SummaryOf(GameEntity game, LibraryMetaService libraryMeta) =>
        ApiEndpoints.WithLibraryMeta(ApiEndpoints.MapToSummaryDto(game), libraryMeta.Snapshot());
}

/// <summary>Category for the log lines of the games added by hand.</summary>
public sealed class ManualGameLog;
