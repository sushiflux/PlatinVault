using System.Collections.Concurrent;
using System.Net;
using Companion.Api.Localization;

namespace Companion.Api.Configuration;

/// <summary>
/// Protection against DNS rebinding for the server/container build (Docker, Unraid, NAS, the browser dev stack), where
/// <c>AllowedHosts</c> is <c>*</c> because the server is reached under whatever address the home network gives it. A rebinding attack
/// needs a public domain the attacker controls; this check only lets through Host values that cannot be one:
/// <list type="bullet">
/// <item>IP literals (IPv4, IPv6 with or without brackets, any port), <c>localhost</c> and <c>*.localhost</c>;</item>
/// <item>single-label names (<c>tower</c>) and names under the private-use suffixes <c>.local</c>, <c>.lan</c>, <c>.home.arpa</c>,
/// <c>.internal</c>, and <c>.fritz.box</c> (the local names of FRITZ!Box home networks; the domain belongs to the router maker again since a WIPO decision in 2024 - owner decision 2026-11-04);</item>
/// <item>every host listed in <c>PLATINVAULT_ALLOWED_HOSTS</c> or <c>Companion:AllowedHosts</c> (<c>;</c>/<c>,</c>-separated,
/// <c>*.example.com</c> = every name below example.com); the single value <c>*</c> switches the check off.</item>
/// </list>
/// The Windows program and the Linux package set their own loopback-only <c>AllowedHosts</c> (ASP.NET Core's host filtering); this
/// check then stays out of the way. The Host header is checked as the client sent it (no forwarded headers are trusted: a rebinding page
/// is same-origin and could set them). A refused request gets 400 with a complete bilingual text that names the host and the variable.
/// </summary>
public static class HostGuard
{
    public const string EnvironmentVariable = "PLATINVAULT_ALLOWED_HOSTS";
    public const string ConfigKey = "Companion:AllowedHosts";

    private static readonly string[] PrivateSuffixes = [".local", ".lan", ".home.arpa", ".internal", ".localhost", ".fritz.box"];

    /// <summary>The configured rules, or null when the check is off (host filtering of ASP.NET Core active, or <c>*</c> configured).</summary>
    public sealed record Rules(IReadOnlyList<string> Exact, IReadOnlyList<string> WildcardSuffixes);

    public static IApplicationBuilder UseHostGuard(this WebApplication app)
    {
        var rules = ReadRules(app.Configuration);
        if (rules is null) return app;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HostGuard).FullName!);
        var logged = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        return app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (IsAllowed(host, rules))
            {
                await next();
                return;
            }

            var shown = Sanitize(host);
            LogOnce(logger, logged, shown);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(ServerTexts.HostRefused(shown).ToString(), context.RequestAborted);
        });
    }

    /// <summary>
    /// Null when the guard does not run: ASP.NET Core's own <c>AllowedHosts</c> is set to a list (Windows program, Linux package), or the
    /// configured list is <c>*</c>.
    /// </summary>
    public static Rules? ReadRules(IConfiguration configuration)
    {
        var framework = configuration["AllowedHosts"];
        if (!string.IsNullOrWhiteSpace(framework) && framework.Trim() != "*") return null;

        var entries = new List<string>();
        foreach (var value in new[] { configuration[EnvironmentVariable], configuration[ConfigKey] })
        {
            if (!string.IsNullOrWhiteSpace(value)) entries.AddRange(value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        entries.AddRange(configuration.GetSection(ConfigKey).GetChildren().Select(c => c.Value?.Trim()).Where(v => !string.IsNullOrEmpty(v))!);
        if (entries.Contains("*")) return null;

        var exact = new List<string>();
        var wildcard = new List<string>();
        foreach (var entry in entries.Select(NormalizeEntry).OfType<string>())
        {
            if (entry.StartsWith("*.", StringComparison.Ordinal)) wildcard.Add(entry[1..]);
            else exact.Add(entry);
        }
        return new Rules(exact, wildcard);
    }

    /// <summary>The decision for one Host value (without port, as <c>HostString.Host</c> gives it). Empty = allowed (HTTP/1.0 without Host).</summary>
    public static bool IsAllowed(string? host, Rules rules)
    {
        if (string.IsNullOrEmpty(host)) return true;
        var name = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (name.Length == 0) return true;

        if (IPAddress.TryParse(name.Trim('[', ']'), out _)) return true;
        if (name == "localhost" || !name.Contains('.')) return true;
        if (PrivateSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal))) return true;

        return rules.Exact.Contains(name, StringComparer.Ordinal)
               || rules.WildcardSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal) && name.Length > s.Length);
    }

    // "Host:port", a URL or a bare name -> the lower-case name ("*.example.com" stays a wildcard)
    private static string? NormalizeEntry(string entry)
    {
        var text = entry.Trim();
        if (text.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(text, UriKind.Absolute, out var uri)) text = uri.Host;
        else if (!text.StartsWith('[') && text.Count(c => c == ':') == 1) text = text[..text.IndexOf(':')];
        text = text.TrimEnd('.').ToLowerInvariant();
        if (text.Length == 0 || text == "*.") return null;
        return text;
    }

    // One warning per refused host and hour; the memory of logged hosts stays small
    private static void LogOnce(ILogger logger, ConcurrentDictionary<string, DateTimeOffset> logged, string host)
    {
        var now = DateTimeOffset.UtcNow;
        if (logged.TryGetValue(host, out var last) && now - last < TimeSpan.FromHours(1)) return;
        if (logged.Count >= 200) logged.Clear();
        logged[host] = now;
        logger.LogWarning(
            "Anfrage mit dem Host \"{Host}\" abgelehnt (Schutz gegen DNS-Rebinding). Wird PlatinVault absichtlich unter diesem Namen aufgerufen (z. B. über einen Reverse-Proxy), {Variable}={Host} setzen.",
            host, EnvironmentVariable, host);
    }

    // Kestrel already refuses invalid Host characters; this keeps the echo short and on one line anyway
    private static string Sanitize(string host)
    {
        var clean = new string(host.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > 253 ? clean[..253] : clean;
    }
}
