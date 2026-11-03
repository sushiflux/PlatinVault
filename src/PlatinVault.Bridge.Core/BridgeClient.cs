using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlatinVault.Bridge;

/// <summary>Body of <c>POST /api/bridge/state</c> (spec section 4). JSON is camelCase.</summary>
internal sealed record BridgeStateDto(
    string SteamId64,
    uint RunningAppId,
    string GameName,
    IReadOnlyList<BridgeAchievementDto> Achievements,
    string SentAt,
    string BridgeVersion,
    // "linux" for the Linux bridge; omitted (null) for the Windows bridge, which the server treats as the default
    string? Platform = null);

/// <summary>
/// Body of <c>POST /api/bridge/library</c>: what the Steam client's files say about the library (release date, Metacritic, review rating,
/// size on disk), one entry per app that has any of it. Unknown values are omitted (null).
/// </summary>
internal sealed record BridgeLibraryDto(IReadOnlyList<BridgeLibraryGameDto> Games);

internal sealed record BridgeLibraryGameDto(long AppId, long? ReleaseUnix, int? Metacritic, int? ReviewPercent, int? ReviewScore, long? SizeBytes, long? InstallSizeBytes);

/// <summary>One achievement; <c>UnlockTime</c> is ISO-8601 UTC and omitted (null) while locked or without a timestamp.</summary>
internal sealed record BridgeAchievementDto(string ApiName, bool Achieved, string? UnlockTime);

/// <summary>
/// 200 answer of <c>POST /api/bridge/state</c>. <c>LatestBridgeVersion</c>/<c>BridgeSha256</c> tell the bridge which
/// exe the server offers at <c>GET /api/bridge/exe</c> (both null when the server has none). <c>UiLanguage</c> is the
/// stored <c>ui.language</c> of the server ("de"/"en"; null while none is chosen yet, and with an older server), which the
/// bridge adopts as its own language (<see cref="BridgeLang"/>).
/// </summary>
internal sealed record BridgeReplyDto(bool Ok, string? LatestBridgeVersion, string? BridgeSha256, string? UiLanguage = null);

internal enum SendOutcome
{
    /// <summary>2xx - the server stored the state.</summary>
    Ok,

    /// <summary>401 - the token is unknown, revoked or wrong.</summary>
    Unauthorized,

    /// <summary>Server not reachable or temporarily failing (network, timeout, 5xx, 429) - retry quickly with back-off.</summary>
    Transient,

    /// <summary>The server answered but refuses the request (other 4xx, redirect) - retry slowly, needs a human.</summary>
    Rejected,
}

internal readonly record struct SendResult(
    SendOutcome Outcome, int StatusCode = 0, string? Reason = null, BridgeReplyDto? Reply = null);

/// <summary>How a command ended on this PC; <c>Status</c> is "done" or "failed", <c>Reason</c> one of the server's failure codes.</summary>
internal sealed record BridgeCommandResultDto(string Id, string Status, string? Reason, string? Detail);

/// <summary>Body of <c>POST /api/bridge/commands</c>: the results of the commands run since the last poll (usually none).</summary>
internal sealed record BridgePollRequestDto(IReadOnlyList<BridgeCommandResultDto> Results);

/// <summary>One command from the server. <c>Type</c> is "launch"; the address is built here from <c>AppId</c>, never taken from the server.</summary>
internal sealed record BridgeCommandDto(string Id, string Type, long AppId, string? GameName);

/// <summary>200 answer of <c>POST /api/bridge/commands</c>: every command that is still open (the server repeats it until a result arrived).</summary>
internal sealed record BridgePollReplyDto(bool Ok, IReadOnlyList<BridgeCommandDto>? Commands);

/// <summary><c>Commands</c> is null when the server answered 2xx but the body was not understood (then nothing is known about the open commands).</summary>
internal readonly record struct PollResult(
    SendOutcome Outcome, int StatusCode = 0, string? Reason = null, IReadOnlyList<BridgeCommandDto>? Commands = null);

/// <summary>Result of downloading the exe; <c>Sha256</c> is lowercase hex of what was actually received.</summary>
internal readonly record struct DownloadResult(bool Ok, string? Sha256, long Bytes, string? Error);

/// <summary>Talks to the PlatinVault server with a bearer token: pushes the state, downloads the updated exe.</summary>
internal sealed class BridgeClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);

    // A bridge exe is a few MB; refuse anything absurd instead of filling the disk.
    private const long MaxDownloadBytes = 256L * 1024 * 1024;

    // Answer of a command poll: eight commands at most, each a few hundred bytes
    private const int PollReplyCap = 64 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _stateEndpoint;
    private readonly Uri _exeEndpoint;
    private readonly Uri _commandsEndpoint;
    private readonly Uri _libraryEndpoint;

    /// <param name="platform">"linux" for the Linux bridge: the self-update download then asks for the Linux binary (<c>?platform=linux</c>). Null = Windows.</param>
    /// <param name="handler">Replaces the real network handler (tests only).</param>
    public BridgeClient(BridgeSettings settings, string version, string? platform = null, HttpMessageHandler? handler = null)
    {
        _libraryEndpoint = settings.LibraryEndpoint;
        _stateEndpoint = settings.StateEndpoint;
        _exeEndpoint = platform is null
            ? settings.ExeEndpoint
            : new Uri(settings.ExeEndpoint + "?platform=" + Uri.EscapeDataString(platform));
        _commandsEndpoint = settings.CommandsEndpoint;

        handler ??= new SocketsHttpHandler
        {
            // A redirect (http -> https) would turn the POST into a GET and drop the token; report it instead.
            AllowAutoRedirect = false,
            ConnectTimeout = RequestTimeout,
            // Re-resolve DNS regularly: the Unraid box may get a new address after a router restart.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        _http = new HttpClient(handler, disposeHandler: true) { Timeout = RequestTimeout };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        // The API requires this header on every state-changing request (CSRF filter); harmless for token auth.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "PlatinVault");
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd($"PlatinVault-Bridge/{SanitizeVersion(version)}");
    }

    public async Task<SendResult> SendAsync(BridgeStateDto state, CancellationToken ct)
    {
        try
        {
            // Serialize up front so the request carries a Content-Length instead of being streamed chunked.
            using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(state, BridgeJsonContext.Default.BridgeStateDto));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var request = new HttpRequestMessage(HttpMethod.Post, _stateEndpoint) { Content = content };
            AddLanguage(request);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
                return new SendResult(SendOutcome.Ok, status, null, await ReadReplyAsync(response, ct).ConfigureAwait(false));

            return await FailureAsync(response, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout surfaces as a cancellation that was not requested by us.
            return new SendResult(SendOutcome.Transient, 0, CoreTexts.Network.Timeout(RequestTimeout.TotalSeconds));
        }
        catch (HttpRequestException ex)
        {
            BridgeLog.Debug(CoreTexts.Network.NetworkErrorDebug(ex.ToString()));
            return new SendResult(SendOutcome.Transient, 0, DescribeNetworkError(ex));
        }
    }

    /// <summary>
    /// Asks the server for work (<c>POST /api/bridge/commands</c>) and reports how the commands ended that ran since the last
    /// call. Small on purpose (a few hundred bytes, unlike the achievement list of <see cref="SendAsync"/>), because it is called
    /// every couple of seconds: this is what makes "Play" on a tablet reach this PC within about two seconds instead of at the
    /// next 60 s heartbeat of <see cref="SendAsync"/>.
    /// </summary>
    public async Task<PollResult> PollAsync(IReadOnlyList<BridgeCommandResultDto> results, CancellationToken ct)
    {
        try
        {
            using var content = new ByteArrayContent(
                JsonSerializer.SerializeToUtf8Bytes(new BridgePollRequestDto(results), BridgeJsonContext.Default.BridgePollRequestDto));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var request = new HttpRequestMessage(HttpMethod.Post, _commandsEndpoint) { Content = content };
            AddLanguage(request);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return new PollResult(SendOutcome.Ok, (int)response.StatusCode, null, await ReadPollReplyAsync(response, ct).ConfigureAwait(false));

            var failure = await FailureAsync(response, ct).ConfigureAwait(false);
            return new PollResult(failure.Outcome, failure.StatusCode, failure.Reason);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new PollResult(SendOutcome.Transient, 0, CoreTexts.Network.Timeout(RequestTimeout.TotalSeconds));
        }
        catch (HttpRequestException ex)
        {
            BridgeLog.Debug(CoreTexts.Network.NetworkErrorDebug(ex.ToString()));
            return new PollResult(SendOutcome.Transient, 0, DescribeNetworkError(ex));
        }
    }

    /// <summary>
    /// Sends the library facts (<c>POST /api/bridge/library</c>). Rare and best effort: the caller only needs to know whether it was
    /// accepted (an older server without the endpoint answers 404, which is simply "not accepted").
    /// </summary>
    public async Task<SendResult> SendLibraryAsync(BridgeLibraryDto library, CancellationToken ct)
    {
        try
        {
            using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(library, BridgeJsonContext.Default.BridgeLibraryDto));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var request = new HttpRequestMessage(HttpMethod.Post, _libraryEndpoint) { Content = content };
            AddLanguage(request);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new SendResult(SendOutcome.Ok, (int)response.StatusCode)
                : await FailureAsync(response, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new SendResult(SendOutcome.Transient, 0, CoreTexts.Network.Timeout(RequestTimeout.TotalSeconds));
        }
        catch (HttpRequestException ex)
        {
            BridgeLog.Debug(CoreTexts.Network.NetworkErrorDebug(ex.ToString()));
            return new SendResult(SendOutcome.Transient, 0, DescribeNetworkError(ex));
        }
    }

    /// <summary>Sorts a non-2xx answer into token problem / redirect / refusal / temporary failure (shared by every call that sends the token).</summary>
    private static async Task<SendResult> FailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.Unauthorized) return new SendResult(SendOutcome.Unauthorized, status);

        var detail = await ReadProblemDetailAsync(response, ct).ConfigureAwait(false);

        if (status is >= 300 and < 400)
        {
            var target = response.Headers.Location?.ToString();
            return new SendResult(SendOutcome.Rejected, status, CoreTexts.Network.Redirect(target));
        }

        var reason = $"HTTP {status}{(detail is null ? string.Empty : ": " + detail)}";
        var transient = status is 408 or 429 or >= 500;
        return new SendResult(transient ? SendOutcome.Transient : SendOutcome.Rejected, status, reason);
    }

    /// <summary>
    /// Downloads <c>GET /api/bridge/exe</c> to <paramref name="targetPath"/> and hashes the bytes while writing, so the
    /// caller can compare the SHA-256 with the one the server announced.
    /// </summary>
    public async Task<DownloadResult> DownloadExeAsync(string targetPath, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);

        try
        {
            // ResponseHeadersRead: the body is streamed to disk and is not subject to the short HttpClient timeout.
            using var request = new HttpRequestMessage(HttpMethod.Get, _exeEndpoint);
            AddLanguage(request);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadProblemDetailAsync(response, timeout.Token).ConfigureAwait(false);
                return new DownloadResult(false, null, 0, $"HTTP {(int)response.StatusCode}{(detail is null ? string.Empty : ": " + detail)}");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;

            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                        return new DownloadResult(false, null, total, CoreTexts.Network.DownloadTooLarge);

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                }
            }

            return new DownloadResult(true, Convert.ToHexStringLower(hash.GetHashAndReset()), total, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult(false, null, 0, CoreTexts.Network.Timeout(DownloadTimeout.TotalSeconds));
        }
        catch (HttpRequestException ex)
        {
            BridgeLog.Debug(CoreTexts.Network.DownloadNetworkErrorDebug(ex.ToString()));
            return new DownloadResult(false, null, 0, DescribeNetworkError(ex));
        }
    }

    public void Dispose() => _http.Dispose();

    // Per request, not per client: the language can change while the bridge runs (it follows the server). The server then
    // answers its error texts (the "detail" the bridge logs) in the language the bridge speaks.
    private static void AddLanguage(HttpRequestMessage request) => request.Headers.AcceptLanguage.ParseAdd(BridgeLang.Current);

    private static string DescribeNetworkError(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => CoreTexts.Network.NameResolution,
        HttpRequestError.ConnectionError => CoreTexts.Network.ConnectionError,
        HttpRequestError.SecureConnectionError => CoreTexts.Network.SecureConnectionError,
        HttpRequestError.ProxyTunnelError => CoreTexts.Network.ProxyError,
        _ => ex.InnerException?.Message ?? ex.Message,
    };

    /// <summary>Reads the small JSON answer of a successful state push; null when empty or not understood.</summary>
    private static async Task<BridgeReplyDto?> ReadReplyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await ReadBodyPrefixAsync(response, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text)
                ? null
                : JsonSerializer.Deserialize(text, BridgeJsonContext.Default.BridgeReplyDto);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            // An older server answers with something else (or nothing): the push itself worked, just no update info.
            BridgeLog.Debug(CoreTexts.Network.ReplyUnreadableDebug(ex.Message));
            return null;
        }
    }

    /// <summary>Reads the answer of a command poll; null when empty or not understood (the caller then keeps what it knows).</summary>
    private static async Task<IReadOnlyList<BridgeCommandDto>?> ReadPollReplyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            // Up to MaxOpen commands with long, JSON-escaped titles can exceed the small default cap of the other answers
            var text = await ReadBodyPrefixAsync(response, ct, PollReplyCap).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var reply = JsonSerializer.Deserialize(text, BridgeJsonContext.Default.BridgePollReplyDto);
            return reply is null ? null : reply.Commands ?? [];
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            BridgeLog.Debug(CoreTexts.Network.ReplyUnreadableDebug(ex.Message));
            return null;
        }
    }

    /// <summary>Extracts the <c>detail</c> (or <c>title</c>) of an RFC 7807 problem response, if there is one - in the language the bridge asked for.</summary>
    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await ReadBodyPrefixAsync(response, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in new[] { "detail", "title" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            // Not JSON or truncated - the status code alone has to do.
        }
        return null;
    }

    // Cap what we read: a misbehaving proxy could answer with a huge HTML page.
    private static async Task<string?> ReadBodyPrefixAsync(HttpResponseMessage response, CancellationToken ct, int maxBytes = 4096)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Pooled: the command poll reads an answer every couple of seconds
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, maxBytes), maxBytes, throwOnEndOfStream: false, ct).ConfigureAwait(false);
            return read == 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Keep only characters that are safe inside a User-Agent product token.
    private static string SanitizeVersion(string version)
    {
        var clean = new string(version.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return clean.Length > 0 ? clean : "dev";
    }
}
