using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Endpoints;

/// <summary>
/// Problem-JSON answers with localized texts (the language was decided by <see cref="RequestLanguage"/>; a <see cref="Msg"/> turns
/// into text in that language when it is passed as a string). Every error of the API has the shape <c>{ title, detail, status }</c>;
/// AI errors add <c>providerMessage</c> (the provider's own text, see <see cref="AiProviderException"/>) and nothing else.
/// </summary>
internal static class Problems
{
    public static IResult Create(int status, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status);

    public static IResult NotFound(string title, string detail) => Create(StatusCodes.Status404NotFound, title, detail);

    public static IResult BadRequest(string title, string detail) => Create(StatusCodes.Status400BadRequest, title, detail);

    public static IResult GameNotFound(string slug) => NotFound(ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));

    /// <summary>
    /// Answer for a failed AI action: detail = our explanation, providerMessage = the provider's raw text (only when there is one).
    /// Other exceptions (a bad answer format, a missing key) have no provider text and give a plain problem.
    /// </summary>
    public static IResult Ai(string title, Exception exception, int status = StatusCodes.Status400BadRequest)
    {
        var extensions = new Dictionary<string, object?>();
        if (exception is AiProviderException ai)
        {
            // aiProvider / aiFailure: which provider failed and why, so the UI can offer "try again with <another AI>"
            if (ai.ProviderMessage is { } providerMessage) extensions["providerMessage"] = providerMessage;
            if (ai.Provider is { } provider) extensions["aiProvider"] = provider;
            extensions["aiFailure"] = ai.Kind.ToId();
        }
        return Results.Problem(title: title, detail: exception.Message, statusCode: status, extensions: extensions.Count > 0 ? extensions : null);
    }
}
