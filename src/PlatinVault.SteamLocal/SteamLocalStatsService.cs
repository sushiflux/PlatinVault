using System.Text;
using Microsoft.Extensions.Logging;

namespace PlatinVault.SteamLocal;

/// <summary>
/// Reads local binary VDF files from Steam\appcache\stats\ (UserGameStatsSchema_*.bin &amp; UserGameStats_*.bin)
/// in under 400ms without requiring a Steam Web API key or waiting for Steam Community XML cache updates.
/// Where Steam lives comes from <see cref="SteamLocationProvider"/>, the running game from an <see cref="IRunningGameSource"/>:
/// Windows = the registry (unchanged behaviour), Linux = the Steam folder search and the process list. In a container without a
/// Steam folder nothing is found and nothing is read (no log lines, no process scan).
/// <para>
/// Linux is EXPERIMENTAL - not verified on a real Linux Steam: that <c>appcache/stats/UserGameStats_*.bin</c> exists there in the
/// same binary format and is rewritten at the moment of an unlock, and how the active account is found (see <see cref="SteamAccounts"/>).
/// The Windows parsers are used unchanged.
/// </para>
/// </summary>
public sealed class SteamLocalStatsService
{
    // The Linux account guess reads a file; the live detection asks several times per second
    private static readonly TimeSpan AccountCacheDuration = TimeSpan.FromSeconds(5);

    private readonly ILogger<SteamLocalStatsService> logger;
    private readonly IRunningGameSource _runningGame;
    private readonly Lock _accountGate = new();
    private (uint Id, SteamAccountMethod Method, string Root, long At)? _account;

    /// <param name="logger">Debug/warning lines of the readers.</param>
    /// <param name="location">Where Steam lives; null = a provider with the defaults of this platform (registry on Windows).</param>
    /// <param name="runningGame">The running-game detection; null = the one of the location's platform.</param>
    public SteamLocalStatsService(ILogger<SteamLocalStatsService> logger, SteamLocationProvider? location = null, IRunningGameSource? runningGame = null)
    {
        this.logger = logger;
        LocationProvider = location ?? new SteamLocationProvider();
        _runningGame = runningGame ?? RunningGameSources.For(LocationProvider.Platform);
    }

    public sealed class LocalAchievementStat
    {
        public string ApiName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        // True when name/description come in the requested language (not an English fallback)
        public bool IsLocalized { get; set; }
        // English name for matching guide entries that were written with the original titles
        public string EnglishName { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string IconGray { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public bool Achieved { get; set; }
        public DateTime? UnlockTime { get; set; }
        // Steam's progress counter for this achievement ("50 / 400"), only for games that define one
        public double? ProgressCurrent { get; set; }
        public double? ProgressMax { get; set; }
    }

    public sealed class LocalSchemaResult
    {
        public string GameName { get; set; } = string.Empty;
        public List<LocalAchievementStat> Achievements { get; set; } = [];
    }

    private sealed class VdfNode
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Strings { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, uint> Ints { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Where Steam is looked for (the server sets its <see cref="SteamLocationProvider.ManualRoot"/> from the setting).</summary>
    public SteamLocationProvider LocationProvider { get; }

    /// <summary>The Steam folder in use and every place that was looked at (for the system check).</summary>
    public SteamLocation Location => LocationProvider.Current;

    /// <summary>How the running game is detected on this platform.</summary>
    public RunningGameMethod RunningGameMethod => _runningGame.Method;

    public bool IsLocalSteamAvailable() => GetSteamPath() is not null;

    public uint GetLocalRunningAppId()
    {
        // The registry value is read as before, with or without a Steam folder (unchanged Windows behaviour). Every other source only
        // runs when a Steam folder exists, so a container without Steam never scans its process list.
        if (_runningGame.Method != RunningGameMethod.Registry && !Location.Found)
            return 0;

        try
        {
            return _runningGame.GetRunningAppId();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Laufendes Spiel nicht lesbar.");
            return 0;
        }
    }

    public string? GetSteamPath() => Location.Root;

    public uint GetActiveAccountId() => GetActiveAccount().AccountId;

    /// <summary>The active Steam account and how it was found (Linux: a best guess, see <see cref="SteamAccounts"/>).</summary>
    public (uint AccountId, SteamAccountMethod Method) GetActiveAccount()
    {
        if (LocationProvider.Platform == SteamPlatform.Windows)
        {
            if (!OperatingSystem.IsWindows())
                return (0, SteamAccountMethod.None);
            var value = SteamLocator.ReadRegistryValue(@"Software\Valve\Steam\ActiveProcess", "ActiveUser");
            return value is not null && uint.TryParse(value, out var userId) ? (userId, SteamAccountMethod.Registry) : (0, SteamAccountMethod.None);
        }

        var root = GetSteamPath();
        if (LocationProvider.Platform != SteamPlatform.Linux || string.IsNullOrEmpty(root))
            return (0, SteamAccountMethod.None);

        lock (_accountGate)
        {
            var now = Environment.TickCount64;
            if (_account is { } cached && cached.Root == root && now - cached.At < AccountCacheDuration.TotalMilliseconds)
                return (cached.Id, cached.Method);
            var (id, method) = SteamAccounts.DetectLinux(root);
            _account = (id, method, root, now);
            return (id, method);
        }
    }

    /// <summary>
    /// Cheap change marker for the local achievement state of a game: the last write time of
    /// SteamappcachestatsUserGameStats_{account}_{app}.bin (0 when unavailable). Steam rewrites this file the moment
    /// an achievement unlocks, so polling the timestamp detects unlocks without any network request.
    /// </summary>
    public long GetLocalStatsStamp(uint appId)
    {
        var steamPath = GetSteamPath();
        var accountId = GetActiveAccountId();
        if (string.IsNullOrEmpty(steamPath) || accountId == 0 || appId == 0) return 0;

        try
        {
            var path = Path.Combine(steamPath, "appcache", "stats", $"UserGameStats_{accountId}_{appId}.bin");
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Own playtime and last session of one game from Steam's localconfig.vdf (userdata/&lt;account&gt;/config): the app
    /// block holds "Playtime" / "Playtime2wks" in minutes and "LastPlayed" as unix time. Null when not available.
    /// </summary>
    public (int PlaytimeMinutes, int Playtime2WeeksMinutes, DateTimeOffset? LastPlayed)? ReadLocalPlaytime(uint appId)
    {
        var steamPath = GetSteamPath();
        var accountId = GetActiveAccountId();
        if (string.IsNullOrEmpty(steamPath) || accountId == 0) return null;

        var path = Path.Combine(steamPath, "userdata", accountId.ToString(System.Globalization.CultureInfo.InvariantCulture), "config", "localconfig.vdf");
        if (!File.Exists(path)) return null;

        try
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                text = reader.ReadToEnd();
            }

            // The app id occurs several times (e.g. in cloud sections); the block with "LastPlayed" is the right one
            var key = $"\"{appId}\"";
            for (var at = text.IndexOf(key, StringComparison.Ordinal); at >= 0; at = text.IndexOf(key, at + key.Length, StringComparison.Ordinal))
            {
                var open = text.IndexOf('{', at + key.Length);
                if (open < 0) break;
                // Only whitespace may sit between the key and its block
                if (text.AsSpan(at + key.Length, open - at - key.Length).Trim().Length > 0) continue;

                var depth = 0;
                var close = open;
                for (; close < text.Length; close++)
                {
                    if (text[close] == '{') depth++;
                    else if (text[close] == '}' && --depth == 0) break;
                }
                var block = text[open..Math.Min(close + 1, text.Length)];
                var lastPlayed = ReadVdfNumber(block, "LastPlayed");
                if (lastPlayed is null) continue;

                return ((int)(ReadVdfNumber(block, "Playtime") ?? 0),
                        (int)(ReadVdfNumber(block, "Playtime2wks") ?? 0),
                        lastPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastPlayed.Value) : null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "localconfig.vdf nicht lesbar.");
        }
        return null;
    }

    // Value of a top-level "key" "number" pair inside a text VDF block (nested blocks are not searched separately)
    private static long? ReadVdfNumber(string block, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(block, $"\"{name}\"\\s+\"(\\d+)\"");
        return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    public LocalSchemaResult? ReadLocalAppData(uint appId, string preferredLanguage = "german")
    {
        var steamPath = GetSteamPath();
        if (string.IsNullOrEmpty(steamPath)) return null;

        var statsDir = Path.Combine(steamPath, "appcache", "stats");
        var schemaPath = Path.Combine(statsDir, $"UserGameStatsSchema_{appId}.bin");
        if (!File.Exists(schemaPath)) return null;

        try
        {
            byte[] schemaBytes;
            using (var fs = new FileStream(schemaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                schemaBytes = new byte[fs.Length];
                fs.ReadExactly(schemaBytes);
            }

            var schemaRoot = ParseBinaryVdf(schemaBytes);
            var appNode = schemaRoot.Children.Values.FirstOrDefault();
            if (appNode is null) return null;

            var gameName = appNode.Strings.TryGetValue("gamename", out var gn) ? gn : string.Empty;

            var accountId = GetActiveAccountId();
            VdfNode? userRoot = null;
            if (accountId > 0)
            {
                var userStatsPath = Path.Combine(statsDir, $"UserGameStats_{accountId}_{appId}.bin");
                if (File.Exists(userStatsPath))
                {
                    byte[] userBytes;
                    using (var fs = new FileStream(userStatsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        userBytes = new byte[fs.Length];
                        fs.ReadExactly(userBytes);
                    }
                    var parsedUser = ParseBinaryVdf(userBytes);
                    userRoot = parsedUser.Children.Values.FirstOrDefault();
                }
            }

            Dictionary<uint, (uint Bits, Dictionary<uint, uint> UnlockTimes)> userProgress = [];
            if (userRoot is not null)
            {
                // Current Steam clients keep the stat blocks directly below "cache"; older ones nested them in "stats".
                var userStatsNode = userRoot.Children.TryGetValue("stats", out var nested) ? nested : userRoot;
                foreach (var statKv in userStatsNode.Children)
                {
                    if (!uint.TryParse(statKv.Key, out var statId)) continue;
                    var statNode = statKv.Value;
                    var bits = statNode.Ints.TryGetValue("data", out var d) ? d : 0u;
                    var times = new Dictionary<uint, uint>();

                    if (statNode.Children.TryGetValue("AchievementTimes", out var timesNode))
                    {
                        foreach (var timeKv in timesNode.Ints)
                        {
                            if (uint.TryParse(timeKv.Key, out var bitIdx))
                            {
                                times[bitIdx] = timeKv.Value;
                            }
                        }
                    }
                    userProgress[statId] = (bits, times);
                }
            }

            // Plain stats (no "bits"): name -> current value of this player, for the achievement progress counters
            var statValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (appNode.Children.TryGetValue("stats", out var allStats))
            {
                foreach (var statEntry in allStats.Children)
                {
                    if (!uint.TryParse(statEntry.Key, out var statId) || statEntry.Value.Children.ContainsKey("bits")) continue;
                    if (!statEntry.Value.Strings.TryGetValue("name", out var statName) || string.IsNullOrEmpty(statName)) continue;
                    var raw = userProgress.TryGetValue(statId, out var userStat) ? userStat.Bits : 0u;
                    // Stat type 2 = float, everything else is an integer (stored as the raw 32 bits)
                    var isFloat = ReadNumber(statEntry.Value, "type") == 2;
                    statValues[statName] = isFloat ? BitConverter.Int32BitsToSingle(unchecked((int)raw)) : unchecked((int)raw);
                }
            }

            var achievements = new List<LocalAchievementStat>();
            if (appNode.Children.TryGetValue("stats", out var statsNode))
            {
                foreach (var statEntry in statsNode.Children)
                {
                    if (!uint.TryParse(statEntry.Key, out var statBlockId)) continue;
                    var statObj = statEntry.Value;

                    if (!statObj.Children.TryGetValue("bits", out var bitsNode)) continue;

                    userProgress.TryGetValue(statBlockId, out var userBlock);

                    foreach (var bitEntry in bitsNode.Children)
                    {
                        if (!uint.TryParse(bitEntry.Key, out var bitIndex)) continue;
                        var bitObj = bitEntry.Value;

                        var apiName = bitObj.Strings.TryGetValue("name", out var n) ? n : $"ACH_{statBlockId}_{bitIndex}";
                        var displayName = apiName;
                        var description = string.Empty;
                        var englishName = string.Empty;
                        var isLocalized = false;
                        var isHidden = false;

                        if (bitObj.Children.TryGetValue("display", out var dispNode))
                        {
                            if (dispNode.Children.TryGetValue("name", out var nameNode))
                            {
                                if (nameNode.Strings.TryGetValue("english", out var originalName)) englishName = originalName;
                                if (nameNode.Strings.TryGetValue(preferredLanguage, out var langName) && !string.IsNullOrWhiteSpace(langName))
                                {
                                    displayName = langName;
                                    isLocalized = true;
                                }
                                else if (preferredLanguage != "english" && nameNode.Strings.TryGetValue("german", out var deName) && !string.IsNullOrWhiteSpace(deName))
                                    displayName = deName;
                                else if (nameNode.Strings.TryGetValue("english", out var enName) && !string.IsNullOrWhiteSpace(enName))
                                    displayName = enName;
                            }

                            if (dispNode.Children.TryGetValue("desc", out var descNode))
                            {
                                if (descNode.Strings.TryGetValue(preferredLanguage, out var langDesc) && !string.IsNullOrWhiteSpace(langDesc))
                                    description = langDesc;
                                else if (preferredLanguage != "english" && descNode.Strings.TryGetValue("german", out var deDesc) && !string.IsNullOrWhiteSpace(deDesc))
                                    description = deDesc;
                                else if (descNode.Strings.TryGetValue("english", out var enDesc) && !string.IsNullOrWhiteSpace(enDesc))
                                    description = enDesc;
                            }

                            if (dispNode.Strings.TryGetValue("hidden", out var hidStr) && hidStr == "1")
                                isHidden = true;
                            else if (dispNode.Ints.TryGetValue("hidden", out var hidInt) && hidInt == 1)
                                isHidden = true;
                        }

                        var iconFile = string.Empty;
                        var iconGrayFile = string.Empty;
                        if (bitObj.Children.TryGetValue(" display", out var dispSpaceNode) ||
                            bitObj.Children.TryGetValue("display", out dispSpaceNode))
                        {
                            if (dispSpaceNode.Strings.TryGetValue("icon", out var ic)) iconFile = ic;
                            if (dispSpaceNode.Strings.TryGetValue("icon_gray", out var icg)) iconGrayFile = icg;
                        }

                        var iconUrl = !string.IsNullOrEmpty(iconFile)
                            ? $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{iconFile}"
                            : string.Empty;
                        var iconGrayUrl = !string.IsNullOrEmpty(iconGrayFile)
                            ? $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{iconGrayFile}"
                            : iconUrl;

                        var achieved = false;
                        DateTime? unlockTime = null;

                        if (bitIndex < 32)
                        {
                            var mask = 1u << (int)bitIndex;
                            achieved = (userBlock.Bits & mask) != 0;
                            if (achieved && userBlock.UnlockTimes is not null &&
                                userBlock.UnlockTimes.TryGetValue(bitIndex, out var unixTs) && unixTs > 0)
                            {
                                unlockTime = DateTimeOffset.FromUnixTimeSeconds(unixTs).LocalDateTime;
                            }
                        }

                        var (progressCurrent, progressMax) = ReadProgress(bitObj, statValues);

                        achievements.Add(new LocalAchievementStat
                        {
                            ApiName = apiName,
                            DisplayName = displayName,
                            Description = description,
                            IsLocalized = isLocalized,
                            EnglishName = englishName,
                            Icon = iconUrl,
                            IconGray = iconGrayUrl,
                            IsHidden = isHidden,
                            Achieved = achieved,
                            UnlockTime = unlockTime,
                            ProgressCurrent = progressCurrent,
                            ProgressMax = progressMax
                        });
                    }
                }
            }

            return new LocalSchemaResult
            {
                GameName = gameName,
                Achievements = achievements
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fehler beim Parsen der lokalen Binär-Statistiken für App {AppId}", appId);
            return null;
        }
    }

    /// <summary>
    /// Steam's progress definition of one achievement: progress { min_val, max_val, value { operation "statvalue",
    /// operand1 = stat name } }. Returns (current, max) clamped to the range, or nulls when the game defines none.
    /// </summary>
    private static (double? Current, double? Max) ReadProgress(VdfNode achievement, Dictionary<string, double> statValues)
    {
        if (!achievement.Children.TryGetValue("progress", out var progress)) return (null, null);
        var max = ReadNumber(progress, "max_val");
        var min = ReadNumber(progress, "min_val") ?? 0;
        if (max is not > 0 || !progress.Children.TryGetValue("value", out var value)) return (null, null);
        if (!value.Strings.TryGetValue("operation", out var operation) ||
            !string.Equals(operation, "statvalue", StringComparison.OrdinalIgnoreCase) ||
            !value.Strings.TryGetValue("operand1", out var statName) ||
            !statValues.TryGetValue(statName, out var current))
        {
            return (null, null);
        }

        return (Math.Clamp(current - min, 0, max.Value - min), max.Value - min);
    }

    private static double? ReadNumber(VdfNode node, string key)
    {
        if (node.Ints.TryGetValue(key, out var number)) return unchecked((int)number);
        return node.Strings.TryGetValue(key, out var text) &&
               double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static VdfNode ParseBinaryVdf(byte[] data)
    {
        var root = new VdfNode { Name = "root" };
        var pos = 0;
        ReadNodeChildren(data, ref pos, root);
        return root;
    }

    private static void ReadNodeChildren(byte[] data, ref int pos, VdfNode parent)
    {
        while (pos < data.Length)
        {
            var type = data[pos++];
            if (type == 0x08)
            {
                break;
            }

            var key = ReadNullTerminatedString(data, ref pos);

            switch (type)
            {
                case 0x00:
                    var child = new VdfNode { Name = key };
                    ReadNodeChildren(data, ref pos, child);
                    parent.Children[key] = child;
                    break;
                case 0x01:
                    var strVal = ReadNullTerminatedString(data, ref pos);
                    parent.Strings[key] = strVal;
                    break;
                case 0x02:
                case 0x03:
                case 0x04:
                case 0x06:
                    if (pos + 4 <= data.Length)
                    {
                        var intVal = BitConverter.ToUInt32(data, pos);
                        pos += 4;
                        parent.Ints[key] = intVal;
                    }
                    else
                    {
                        pos = data.Length;
                    }
                    break;
                case 0x05:
                    var wStr = ReadNullTerminatedWideString(data, ref pos);
                    parent.Strings[key] = wStr;
                    break;
                case 0x07:
                case 0x0A:
                    pos = Math.Min(data.Length, pos + 8);
                    break;
                default:
                    return;
            }
        }
    }

    private static string ReadNullTerminatedString(byte[] data, ref int pos)
    {
        var start = pos;
        while (pos < data.Length && data[pos] != 0)
        {
            pos++;
        }
        var result = Encoding.UTF8.GetString(data, start, pos - start);
        if (pos < data.Length) pos++;
        return result;
    }

    private static string ReadNullTerminatedWideString(byte[] data, ref int pos)
    {
        var start = pos;
        while (pos + 1 < data.Length && (data[pos] != 0 || data[pos + 1] != 0))
        {
            pos += 2;
        }
        var result = Encoding.Unicode.GetString(data, start, pos - start);
        if (pos + 1 < data.Length) pos += 2;
        return result;
    }
}

