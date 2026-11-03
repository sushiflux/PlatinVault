namespace PlatinVault.Desktop.Ui;

/// <summary>What the program window does with a top-level navigation.</summary>
internal enum NavigationDecision
{
    /// <summary>Shown in the window.</summary>
    Allow,

    /// <summary>Not in the window: opened in the default browser instead.</summary>
    External,
}

/// <summary>
/// "Mit Steam anmelden" inside the program window. The general rule stays: pages of other sites open in the default browser. The one
/// exception is Steam's OpenID sign-in, and only while it is under way: it starts with the app's own <c>/api/steam/openid/start</c>
/// (which sets the one-time state cookie in the window's cookie store), may then show Steam's sign-in pages
/// (<c>https://steamcommunity.com/openid/…</c> and <c>https://login.steampowered.com/…</c>) and ends as soon as the window is back on
/// the app (Steam's return to <c>/api/steam/openid/callback</c>, which needs that cookie) - or after 15 minutes. Any other site, even
/// another Steam page, still goes to the default browser.
/// </summary>
internal sealed class SteamLoginFlow
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(15);
    private DateTime? _activeUntil;

    public bool IsActive(DateTime now) => _activeUntil is { } until && now < until;

    public NavigationDecision Decide(Uri target, bool own, DateTime now)
    {
        if (own)
        {
            // Back on the app (or any page of it): a sign-in ends; the start page begins one
            _activeUntil = IsStart(target) ? now + MaxDuration : null;
            return NavigationDecision.Allow;
        }
        return IsActive(now) && IsSteamSignInPage(target) ? NavigationDecision.Allow : NavigationDecision.External;
    }

    public static bool IsStart(Uri target) =>
        target.AbsolutePath.Equals("/api/steam/openid/start", StringComparison.OrdinalIgnoreCase);

    public static bool IsSteamSignInPage(Uri target) =>
        target.Scheme == Uri.UriSchemeHttps && target.IsDefaultPort &&
        ((target.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) && target.AbsolutePath.StartsWith("/openid/", StringComparison.OrdinalIgnoreCase))
         || target.Host.Equals("login.steampowered.com", StringComparison.OrdinalIgnoreCase));
}
