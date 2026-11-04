using System.Threading.RateLimiting;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Desktop;
using Companion.Api.Endpoints;
using Companion.Api.Hubs;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Companion.Api;

/// <summary>How a host starts the PlatinVault server. The defaults are those of the plain server (Docker, Unraid, dev).</summary>
public sealed class CompanionHostOptions
{
    public string[] Args { get; init; } = [];

    /// <summary>Content root (null: the default of ASP.NET Core = the working directory / app folder).</summary>
    public string? ContentRootPath { get; init; }

    /// <summary>Folder of the built React app (null: <c>wwwroot</c> under the content root).</summary>
    public string? WebRootPath { get; init; }

    /// <summary>"Production", "Development" ... (null: from ASPNETCORE_ENVIRONMENT).</summary>
    public string? EnvironmentName { get; init; }

    /// <summary>
    /// Runs after the server's own registrations: the Windows program uses it for its configuration (data folder, database,
    /// loopback address) and to register <see cref="IDesktopHost"/>.
    /// </summary>
    public Action<WebApplicationBuilder>? ConfigureBuilder { get; init; }
}

/// <summary>
/// The PlatinVault server as a reusable host: <c>Program.cs</c> (Docker, Unraid, development) and the Windows program
/// (<c>PlatinVault.Desktop</c>, which runs the server in its own process) build exactly the same app from here.
/// </summary>
public static class CompanionHost
{
    public static WebApplicationBuilder CreateBuilder(CompanionHostOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = options.Args,
            ContentRootPath = options.ContentRootPath,
            WebRootPath = options.WebRootPath,
            EnvironmentName = options.EnvironmentName,
            // The same name for every host, so nothing depends on which exe started the server
            ApplicationName = typeof(CompanionHost).Assembly.GetName().Name,
        });

        // The host's own settings first: the data folder decides where the key ring below lives
        options.ConfigureBuilder?.Invoke(builder);
        AddServices(builder);
        return builder;
    }

    private static void AddServices(WebApplicationBuilder builder)
    {
        var branding = Branding.Load(AppContext.BaseDirectory);
        builder.Services.AddSingleton(branding);
        builder.Services.AddSingleton<AppVersion>();

        builder.Services.Configure<CompanionOptions>(builder.Configuration.GetSection(CompanionOptions.Section));

        var companionOpts = builder.Configuration.GetSection(CompanionOptions.Section).Get<CompanionOptions>() ?? new CompanionOptions();
        var dataProtectionKeysDir = companionOpts.ResolvedKeyRingDir;
        try
        {
            Directory.CreateDirectory(dataProtectionKeysDir);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Operator-facing: the usual cause is a data folder owned by another user (container PUID/PGID, Unraid appdata)
            throw new InvalidOperationException(
                $"Kein Schreibzugriff auf den Datenordner '{companionOpts.DataDir}'. Der Ordner muss dem Benutzer gehoeren, unter dem " +
                "PlatinVault laeuft (Docker/Unraid: PUID/PGID, Standard 99/100).", ex);
        }

        var dataProtection = builder.Services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir))
            .SetApplicationName(SecretStore.ApplicationName);
        // Our own key folder switches off .NET's encryption of the key ring at rest: DPAPI on Windows, owner-only folders elsewhere
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }
        builder.Services.AddSingleton(new KeyRingFolder(dataProtectionKeysDir, KeyRingProtection.RestrictToOwner(dataProtectionKeysDir)));

        // One SQLite file (Companion:Database:Path, default <DataDir>/platinvault.db), WAL, busy timeout, foreign keys - see SqliteDatabase
        var connectionSetup = new SqliteConnectionSetup();
        builder.Services.AddDbContext<CompanionDbContext>((sp, options) =>
        {
            var opts = sp.GetRequiredService<IOptions<CompanionOptions>>().Value;
            options.UseSqlite(SqliteDatabase.ConnectionString(opts.DatabaseFile), sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            options.AddInterceptors(connectionSetup);
        });
        builder.Services.AddSingleton(DatabaseCommands.CreateBackupService);
        builder.Services.AddHostedService<DatabaseBackupWorker>();

        builder.Services.AddHttpClient(SteamClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PlatinVault/1.0 (+https://sushiflux.de)");
        });

        builder.Services.AddHttpClient("ai", client =>
        {
            // A provider can take minutes for a long chapter or when it is busy; 3 minutes cut off answers that were still coming
            client.Timeout = TimeSpan.FromMinutes(6);
        });

        // Twitch (token, live streams) and IGDB share one client: both sign in with the same Twitch developer app
        builder.Services.AddHttpClient(TwitchAuthService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PlatinVault/1.0 (+https://sushiflux.de)");
        });

        // Update check against the public repository's GitHub releases (Settings > System > Updates, the Windows program's daily check)
        builder.Services.AddHttpClient(UpdateService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(UpdateService.TimeoutSeconds);
        });
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<UpdateService>();

        builder.Services.AddSignalR();

        builder.Services.AddSingleton<LiveEventFeed>();
        builder.Services.AddSingleton(_ => new LiveEventLog());
        builder.Services.AddSingleton<LiveConnectionCounter>();
        builder.Services.AddSingleton<LiveAnnouncer>();
        builder.Services.AddSingleton<UnlockWatermarkStore>();
        builder.Services.AddSingleton<SecretStore>();
        // Where the Steam client of this computer lives: Companion:Steam:Root, the setting "Steam-Ordner" (applied after the migration,
        // see BuildAsync, and kept in sync by SettingsStore), the environment and the platform's own search. TryAdd: tests bring their own.
        builder.Services.TryAddSingleton(sp => new SteamLocationProvider(
            new SteamLocatorInput { ConfiguredRoot = sp.GetRequiredService<IOptions<CompanionOptions>>().Value.Steam.Root },
            sp.GetRequiredService<ILogger<SteamLocationProvider>>()));
        builder.Services.AddSingleton<SteamLocalStatsService>();
        builder.Services.AddSingleton<SteamOpenIdService>();
        builder.Services.AddSingleton<TranslationService>();
        builder.Services.AddSingleton<SteamLiveTrackerService>();
        builder.Services.AddSingleton<BridgeIngestService>();
        builder.Services.AddSingleton<BridgeBundle>();
        builder.Services.AddSingleton<BridgeCommandQueue>();
        builder.Services.AddSingleton<HandbookFiles>();
        builder.Services.AddSingleton<LicenseFiles>();
        builder.Services.AddSingleton<LibraryStatsService>();
        builder.Services.AddSingleton<SteamLocalLibraryService>();
        builder.Services.AddSingleton<LibraryMetaService>();
        builder.Services.AddSingleton<GuideTranslationService>();
        builder.Services.AddSingleton<TwitchAuthService>();
        builder.Services.AddSingleton<IgdbService>();
        builder.Services.AddSingleton<TwitchStreamsService>();
        builder.Services.AddSingleton<TwitchLiveCountService>();
        builder.Services.AddHostedService<SteamMonitorWorker>();
        builder.Services.AddHostedService<HistoryRecorderWorker>();

        builder.Services.AddScoped<SettingsStore>();
        builder.Services.AddScoped<BridgeTokenStore>();
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<SteamClient>();
        builder.Services.AddScoped<SteamStatsService>();
        // Which AI provider does the work: the memory of failed providers is shared by all requests, the router is per request
        builder.Services.AddSingleton<AiProviderHealth>();
        builder.Services.AddSingleton<AiJobService>();
        builder.Services.AddScoped<AiRouter>();
        builder.Services.AddScoped<AiGuideGeneratorService>();

        builder.Services.AddProblemDetails();

        // Built-in rate limiter, partitioned by client IP, no queueing: an over-limit request gets an immediate 429.
        // "bridge": the realtime bridge pushes on every unlock plus a heartbeat, so 20 per second is generous for it and
        // still stops a flood from a client that guesses tokens.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                // Runs after UseRequestLanguage, so the text follows the language of the request
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        title = ServerTexts.TooManyRequestsTitle.ToString(),
                        status = 429,
                        detail = ServerTexts.TooManyRequestsDetail.ToString()
                    }),
                    ct);
            };
            options.AddPolicy("bridge", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromSeconds(1),
                    QueueLimit = 0
                }));
        });
    }

    /// <summary>Builds the app: middleware, database migrations and every endpoint. The caller runs or starts it.</summary>
    public static async Task<WebApplication> BuildAsync(WebApplicationBuilder builder, CancellationToken ct = default)
    {
        var app = builder.Build();
        GuideSectionReader.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Companion.Api.GuideSections");

        // Before anything uses DataProtection: move key files from before DPAPI onto a protected key (Windows, once)
        var keyRing = app.Services.GetRequiredService<KeyRingFolder>();
        KeyRingProtection.MigrateUnprotectedKeys(app.Services.GetRequiredService<SecretStore>(), keyRing.Path, app.Logger);
        if (!keyRing.Restricted)
        {
            app.Logger.LogWarning("Rechte des Schlüsselordners {Dir} konnten nicht auf den App-Benutzer beschränkt werden", keyRing.Path);
        }

        // Add baseline security headers for local and Unraid deployment
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();
        // Language of the request (Accept-Language, else the stored app language) - before everything that produces texts
        app.UseRequestLanguage();
        app.UseRateLimiter();

        // Check the database file and bring its schema up to date (EF Core migrations) before serving requests. A damaged file or a
        // database of a newer PlatinVault stops the start (nothing is changed); the app is disposed first so the explanation in the
        // log is flushed.
        var companion = app.Services.GetRequiredService<IOptions<CompanionOptions>>().Value;
        try
        {
            try
            {
                await SqliteDatabase.PrepareAsync(companion.DatabaseFile, app.Logger, ct);
                await DatabaseInitializer.MigrateAsync(app.Services, app.Logger, ct);
            }
            catch (Exception ex) when (ex is not DatabaseRefusedException and not OperationCanceledException && SqliteDatabase.IsCannotOpen(ex))
            {
                // SQLite error 14 (file or helper files cannot be opened/created): a plain explanation instead of the bare SQLite text
                throw SqliteDatabase.CannotOpen(companion.DatabaseFile, ex, app.Logger);
            }
        }
        catch (DatabaseRefusedException)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await app.DisposeAsync();
            throw;
        }

        // The Steam folder set by hand in the settings, before the live detection asks for the first time
        await SettingsStore.ApplyStoredSteamRootAsync(app.Services, ct);

        // After the last hosted service stopped (the backup worker made its last backup): close the pooled SQLite connections, so SQLite
        // folds the WAL back into the file and removes it - the file is complete on its own once the server is gone
        app.Lifetime.ApplicationStopped.Register(Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools);

        app.MapGet("/health", async (CompanionDbContext db, AppVersion version, CancellationToken ct) =>
        {
            var dbOk = await db.Database.CanConnectAsync(ct);
            return dbOk
                ? Results.Ok(new { status = "ok", version = version.Current, database = "connected" })
                : Results.Json(new { status = "degraded", version = version.Current, database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        app.MapGet("/api/branding", (Branding b, AppVersion v, IServiceProvider services) => Results.Ok(new
        {
            productName = b.ProductName,
            tagline = b.Tagline,
            company = b.Company,
            // The public source code (AGPL-3.0-only, section 13): the licence viewer links to it
            sourceUrl = b.Source,
            version = v.Current,
            // "desktop" = the Windows program shows this UI in its own window and reads Steam on this PC (no bridge needed)
            host = services.GetService<IDesktopHost>() is null ? "server" : "desktop"
        }));

        app.MapGet("/api/version", (AppVersion v) => Results.Ok(new { version = v.Current }));

        app.MapHub<AchievementHub>("/hubs/achievements");
        app.MapCompanionApi();

        // SPA fallback for React client-side routes
        app.MapFallbackToFile("index.html");

        return app;
    }
}

/// <summary>Where the Data Protection key ring lives and whether its folder could be limited to the app user.</summary>
internal sealed record KeyRingFolder(string Path, bool Restricted);
