using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Hubs;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>Maps all REST API endpoints for the PlatinVault Dashboard, Live Trophy Tracker, and 100% Guide viewer.</summary>
public static class ApiEndpoints
{
    // A full achievement list is a few hundred KB at most; anything bigger is not a bridge
    private const long MaxBridgeBodyBytes = 2 * 1024 * 1024;
    private const int MaxLibraryGames = 20_000;
    private const long MaxBridgeLibraryBodyBytes = 4 * 1024 * 1024;

    // Upper bound for PUT /api/games/order (a Steam library has a few hundred games at most)
    private const int MaxReorderEntries = 2000;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void MapCompanionApi(this WebApplication app)
    {
        // Realtime bridge ("PlatinVault Bridge", PlatinVault.Bridge.exe on the gaming PC). Deliberately outside the /api group: it is called by a
        // program, not by a browser, so the X-Requested-With CSRF header does not apply - the bearer token authenticates it.
        // Rate limited per IP (policy "bridge", registered in Program.cs).
        app.MapPost("/api/bridge/state", async (
            HttpContext http,
            BridgeTokenStore tokens,
            BridgeIngestService ingest,
            BridgeBundle bundle,
            SettingsStore settingsStore,
            ILogger<BridgeIngestService> logger,
            CancellationToken ct) =>
        {
            if (!await tokens.AuthenticateAsync(http.Request.Headers.Authorization.ToString(), ct))
            {
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return Problems.Create(StatusCodes.Status401Unauthorized, ServerTexts.Bridge.UnauthorizedTitle, ServerTexts.Bridge.UnauthorizedDetail);
            }

            // Body size is capped only now, so unauthenticated callers can never make the server buffer a large body
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = MaxBridgeBodyBytes;
            }

            BridgeStateRequest? req;
            try
            {
                req = await http.Request.ReadFromJsonAsync<BridgeStateRequest>(JsonOptions, ct);
            }
            catch (Exception ex) when (ex is JsonException or BadHttpRequestException or InvalidOperationException)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.StateNotJson);
            }

            if (req is null || req.RunningAppId is < 0 or > uint.MaxValue)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.RunningAppIdInvalid);
            }
            var rawAchievements = req.Achievements ?? [];
            if (rawAchievements.Count > BridgeIngestService.MaxAchievements)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.TooManyAchievements(BridgeIngestService.MaxAchievements));
            }
            if (rawAchievements.Any(a => string.IsNullOrWhiteSpace(a.ApiName) || a.ApiName.Length > BridgeIngestService.MaxNameLength))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.ApiNameMissing);
            }

            // The first SteamID the bridge reports is adopted while none is stored; once one is stored, a bridge that runs on
            // a different Steam account must not feed its unlocks into this account's lists.
            var reportedSteamId = req.SteamId64?.Trim();
            if (reportedSteamId is not null && !System.Text.RegularExpressions.Regex.IsMatch(reportedSteamId, @"^\d{17}$"))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.SteamId64Invalid);
            }
            if (reportedSteamId is not null)
            {
                var storedSteamId = await settingsStore.GetStoredSteamUserIdAsync(ct);
                if (string.IsNullOrWhiteSpace(storedSteamId))
                {
                    await settingsStore.SetSteamUserIdAsync(reportedSteamId, ct);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(storedSteamId, @"^\d{17}$") && storedSteamId != reportedSteamId)
                {
                    return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Bridge.OtherAccountTitle, ServerTexts.Bridge.OtherAccountDetail(reportedSteamId, storedSteamId));
                }
            }

            var achievements = rawAchievements
                .Select(a => new BridgeAchievement(a.ApiName.Trim(), a.Achieved, a.UnlockTime?.LocalDateTime))
                .ToList();
            var gameName = req.GameName?.Trim() ?? string.Empty;
            await ingest.IngestAsync((uint)req.RunningAppId, gameName.Length > 240 ? gameName[..240] : gameName, reportedSteamId, achievements, ct);
            await tokens.TouchThrottledAsync(ct);

            logger.LogDebug("Bridge-Status: App {AppId}, {Count} Achievements.", req.RunningAppId, achievements.Count);
            // The bridge compares these with its own version/hash and updates itself when they differ. uiLanguage is the stored
            // ui.language ("de"/"en"; null until the first-run choice was made): the bridge speaks the language of this server, so
            // it is read from the setting, not from the request (the bridge's own Accept-Language only chooses the error texts).
            var uiLanguage = await RequestLanguage.GetStoredAsync(http.RequestServices, ct);
            // The Linux bridge says so in its state; the Windows bridge (and every older one) says nothing and gets the Windows bundle
            var platform = BridgeBundle.ParsePlatform(req.Platform);
            return Results.Ok(new { ok = true, latestBridgeVersion = bundle.VersionOf(platform), bridgeSha256 = bundle.Sha256Of(platform), uiLanguage });
        }).RequireRateLimiting("bridge");

        // Self-update download for the bridge: same bearer token and rate limit as the state push, streams the bundled exe.
        app.MapGet("/api/bridge/exe", async (HttpContext http, BridgeTokenStore tokens, BridgeBundle bundle, CancellationToken ct) =>
        {
            if (!await tokens.AuthenticateAsync(http.Request.Headers.Authorization.ToString(), ct))
            {
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return Problems.Create(StatusCodes.Status401Unauthorized, ServerTexts.Bridge.UnauthorizedTitle, ServerTexts.Bridge.UnauthorizedDetail);
            }

            // ?platform=linux is the Linux bridge's self-update; without it the Windows exe (all existing bridges)
            var platform = BridgeBundle.ParsePlatform(http.Request.Query["platform"].ToString());
            if (!bundle.IsAvailableFor(platform))
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.Bridge.NoBridgeTitle, bundle.MissingHintFor(platform));
            }

            return Results.Stream(bundle.OpenExe(platform), "application/octet-stream", bundle.FileNameOf(platform), enableRangeProcessing: true);
        }).RequireRateLimiting("bridge");

        // Library facts of the gaming PC for the dashboard's sort orders (release date, Metacritic, reviews, size on disk), read by the bridge
        // from the Steam client's own files. Same bearer token and rate limit; the bridge sends it rarely (at the start and when it changes).
        app.MapPost("/api/bridge/library", async (HttpContext http, BridgeTokenStore tokens, LibraryMetaService libraryMeta, CancellationToken ct) =>
        {
            if (!await tokens.AuthenticateAsync(http.Request.Headers.Authorization.ToString(), ct))
            {
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return Problems.Create(StatusCodes.Status401Unauthorized, ServerTexts.Bridge.UnauthorizedTitle, ServerTexts.Bridge.UnauthorizedDetail);
            }

            // Capped only after the token was accepted (see /api/bridge/state); up to MaxLibraryGames entries of about 100 bytes
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = MaxBridgeLibraryBodyBytes;
            }

            BridgeLibraryRequest? req;
            try
            {
                req = await http.Request.ReadFromJsonAsync<BridgeLibraryRequest>(JsonOptions, ct);
            }
            catch (Exception ex) when (ex is JsonException or BadHttpRequestException or InvalidOperationException)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.LibraryNotJson);
            }

            var games = req?.Games;
            if (games is null)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.LibraryNotJson);
            }
            if (games.Count > MaxLibraryGames)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.TooManyLibraryGames(MaxLibraryGames));
            }

            // Values outside their range are dropped (unknown), not stored: a sort order must never be fed by garbage
            var entries = new Dictionary<int, LocalLibraryEntry>();
            foreach (var game in games)
            {
                if (game is null || game.AppId is <= 0 or > int.MaxValue) continue;
                entries[(int)game.AppId] = new LocalLibraryEntry(
                    game.ReleaseUnix is > 0 and < 4_102_444_800 ? game.ReleaseUnix : null,
                    game.Metacritic is >= 0 and <= 100 ? game.Metacritic : null,
                    game.ReviewPercent is >= 0 and <= 100 ? game.ReviewPercent : null,
                    game.ReviewScore is >= 1 and <= 9 ? game.ReviewScore : null,
                    game.SizeBytes is > 0 ? game.SizeBytes : null,
                    game.InstallSizeBytes is > 0 ? game.InstallSizeBytes : null);
            }
            await libraryMeta.IngestBridgeAsync(entries, ct);
            return Results.Ok(new { ok = true, games = entries.Count });
        }).RequireRateLimiting("bridge");

        // Command channel to the bridge ("start this game on the gaming PC"), same token and rate limit - see LaunchEndpoints
        app.MapBridgeCommands();

        // "Mit Steam anmelden": top-level browser navigations (no custom header possible), protected by a one-time
        // state value that lives in an HttpOnly cookie and inside Steam's signed return_to URL.
        app.MapGet("/api/steam/openid/start", (string? origin, HttpContext http, IWebHostEnvironment env) =>
        {
            var safeOrigin = ResolveTrustedOrigin(origin, http.Request, env);
            if (safeOrigin is null)
            {
                return Results.Redirect("/?steamLink=error&reason=origin");
            }

            var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            http.Response.Cookies.Append(SteamOpenIdService.StateCookieName, $"{state}|{safeOrigin}", new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = safeOrigin.StartsWith("https://", StringComparison.Ordinal),
                Path = "/api/steam/openid",
                MaxAge = TimeSpan.FromMinutes(10),
                IsEssential = true
            });
            return Results.Redirect(SteamOpenIdService.BuildLoginUrl(safeOrigin, state));
        });

        app.MapGet(SteamOpenIdService.CallbackPath, async (
            HttpContext http,
            SteamOpenIdService openId,
            SettingsStore settingsStore,
            ILogger<SteamOpenIdService> logger,
            CancellationToken ct) =>
        {
            var cookie = http.Request.Cookies[SteamOpenIdService.StateCookieName];
            http.Response.Cookies.Delete(SteamOpenIdService.StateCookieName, new CookieOptions { Path = "/api/steam/openid" });

            var parts = cookie?.Split('|', 2);
            if (parts is not { Length: 2 } || parts[0] != http.Request.Query["state"].ToString())
            {
                return Results.Redirect("/?steamLink=error&reason=state");
            }

            var (state, origin) = (parts[0], parts[1]);
            var (steamId, error) = await openId.VerifyAsync(http.Request.Query, SteamOpenIdService.BuildReturnTo(origin, state), ct);
            if (steamId is null)
            {
                logger.LogWarning("Steam-Anmeldung abgelehnt: {Error}", error);
                return Results.Redirect($"{origin}/?steamLink=error&reason={Uri.EscapeDataString(error ?? "unknown")}");
            }

            await settingsStore.SetSteamUserIdAsync(steamId, ct);
            logger.LogInformation("Steam-Konto per OpenID verknüpft: {SteamId}", steamId);
            return Results.Redirect($"{origin}/?steamLink=ok");
        });

        var api = app.MapGroup("/api");

        // Require custom header on state-changing browser requests to prevent cross-site form attacks
        api.AddEndpointFilter(async (context, next) =>
        {
            var method = context.HttpContext.Request.Method;
            if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method))
            {
                if (!context.HttpContext.Request.Headers.TryGetValue("X-Requested-With", out var val) ||
                    (!string.Equals(val.ToString(), "CompanionVault", StringComparison.Ordinal) &&
                     !string.Equals(val.ToString(), "PlatinVault", StringComparison.Ordinal)))
                {
                    return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.SecurityHeaderTitle, ServerTexts.SecurityHeaderDetail);
                }
            }
            return await next(context);
        });

        // Token for the realtime bridge. The plain token is returned only by POST (shown once); only its hash is stored.
        api.MapGet("/bridge/token", async (BridgeTokenStore tokens, SteamLiveTrackerService liveTracker, BridgeBundle bundle, CancellationToken ct) =>
        {
            var status = await tokens.GetStatusAsync(ct);
            // The live tracker knows the last push to the second; the stored value is only written every few seconds
            var lastSeen = liveTracker.GetLastBridgeEventAt() is { } live && (status.LastSeenAt is null || live > status.LastSeenAt)
                ? live
                : status.LastSeenAt;
            return Results.Ok(status with { LastSeenAt = status.HasToken ? lastSeen : null, BundledVersion = bundle.IsAvailable ? bundle.Version : null,
                LinuxBundledVersion = bundle.IsAvailableFor(BridgePlatform.Linux) ? bundle.VersionOf(BridgePlatform.Linux) : null });
        });

        // Ready-to-use bridge download: a ZIP with the exe, the package templates and a bridge.json that already carries the
        // server address and a NEW token (the previous token stops working, exactly like POST /bridge/token). The token is
        // part of the download only - it is never logged.
        api.MapPost("/bridge/package", async (
            HttpContext http,
            IWebHostEnvironment env,
            BridgeTokenStore tokens,
            BridgeBundle bundle,
            HandbookFiles handbooks,
            LicenseFiles licenses,
            SteamLiveTrackerService liveTracker,
            ILogger<BridgeTokenStore> logger,
            CancellationToken ct) =>
        {
            // The bridge for Windows (ZIP, default) or Linux (tar.gz): query parameter "os" or "os" in the JSON body
            var origin = http.Request.Query["origin"].ToString();
            var os = http.Request.Query["os"].ToString();
            if ((string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(os)) && http.Request.HasJsonContentType() && http.Request.ContentLength is > 0 and <= 4096)
            {
                try
                {
                    var body = await http.Request.ReadFromJsonAsync<BridgePackageRequest>(JsonOptions, ct);
                    if (string.IsNullOrWhiteSpace(origin)) origin = body?.Origin ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(os)) os = body?.Os ?? string.Empty;
                }
                catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
                {
                    return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.PackageBodyInvalid);
                }
            }
            if (!string.IsNullOrWhiteSpace(os) && !os.Trim().Equals("windows", StringComparison.OrdinalIgnoreCase) && !os.Trim().Equals("linux", StringComparison.OrdinalIgnoreCase))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Bridge.PackageOsInvalid(os.Length > 40 ? os[..40] : os));
            }
            var platform = BridgeBundle.ParsePlatform(os);

            // Everything that can be wrong is checked BEFORE the token is replaced, so a failed request never costs the user a working token
            if (!bundle.IsAvailableFor(platform))
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.Bridge.NoBridgeTitle, bundle.MissingHintFor(platform));
            }
            if (string.IsNullOrWhiteSpace(origin))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Bridge.ServerAddressMissingTitle, ServerTexts.Bridge.ServerAddressMissingDetail);
            }

            var safeOrigin = ResolveTrustedOrigin(origin, http.Request, env);
            if (safeOrigin is null)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Bridge.ServerAddressInvalidTitle, ServerTexts.Bridge.ServerAddressInvalidDetail(origin.Length > 80 ? origin[..80] : origin));
            }

            var created = await tokens.CreateAsync(ct);
            liveTracker.ClearBridgeState();
            logger.LogInformation("Bridge-Token erzeugt (Hinweis ...{Hint}), Bridge-Paket ({Platform}) wird ausgeliefert.", created.Hint, platform);

            // The package carries the language of the request (the UI language), so the very first start of the bridge already matches
            var language = Lang.Current;

            http.Response.Headers.CacheControl = "no-store";
            if (platform == BridgePlatform.Linux)
            {
                return Results.Stream(
                    stream => BridgePackageWriter.WriteLinuxAsync(stream, bundle, handbooks, licenses, safeOrigin, created.Token, language, ct),
                    "application/gzip",
                    BridgePackageWriter.LinuxPackageFileName);
            }
            return Results.Stream(
                stream => BridgePackageWriter.WriteAsync(stream, bundle, handbooks, licenses, safeOrigin, created.Token, language, ct),
                "application/zip",
                "PlatinVault-Bridge.zip");
        });

        api.MapPost("/bridge/token", async (BridgeTokenStore tokens, SteamLiveTrackerService liveTracker, ILogger<BridgeTokenStore> logger, CancellationToken ct) =>
        {
            var created = await tokens.CreateAsync(ct);
            liveTracker.ClearBridgeState();
            logger.LogInformation("Bridge-Token erzeugt (Hinweis ...{Hint}).", created.Hint);
            return Results.Ok(created);
        });

        api.MapDelete("/bridge/token", async (BridgeTokenStore tokens, SteamLiveTrackerService liveTracker, ILogger<BridgeTokenStore> logger, CancellationToken ct) =>
        {
            await tokens.RevokeAsync(ct);
            liveTracker.ClearBridgeState();
            logger.LogInformation("Bridge-Token gelöscht.");
            return Results.NoContent();
        });

        // Guide export / import (shareable JSON file)
        api.MapGuideTransfer();
        api.MapGuideTranslation();
        api.MapAi();
        api.MapAiActions();
        api.MapManualGuide();
        api.MapSystem();
        api.MapTwitchIgdb();
        api.MapDocs();
        api.MapStatistics();
        api.MapShowcase();
        api.MapLaunch();
        api.MapUpdates();
        api.MapDesktop();
        api.MapPins();

        // Live Steam Achievement & Trophy Tracker endpoints
        api.MapGet("/live/status", async (SteamLiveTrackerService liveTracker, CancellationToken ct) =>
        {
            return Results.Ok(await liveTracker.GetStatusAsync(ct));
        });

        // Name and artwork of the game that is running right now, vault game or not (the browser shows "you are playing X" for a game that
        // has no guide yet). 204 when nothing runs. Only ever the running game, never a caller-chosen AppID.
        api.MapGet("/live/running-game", async Task<IResult> (SteamLiveTrackerService liveTracker, CancellationToken ct) =>
        {
            var status = await liveTracker.GetStatusAsync(ct);
            if (status.RunningAppId == 0)
            {
                return Results.NoContent();
            }
            return Results.Ok(await liveTracker.GetGameInfoAsync(status.RunningAppId, status.SteamId64, status.RunningGameSlug, ct));
        });

        api.MapGet("/live/achievements", async (uint? appId, SteamLiveTrackerService liveTracker, CancellationToken ct) =>
        {
            var data = await liveTracker.GetAchievementsAsync(appId, ct);
            return Results.Ok(data);
        });

        // Unlocks/platinum of the last minutes: a window that (re)connects to the live hub shows what it missed while it was not connected
        api.MapGet("/live/events", (LiveEventLog eventLog) =>
        {
            var events = eventLog.Recent()
                .Select(e => new LiveEventDto(e.Id, e.At, e.Kind, e.Achievement, e.GameName, e.AppId, e.Total))
                .ToList();
            return Results.Ok(new LiveEventsResponseDto(eventLog.InstanceId, eventLog.LatestId, events));
        });

        api.MapPost("/live/note", async (SaveAchievementNoteRequest req, SettingsStore settingsStore, CancellationToken ct) =>
        {
            if (req.AppId == 0 || string.IsNullOrWhiteSpace(req.ApiName))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Games.NoteInvalidDetail);
            }
            await settingsStore.SaveNoteAsync(req.AppId, req.ApiName, req.Note, ct);
            return Results.Ok(new { success = true });
        });

        api.MapPost("/live/test/{type}", async (
            string type,
            string? gameName,
            int? total,
            IHubContext<AchievementHub> hubContext,
            LiveEventFeed liveEvents,
            CancellationToken ct) =>
        {
            var normalized = type.Trim().ToLowerInvariant();
            if (normalized == "platinum")
            {
                var targetGame = string.IsNullOrWhiteSpace(gameName) ? "Final Fantasy VII Remake Intergrade" : gameName;
                var targetTotal = total ?? 63;
                await hubContext.Clients.All.SendAsync("PlatinumUnlocked", new { gameName = targetGame, total = targetTotal }, ct);
                liveEvents.PublishPlatinum(new LivePlatinumEvent(targetGame, targetTotal, 0));
                return Results.Ok(new { success = true, type = "platinum", gameName = targetGame, total = targetTotal });
            }

            var tier = normalized switch
            {
                "bronze" => "Bronze",
                "silver" => "Silver",
                _ => "Gold"
            };
            var pct = tier switch
            {
                "Bronze" => 64.2,
                "Silver" => 14.8,
                _ => 2.4
            };

            var sample = new LiveAchievementItemDto(
                ApiName: $"TEST_{tier.ToUpperInvariant()}",
                DisplayName: ServerTexts.Live.TestName(tier, Lang.Current),
                Description: ServerTexts.Live.TestDescription(tier, Lang.Current),
                OriginalDescription: null,
                IsTranslated: false,
                IconUrl: "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1462040/header.jpg",
                IconGrayUrl: "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1462040/header.jpg",
                Achieved: true,
                UnlockTime: DateTime.Now,
                GlobalPercent: pct,
                RarityTier: tier,
                RarityLabel: SteamLiveTrackerService.CalculateRarityLabel(pct),
                IsHidden: false,
                UserNote: string.Empty
            )
            {
                HasGlobalPercent = true,
                RarityBand = SteamLiveTrackerService.CalculateRarityBand(pct),
            };

            await hubContext.Clients.All.SendAsync("AchievementUnlocked", sample, ct);
            liveEvents.PublishUnlock(new LiveUnlockEvent(sample, gameName, 0));
            return Results.Ok(new { success = true, type = tier, achievement = sample });
        });

        api.MapGet("/games", async (CompanionDbContext db, SteamClient steamClient, SteamLiveTrackerService liveTracker, LibraryStatsService libraryStats, LibraryMetaService libraryMeta, CancellationToken ct) =>
        {
            var games = await db.Games
                .AsNoTracking()
                .Include(g => g.Tabs)
                .Include(g => g.ProgressChecks)
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Title)
                .ToListAsync(ct);

            await HealSteamArtworkAsync(db, steamClient, games, ct);

            // Not refreshed yet (e.g. just starred): show the figures of the library evaluation right away
            foreach (var game in games.Where(g => g.TrophiesUpdatedAt is null && g.SteamAppId is > 0))
            {
                if (libraryStats.Get(game.SteamAppId!.Value) is { Total: > 0 } stat)
                {
                    game.TrophiesUnlocked = stat.Unlocked;
                    game.TrophiesTotal = stat.Total;
                }
            }

            // Trophy states older than half an hour are refreshed in the background; the next list request shows them
            liveTracker.RefreshTrophySnapshots(games
                .Where(g => g.SteamAppId is > 0 && g.IsFavorite &&
                            (g.TrophiesUpdatedAt is null || DateTimeOffset.UtcNow - g.TrophiesUpdatedAt > TimeSpan.FromMinutes(30) ||
                             g.FactsUpdatedAt is null || DateTimeOffset.UtcNow - g.FactsUpdatedAt > TimeSpan.FromMinutes(30)))
                .Select(g => (uint)g.SteamAppId!.Value));

            // Card texts the AI wrote (subtitle, hours, ...) come in the language of the request once they are translated
            var translatedTexts = await GuideTranslations.CurrentGameTextsAsync(db, games, Lang.Current, ct);
            // The values of the dashboard's sort orders (release date, size on disk, ...) come from the library data of the whole Steam library
            var meta = libraryMeta.Snapshot();
            var result = games.Select(g => WithLibraryMeta(MapToSummaryDto(g, translatedTexts.GetValueOrDefault(g.Id)), meta)).ToList();
            return Results.Ok(result);
        });

        // Links a game to its Steam app (e.g. imported or manually added games): sets the AppID, fetches the official
        // cover/banner and returns the updated game. Achievements, statistics and covers all need the AppID.
        api.MapPut("/games/{slug}/steam-app", async (
            string slug,
            LinkSteamAppRequest req,
            CompanionDbContext db,
            GuideTranslationService translations,
            SteamClient steamClient,
            SteamStatsService steamStats,
            CancellationToken ct) =>
        {
            var game = await db.Games
                .Include(g => g.Tabs)
                .Include(g => g.ProgressChecks)
                .FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));
            }
            if (req.SteamAppId <= 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.InvalidSteamAppIdTitle, ServerTexts.Games.InvalidSteamAppIdDetail);
            }
            if (await db.Games.AnyAsync(g => g.Id != game.Id && g.SteamAppId == req.SteamAppId, ct))
            {
                return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Games.AlreadyLinkedTitle, ServerTexts.Games.AlreadyLinkedDetail(req.SteamAppId));
            }
            if (await steamStats.GetStoreFactsAsync(req.SteamAppId, ct) is null)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.SteamGameNotFoundTitle, ServerTexts.Games.SteamGameNotFoundDetail(req.SteamAppId));
            }

            var assets = await steamClient.ResolveSingleAppAssetsAsync(req.SteamAppId, ct);
            game.SteamAppId = req.SteamAppId;
            game.CoverUrl = assets.CoverUrl;
            game.HeroBannerUrl = assets.HeroUrl;
            game.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            game.Tabs = game.Tabs.OrderBy(t => t.SortOrder).ToList();
            return Results.Ok(await BuildDetailAsync(db, translations, game, null, ct));
        });

        // Star on the dashboard: marks or unmarks a game as favourite
        api.MapPut("/games/{slug}/favorite", async (string slug, SetFavoriteRequest req, CompanionDbContext db, CancellationToken ct) =>
        {
            var game = await db.Games
                .Include(g => g.Tabs)
                .Include(g => g.ProgressChecks)
                .FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));
            }

            // A game that is starred again goes to the END of the dashboard, so a custom order is never scrambled
            if (req.IsFavorite && !game.IsFavorite)
            {
                game.SortOrder = await NextGameSortOrderAsync(db, ct);
            }
            game.IsFavorite = req.IsFavorite;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapToSummaryDto(game));
        });

        // Dashboard drag & drop: the listed games get SortOrder 0..n-1 in the sent order; every other game (e.g. unstarred ones)
        // keeps its relative order and follows them. The CSRF header is enforced by the endpoint filter of the /api group.
        api.MapPut("/games/order", async (ReorderGamesRequest req, CompanionDbContext db, CancellationToken ct) =>
        {
            var slugs = req.Slugs;
            if (slugs is null)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.OrderMissingTitle, ServerTexts.Games.OrderMissingDetail);
            }
            if (slugs.Count > MaxReorderEntries)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.TooManyGamesTitle, ServerTexts.Games.TooManyGamesDetail(slugs.Count, MaxReorderEntries));
            }
            if (slugs.Any(string.IsNullOrWhiteSpace))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.InvalidOrderTitle, ServerTexts.Games.OrderEmptyEntry);
            }

            var duplicates = slugs.GroupBy(s => s, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.InvalidOrderTitle, ServerTexts.Games.OrderDuplicates(duplicates));
            }

            // Tracked without includes: only SortOrder changes, and only for games whose position actually moved
            var games = await db.Games.ToListAsync(ct);
            var bySlug = games.ToDictionary(g => g.Slug, StringComparer.Ordinal);
            var unknown = slugs.Where(s => !bySlug.ContainsKey(s)).ToList();
            if (unknown.Count > 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.UnknownGamesTitle, ServerTexts.Games.UnknownGamesDetail(unknown));
            }

            var listed = slugs.Select(s => bySlug[s]).ToList();
            var listedIds = listed.Select(g => g.Id).ToHashSet();
            var others = games.Where(g => !listedIds.Contains(g.Id)).OrderBy(g => g.SortOrder).ThenBy(g => g.Title);

            var position = 0;
            foreach (var game in listed.Concat(others))
            {
                game.SortOrder = position++;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ?lang=de|en asks for that language (default: the language of the request): the translated chapters when the translation is
        // complete, else the original together with the state of the translation (see GuideTranslationDto)
        api.MapGet("/games/{slug}", async (string slug, string? lang, CompanionDbContext db, GuideTranslationService translations, CancellationToken ct) =>
        {
            var game = await db.Games
                .AsNoTracking()
                .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
                .Include(g => g.ProgressChecks)
                .FirstOrDefaultAsync(g => g.Slug == slug, ct);

            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GuideNotFound(slug));
            }

            return Results.Ok(await BuildDetailAsync(db, translations, game, lang, ct));
        });

        api.MapPost("/games", async (CreateGameRequest req, CompanionDbContext db, SteamClient steamClient, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.TitleMissingTitle, ServerTexts.Games.TitleMissingDetail);
            }

            var baseSlug = AiGuideGeneratorService.Slugify(req.Title);
            var slug = await EnsureUniqueSlugAsync(db, baseSlug, ct);
            var maxSort = await db.Games.MaxAsync(g => (int?)g.SortOrder, ct) ?? 0;

            var resolvedSteam = req.SteamAppId.HasValue
                ? await steamClient.ResolveSingleAppAssetsAsync(req.SteamAppId.Value, ct)
                : (CoverUrl: string.Empty, HeroUrl: string.Empty, HeaderUrl: string.Empty);

            var coverUrl = !string.IsNullOrWhiteSpace(req.CoverUrl)
                ? req.CoverUrl.Trim()
                : resolvedSteam.CoverUrl;

            var heroUrl = !string.IsNullOrWhiteSpace(req.HeroBannerUrl)
                ? req.HeroBannerUrl.Trim()
                : resolvedSteam.HeroUrl;

            var gameId = Guid.NewGuid();
            // A new guide is written in the language of the UI; the card texts that were not given follow it
            var language = Lang.Current;
            var defaults = AiGuideGeneratorService.DefaultsFor(language);
            var game = new GameEntity
            {
                Id = gameId,
                Slug = slug,
                Title = req.Title.Trim(),
                Subtitle = string.IsNullOrWhiteSpace(req.Subtitle) ? defaults.Subtitle : req.Subtitle.Trim(),
                SteamAppId = req.SteamAppId,
                CoverUrl = coverUrl,
                HeroBannerUrl = heroUrl,
                AccentColor = string.IsNullOrWhiteSpace(req.AccentColor) ? "emerald" : req.AccentColor.Trim(),
                EstimatedHours = string.IsNullOrWhiteSpace(req.EstimatedHours) ? defaults.EstimatedHours : req.EstimatedHours.Trim(),
                MissablesInfo = string.IsNullOrWhiteSpace(req.MissablesInfo) ? defaults.MissablesInfo : req.MissablesInfo.Trim(),
                DifficultyInfo = string.IsNullOrWhiteSpace(req.DifficultyInfo) ? defaults.DifficultyInfo : req.DifficultyInfo.Trim(),
                GenreTags = string.IsNullOrWhiteSpace(req.GenreTags) ? defaults.GenreTags : req.GenreTags.Trim(),
                ContentLanguage = language,
                SortOrder = maxSort + 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var starterTab = CreateStarterTab(game);

            db.Games.Add(game);
            db.GuideTabs.Add(starterTab);
            await db.SaveChangesAsync(ct);

            game.Tabs = [starterTab];
            return Results.Ok(MapToSummaryDto(game));
        });

        // Fixed topic list every guide is checked against (quick picks for "Add chapter"). Titles and briefs come in the language of the
        // request; ?lang=de|en overrides it (the game page asks for the language of the guide it is about to extend).
        api.MapGet("/guide/topics", (string? lang) => Results.Ok(AiGuideGeneratorService.Topics(Lang.NormalizeOr(lang, Lang.Current))
            .Select(t => new GuideTopicDto(t.Id, t.Title, t.Icon, t.Brief, GuideGroups.ForTopic(t.Id) ?? GuideGroups.Extras))));

        // The groups the chapters are listed under in the side navigation, in display order, with their labels in the language of the request
        // (?lang=de|en overrides it, like on the topics endpoint)
        api.MapGet("/guide/groups", (string? lang) =>
        {
            var language = Lang.NormalizeOr(lang, Lang.Current);
            return Results.Ok(GuideGroups.Keys.Select(key => new GuideGroupDto(key, GuideGroups.Label(key, language))));
        });

        // The AI actions that take minutes (ai-generate, ai-plan, ai-complete, ai-expand-tab, ai-lore) are background jobs: see AiActionEndpoints

        api.MapGet("/games/{slug}/stats", async (
            string slug,
            CompanionDbContext db,
            SettingsStore settingsStore,
            SteamLiveTrackerService liveTracker,
            SteamStatsService stats,
            LibraryMetaService libraryMeta,
            CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));
            }
            if (game.SteamAppId is not > 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.NoSteamAppIdTitle, ServerTexts.Games.NoSteamAppIdDetail);
            }

            var settings = await settingsStore.GetAsync(ct);
            var achievements = await liveTracker.GetAchievementsAsync((uint)game.SteamAppId.Value, ct);
            var result = await stats.GetGameStatsAsync(game.SteamAppId.Value, settings.SteamUserId, achievements, ct);
            // Size on disk / estimated install size from the library data (local Steam files, bridge)
            var meta = libraryMeta.Snapshot().Get(game.SteamAppId.Value);
            return Results.Ok(result with { SizeOnDiskBytes = meta?.SizeBytes, InstallSizeBytes = meta?.InstallSizeBytes });
        });

        api.MapDelete("/games/{id:guid}", async (Guid id, CompanionDbContext db, CancellationToken ct) =>
        {
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id, ct);
            if (game is null)
            {
                return Results.NotFound();
            }

            db.Games.Remove(game);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/games/{slug}/progress", async (
            string slug,
            ToggleProgressRequest req,
            CompanionDbContext db,
            CancellationToken ct) =>
        {
            var game = await db.Games.FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Results.NotFound();
            }

            // The own id of a row that points at another chapter's row ("ref") has no tick: the tick belongs to the row it points at
            var key = GuideLinker.TickKeyFor(
                await db.GuideTabs.AsNoTracking().Where(t => t.GameId == game.Id).Select(t => t.SectionsJson).ToListAsync(ct), req.ItemKey.Trim());
            var existing = await db.ProgressChecks.FirstOrDefaultAsync(p => p.GameId == game.Id && p.ItemKey == key, ct);

            try
            {
                if (req.Checked && existing is null)
                {
                    db.ProgressChecks.Add(new ProgressCheckEntity
                    {
                        Id = Guid.NewGuid(),
                        GameId = game.Id,
                        ItemKey = key,
                        CheckedAt = DateTimeOffset.UtcNow
                    });
                    game.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                else if (!req.Checked && existing is not null)
                {
                    db.ProgressChecks.Remove(existing);
                    game.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (DbUpdateException)
            {
                // Another request (a second window, the linker) set or removed exactly this tick in the meantime: the wanted state is there already
                db.ChangeTracker.Clear();
            }

            var checkedKeys = await db.ProgressChecks
                .AsNoTracking()
                .Where(p => p.GameId == game.Id)
                .Select(p => p.ItemKey)
                .ToListAsync(ct);

            return Results.Ok(new { checkedItemKeys = checkedKeys });
        });

        api.MapPost("/games/{slug}/progress/reset", async (string slug, CompanionDbContext db, CancellationToken ct) =>
        {
            var game = await db.Games.FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Results.NotFound();
            }

            var checks = await db.ProgressChecks.Where(p => p.GameId == game.Id).ToListAsync(ct);
            if (checks.Count > 0)
            {
                db.ProgressChecks.RemoveRange(checks);
                game.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { checkedItemKeys = Array.Empty<string>() });
        });

        api.MapGet("/steam/search", async (string? q, SteamClient steamClient, CancellationToken ct) =>
        {
            var items = await steamClient.SearchGamesAsync(q ?? string.Empty, ct);
            return Results.Ok(items);
        });

        api.MapGet("/steam/profile", async (SteamClient steamClient, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct) =>
            Results.Ok(await steamClient.GetProfileAsync(cache, ct)));

        api.MapPut("/steam/library/{appId:int}/favorite", async (
            int appId,
            LibraryFavoriteRequest req,
            CompanionDbContext db,
            SteamClient steamClient,
            LibraryStatsService libraryStats,
            SteamLiveTrackerService liveTracker,
            CancellationToken ct) =>
        {
            if (appId <= 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.InvalidAppIdTitle, ServerTexts.Games.InvalidSteamAppIdDetail);
            }

            var game = await db.Games
                .Include(g => g.Tabs)
                .Include(g => g.ProgressChecks)
                .FirstOrDefaultAsync(g => g.SteamAppId == appId, ct);

            if (game is not null)
            {
                // Starred again: back at the END of the dashboard (see PUT /games/{slug}/favorite)
                if (req.IsFavorite && !game.IsFavorite)
                {
                    game.SortOrder = await NextGameSortOrderAsync(db, ct);
                }
                game.IsFavorite = req.IsFavorite;
                await db.SaveChangesAsync(ct);
                if (req.IsFavorite) liveTracker.RefreshTrophySnapshots([(uint)appId]);
                return Results.Ok(MapToSummaryDto(game));
            }

            if (!req.IsFavorite)
            {
                return Results.NoContent();
            }

            var tileLanguage = Lang.Current;
            var title = string.IsNullOrWhiteSpace(req.Name) ? ServerTexts.Games.SteamAppName(appId).Resolve(tileLanguage) : req.Name.Trim();
            if (title.Length > 240) title = title[..240];
            var tileDefaults = AiGuideGeneratorService.DefaultsFor(tileLanguage);
            var assets = await steamClient.ResolveSingleAppAssetsAsync(appId, ct);
            var tile = new GameEntity
            {
                Id = Guid.NewGuid(),
                Slug = await EnsureUniqueSlugAsync(db, AiGuideGeneratorService.Slugify(title), ct),
                Title = title,
                Subtitle = tileDefaults.Subtitle,
                SteamAppId = appId,
                CoverUrl = assets.CoverUrl,
                HeroBannerUrl = assets.HeroUrl,
                AccentColor = "emerald",
                EstimatedHours = tileDefaults.EstimatedHours,
                MissablesInfo = tileDefaults.MissablesInfo,
                DifficultyInfo = tileDefaults.DifficultyInfo,
                GenreTags = tileDefaults.GenreTags,
                ContentLanguage = tileLanguage,
                IsFavorite = true,
                SortOrder = (await db.Games.MaxAsync(g => (int?)g.SortOrder, ct) ?? 0) + 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            // The card shows the trophies at once (library evaluation); the background refresh adds the rest in seconds
            if (libraryStats.Get(appId) is { Total: > 0 } stat)
            {
                tile.TrophiesUnlocked = stat.Unlocked;
                tile.TrophiesTotal = stat.Total;
            }
            // No AI here: the guide is researched later on the game page ("Guide vervollständigen")
            db.Games.Add(tile);
            await db.SaveChangesAsync(ct);
            liveTracker.RefreshTrophySnapshots([(uint)appId]);
            return Results.Ok(MapToSummaryDto(tile));
        });

        // Library-wide figures for the dashboard (trophies unlocked, games at 100 %); starts the background evaluation
        api.MapGet("/steam/library/summary", async (
            SteamClient steamClient,
            LibraryStatsService libraryStats,
            LibraryMetaService libraryMeta,
            Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
            SecretStore secrets,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(secrets.GetSecret(SettingsStore.SecretSteam)))
            {
                return Results.NoContent();
            }

            try
            {
                var library = await Microsoft.Extensions.Caching.Memory.CacheExtensions.GetOrCreateAsync(cache, "steam-library-summary-source", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                    return await steamClient.FetchOwnedGamesAsync(null, [], ct);
                });
                if (library is null) return Results.NoContent();
                libraryStats.StartScan(library.ResolvedSteamId, library.Games);
                // Release date / reviews of the apps no local Steam client can tell about: slowly, in the background
                _ = libraryMeta.StartScan(library.Games);
                var (metaScanning, metaDone, metaTotal) = libraryMeta.ScanState;
                return Results.Ok(libraryStats.Summarize(library.TotalGames) with { MetaScanning = metaScanning, MetaDone = metaDone, MetaTotal = metaTotal });
            }
            catch (InvalidOperationException ex)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.LibraryUnavailableTitle, ex.Message);
            }
        });

        api.MapGet("/steam/library", async (
            string? steamId,
            CompanionDbContext db,
            SteamClient steamClient,
            LibraryStatsService libraryStats,
            LibraryMetaService libraryMeta,
            CancellationToken ct) =>
        {
            try
            {
                var existingGames = await db.Games
                    .AsNoTracking()
                    .Where(g => g.SteamAppId != null)
                    .Select(g => new { AppId = g.SteamAppId!.Value, g.Slug, g.IsFavorite })
                    .ToListAsync(ct);

                var appIdMap = new Dictionary<int, string>();
                foreach (var g in existingGames)
                {
                    appIdMap[g.AppId] = g.Slug;
                }
                var favorites = existingGames.Where(g => g.IsFavorite).Select(g => g.AppId).ToHashSet();

                var library = await steamClient.FetchOwnedGamesAsync(steamId, appIdMap, ct, favorites);
                // Keeps the library-wide trophy figures current (only games played since their last check are asked)
                libraryStats.StartScan(library.ResolvedSteamId, library.Games);
                _ = libraryMeta.StartScan(library.Games);
                var meta = libraryMeta.Snapshot();
                return Results.Ok(library with
                {
                    Games = library.Games
                        .Select(g =>
                        {
                            var known = meta.Get(g.AppId);
                            return (libraryStats.Get(g.AppId) is { } stat ? g with { TrophiesUnlocked = stat.Unlocked, TrophiesTotal = stat.Total } : g) with
                            {
                                ReleaseAt = known?.ReleaseAt,
                                SizeOnDiskBytes = known?.SizeBytes,
                                InstallSizeBytes = known?.InstallSizeBytes,
                                SortMetacritic = known?.Metacritic,
                                SortReviewPercent = known?.ReviewPercent,
                            };
                        })
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.LibraryLoadFailedTitle, ex.Message);
            }
        });

        api.MapGet("/settings", async (SettingsStore store, CancellationToken ct) =>
        {
            return Results.Ok(await store.GetAsync(ct));
        });

        api.MapPut("/settings", async (UpdateSettingsRequest req, SettingsStore store, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await store.UpdateAsync(req, ct));
            }
            catch (SettingsValidationException ex)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.SettingsInvalidTitle, ex.Message);
            }
        });

        // Live check of the field "Steam-Ordner" while it is typed: the same rules as PUT /api/settings, nothing is stored. Always 200;
        // "valid" says whether the folder could be saved, "message" what is wrong with it (in the request's language).
        api.MapPost("/steam/local/check", (SteamRootCheckRequest req, SettingsStore store) =>
        {
            var path = req.Path ?? string.Empty;
            if (path.Length > 4096)
            {
                path = path[..4096];
            }
            return Results.Ok(SteamLocalInfo.CheckResponse(store.CheckSteamRoot(path), store.SteamRootPlatform));
        });

        api.MapPost("/ai/models", async (AiProviderModelsRequestDto req, AiGuideGeneratorService aiService,
            CancellationToken ct) =>
        {
            var res = await aiService.ProbeProviderModelsAsync(req.Provider, req.ApiKey, ct);
            return Results.Ok(res);
        });

        // Real test of the stored Gemini key: one tiny request without and one with Google Search (the guide research needs it)
        api.MapPost("/ai/gemini/diagnose", async (GeminiDiagnoseRequest req, AiGuideGeneratorService aiService,
            CancellationToken ct) =>
            Results.Ok(await aiService.DiagnoseGeminiAsync(req.Model, ct)));
    }

    /// <summary>
    /// Accepts the browser origin for the Steam login round trip only if it is this server itself, or - in development,
    /// where Vite on another port proxies the API - a localhost origin. Prevents redirects to foreign sites.
    /// </summary>
    internal static string? ResolveTrustedOrigin(string? origin, HttpRequest request, IWebHostEnvironment env)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var sameHost = string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
        var devLocalhost = env.IsDevelopment() && uri.IsLoopback;
        return sameHost || devLocalhost ? uri.GetLeftPart(UriPartial.Authority) : null;
    }

    /// <summary>The empty overview tab every new guide starts with (also used when an imported file has no tabs), in the guide's language.</summary>
    internal static GuideTabEntity CreateStarterTab(GameEntity game)
    {
        var language = AiGuideGeneratorService.GuideLanguage(game);
        var starterSection = new List<GuideSectionDto>
        {
            new(
                Type: "alert",
                Variant: "crystal",
                Icon: "mdi:sparkles",
                Title: AiGuideGeneratorService.StarterAlertTitle(game.Title, language),
                HtmlContent: AiGuideGeneratorService.StarterAlertText(language)
            )
        };

        return new GuideTabEntity
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Slug = "overview",
            Title = AiGuideGeneratorService.StarterTabTitle(language),
            GroupKey = GuideGroups.Start,
            Icon = "mdi:compass-outline",
            SortOrder = 1,
            SectionsJson = JsonSerializer.Serialize(starterSection, JsonOptions)
        };
    }

    /// <summary>
    /// The ids of everything a reader can tick in these sections: checklist rows of checkable tables and puzzles that have an itemId. A row with a Ref
    /// has no check of its own (it shows the check of the defining row in another chapter), so it is not counted here.
    /// </summary>
    internal static IEnumerable<string> CheckableItemIds(IEnumerable<GuideSectionDto> sections)
    {
        foreach (var sec in sections)
        {
            if (sec.Table is { Checkable: true, Rows: not null })
            {
                foreach (var row in sec.Table.Rows)
                {
                    if (string.IsNullOrEmpty(row.Ref))
                    {
                        yield return row.ItemId;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(sec.Puzzle?.ItemId))
            {
                yield return sec.Puzzle.ItemId;
            }
        }
    }

    /// <summary>
    /// The item ids that DEFINE a tick (rows without a Ref, tickable puzzles): the keys of <c>progress_checks</c> that belong to these tabs. A row with a Ref
    /// is not a definer - it shows the check of the row it points to. Use <see cref="CollectTakenItemIds"/> when ids have to stay unique.
    /// </summary>
    internal static HashSet<string> CollectItemIds(IEnumerable<GuideTabEntity> tabs) => CollectIds(tabs, includeRefRows: false);

    /// <summary>Every item id in use, ref rows included (their own ids are identities too): the "taken" set for <see cref="MakeItemIdsUnique"/>.</summary>
    internal static HashSet<string> CollectTakenItemIds(IEnumerable<GuideTabEntity> tabs) => CollectIds(tabs, includeRefRows: true);

    private static HashSet<string> CollectIds(IEnumerable<GuideTabEntity> tabs, bool includeRefRows)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tab in tabs)
        {
            foreach (var section in DeserializeSections(tab.SectionsJson))
            {
                foreach (var row in section.Table?.Rows ?? [])
                {
                    if (includeRefRows || string.IsNullOrEmpty(row.Ref))
                    {
                        ids.Add(row.ItemId);
                    }
                }
                if (!string.IsNullOrWhiteSpace(section.Puzzle?.ItemId))
                {
                    ids.Add(section.Puzzle.ItemId);
                }
            }
        }
        return ids;
    }

    /// <summary>
    /// Checklist progress is stored per game and item id, so an id may occur only once per game. AI answers often reuse
    /// template ids ("item-1", "custom-1") across tables - duplicates get the tab slug prefixed (and a counter if needed).
    /// </summary>
    internal static void MakeItemIdsUnique(IEnumerable<GuideTabEntity> tabs, HashSet<string> taken)
    {
        foreach (var tab in tabs)
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(tab.SectionsJson) ? "[]" : tab.SectionsJson)
                is not System.Text.Json.Nodes.JsonArray sections)
            {
                continue;
            }

            var changed = false;
            // Checklist rows and tickable puzzles (puzzle.itemId; a puzzle without one is not tickable and stays without)
            var rowObjects = sections
                .Select(s => s?["table"]?["rows"])
                .OfType<System.Text.Json.Nodes.JsonArray>()
                .SelectMany(rows => rows)
                .OfType<System.Text.Json.Nodes.JsonObject>();
            var puzzleObjects = sections
                .Select(s => s?["puzzle"])
                .OfType<System.Text.Json.Nodes.JsonObject>()
                .Where(p => !string.IsNullOrWhiteSpace(p["itemId"]?.ToString()));
            foreach (var row in rowObjects.Concat(puzzleObjects))
            {
                var id = row["itemId"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    id = $"{tab.Slug}:item";
                }

                var candidate = id;
                if (taken.Contains(candidate))
                {
                    candidate = $"{tab.Slug}:{id}";
                    var counter = 2;
                    while (taken.Contains(candidate))
                    {
                        candidate = $"{tab.Slug}:{id}-{counter++}";
                    }
                }

                if (candidate != row["itemId"]?.GetValue<string>())
                {
                    row["itemId"] = candidate;
                    changed = true;
                }
                taken.Add(candidate);
            }

            if (changed)
            {
                tab.SectionsJson = sections.ToJsonString();
            }
        }
    }

    /// <summary>
    /// Games with a Steam AppID but no usable cover (empty, or a former local "/covers/..." file that no longer ships)
    /// get the official Steam artwork once; the fix is stored so later loads need no Steam request.
    /// </summary>
    private static async Task HealSteamArtworkAsync(CompanionDbContext db, SteamClient steamClient, List<GameEntity> games, CancellationToken ct)
    {
        static bool NeedsArtwork(string url) => string.IsNullOrWhiteSpace(url) || url.StartsWith('/');
        var broken = games.Where(g => g.SteamAppId is > 0 && (NeedsArtwork(g.CoverUrl) || NeedsArtwork(g.HeroBannerUrl))).ToList();
        if (broken.Count == 0)
        {
            return;
        }

        var assets = await steamClient.ResolveStoreAssetsAsync(broken.Select(g => g.SteamAppId!.Value).ToList(), ct);
        foreach (var game in broken)
        {
            if (!assets.TryGetValue(game.SteamAppId!.Value, out var art))
            {
                continue;
            }
            if (NeedsArtwork(game.CoverUrl)) game.CoverUrl = art.CoverUrl;
            if (NeedsArtwork(game.HeroBannerUrl)) game.HeroBannerUrl = art.HeroUrl;
            await db.Games
                .Where(g => g.Id == game.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.CoverUrl, game.CoverUrl)
                    .SetProperty(g => g.HeroBannerUrl, game.HeroBannerUrl), ct);
        }
    }

    /// <summary>Position behind the last game, for a game that is added or starred again (keeps a custom order intact).</summary>
    internal static async Task<int> NextGameSortOrderAsync(CompanionDbContext db, CancellationToken ct) =>
        (await db.Games.MaxAsync(g => (int?)g.SortOrder, ct) ?? 0) + 1;

    /// <summary>
    /// A game without any researched chapter (no tab, or only the starter overview) has nothing a second language could be mixed
    /// with: before its first research it takes the language of the UI - ContentLanguage, the starter tab and the card texts that
    /// are still the defaults of the old language. Changes the entity in memory only; the caller saves it together with the research.
    /// </summary>
    internal static bool AdoptRequestLanguageIfEmpty(GameEntity game)
    {
        var requested = Lang.Current;
        var current = AiGuideGeneratorService.GuideLanguage(game);
        if (current == requested || game.Tabs.Any(t => t.Slug != "overview"))
        {
            return false;
        }

        var from = AiGuideGeneratorService.DefaultsFor(current);
        var to = AiGuideGeneratorService.DefaultsFor(requested);
        game.ContentLanguage = requested;
        if (game.Subtitle == from.Subtitle) game.Subtitle = to.Subtitle;
        if (game.EstimatedHours == from.EstimatedHours) game.EstimatedHours = to.EstimatedHours;
        if (game.MissablesInfo == from.MissablesInfo) game.MissablesInfo = to.MissablesInfo;
        if (game.DifficultyInfo == from.DifficultyInfo) game.DifficultyInfo = to.DifficultyInfo;
        if (game.GenreTags == from.GenreTags) game.GenreTags = to.GenreTags;
        if (game.Tabs.FirstOrDefault(t => t.Slug == "overview") is { } starter)
        {
            var fresh = CreateStarterTab(game);
            starter.Title = fresh.Title;
            starter.SectionsJson = fresh.SectionsJson;
        }
        return true;
    }

    internal static async Task<string> EnsureUniqueSlugAsync(CompanionDbContext db, string baseSlug, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(baseSlug) ? "game-guide" : baseSlug;
        var candidate = slug;
        var counter = 2;
        while (await db.Games.AnyAsync(g => g.Slug == candidate, ct))
        {
            candidate = $"{slug}-{counter}";
            counter++;
        }
        return candidate;
    }

    /// <summary>
    /// The guide's Steam achievement table (built from Steam's list, or the "achievements" tab of older guides) is not a
    /// chapter: Steam reports what is unlocked, so it neither shows as a tab nor counts towards the guide progress - it
    /// only carries the how-to tips that the live trophies tab shows next to each achievement.
    /// </summary>
    internal static bool IsAchievementList(GuideTabEntity tab, List<GuideSectionDto> sections) =>
        string.Equals(tab.Slug, "achievements", StringComparison.OrdinalIgnoreCase) ||
        sections.Any(s => s.Table?.TableId == "steam-achievements");

    /// <summary>Adds the library values of the sort orders to a card; the card's own facts stay as they are (see <see cref="GameSummaryDto"/>).</summary>
    internal static GameSummaryDto WithLibraryMeta(GameSummaryDto dto, LibraryMetaView view)
    {
        var meta = dto.SteamAppId is > 0 ? view.Get(dto.SteamAppId.Value) : null;
        return dto with
        {
            ReleaseAt = meta?.ReleaseAt,
            SizeOnDiskBytes = meta?.SizeBytes,
            InstallSizeBytes = meta?.InstallSizeBytes,
            SortMetacritic = dto.MetacriticScore ?? meta?.Metacritic,
            SortReviewPercent = dto.ReviewPercent ?? meta?.ReviewPercent,
        };
    }

    internal static GameSummaryDto MapToSummaryDto(GameEntity game, GameTranslationEntity? translation = null)
    {
        var checkedSet = game.ProgressChecks.Select(p => p.ItemKey).ToHashSet(StringComparer.Ordinal);
        var totalCheckable = 0;
        var checkedCount = 0;
        var chapterCount = 0;

        foreach (var tab in game.Tabs)
        {
            var sections = DeserializeSections(tab.SectionsJson);
            if (IsAchievementList(tab, sections)) continue;
            chapterCount++;
            foreach (var itemId in CheckableItemIds(sections))
            {
                totalCheckable++;
                if (checkedSet.Contains(itemId))
                {
                    checkedCount++;
                }
            }
        }

        var pct = totalCheckable > 0 ? (int)Math.Round(checkedCount * 100.0 / totalCheckable) : 0;

        return new GameSummaryDto(
            Id: game.Id,
            Slug: game.Slug,
            Title: game.Title,
            Subtitle: translation?.Subtitle ?? game.Subtitle,
            SteamAppId: game.SteamAppId,
            CoverUrl: game.CoverUrl,
            HeroBannerUrl: game.HeroBannerUrl,
            AccentColor: game.AccentColor,
            EstimatedHours: translation?.EstimatedHours ?? game.EstimatedHours,
            MissablesInfo: translation?.MissablesInfo ?? game.MissablesInfo,
            DifficultyInfo: translation?.DifficultyInfo ?? game.DifficultyInfo,
            GenreTags: translation?.GenreTags ?? game.GenreTags,
            ContentLanguage: AiGuideGeneratorService.GuideLanguage(game),
            SteamGenres: game.SteamGenres,
            TotalCheckableItems: totalCheckable,
            CheckedItemsCount: checkedCount,
            ProgressPercent: pct,
            TabCount: chapterCount,
            TrophiesUnlocked: game.TrophiesUnlocked,
            TrophiesTotal: game.TrophiesTotal,
            TrophiesPercent: game.TrophiesTotal > 0 ? (int)Math.Floor(game.TrophiesUnlocked * 100.0 / game.TrophiesTotal) : 0,
            PlatinumAt: game.PlatinumAt,
            TrophiesCheckedAt: game.TrophiesUpdatedAt,
            IsFavorite: game.IsFavorite,
            PlaytimeMinutes: game.PlaytimeMinutes,
            LastPlayedAt: game.LastPlayedAt,
            MetacriticScore: game.MetacriticScore,
            ReviewScoreDesc: game.ReviewScoreDesc,
            ReviewPercent: game.ReviewPercent,
            ReleaseDate: game.ReleaseDate,
            CompletionHours: game.CompletionHours,
            IgdbRating: game.IgdbRating,
            UpdatedAt: game.UpdatedAt
        );
    }

    /// <summary>The detail of a game in the requested language: translated chapters when the translation is complete, else the original (+ the translation state).</summary>
    internal static async Task<GameDetailDto> BuildDetailAsync(CompanionDbContext db, GuideTranslationService translations, GameEntity game, string? language, CancellationToken ct)
    {
        var target = Lang.NormalizeOr(language, Lang.Current);
        var view = await GuideTranslations.EvaluateAsync(db, game, target, translations.GetJob(game.Id, target), ct);
        return MapToDetailDto(game, view);
    }

    private static GameDetailDto MapToDetailDto(GameEntity game, TranslationView view)
    {
        var translatedGame = view.UseTranslation ? view.Game : null;
        var checkedSet = game.ProgressChecks.Select(p => p.ItemKey).ToHashSet(StringComparer.Ordinal);
        var totalCheckable = 0;
        var checkedCount = 0;
        var tabDtos = new List<GuideTabDto>();

        foreach (var tab in WalkthroughTab.InGuideOrder(game.Tabs))
        {
            // The same chapter in the requested language: structure and itemIds are identical, so progress is shared
            var translatedTab = view.UseTranslation && view.Tabs.TryGetValue(tab.Id, out var found) ? found : null;
            var sections = DeserializeSections(translatedTab?.SectionsJson ?? tab.SectionsJson);
            // A chapter that could not be researched is shown from its ORIGINAL placeholder (the translation of an error text is pointless and
            // would lose what the retry needs); old placeholders get their topic read from the text, nothing is rewritten in the database
            if (translatedTab is not null && FailedChapters.Read(tab.SectionsJson) is not null)
            {
                sections = DeserializeSections(tab.SectionsJson);
            }
            else if (translatedTab is not null)
            {
                // Links are ids, not text: the translation stays current when the linker changes a ref, and shows the refs of the original
                sections = GuideTranslations.ApplyOriginalRefs(sections, DeserializeSections(tab.SectionsJson));
            }
            if (FailedChapters.Read(sections) is { } failedChapter && sections.Count == 1 && sections[0].FailedChapter is null)
            {
                sections = [sections[0] with { FailedChapter = failedChapter }];
            }
            var isAchievementList = IsAchievementList(tab, sections);
            var tabTotal = 0;
            var tabChecked = 0;

            foreach (var itemId in CheckableItemIds(sections))
            {
                tabTotal++;
                if (checkedSet.Contains(itemId))
                {
                    tabChecked++;
                }
            }

            if (!isAchievementList)
            {
                totalCheckable += tabTotal;
                checkedCount += tabChecked;
            }

            tabDtos.Add(new GuideTabDto(
                Id: tab.Id,
                Slug: tab.Slug,
                Title: translatedTab?.Title ?? tab.Title,
                Icon: tab.Icon,
                SortOrder: tab.SortOrder,
                TotalCheckableItems: tabTotal,
                CheckedItemsCount: tabChecked,
                Sections: sections,
                IsAchievementList: isAchievementList,
                // The ORIGINAL title decides, so a chapter stays in its group whatever language the shown texts are in
                GroupKey: GuideGroups.Resolve(tab.GroupKey, tab.Title, AiGuideGeneratorService.GuideLanguage(game)),
                // The walkthrough is known by its fixed address also when it was saved before the column existed
                TopicId: tab.TopicId ?? (WalkthroughTab.Is(tab) ? WalkthroughTab.TopicId : null)
            ));
        }

        // Links are read from the chapters as shown (a translation keeps the structure, so the ids and refs are the same)
        var (itemLinks, refTargets) = GuideLinker.BuildMaps(tabDtos);

        var pct = totalCheckable > 0 ? (int)Math.Round(checkedCount * 100.0 / totalCheckable) : 0;

        return new GameDetailDto(
            Id: game.Id,
            Slug: game.Slug,
            Title: game.Title,
            Subtitle: translatedGame?.Subtitle ?? game.Subtitle,
            SteamAppId: game.SteamAppId,
            CoverUrl: game.CoverUrl,
            HeroBannerUrl: game.HeroBannerUrl,
            AccentColor: game.AccentColor,
            EstimatedHours: translatedGame?.EstimatedHours ?? game.EstimatedHours,
            MissablesInfo: translatedGame?.MissablesInfo ?? game.MissablesInfo,
            DifficultyInfo: translatedGame?.DifficultyInfo ?? game.DifficultyInfo,
            GenreTags: translatedGame?.GenreTags ?? game.GenreTags,
            ContentLanguage: AiGuideGeneratorService.GuideLanguage(game),
            SteamGenres: game.SteamGenres,
            TotalCheckableItems: totalCheckable,
            CheckedItemsCount: checkedCount,
            ProgressPercent: pct,
            Tabs: tabDtos,
            CheckedItemKeys: checkedSet.ToList(),
            Language: view.UseTranslation ? view.Language : view.SourceLanguage,
            Translation: view.ToDto(),
            ItemLinks: itemLinks,
            RefTargets: refTargets
        );
    }

    /// <summary>
    /// Stored chapter JSON -> sections. Valid data reads exactly as before; a section with a wrongly typed field is repaired or skipped on its own
    /// (<see cref="GuideSectionReader"/>) instead of emptying the whole chapter.
    /// </summary>
    internal static List<GuideSectionDto> DeserializeSections(string json) => GuideSectionReader.ReadStored(json);
}
