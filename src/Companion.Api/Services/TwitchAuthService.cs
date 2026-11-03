using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;

namespace Companion.Api.Services;

/// <summary>
/// A problem with Twitch or IGDB. The message is ready to show, never contains credentials, and is resolved in the language of whoever
/// reads it (<see cref="Message"/>) - so a failure remembered by the token cache is still shown in the language of the next request.
/// </summary>
public sealed class TwitchApiException(Msg text, Exception? inner = null, bool isRateLimit = false) : Exception(text.De, inner)
{
    public Msg Text { get; } = text;

    /// <summary>Twitch answered 429: its request bucket is empty (https://dev.twitch.tv/docs/api/guide/#twitch-rate-limits).</summary>
    public bool IsRateLimit { get; } = isRateLimit;

    public override string Message => Text.ToString();
}

public sealed record TwitchCredentials(string ClientId, string ClientSecret);

public sealed record TwitchToken(string AccessToken, int ExpiresInSeconds);

/// <summary>State of the stored credentials for the system check.</summary>
public sealed record TwitchCredentialCheck(bool HasCredentials, bool TokenOk, string? Error);

/// <summary>
/// App access token of the Twitch developer app (OAuth client credentials grant,
/// https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/#client-credentials-grant-flow). One token serves both the
/// Twitch Helix API (live streams) and IGDB (game facts) - IGDB signs in with the same Twitch app.
/// The token is cached until shortly before it expires and replaced when a call comes back with 401. A failed token request
/// is remembered for a short time so a wrong secret does not hit id.twitch.tv on every request.
/// </summary>
public sealed class TwitchAuthService(IHttpClientFactory httpClientFactory, SecretStore secrets, ILogger<TwitchAuthService> logger)
{
    public const string HttpClientName = "twitch";
    public const string IgdbBaseUrl = "https://api.igdb.com/v4/";
    public const string HelixBaseUrl = "https://api.twitch.tv/helix/";
    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";

    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromHours(1);

    private sealed record CachedToken(TwitchCredentials Credentials, string AccessToken, DateTimeOffset RefreshAt);
    private sealed record CachedFailure(TwitchCredentials Credentials, Msg Message, DateTimeOffset RetryAt);

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private CachedToken? _token;
    private CachedFailure? _failure;
    private DateTimeOffset _lastFailureLog = DateTimeOffset.MinValue;

    /// <summary>The stored Client-ID and Client-Secret, or null while either is missing.</summary>
    public TwitchCredentials? GetStoredCredentials()
    {
        var id = secrets.GetSecret(SettingsStore.SecretTwitchClientId);
        var secret = secrets.GetSecret(SettingsStore.SecretTwitchClientSecret);
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : new TwitchCredentials(id, secret);
    }

    public bool HasCredentials => GetStoredCredentials() is not null;

    /// <summary>
    /// Sends one request to Twitch or IGDB with the stored credentials. <paramref name="build"/> creates the request from the
    /// Client-ID and the access token (it is called again for the single retry after a 401). <paramref name="beforeSend"/>
    /// runs before every send, e.g. to keep to IGDB's rate limit. The caller disposes the response.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        Func<TwitchCredentials, string, HttpRequestMessage> build,
        Func<CancellationToken, Task>? beforeSend,
        CancellationToken ct)
    {
        var credentials = GetStoredCredentials()
            ?? throw new TwitchApiException(ServerTexts.Twitch.NoCredentials);

        var client = httpClientFactory.CreateClient(HttpClientName);
        string? rejectedToken = null;
        for (var attempt = 0; ; attempt++)
        {
            var accessToken = await GetAccessTokenAsync(credentials, rejectedToken, ct);
            if (beforeSend is not null)
            {
                await beforeSend(ct);
            }

            HttpResponseMessage response;
            try
            {
                using var request = build(credentials, accessToken);
                response = await client.SendAsync(request, ct);
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                throw new TwitchApiException(ServerTexts.Twitch.Unreachable(ex.GetBaseException().Message), ex);
            }

            // 401 = the token was revoked or replaced: fetch a new one once, then give up
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt >= 1)
            {
                return response;
            }

            response.Dispose();
            rejectedToken = accessToken;
            logger.LogInformation("Twitch/IGDB hat den Zugriffs-Token abgelehnt (401) - es wird ein neuer geholt.");
        }
    }

    /// <summary>Requests a fresh app access token without touching the cache (used by the connection test).</summary>
    public async Task<TwitchToken> RequestTokenAsync(TwitchCredentials credentials, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
                ["grant_type"] = "client_credentials"
            })
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            throw new TwitchApiException(ServerTexts.Twitch.TokenUnreachable(ex.GetBaseException().Message), ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new TwitchApiException(DescribeTokenFailure(response.StatusCode, ReadMessage(body)));
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var token = doc.RootElement.TryGetProperty("access_token", out var at) && at.ValueKind == JsonValueKind.String ? at.GetString() : null;
                var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var seconds) ? seconds : 0;
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new TwitchApiException(ServerTexts.Twitch.NoAccessToken);
                }
                return new TwitchToken(token, expiresIn);
            }
            catch (JsonException ex)
            {
                throw new TwitchApiException(ServerTexts.Twitch.TokenAnswerUnreadable, ex);
            }
        }
    }

    /// <summary>Does the stored Client-ID/Secret get a token? Uses the cached token when there is one (system check).</summary>
    public async Task<TwitchCredentialCheck> CheckStoredCredentialsAsync(CancellationToken ct)
    {
        var credentials = GetStoredCredentials();
        if (credentials is null)
        {
            return new TwitchCredentialCheck(false, false, null);
        }

        try
        {
            await GetAccessTokenAsync(credentials, null, ct);
            return new TwitchCredentialCheck(true, true, null);
        }
        catch (TwitchApiException ex)
        {
            return new TwitchCredentialCheck(true, false, ex.Message);
        }
    }

    /// <summary>
    /// Connection test for the settings page: token, one cheap IGDB call and one cheap Twitch call. Blank values fall back to
    /// the stored ones. The tested credentials are not stored and the cached token is not touched.
    /// </summary>
    public async Task<TwitchTestResponseDto> TestAsync(string? clientId, string? clientSecret, CancellationToken ct)
    {
        var stored = GetStoredCredentials();
        var id = string.IsNullOrWhiteSpace(clientId) ? stored?.ClientId : clientId.Trim();
        var secret = string.IsNullOrWhiteSpace(clientSecret) ? stored?.ClientSecret : clientSecret.Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
        {
            return new TwitchTestResponseDto(false, ServerTexts.Twitch.TestEnterCredentials, false, false, false);
        }
        if (!SettingsStore.IsPlausibleTwitchCredential(id) || !SettingsStore.IsPlausibleTwitchCredential(secret))
        {
            return new TwitchTestResponseDto(false, ServerTexts.Twitch.TestIllegalCharacters, false, false, false);
        }

        var credentials = new TwitchCredentials(id, secret);
        TwitchToken token;
        try
        {
            token = await RequestTokenAsync(credentials, ct);
        }
        catch (TwitchApiException ex)
        {
            return new TwitchTestResponseDto(false, ex.Message, false, false, false);
        }

        var igdbError = await ProbeAsync(credentials, token.AccessToken, HttpMethod.Post, IgdbBaseUrl + "external_game_sources", "fields name; limit 1;", "IGDB", ct);
        var streamsError = await ProbeAsync(credentials, token.AccessToken, HttpMethod.Get, HelixBaseUrl + "streams?first=1&type=live", null, "Twitch (Streams)", ct);

        if (igdbError is null && streamsError is null)
        {
            return new TwitchTestResponseDto(true, ServerTexts.Twitch.TestOk, true, true, true);
        }

        var problems = igdbError is { } igdbProblem && streamsError is { } streamsProblem
            ? Msg.Join(" ", igdbProblem, streamsProblem)
            : igdbError ?? streamsError!.Value;
        return new TwitchTestResponseDto(false, ServerTexts.Twitch.TestPartial(problems), true, igdbError is null, streamsError is null);
    }

    private async Task<Msg?> ProbeAsync(
        TwitchCredentials credentials, string accessToken, HttpMethod method, string url, string? body, string label, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Client-Id", credentials.ClientId);
            if (body is not null)
            {
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain");
            }

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }
            return ServerTexts.Twitch.ProbeStatus(label, (int)response.StatusCode, FormatMessage(ReadMessage(await response.Content.ReadAsStringAsync(ct))));
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            return ServerTexts.Twitch.ProbeUnreachable(label, ex.GetBaseException().Message);
        }
    }

    private async Task<string> GetAccessTokenAsync(TwitchCredentials credentials, string? rejectedToken, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_token is { } cached && cached.Credentials == credentials && now < cached.RefreshAt && cached.AccessToken != rejectedToken)
            {
                return cached.AccessToken;
            }
            if (_failure is { } failed && failed.Credentials == credentials && now < failed.RetryAt)
            {
                throw new TwitchApiException(failed.Message);
            }

            try
            {
                var token = await RequestTokenAsync(credentials, ct);
                _token = new CachedToken(credentials, token.AccessToken, now + RefreshAfter(token.ExpiresInSeconds));
                _failure = null;
                return token.AccessToken;
            }
            catch (TwitchApiException ex)
            {
                _token = null;
                _failure = new CachedFailure(credentials, ex.Text, now + FailureBackoff);
                if (now - _lastFailureLog > FailureLogInterval)
                {
                    _lastFailureLog = now;
                    logger.LogWarning("Twitch-Token konnte nicht geholt werden: {Message}", ex.Message);
                }
                throw;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>App tokens last weeks; renew well before the end. A very short token is renewed after half of its life.</summary>
    private static TimeSpan RefreshAfter(int expiresInSeconds)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Max(expiresInSeconds, 0));
        return lifetime > TimeSpan.FromHours(1) ? lifetime - TimeSpan.FromMinutes(30) : lifetime / 2;
    }

    private static Msg DescribeTokenFailure(HttpStatusCode status, string? message)
    {
        var detail = FormatMessage(message);
        if (status == HttpStatusCode.Forbidden || message?.Contains("secret", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ServerTexts.Twitch.SecretRejected(detail);
        }
        if (status == HttpStatusCode.BadRequest || message?.Contains("client", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ServerTexts.Twitch.ClientIdRejected(detail);
        }
        if (status == HttpStatusCode.TooManyRequests)
        {
            return ServerTexts.Twitch.TokenRateLimited;
        }
        return ServerTexts.Twitch.TokenNotIssued((int)status, detail);
    }

    private static string FormatMessage(string? message) => string.IsNullOrWhiteSpace(message) ? string.Empty : $" ({message})";

    /// <summary>The "message" field of a Twitch/IGDB error body, shortened; null when the body has none.</summary>
    internal static string? ReadMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var element = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString()?.Trim();
                return string.IsNullOrEmpty(text) ? null : text.Length > 140 ? text[..140] : text;
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML error page of a proxy): no message
        }
        return null;
    }
}
