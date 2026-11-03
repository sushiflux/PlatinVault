using System.Globalization;
using Companion.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// Per game the moment up to which this server has taken every unlock into account (row <c>live.seen_through.{appId}</c> in
/// <c>app_settings</c>, UTC, ISO 8601). Written when a baseline is taken and after every announcement, read when a new baseline is
/// taken - so an unlock that happened while the server restarted (e.g. <c>dotnet watch</c> after saving a file) is still announced
/// once. A failure here never stops an announcement; it only means a restart may miss an unlock, and it is logged.
/// </summary>
public sealed class UnlockWatermarkStore(IServiceScopeFactory scopeFactory, ILogger<UnlockWatermarkStore> logger)
{
    public const string KeyPrefix = "live.seen_through.";

    public async Task<DateTimeOffset?> GetAsync(uint appId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var key = KeyPrefix + appId.ToString(CultureInfo.InvariantCulture);
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
            return row is not null && DateTimeOffset.TryParse(row.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at.ToUniversalTime()
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Stand der angekündigten Erfolge für App {AppId} nicht lesbar - nach einem Neustart wird nichts nachgeholt.", appId);
            return null;
        }
    }

    public async Task SetAsync(uint appId, DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var key = KeyPrefix + appId.ToString(CultureInfo.InvariantCulture);
            var value = at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
            if (row is null)
            {
                db.AppSettings.Add(new AppSettingEntity { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Stand der angekündigten Erfolge für App {AppId} nicht gespeichert - ein Neustart könnte eine Freischaltung verpassen.", appId);
        }
    }
}
