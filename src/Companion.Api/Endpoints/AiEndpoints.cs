using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Endpoints;

/// <summary>
/// The AI selection as seen from the outside: GET /api/ai/status (mode, Standard-KI, fallback switch, every provider with its key and
/// state - what the settings, the AI buttons and the chooser dialog show) and POST /api/ai/providers/{id}/reset (forget the
/// remembered "quota used up / key rejected / unavailable" state of one provider).
/// </summary>
public static class AiEndpoints
{
    public static void MapAi(this RouteGroupBuilder api)
    {
        api.MapGet("/ai/status", async (AiRouter router, CancellationToken ct) => Results.Ok(await router.GetStatusAsync(ct)));

        api.MapPost("/ai/providers/{id}/reset", async (string id, AiRouter router, CancellationToken ct) =>
            router.ResetProvider(id)
                ? Results.Ok(await router.GetStatusAsync(ct))
                : Problems.BadRequest(ServerTexts.InvalidRequestTitle, ServerTexts.Ai.UnknownProvider));
    }
}

/// <summary>Turns what an action recorded about its providers into the answer the UI shows.</summary>
public static class AiUsageMapping
{
    public static AiUsageDto ToDto(this AiUsage usage) =>
        new([.. usage.Providers], [.. usage.Switches.Select(s => new AiSwitchDto(s.From, s.To, s.Reason.ToId(), NoticeOf(s, usage.Research)))]);

    /// <summary>"Gemini quota used up – continuing with Claude." in the language of the request.</summary>
    public static string NoticeOf(AiSwitch change, bool research) =>
        ServerTexts.Ai.FallbackNotice(change.From, change.To, change.Reason, noWebSearch: research && !AiProvider.HasWebSearch(change.To)).ToString();
}
