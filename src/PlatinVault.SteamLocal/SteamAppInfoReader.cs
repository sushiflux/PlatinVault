using System.Buffers.Binary;
using System.Text;

namespace PlatinVault.SteamLocal;

/// <summary>What the Steam client keeps about one app in the <c>common</c> section of its local <c>appcache/appinfo.vdf</c>.</summary>
/// <param name="Type">"Game", "DLC", "Tool", ... (null when the app has no <c>common</c> section).</param>
/// <param name="ReleaseUnix">
/// <c>steam_release_date</c> (Unix seconds), else <c>original_release_date</c>; null when the app has none or it is 0.
/// </param>
/// <param name="Metacritic"><c>metacritic_score</c> 0-100; null when Steam knows none.</param>
/// <param name="ReviewPercent"><c>review_percentage</c>: share of positive user reviews, 0-100; null when the app has no review score.</param>
/// <param name="ReviewScore"><c>review_score</c> 1-9 (Steam's rating scale, 9 = "Overwhelmingly Positive"); null when unrated.</param>
/// <param name="InstallSizeBytes">Estimated install size of the base game for Windows from the app's depots (<see cref="SteamAppInfoReader"/>); null when the app has no depot data.</param>
public sealed record AppInfoCommon(string? Type, long? ReleaseUnix, int? Metacritic, int? ReviewPercent, int? ReviewScore, long? InstallSizeBytes = null);

/// <summary>
/// Reads the few facts the library sort orders need from Steam's binary <c>appcache/appinfo.vdf</c> (the client's local copy of the
/// store data of every app it knows). Read only; nothing is written and no account data is touched (the file holds public app data).
/// <para>
/// Format (binary KeyValues, as in the open-source readers of SteamDatabase / SteamKit / ValveKeyValue): a header of
/// <c>uint32 magic</c> (<c>0x07564428</c> = v40, <c>0x07564429</c> = v41) and <c>uint32 universe</c>; v41 then has <c>int64
/// stringTableOffset</c> - the key names live in a table at the end of the file and the keys inside the entries are indexes into
/// it, in v40 the keys are inline NUL-terminated strings. Then one entry per app: <c>uint32 appid</c> (0 ends the list),
/// <c>uint32 size</c>, and <c>size</c> bytes holding 60 bytes of metadata (state, update time, token, two SHA-1, change number)
/// followed by the app's KeyValues (<c>appinfo</c> -> <c>common</c> -> ...).
/// Verified against the v41 file of this PC (2182 entries). The v40 layout (inline keys, same 60 byte entry head) follows the open-source
/// readers SCSKiller and DroidDeck; other readers (raycast "next-game") assume a string table for v40 too, so it is covered by unit tests
/// with a synthetic file only and not verified against a real v40 file. Any other magic number (e.g. 0x0756442A) is reported as
/// unsupported instead of guessed at - the dashboard then gets release date / reviews from Steam's store.
/// </para>
/// </summary>
public static class SteamAppInfoReader
{
    private const uint MagicV40 = 0x07564428;
    private const uint MagicV41 = 0x07564429;
    private const int EntryMetadataBytes = 60;


    /// <summary>The facts of every app in the file; null when the file is missing, unreadable or in a format this reader does not know.</summary>
    public static Dictionary<int, AppInfoCommon>? ReadFile(string path)
    {
        try
        {
            return Parse(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses the content of an appinfo.vdf; null for an unknown format or a damaged file (never throws on bad bytes).</summary>
    public static Dictionary<int, AppInfoCommon>? Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < 8) return null;
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (magic is not (MagicV40 or MagicV41)) return null;

            string[]? strings = null;
            var pos = 8;
            if (magic == MagicV41)
            {
                var tableOffset = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
                pos = 16;
                if (tableOffset < pos || tableOffset >= data.Length - 3) return null;
                strings = ReadStringTable(data, (int)tableOffset);
            }

            var result = new Dictionary<int, AppInfoCommon>();
            var listEnd = strings is null ? data.Length : (int)BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
            while (pos + 8 <= listEnd)
            {
                var appId = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
                if (appId == 0) break;
                var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]);
                var start = pos + 8;
                var end = (long)start + size;
                if (size < EntryMetadataBytes || end > listEnd) return null;

                var reader = new KvReader(data, start + EntryMetadataBytes, (int)end, strings);
                var common = reader.ReadCommon();
                if (common is not null && appId <= int.MaxValue) result[(int)appId] = common;
                pos = (int)end;
            }
            return result;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }
    }

    private static string[] ReadStringTable(ReadOnlySpan<byte> data, int offset)
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        // Each name takes at least its terminating NUL byte: a larger count than the bytes left means a damaged file
        if (count > data.Length - offset - 4) throw new InvalidDataException("String table larger than the file.");
        var table = new string[count];
        var p = offset + 4;
        for (var i = 0; i < count; i++)
        {
            var terminator = data[p..].IndexOf((byte)0);
            if (terminator < 0) throw new InvalidDataException("Unterminated string table entry.");
            table[i] = Encoding.UTF8.GetString(data.Slice(p, terminator));
            p += terminator + 1;
        }
        return table;
    }

    /// <summary>One depot of an app (<c>appinfo/depots/&lt;id&gt;</c>), as far as the install size needs it.</summary>
    private sealed class Depot
    {
        public long? PublicSize;
        public bool Dlc;
        public bool SteamDeck;
        public string OsList = string.Empty;
        public string OsArch = string.Empty;
        public string Language = string.Empty;
    }

    /// <summary>One depot as the size calculation sees it (see <see cref="InstallSizeOf"/>).</summary>
    internal readonly record struct DepotInfo(long? Size, bool Dlc, bool SteamDeck, string OsList, string OsArch, string Language);

    /// <summary>
    /// Estimated size of the installed base game for Windows: the sum of the <c>public</c> manifest sizes of the depots that apply -
    /// no DLC depots (<c>dlcappid</c>; whether the user owns the DLC is not known), no Steam Deck depots (<c>config.steamdeck</c>), no
    /// depots of other systems (<c>oslist</c> set and without windows), only the default language (no <c>language</c> or "english"), and
    /// no 32-bit depot when a 64-bit one exists. Null when no depot applies (the app has no depot data). It is an estimate: where an app
    /// has several depots that differ in nothing the file shows (e.g. voice packs), all of them count, so the result can be too high -
    /// never too low (compared with the real SizeOnDisk of 46 installed games: 37 within 5 %, 40 within 15 %, none lower).
    /// </summary>
    internal static long? InstallSizeOf(IReadOnlyList<DepotInfo> depots)
    {
        var applicable = depots
            .Where(d => d.Size is > 0 && !d.Dlc && !d.SteamDeck && !IsOtherSystem(d.OsList) && (d.Language.Length == 0 || d.Language == "english"))
            .ToList();
        var has64 = applicable.Any(d => d.OsArch == "64");
        var total = applicable.Where(d => !(has64 && d.OsArch == "32")).Sum(d => d.Size!.Value);
        return total > 0 ? total : null;
    }

    // oslist is a comma separated list ("windows,macos"); empty = every system
    private static bool IsOtherSystem(string osList) =>
        osList.Length > 0 && !osList.Split(',').Any(os => os.Trim().Equals("windows", StringComparison.OrdinalIgnoreCase));

    /// <summary>Walks one entry's KeyValues; keeps values only of appinfo -> common and appinfo -> depots, skips everything else.</summary>
    private ref struct KvReader(ReadOnlySpan<byte> data, int pos, int end, string[]? strings)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private readonly int _end = end;
        private readonly string[]? _strings = strings;
        private readonly List<string> _path = [];
        private readonly List<Depot> _depots = [];
        private Depot? _depot;
        private int _pos = pos;
        private string? _type;
        private long? _steamRelease;
        private long? _originalRelease;
        private int? _metacritic;
        private int? _reviewPercent;
        private int? _reviewScore;
        private bool _foundCommon;

        public AppInfoCommon? ReadCommon()
        {
            Walk();
            if (!_foundCommon) return null;
            var release = _steamRelease is > 0 ? _steamRelease : _originalRelease is > 0 ? _originalRelease : null;
            var depots = _depots.Select(d => new DepotInfo(d.PublicSize, d.Dlc, d.SteamDeck, d.OsList, d.OsArch, d.Language)).ToList();
            return new AppInfoCommon(_type, release, _metacritic, _reviewPercent, _reviewScore, InstallSizeOf(depots));
        }

        // Reads the members of one object up to its end marker
        private void Walk()
        {
            while (_pos < _end)
            {
                var type = _data[_pos++];
                if (type == 8) return;
                var key = ReadKey();
                if (type == 0)
                {
                    _path.Add(key);
                    Enter();
                    Walk();
                    Leave();
                    _path.RemoveAt(_path.Count - 1);
                    continue;
                }

                string? text = null;
                long? number = null;
                switch (type)
                {
                    case 1:
                        text = ReadString();
                        break;
                    case 2:
                    case 4:
                    case 6:
                        number = BinaryPrimitives.ReadInt32LittleEndian(_data[_pos..]);
                        _pos += 4;
                        break;
                    case 3:
                        _pos += 4;
                        break;
                    case 5:
                        // UTF-16 string: skip to the double-NUL terminator
                        while (BinaryPrimitives.ReadUInt16LittleEndian(_data[_pos..]) != 0) _pos += 2;
                        _pos += 2;
                        break;
                    case 7:
                    case 10:
                        number = BinaryPrimitives.ReadInt64LittleEndian(_data[_pos..]);
                        _pos += 8;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown KeyValues type {type}.");
                }
                OnValue(key, text, number);
            }
        }

        private readonly bool AtCommon => _path.Count == 2 && _path[0] == "appinfo" && _path[1] == "common";

        private readonly bool AtDepot => _path.Count == 3 && _path[0] == "appinfo" && _path[1] == "depots" && IsNumber(_path[2]);

        private static bool IsNumber(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

        private void Enter()
        {
            if (AtCommon) _foundCommon = true;
            if (AtDepot) _depot = new Depot();
        }

        private void Leave()
        {
            if (AtDepot && _depot is not null)
            {
                _depots.Add(_depot);
                _depot = null;
            }
        }

        private void OnValue(string key, string? text, long? number)
        {
            if (AtCommon)
            {
                switch (key)
                {
                    case "type": _type = text; break;
                    case "steam_release_date": _steamRelease = number; break;
                    case "original_release_date": _originalRelease = number; break;
                    case "metacritic_score": _metacritic = (int?)number; break;
                    case "review_percentage": _reviewPercent = (int?)number; break;
                    case "review_score": _reviewScore = (int?)number; break;
                }
                return;
            }

            // Inside a depot: appinfo / depots / <id> [/ config | / manifests / public]
            if (_depot is null) return;
            if (_path.Count == 3 && key == "dlcappid")
            {
                _depot.Dlc = true;
            }
            else if (_path.Count == 4 && _path[3] == "config")
            {
                // osarch is a string in some apps and a number in others
                var value = text ?? number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                switch (key)
                {
                    case "oslist": _depot.OsList = value; break;
                    case "osarch": _depot.OsArch = value; break;
                    case "language": _depot.Language = value; break;
                    case "steamdeck": _depot.SteamDeck = true; break;
                }
            }
            else if (_path.Count == 5 && _path[3] == "manifests" && _path[4] == "public" && key == "size")
            {
                // Sizes above 2 GB are stored as text (a KeyValues integer has 32 bits), smaller ones as numbers
                _depot.PublicSize = number ?? (long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null);
            }
        }

        private string ReadKey()
        {
            if (_strings is not null)
            {
                var index = BinaryPrimitives.ReadUInt32LittleEndian(_data[_pos..]);
                _pos += 4;
                if (index >= _strings.Length) throw new InvalidDataException("Key index outside the string table.");
                return _strings[index];
            }
            return ReadString();
        }

        private string ReadString()
        {
            var terminator = _data[_pos.._end].IndexOf((byte)0);
            if (terminator < 0) throw new InvalidDataException("Unterminated string.");
            var text = Encoding.UTF8.GetString(_data.Slice(_pos, terminator));
            _pos += terminator + 1;
            return text;
        }
    }
}
