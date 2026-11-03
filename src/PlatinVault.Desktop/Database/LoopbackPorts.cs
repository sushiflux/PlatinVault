using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace PlatinVault.Desktop.Database;

/// <summary>
/// Ports of the installed program that stay the same across restarts, so the window's origin (http://127.0.0.1:&lt;port&gt;) - and with it
/// everything the app keeps in localStorage (tour, display size, navigation) - stays too.
///
/// Why not a port-independent origin: WebView2 can map a fixed host name only onto a folder (SetVirtualHostNameToFolderMapping) or
/// answer requests itself (WebResourceRequested), but neither carries WebSockets, which SignalR's live trophies use - so the origin has
/// to be the real Kestrel address, and the port has to be stable.
///
/// Rules: a port is never taken from Windows' dynamic range (default 49152-65535: Hyper-V, WSL and Docker reserve blocks there,
/// sometimes different ones after every restart) and never from a reserved block (<c>netsh int ipv4 show excludedportrange
/// protocol=tcp</c>). The first choice is derived from the Windows account and the purpose, so even a reinstall finds the same port;
/// it is remembered in desktop.json. A remembered port that is busy right now is waited for a few seconds (a previous run may just be
/// ending) before another one is chosen.
/// </summary>
internal static partial class LoopbackPorts
{
    public const int PreferredFirst = 42000;
    public const int PreferredLast = 48999;
    public const int FallbackFirst = 30000;
    public const int FallbackLast = 41999;

    public sealed record Reserved(int DynamicFirst, int DynamicLast, IReadOnlyList<(int First, int Last)> Excluded)
    {
        public static readonly Reserved WindowsDefault = new(49152, 65535, []);

        public bool Contains(int port) =>
            (port >= DynamicFirst && port <= DynamicLast) || Excluded.Any(r => port >= r.First && port <= r.Last);
    }

    /// <summary>
    /// The port to listen on. A fixed port (development run, fixed by start-windows.ps1) is used as is and a busy one is an error - it is
    /// never replaced silently. Without one: the port remembered from the last run if it is still free, else a free one.
    /// </summary>
    public static int ChoosePort(int? fixedPort, int? remembered, Func<int, Exception> busy, string purpose = "port")
    {
        if (fixedPort is { } wanted)
        {
            if (!IsPortFree(wanted)) throw busy(wanted);
            return wanted;
        }

        var reserved = ReadReserved();
        if (remembered is { } stored && !reserved.Contains(stored))
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (IsPortFree(stored)) return stored;
                Thread.Sleep(250);
            }
            DesktopLog.Warn($"Gemerkter Port {stored} ({purpose}) ist belegt - wähle einen anderen (gespeicherte Ansichtseinstellungen des Fensters beginnen dann neu).");
        }
        return Pick(Seed(purpose), reserved, IsPortFree, remembered);
    }

    public static bool IsPortFree(int port)
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    /// <summary>Any free loopback port right now (tests).</summary>
    public static int FreeLoopbackPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>The stable choice; <paramref name="isFree"/> is the real bind check (replaced in tests).</summary>
    public static int Pick(string seed, Reserved reserved, Func<int, bool> isFree, int? remembered)
    {
        if (remembered is { } stored && !reserved.Contains(stored) && isFree(stored)) return stored;
        foreach (var port in Candidates(seed))
        {
            if (port == remembered || reserved.Contains(port)) continue;
            if (isFree(port)) return port;
        }
        throw new InvalidOperationException("No free loopback port outside the dynamic and reserved ranges.");
    }

    /// <summary>Every port of the preferred range, then of the fallback range, starting at a place derived from <paramref name="seed"/>.</summary>
    public static IEnumerable<int> Candidates(string seed)
    {
        var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(seed)), 0);
        foreach (var (first, last) in new[] { (PreferredFirst, PreferredLast), (FallbackFirst, FallbackLast) })
        {
            var size = last - first + 1;
            var start = (int)(hash % (uint)size);
            for (var i = 0; i < size; i++) yield return first + (start + i) % size;
        }
    }

    /// <summary>The seed: the Windows account (hashed SID) plus what the port is for (different purposes get different ports).</summary>
    public static string Seed(string purpose)
    {
        using var identity = WindowsIdentity.GetCurrent();
        return $"{identity.User?.Value ?? Environment.UserName}|{purpose}";
    }

    /// <summary>Windows' dynamic range and reserved blocks as netsh reports them; the default range when netsh cannot be asked.</summary>
    public static Reserved ReadReserved()
    {
        try
        {
            var dynamicNumbers = Numbers().Matches(Netsh("int ipv4 show dynamicport tcp")).Select(m => int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var (first, last) = dynamicNumbers.Count >= 2 ? (dynamicNumbers[0], dynamicNumbers[0] + dynamicNumbers[1] - 1) : (49152, 65535);
            return new Reserved(first, last, ParseExcluded(Netsh("int ipv4 show excludedportrange protocol=tcp")));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FormatException or OverflowException)
        {
            DesktopLog.Warn($"Portbereiche von Windows nicht lesbar ({ex.Message}) - nehme den Standardbereich an.");
            return Reserved.WindowsDefault;
        }
    }

    /// <summary>The "start end" pairs of netsh's excludedportrange table (headers are text in the Windows language, the rows only numbers).</summary>
    public static IReadOnlyList<(int First, int Last)> ParseExcluded(string output)
    {
        var ranges = new List<(int, int)>();
        foreach (var line in output.Split('\n'))
        {
            var match = RangeLine().Match(line);
            if (match.Success) ranges.Add((int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
        return ranges;
    }

    private static string Netsh(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("netsh did not start");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        return output;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    [GeneratedRegex(@"^\s*(\d+)\s+(\d+)\s*\*?\s*$")]
    private static partial Regex RangeLine();
}
