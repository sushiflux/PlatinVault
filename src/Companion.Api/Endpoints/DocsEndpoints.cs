using System.Security.Cryptography;
using System.Text;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.Net.Http.Headers;

namespace Companion.Api.Endpoints;

/// <summary>
/// Documents that ship with the product. GET /api/docs/handbook?lang=de|en serves the end-user handbook as PDF, inline, so the
/// browser shows it right away. GET /api/docs/licenses?file=license|notices serves the licence (the GNU AGPL v3, plain
/// text) and the third-party notices (Markdown); the app shows them in its "Lizenzen" viewer. GET only - no state changes, so no
/// X-Requested-With header is needed.
/// </summary>
public static class DocsEndpoints
{
    public static void MapDocs(this RouteGroupBuilder api)
    {
        api.MapGet("/docs/handbook", (string? lang, HandbookFiles handbooks, HttpContext http) =>
        {
            var normalized = HandbookFiles.NormalizeLanguage(lang);
            var handbook = handbooks.Find(normalized);
            if (handbook is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.Docs.HandbookNotFoundTitle, ServerTexts.Docs.HandbookNotFoundDetail(normalized));
            }

            var info = new FileInfo(handbook.Path);
            var disposition = new ContentDispositionHeaderValue("inline");
            disposition.SetHttpFileName(handbook.FileName);
            http.Response.Headers.ContentDisposition = disposition.ToString();
            // Revalidate on every request (ETag below): a regenerated PDF shows up at once, an unchanged one costs a 304
            http.Response.Headers.CacheControl = "no-cache";
            // The app shows the handbook in its own viewer (iframe): allow framing by the app itself only, not by other sites
            http.Response.Headers.XFrameOptions = "SAMEORIGIN";
            http.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'self'";

            var etag = new EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"");
            return Results.File(handbook.Path, "application/pdf", fileDownloadName: null, lastModified: info.LastWriteTimeUtc, entityTag: etag, enableRangeProcessing: true);
        });

        api.MapGet("/docs/licenses", (string? file, LicenseFiles licenses, HttpContext http) =>
        {
            if (!LicenseFiles.IsKnownKey(file))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Docs.LicenseUnknownTitle, ServerTexts.Docs.LicenseUnknownDetail(file ?? string.Empty));
            }

            var document = licenses.Find(file);
            if (document is null)
            {
                var isNotices = string.Equals(file?.Trim(), "notices", StringComparison.OrdinalIgnoreCase);
                return Problems.Create(
                    StatusCodes.Status404NotFound,
                    ServerTexts.Docs.LicenseNotFoundTitle,
                    isNotices ? ServerTexts.Docs.NoticesNotFoundDetail : ServerTexts.Docs.LicenseNotFoundDetail);
            }

            // Notes to the owner (HTML comments) are stripped; the text is small (a few hundred KB at most), so it is read per request
            var text = LicenseFiles.ReadShippedText(document.Path);
            var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16]}\"";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.ETag = etag;
            if (http.Request.Headers.IfNoneMatch == etag)
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
            return Results.Text(text, document.IsMarkdown ? "text/markdown" : "text/plain", Encoding.UTF8);
        });
    }
}
