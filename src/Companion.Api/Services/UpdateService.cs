using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Api.Configuration;
using Companion.Api.Localization;
using Microsoft.Extensions.Options;
using PlatinVault.Versioning;

namespace Companion.Api.Services;

/// <summary>How eagerly <see cref="UpdateService.GetAsync"/> asks GitHub.</summary>
public enum UpdateCheckMode
{
    /// <summary>Never asks GitHub, only returns what is known (e.g. to verify an install request against the last check).</summary>
    CacheOnly,

    /// <summary>The settings page and the Windows program's daily check: asks only when the last good answer is older than the cache time.</summary>
    Automatic,

    /// <summary>The button "Jetzt nach Updates suchen": asks GitHub now, but at most once per minute.</summary>
    Manual,
}

/// <summary>
/// What the update check knows right now. <see cref="Releases"/> is the last successfully read list (may be older than a failure that
/// followed it); <see cref="Error"/> is set when the LATEST attempt failed - a failure is never kept as a result, the next check simply
/// tries again. <see cref="Throttled"/> = a manual check was asked within a minute of the previous attempt and answered from memory.
/// </summary>
public sealed record ReleaseListResult(
    bool Enabled,
    IReadOnlyList<PublishedRelease>? Releases,
    DateTimeOffset? CheckedAt,
    Msg? Error,
    DateTimeOffset? ErrorAt,
    bool Throttled,
    DateTimeOffset? NextManualCheckAt);

/// <summary>
/// Reads the published betas and releases from the public repository's GitHub releases (unauthenticated REST API: 60 requests per hour
/// and IP). One instance per server: a good answer is reused for <see cref="UpdateCheckOptions.CacheHours"/>; a manual check bypasses that
/// at most once per minute; a failed attempt is reported with a specific message and never cached (automatic checks retry it after a few
/// minutes, so a page left open does not hammer GitHub). Which version is offered is decided by <see cref="UpdateStatusBuilder"/> with the
/// shared rules in <see cref="ReleaseFeed"/> - the same for the Windows program, Unraid, a NAS and plain Docker.
/// </summary>
public sealed partial class UpdateService(
    IHttpClientFactory httpFactory,
    IOptions<CompanionOptions> options,
    TimeProvider time,
    ILogger<UpdateService> logger)
{
    public const string HttpClientName = "updates";

    /// <summary>Request timeout of the named HTTP client (also used in the timeout message).</summary>
    public const int TimeoutSeconds = 20;

    /// <summary>A manual check asks GitHub at most this often.</summary>
    public static readonly TimeSpan ManualInterval = TimeSpan.FromMinutes(1);

    /// <summary>After a failed attempt, automatic checks wait this long before they try again.</summary>
    public static readonly TimeSpan AutomaticRetryAfterFailure = TimeSpan.FromMinutes(5);

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryPattern();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<PublishedRelease>? _releases;
    private DateTimeOffset? _fetchedAt;
    private DateTimeOffset? _lastAttemptAt;
    private Msg? _error;
    private DateTimeOffset? _errorAt;

    /// <summary>Checks with the interval taken from <see cref="UpdateCheckOptions.CacheHours"/> (fixed cache time, no user setting).</summary>
    public Task<ReleaseListResult> GetAsync(UpdateCheckMode mode, CancellationToken ct) => GetAsync(mode, interval: null, ct);

    /// <summary>
    /// <paramref name="interval"/> is the user's setting "Nach Updates suchen": it only decides when an <see cref="UpdateCheckMode.Automatic"/>
    /// request may ask GitHub (null = the fixed <see cref="UpdateCheckOptions.CacheHours"/>). Manual checks and the retry pause after a failure are the same for every interval.
    /// </summary>
    public async Task<ReleaseListResult> GetAsync(UpdateCheckMode mode, UpdateCheckInterval? interval, CancellationToken ct)
    {
        var settings = options.Value.Updates;
        if (!settings.Enabled) return new ReleaseListResult(false, null, null, null, null, false, null);

        await _gate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            var throttled = false;
            switch (mode)
            {
                case UpdateCheckMode.Manual when _lastAttemptAt is { } last && now - last < ManualInterval:
                    throttled = true;
                    break;
                case UpdateCheckMode.Manual:
                    await FetchAsync(settings, now, ct);
                    break;
                case UpdateCheckMode.Automatic when IsStale(settings, interval, now) && !RecentlyFailed(now):
                    await FetchAsync(settings, now, ct);
                    break;
            }
            return new ReleaseListResult(true, _releases, _fetchedAt, _error, _errorAt, throttled, _lastAttemptAt + ManualInterval);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsStale(UpdateCheckOptions settings, UpdateCheckInterval? interval, DateTimeOffset now)
    {
        // Never: only a check by hand asks. Startup: one good answer per process, then nothing until the next start.
        if (interval == UpdateCheckInterval.Never) return false;
        if (_fetchedAt is not { } fetched) return true;
        if (interval == UpdateCheckInterval.Startup) return false;
        var period = interval is { } chosen ? UpdateCheckIntervals.Period(chosen)!.Value : TimeSpan.FromHours(Math.Max(0.05, settings.CacheHours));
        return now - fetched >= period;
    }

    private bool RecentlyFailed(DateTimeOffset now) => _errorAt is { } failed && now - failed < AutomaticRetryAfterFailure;

    private async Task FetchAsync(UpdateCheckOptions settings, DateTimeOffset now, CancellationToken ct)
    {
        _lastAttemptAt = now;
        var repository = settings.Repository.Trim();
        try
        {
            if (!RepositoryPattern().IsMatch(repository) || !Uri.TryCreate(settings.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
            {
                Fail(ServerTexts.Updates.NotFound(repository), now, "invalid configuration");
                return;
            }

            var http = httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, $"repos/{repository}/releases?per_page=30"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PlatinVault", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (RateLimitReset(response, now) is { } reset)
            {
                var minutes = (int)Math.Max(1, Math.Ceiling((reset - now).TotalMinutes));
                Fail(ServerTexts.Updates.RateLimited(minutes, reset.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)), now, $"rate limit until {reset:O}");
                return;
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Fail(ServerTexts.Updates.NotFound(repository), now, "HTTP 404");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;
                Fail(ServerTexts.Updates.HttpStatus((int)response.StatusCode, reason), now, $"HTTP {(int)response.StatusCode}");
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                Fail(ServerTexts.Updates.InvalidAnswer($"expected a JSON array, got {json.RootElement.ValueKind}"), now, "not an array");
                return;
            }

            _releases = ReleaseFeed.Parse(json.RootElement);
            _fetchedAt = now;
            _error = null;
            _errorAt = null;
            logger.LogInformation("Update-Prüfung: {Count} veröffentlichte Versionen gelesen.", _releases.Count);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Fail(ServerTexts.Updates.Timeout(TimeoutSeconds), now, "timeout");
        }
        catch (HttpRequestException ex)
        {
            Fail(ServerTexts.Updates.Unreachable(ex.Message), now, ex.Message);
        }
        catch (JsonException ex)
        {
            Fail(ServerTexts.Updates.InvalidAnswer(ex.Message), now, ex.Message);
        }
    }

    /// <summary>
    /// GitHub's "rate limit exceeded" answer: 403 or 429 with <c>x-ratelimit-remaining: 0</c> (reset time in <c>x-ratelimit-reset</c>,
    /// Unix seconds) or a <c>retry-after</c> header. Null when the answer is not a rate limit.
    /// </summary>
    private static DateTimeOffset? RateLimitReset(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)) return null;
        if (response.Headers.RetryAfter?.Delta is { } delta) return now + delta;
        var remaining = Header(response, "x-ratelimit-remaining");
        if (response.StatusCode == HttpStatusCode.Forbidden && remaining != "0") return null;
        if (long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(unix);
            return reset > now ? reset : now + TimeSpan.FromMinutes(1);
        }
        return now + TimeSpan.FromHours(1);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;

    private void Fail(Msg message, DateTimeOffset now, string logDetail)
    {
        _error = message;
        _errorAt = now;
        logger.LogWarning("Update-Prüfung fehlgeschlagen: {Detail}", logDetail);
    }
}
