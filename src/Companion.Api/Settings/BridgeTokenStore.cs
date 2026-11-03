using System.Security.Cryptography;
using System.Text;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Settings;

/// <summary>
/// The token for the realtime bridge ("PlatinVault Bridge", PlatinVault.Bridge.exe on the gaming PC). Only its SHA-256 hash is stored, so a
/// database leak does not expose a working token; the plain token is returned exactly once, when it is created.
/// Lives in <c>app_settings</c> (keys bridge.*) - the app is single-user, so there is one token for the instance.
/// </summary>
public sealed class BridgeTokenStore(CompanionDbContext db)
{
    public const string KeyTokenHash = "bridge.token_hash";
    public const string KeyTokenHint = "bridge.token_hint";
    public const string KeyLastSeenAt = "bridge.last_seen_at";

    // 32 random bytes in base64url are 43 characters
    private const int MinTokenLength = 20;
    private const int MaxTokenLength = 200;

    private static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(20);
    private static long _lastTouchTicks;

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<BridgeTokenStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var rows = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key == KeyTokenHash || x.Key == KeyTokenHint || x.Key == KeyLastSeenAt)
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        var hasToken = rows.TryGetValue(KeyTokenHash, out var hash) && !string.IsNullOrEmpty(hash);
        DateTimeOffset? lastSeen = rows.TryGetValue(KeyLastSeenAt, out var seen) &&
                                   DateTimeOffset.TryParse(seen, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
        return new BridgeTokenStatusDto(hasToken, hasToken ? rows.GetValueOrDefault(KeyTokenHint) : null, hasToken ? lastSeen : null);
    }

    /// <summary>Creates a new token; a previous one stops working immediately.</summary>
    public async Task<BridgeTokenCreatedDto> CreateAsync(CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hint = token[^4..];

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UpsertAsync(KeyTokenHash, HashToken(token), ct);
        await UpsertAsync(KeyTokenHint, hint, ct);
        await db.AppSettings.Where(x => x.Key == KeyLastSeenAt).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return new BridgeTokenCreatedDto(token, hint);
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        await db.AppSettings
            .Where(x => x.Key == KeyTokenHash || x.Key == KeyTokenHint || x.Key == KeyLastSeenAt)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>True when the Authorization header carries the current bridge token. The hash comparison is constant-time.</summary>
    public async Task<bool> AuthenticateAsync(string? authorizationHeader, CancellationToken ct = default)
    {
        const string prefix = "Bearer ";
        if (authorizationHeader is null || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorizationHeader[prefix.Length..].Trim();
        var storedHash = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key == KeyTokenHash)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(ct);

        // Compare something even when no token exists or the input is malformed, so timing reveals nothing
        var wellFormed = token.Length is >= MinTokenLength and <= MaxTokenLength;
        var presented = Encoding.UTF8.GetBytes(wellFormed ? HashToken(token) : new string('0', 64));
        var expected = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(storedHash) ? new string('1', 64) : storedHash);
        var equal = presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected);
        return equal && wellFormed && !string.IsNullOrEmpty(storedHash);
    }

    /// <summary>Stamps "last seen" in the database; at most every <see cref="TouchInterval"/> so a busy bridge does not write on every push.</summary>
    public async Task TouchThrottledAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastTouchTicks);
        if (now - last < TouchInterval.Ticks || Interlocked.CompareExchange(ref _lastTouchTicks, now, last) != last)
        {
            return;
        }

        await UpsertAsync(KeyLastSeenAt, DateTimeOffset.UtcNow.ToString("O"), ct);
    }

    // INSERT ... ON CONFLICT is atomic, so concurrent pushes can never collide on the primary key
    private Task<int> UpsertAsync(string key, string value, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO app_settings ("Key", "Value", "UpdatedAt") VALUES ({key}, {value}, {DateTimeOffset.UtcNow})
            ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            ct);
}
