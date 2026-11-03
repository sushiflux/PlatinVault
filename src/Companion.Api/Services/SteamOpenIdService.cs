using System.Text.RegularExpressions;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// "Mit Steam anmelden" via Steam's OpenID 2.0 provider (https://partner.steamgames.com/doc/features/auth).
/// Steam only confirms the user's SteamID64 - it hands out no access token - so the Steam Web API key stays necessary
/// for library and achievement data. Every assertion is verified server-side with check_authentication.
/// </summary>
public sealed partial class SteamOpenIdService(IHttpClientFactory httpClientFactory, ILogger<SteamOpenIdService> logger)
{
    public const string StateCookieName = "pv_steam_openid";
    public const string CallbackPath = "/api/steam/openid/callback";

    private const string ProviderEndpoint = "https://steamcommunity.com/openid/login";
    private const string OpenIdNamespace = "http://specs.openid.net/auth/2.0";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";
    private static readonly string[] RequiredSignedFields = ["op_endpoint", "claimed_id", "identity", "return_to", "response_nonce"];

    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/(\d{17})$")]
    private static partial Regex ClaimedIdPattern();

    public static string BuildReturnTo(string origin, string state) =>
        $"{origin}{CallbackPath}?state={Uri.EscapeDataString(state)}";

    public static string BuildLoginUrl(string origin, string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["openid.ns"] = OpenIdNamespace,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = BuildReturnTo(origin, state),
            ["openid.realm"] = origin,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect
        };
        return ProviderEndpoint + "?" + string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    /// <summary>Validates Steam's positive assertion and returns the SteamID64, or an error text for the user.</summary>
    public async Task<(string? SteamId64, string? Error)> VerifyAsync(IQueryCollection query, string expectedReturnTo, CancellationToken ct)
    {
        if (query["openid.mode"] != "id_res")
        {
            return (null, ServerTexts.Steam.OpenIdCancelled);
        }

        if (query["openid.op_endpoint"] != ProviderEndpoint || query["openid.return_to"] != expectedReturnTo)
        {
            return (null, ServerTexts.Steam.OpenIdMismatch);
        }

        var signed = query["openid.signed"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (RequiredSignedFields.Any(field => !signed.Contains(field)))
        {
            return (null, ServerTexts.Steam.OpenIdNotSigned);
        }

        var match = ClaimedIdPattern().Match(query["openid.claimed_id"].ToString());
        if (!match.Success)
        {
            return (null, ServerTexts.Steam.OpenIdNoSteamId);
        }

        var form = query
            .Where(p => p.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value.ToString());
        form["openid.mode"] = "check_authentication";

        try
        {
            var client = httpClientFactory.CreateClient(SteamClient.HttpClientName);
            using var response = await client.PostAsync(ProviderEndpoint, new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var valid = response.IsSuccessStatusCode &&
                body.Split('\n').Any(line => line.Trim() == "is_valid:true");
            return valid
                ? (match.Groups[1].Value, null)
                : (null, ServerTexts.Steam.OpenIdNotConfirmed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Steam OpenID check_authentication fehlgeschlagen.");
            return (null, ServerTexts.Steam.OpenIdUnreachable);
        }
    }
}
