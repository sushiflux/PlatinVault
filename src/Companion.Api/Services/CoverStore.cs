using System.Security.Cryptography;
using Companion.Api.Data;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>The verdict on an uploaded image: either the content type to store, or why it was refused (with the HTTP status to answer).</summary>
public sealed record CoverCheck(string? ContentType, Msg? Error, int Status)
{
    public bool Ok => ContentType is not null;
}

/// <summary>
/// Uploaded covers of games, stored in the table <c>game_covers</c> (decision D1: in the database, so every SQLite backup contains them;
/// never files in the data folder). Served at <see cref="UrlFor"/>. Scoped (one per request, like the database context).
/// An upload is accepted only when the declared type is PNG, JPEG, WebP or GIF, the first bytes of the file really are that type (a script or
/// an SVG renamed to an image never gets through) and it is not larger than <see cref="MaxBytes"/>.
/// </summary>
public sealed class CoverStore(CompanionDbContext db)
{
    /// <summary>Largest accepted image (5 MB).</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Accepted image types (never SVG: it can carry script).</summary>
    public static readonly IReadOnlyList<string> ContentTypes = ["image/png", "image/jpeg", "image/webp", "image/gif"];

    /// <summary>Start of every address of an uploaded cover.</summary>
    public const string UrlPrefix = "/api/covers/";

    /// <summary>The address of a game's uploaded cover; <c>v</c> changes with every upload, so the long browser cache never shows an old image.</summary>
    public static string UrlFor(Guid gameId, DateTimeOffset updatedAt) => $"{UrlPrefix}{gameId:N}?v={updatedAt.UtcTicks}";

    /// <summary>Whether an address points at an uploaded cover of this installation (the guide export drops such addresses).</summary>
    public static bool IsLocalCoverUrl(string? url) => url is not null && url.StartsWith(UrlPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The stored cover of a game, null when it has none.</summary>
    public Task<GameCoverEntity?> FindAsync(Guid gameId, CancellationToken ct) =>
        db.GameCovers.AsNoTracking().FirstOrDefaultAsync(c => c.GameId == gameId, ct);

    // ---- Checking an upload --------------------------------------------------------------------------------------------

    /// <summary>
    /// The content type of an image by its first bytes (PNG, JPEG, GIF, WebP); null for anything else (SVG, HTML, text, a truncated file).
    /// The declared type of a request is never trusted on its own.
    /// </summary>
    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    /// <summary>The declared Content-Type without parameters, lower case ("image/jpg" counts as "image/jpeg"); null when there is none.</summary>
    public static string? NormalizeContentType(string? declared)
    {
        var type = declared?.Split(';')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
        {
            return null;
        }
        return type == "image/jpg" ? "image/jpeg" : type;
    }

    /// <summary>
    /// Decides whether <paramref name="data"/> may be stored as a cover. Refusals: nothing sent (400), a type that is not on the list or no type
    /// at all - SVG included (415), a file that is too large (413), a file whose first bytes do not match the declared type (400).
    /// </summary>
    public static CoverCheck Check(string? declaredContentType, ReadOnlySpan<byte> data, bool tooLarge = false)
    {
        var type = NormalizeContentType(declaredContentType);
        if (type is null || !ContentTypes.Contains(type))
        {
            return new CoverCheck(null, ServerTexts.ManualGames.CoverTypeUnsupported(type), StatusCodes.Status415UnsupportedMediaType);
        }
        if (tooLarge || data.Length > MaxBytes)
        {
            return new CoverCheck(null, ServerTexts.ManualGames.CoverTooLarge(MaxBytes / (1024 * 1024)), StatusCodes.Status413PayloadTooLarge);
        }
        if (data.Length == 0)
        {
            return new CoverCheck(null, ServerTexts.ManualGames.CoverEmpty, StatusCodes.Status400BadRequest);
        }
        return Sniff(data) == type
            ? new CoverCheck(type, null, StatusCodes.Status200OK)
            : new CoverCheck(null, ServerTexts.ManualGames.CoverContentMismatch(type), StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Reads at most <see cref="MaxBytes"/> bytes of a request body. <c>TooLarge</c> = the body is bigger (reading stops one byte past the limit,
    /// so a huge upload never fills memory).
    /// </summary>
    public static async Task<(byte[] Data, bool TooLarge)> ReadBodyAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await body.ReadAsync(chunk, ct);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaxBytes)
            {
                return ([], true);
            }
            buffer.Write(chunk, 0, read);
        }
        return (buffer.ToArray(), false);
    }

    // ---- Storing -------------------------------------------------------------------------------------------------------

    /// <summary>Stores (or replaces) the cover of a game and returns its row; <paramref name="now"/> is the version in the address.</summary>
    public async Task<GameCoverEntity> SaveAsync(Guid gameId, string contentType, byte[] data, DateTimeOffset now, CancellationToken ct)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(data));
        now = now.ToUniversalTime();
        for (var attempt = 0; ; attempt++)
        {
            var row = await db.GameCovers.FirstOrDefaultAsync(c => c.GameId == gameId, ct);
            if (row is null)
            {
                row = new GameCoverEntity { GameId = gameId };
                db.GameCovers.Add(row);
            }
            row.ContentType = contentType;
            row.Data = data;
            row.Sha256 = sha;
            row.UpdatedAt = now;
            try
            {
                await db.SaveChangesAsync(ct);
                return row;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // A second server created the row at the same moment: read it again and replace its content
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Removes the uploaded cover of a game; false when it had none.</summary>
    public async Task<bool> DeleteAsync(Guid gameId, CancellationToken ct) =>
        await db.GameCovers.Where(c => c.GameId == gameId).ExecuteDeleteAsync(ct) > 0;
}
