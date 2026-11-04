using System.Net;

namespace Companion.Api.Hubs;

/// <summary>
/// Origin check in front of every SignalR request under <c>/hubs</c> (negotiate, WebSocket upgrade, server-sent events, long polling).
/// A browser lets any web page open a WebSocket to another origin (there is no CORS for WebSockets), so without this check a page the
/// user visits could listen to the live events (running game, unlocks with the personal note, pins). A request is let through when:
/// <list type="number">
/// <item>it carries no <c>Origin</c> header (not a browser: the bridge, scripts, tests);</item>
/// <item>the origin (scheme + host + port) is the request's own: <c>Request.Scheme</c>/<c>Request.Host</c> (already rewritten when a
/// forwarded-headers middleware runs first) or the values a reverse proxy passes in <c>X-Forwarded-Proto</c>/<c>X-Forwarded-Host</c>.
/// Trusting these two is safe here: a cross-site page cannot add headers to a WebSocket handshake, and a cross-site negotiate request
/// with extra headers needs a CORS preflight, which this server never grants;</item>
/// <item>in Development: any loopback origin (the Vite dev servers proxy <c>/hubs</c> with <c>changeOrigin</c>, so origin and host differ);</item>
/// <item>the origin is listed in <c>PLATINVAULT_ALLOWED_ORIGINS</c> or <c>Companion:AllowedOrigins</c> (comma/semicolon separated, or an
/// array) for proxies that rewrite the host.</item>
/// </list>
/// Everything else gets 403 before the hub sees it (no upgrade). An <c>Origin: null</c> (sandboxed frame, file page) is refused.
/// </summary>
public static class HubOriginGuard
{
    public const string AllowedOriginsKey = "Companion:AllowedOrigins";
    public const string EnvironmentVariable = "PLATINVAULT_ALLOWED_ORIGINS";

    /// <summary>Registers the check for every path under <c>/hubs</c>. Belongs before the endpoints, next to where CORS would sit.</summary>
    public static IApplicationBuilder UseHubOriginGuard(this WebApplication app)
    {
        var allowed = ReadAllowedOrigins(app.Configuration);
        var development = app.Environment.IsDevelopment();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HubOriginGuard).FullName!);
        var logged = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs"))
            {
                var request = context.Request;
                var origin = request.Headers.Origin.ToString();
                if (!IsAllowed(origin, request.Scheme, request.Host.Value, request.Headers["X-Forwarded-Proto"].ToString(),
                        request.Headers["X-Forwarded-Host"].ToString(), development, allowed))
                {
                    // One warning per origin and hour: a foreign page reconnecting in a loop must not flood the log
                    var shown = Sanitize(origin);
                    var now = DateTimeOffset.UtcNow;
                    if (!logged.TryGetValue(shown, out var last) || now - last >= TimeSpan.FromHours(1))
                    {
                        if (logged.Count >= 200) logged.Clear();
                        logged[shown] = now;
                        logger.LogWarning(
                            "Verbindung zum Live-Kanal von fremder Herkunft {Origin} abgelehnt (Host {Host}). Läuft PlatinVault hinter einem Reverse-Proxy, der den Host umschreibt, die Adresse in PLATINVAULT_ALLOWED_ORIGINS eintragen.",
                            shown, Sanitize(request.Host.Value));
                    }
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            await next();
        });
    }

    /// <summary>The decision itself (pure, for tests). <paramref name="origin"/> empty = no Origin header.</summary>
    public static bool IsAllowed(
        string? origin,
        string requestScheme,
        string? requestHost,
        string? forwardedProto,
        string? forwardedHost,
        bool development,
        IReadOnlyCollection<string> allowedOrigins)
    {
        if (string.IsNullOrEmpty(origin)) return true;
        if (Normalize(origin) is not { } wanted) return false;

        if (Same(wanted, requestScheme, requestHost)) return true;
        // A proxy may pass a list ("https, http"): the first entry is the client-facing one
        var proto = First(forwardedProto);
        var host = First(forwardedHost);
        if ((proto is not null || host is not null) && Same(wanted, proto ?? requestScheme, host ?? requestHost)) return true;

        if (development && Uri.TryCreate(wanted, UriKind.Absolute, out var uri) && IsLoopbackHost(uri.Host)) return true;

        return allowedOrigins.Contains(wanted, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "scheme://host:port" in lower case with the port always written, or null for anything that is not an http(s) origin
    /// ("null", a path, user info, a query).
    /// </summary>
    public static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return null;
        var text = origin.Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/") return null;
        return $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}:{uri.Port}";
    }

    /// <summary><c>Companion:AllowedOrigins</c> as a string ("https://a.example, https://b.example") or as an array; invalid entries are dropped.</summary>
    public static IReadOnlyCollection<string> ReadAllowedOrigins(IConfiguration configuration)
    {
        var section = configuration.GetSection(AllowedOriginsKey);
        var raw = new List<string>();
        foreach (var value in new[] { section.Value, configuration[EnvironmentVariable] })
        {
            if (!string.IsNullOrWhiteSpace(value)) raw.AddRange(value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries));
        }
        raw.AddRange(section.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v))!);
        return raw.Select(Normalize).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool Same(string wanted, string? scheme, string? host)
    {
        if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(host)) return false;
        return string.Equals(Normalize($"{scheme.Trim()}://{host.Trim()}"), wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string? First(string? list)
    {
        if (string.IsNullOrWhiteSpace(list)) return null;
        var first = list.Split(',')[0].Trim();
        return first.Length == 0 ? null : first;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);
    }

    // Header values go into the log: keep them short and on one line
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var clean = new string(value.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return clean.Length > 200 ? clean[..200] + "…" : clean;
    }
}
