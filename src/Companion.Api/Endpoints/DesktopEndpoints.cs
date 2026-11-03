using Companion.Api.Desktop;
using Companion.Api.Localization;

namespace Companion.Api.Endpoints;

/// <summary>One entry of <c>GET /api/desktop/folders</c>: <c>Kind</c> is "program" | "data" | "backups" | "logs".</summary>
public sealed record DesktopFolderDto(string Kind, string Path);

/// <summary>Answer of <c>GET /api/desktop/folders</c>.</summary>
public sealed record DesktopFoldersResponse(IReadOnlyList<DesktopFolderDto> Folders);

/// <summary>
/// Endpoints that only exist in the Windows program (the host registers <see cref="IDesktopHost"/>): the folders of the program, which the
/// settings show and open in Windows Explorer. In a container or the browser dev stack they answer 404. Inside the <c>/api</c> group, so the
/// POST needs the X-Requested-With header like every state-changing call. The path to open is NEVER taken from the request: the request
/// names one of four known kinds and the host resolves the path itself.
/// </summary>
public static class DesktopEndpoints
{
    public static void MapDesktop(this RouteGroupBuilder api)
    {
        api.MapGet("/desktop/folders", (IServiceProvider services) =>
        {
            if (services.GetService<IDesktopHost>() is not { } desktop) return NotDesktop();
            var folders = desktop.Folders
                .Where(f => DesktopFolderKinds.IsKnown(f.Kind))
                .Select(f => new DesktopFolderDto(f.Kind, f.Path))
                .ToList();
            return Results.Ok(new DesktopFoldersResponse(folders));
        });

        api.MapPost("/desktop/folders/{kind}/open", (string kind, IServiceProvider services) =>
        {
            if (services.GetService<IDesktopHost>() is not { } desktop) return NotDesktop();
            if (!DesktopFolderKinds.IsKnown(kind))
            {
                return Problems.BadRequest(ServerTexts.Desktop.OpenFolderTitle, ServerTexts.Desktop.UnknownFolder(kind));
            }
            // Only a folder the host itself lists can be opened
            if (!desktop.Folders.Any(f => string.Equals(f.Kind, kind, StringComparison.Ordinal)))
            {
                return Problems.NotFound(ServerTexts.Desktop.OpenFolderTitle, ServerTexts.Desktop.FolderNotAvailable(kind));
            }
            var result = desktop.OpenFolder(kind);
            return result.Opened
                ? Results.NoContent()
                : Problems.Create(StatusCodes.Status500InternalServerError, ServerTexts.Desktop.OpenFolderTitle, ServerTexts.Desktop.OpenFailed(kind, result.Detail ?? "?"));
        });
    }

    private static IResult NotDesktop() => Problems.NotFound(ServerTexts.Desktop.NotDesktopTitle, ServerTexts.Desktop.NotDesktop);
}
