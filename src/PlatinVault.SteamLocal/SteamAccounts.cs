using System.Globalization;
using System.Text.RegularExpressions;

namespace PlatinVault.SteamLocal;

/// <summary>Where the active Steam account came from.</summary>
public enum SteamAccountMethod
{
    None,

    /// <summary>Windows: <c>HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser</c>.</summary>
    Registry,

    /// <summary>Linux (unverified): the account marked <c>MostRecent</c> in <c>config/loginusers.vdf</c>.</summary>
    LoginUsers,

    /// <summary>Linux (unverified): the account of the newest <c>appcache/stats/UserGameStats_&lt;account&gt;_&lt;app&gt;.bin</c>.</summary>
    StatsFiles,
}

/// <summary>
/// The active Steam account without the Windows registry. NOT VERIFIED on a real Linux Steam: Windows knows the signed-in account
/// from <c>ActiveProcess\ActiveUser</c>; for Linux no documented equivalent was found (<c>ActiveProcess</c> in <c>registry.vdf</c>
/// is not documented). Used instead, in this order: the <c>MostRecent</c> account of <c>config/loginusers.vdf</c> (the account Steam
/// signs in with; the file exists per third-party tools, the property is not documented) and, as a last resort, the account id in
/// the name of the most recently written stats file. Both can name an account that is not signed in right now.
/// </summary>
public static partial class SteamAccounts
{
    /// <summary>SteamID64 = this base + the 32 bit account id.</summary>
    public const ulong SteamId64Base = 76561197960265728UL;

    [GeneratedRegex("\"(7656\\d{13})\"\\s*\\{([^{}]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex UserBlock();

    [GeneratedRegex("\"MostRecent\"\\s+\"1\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MostRecentFlag();

    [GeneratedRegex("\"Timestamp\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimestampValue();

    [GeneratedRegex("^UserGameStats_(\\d+)_\\d+\\.bin$", RegexOptions.CultureInvariant)]
    private static partial Regex StatsFileName();

    /// <summary>The 32 bit account id of a SteamID64 (0 when it is not an individual account id).</summary>
    public static uint AccountIdOf(ulong steamId64) =>
        steamId64 > SteamId64Base && steamId64 - SteamId64Base <= uint.MaxValue ? (uint)(steamId64 - SteamId64Base) : 0;

    /// <summary>
    /// The account of <c>loginusers.vdf</c>: the one with <c>"MostRecent" "1"</c>, else the one with the highest <c>Timestamp</c>,
    /// else 0. Text VDF: <c>"users" { "7656119..." { "AccountName" "..." "MostRecent" "1" "Timestamp" "..." } }</c>.
    /// </summary>
    public static uint FromLoginUsers(string vdfText)
    {
        uint best = 0;
        long bestStamp = -1;
        foreach (Match user in UserBlock().Matches(vdfText))
        {
            if (!ulong.TryParse(user.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId)) continue;
            var account = AccountIdOf(steamId);
            if (account == 0) continue;
            var block = user.Groups[2].Value;
            if (MostRecentFlag().IsMatch(block)) return account;
            var stamp = TimestampValue().Match(block) is { Success: true } t && long.TryParse(t.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (stamp > bestStamp)
            {
                bestStamp = stamp;
                best = account;
            }
        }
        return best;
    }

    /// <summary>The account in the name of the most recently written <c>UserGameStats_&lt;account&gt;_&lt;app&gt;.bin</c> of a folder, else 0.</summary>
    public static uint FromStatsFiles(string statsDirectory)
    {
        try
        {
            if (!Directory.Exists(statsDirectory)) return 0;
            uint best = 0;
            var bestTime = DateTime.MinValue;
            foreach (var file in new DirectoryInfo(statsDirectory).EnumerateFiles("UserGameStats_*_*.bin"))
            {
                var match = StatsFileName().Match(file.Name);
                if (!match.Success || !uint.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var account) || account == 0) continue;
                if (file.LastWriteTimeUtc > bestTime)
                {
                    bestTime = file.LastWriteTimeUtc;
                    best = account;
                }
            }
            return best;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The active account of a Linux Steam folder (see the class summary for why this is a best guess): <c>config/loginusers.vdf</c>
    /// first, then the stats file names.
    /// </summary>
    public static (uint AccountId, SteamAccountMethod Method) DetectLinux(string steamRoot)
    {
        var loginUsers = Path.Combine(steamRoot, "config", "loginusers.vdf");
        try
        {
            if (File.Exists(loginUsers))
            {
                string text;
                using (var stream = new FileStream(loginUsers, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    text = reader.ReadToEnd();
                }
                var account = FromLoginUsers(text);
                if (account != 0) return (account, SteamAccountMethod.LoginUsers);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not readable right now: fall back to the file names
        }

        var fromFiles = FromStatsFiles(Path.Combine(steamRoot, "appcache", "stats"));
        return fromFiles != 0 ? (fromFiles, SteamAccountMethod.StatsFiles) : (0, SteamAccountMethod.None);
    }
}
