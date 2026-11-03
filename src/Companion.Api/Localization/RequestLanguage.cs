using Companion.Api.Data;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Localization;

/// <summary>
/// Decides the language of one API request and makes it the ambient <see cref="Lang.Current"/>:
/// 1. the <c>Accept-Language</c> header (the frontend sends its active UI language with every call, so a language switch is
///    effective before the setting is saved),
/// 2. else the stored <c>ui.language</c> setting (curl, the bridge, browser navigations without the header),
/// 3. else German.
/// Calls that are plain browser navigations (the Steam login round trip) skip step 1: a browser's own Accept-Language says
/// nothing about the language the user picked in the app.
/// </summary>
public static class RequestLanguage
{
    private static readonly TimeSpan StoredLifetime = TimeSpan.FromSeconds(5);
    private static readonly Lock StoredLock = new();
    private static (string? Language, DateTime FetchedAt)? _stored;

    /// <summary>Forget the cached stored language (call after it was saved).</summary>
    public static void InvalidateStored()
    {
        lock (StoredLock)
        {
            _stored = null;
        }
    }

    public static IApplicationBuilder UseRequestLanguage(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/hubs"))
            {
                await next();
                return;
            }

            var isNavigation = path.StartsWithSegments("/api/steam/openid");
            var language = (isNavigation ? null : Lang.FromAcceptLanguage(context.Request.Headers.AcceptLanguage.ToString()))
                           ?? await GetStoredAsync(context.RequestServices, context.RequestAborted)
                           ?? Lang.German;

            using (Lang.Use(language))
            {
                await next();
            }
        });

    /// <summary>The stored <c>ui.language</c>, or null when none is chosen yet or the database cannot be read. Cached for a few seconds.</summary>
    public static async Task<string?> GetStoredAsync(IServiceProvider services, CancellationToken ct)
    {
        lock (StoredLock)
        {
            if (_stored is { } cached && DateTime.UtcNow - cached.FetchedAt < StoredLifetime)
            {
                return cached.Language;
            }
        }

        try
        {
            var db = services.GetRequiredService<CompanionDbContext>();
            var value = await db.AppSettings.AsNoTracking()
                .Where(x => x.Key == SettingsStore.KeyUiLanguage)
                .Select(x => x.Value)
                .FirstOrDefaultAsync(ct);
            var language = SettingsStore.NormalizeUiLanguage(value);
            lock (StoredLock)
            {
                _stored = (language, DateTime.UtcNow);
            }
            return language;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No database (starting up, down): German texts are better than a failing language lookup
            return null;
        }
    }
}
