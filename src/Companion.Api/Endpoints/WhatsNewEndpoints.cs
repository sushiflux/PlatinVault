using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Endpoints;

/// <summary>
/// "What's new" after an update (rules: <see cref="WhatsNew"/>). <c>GET /api/whats-new</c> answers <c>{ version, show, notes }</c> from the
/// changelog that ships with the product (never GitHub: it must work offline), in the language of the request.
/// <c>POST /api/whats-new/seen</c> marks the running version as shown; inside the <c>/api</c> group, so it needs the X-Requested-With header
/// like every state-changing call.
/// </summary>
public static class WhatsNewEndpoints
{
    public static void MapWhatsNew(this RouteGroupBuilder api)
    {
        api.MapGet("/whats-new", async (CompanionDbContext db, AppVersion version, ChangelogFiles changelog, CancellationToken ct) =>
        {
            // Not cached: the answer changes the moment "seen" is posted, and the file is small
            var dto = await WhatsNew.GetAsync(db, version.Running, changelog.ReadText(), Lang.Current, ct);
            return Results.Ok(dto);
        });

        api.MapPost("/whats-new/seen", async (CompanionDbContext db, AppVersion version, CancellationToken ct) =>
        {
            await WhatsNew.MarkSeenAsync(db, version.Running, ct);
            return Results.NoContent();
        });
    }
}
