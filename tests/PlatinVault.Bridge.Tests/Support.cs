using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PlatinVault.Bridge.Tests;

/// <summary>The bridges share static state (log file, language): every test class that touches it runs one after the other.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BridgeGlobals
{
    public const string Name = "bridge globals";
}

/// <summary>A temporary folder that is removed again.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pv-bridge-test-" + Guid.NewGuid().ToString("N")[..10]);

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Scripted <see cref="ILocalSteam"/>.</summary>
internal sealed class ScriptedSteam : ILocalSteam
{
    public bool Available { get; set; } = true;
    public uint RunningAppId { get; set; }
    public uint AccountId { get; set; } = 12345;
    public long Stamp { get; set; } = 1;
    public LocalAppData? Data { get; set; }
    public BridgeLibraryDto? Library { get; set; }
    public Queue<LocalAppData?> ReadQueue { get; } = new();

    public bool IsAvailable => Available;
    public uint GetRunningAppId() => RunningAppId;
    public long GetStatsStamp(uint appId) => Stamp;
    public uint GetActiveAccountId() => AccountId;
    public LocalAppData? ReadAppData(uint appId) => ReadQueue.Count > 0 ? ReadQueue.Dequeue() : Data;
    public BridgeLibraryDto? ReadLibrary() => Library;
}

/// <summary>An HttpMessageHandler that answers from a function and records the requests (body included).</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>
/// A tiny HTTP server on a loopback port that plays the PlatinVault server for the bridge: records what arrives and answers the three
/// endpoints (state, commands, library) and the binary download. Plain TCP, so no URL reservation and no admin rights are needed.
/// </summary>
internal sealed class FakeBridgeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accept;
    private readonly object _gate = new();

    public FakeBridgeServer()
    {
        _listener.Start();
        _accept = Task.Run(AcceptLoopAsync);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    /// <summary>The token the server accepts; any other bearer token gets 401.</summary>
    public string Token { get; set; } = "token-for-the-tests-1234567890";

    public string? UiLanguage { get; set; } = "de";
    public string? LatestVersion { get; set; }
    public string? Sha256 { get; set; }
    public byte[] Binary { get; set; } = [];

    /// <summary>Commands the next command polls answer with (until a result for their id arrives).</summary>
    public List<(string Id, long AppId, string Type)> Commands { get; } = [];

    public List<RecordedRequest> Requests { get; } = [];

    public IReadOnlyList<RecordedRequest> Snapshot()
    {
        lock (_gate) return Requests.ToList();
    }

    public async Task<RecordedRequest?> WaitForAsync(Func<RecordedRequest, bool> predicate, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var found = Snapshot().FirstOrDefault(predicate);
            if (found is not null) return found;
            await Task.Delay(50);
        }
        return null;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            while (!_stop.IsCancellationRequested)
            {
                var request = await ReadRequestAsync(stream);
                if (request is null) return;
                lock (_gate) Requests.Add(request);

                var (status, contentType, body) = Respond(request);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: keep-alive\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // the bridge closed the connection
        }
    }

    private static async Task<RecordedRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var headerBytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one) == 0) return null;
            headerBytes.Add(one[0]);
            var n = headerBytes.Count;
            if (n >= 4 && headerBytes[n - 4] == '\r' && headerBytes[n - 3] == '\n' && headerBytes[n - 2] == '\r' && headerBytes[n - 1] == '\n') break;
        }

        var lines = Encoding.ASCII.GetString([.. headerBytes]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var first = lines[0].Split(' ');
        var headers = lines.Skip(1).Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
        var length = headers.TryGetValue("Content-Length", out var raw) ? int.Parse(raw) : 0;
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var got = await stream.ReadAsync(body.AsMemory(read));
            if (got == 0) return null;
            read += got;
        }
        return new RecordedRequest(first[0], first[1], headers, Encoding.UTF8.GetString(body));
    }

    private (string Status, string ContentType, byte[] Body) Respond(RecordedRequest request)
    {
        static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

        if (request.Headers.GetValueOrDefault("Authorization") != $"Bearer {Token}")
            return ("401 Unauthorized", "application/problem+json", Utf8("{\"title\":\"Bridge not authorized\",\"detail\":\"The bridge token is missing or invalid.\"}"));

        var path = request.Target.Split('?')[0];
        switch (request.Method + " " + path)
        {
            case "POST /api/bridge/state":
                var reply = $"{{\"ok\":true,\"latestBridgeVersion\":{Quote(LatestVersion)},\"bridgeSha256\":{Quote(Sha256)},\"uiLanguage\":{Quote(UiLanguage)}}}";
                return ("200 OK", "application/json", Utf8(reply));

            case "POST /api/bridge/commands":
                var done = System.Text.Json.JsonDocument.Parse(request.Body).RootElement.TryGetProperty("results", out var results)
                    ? results.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToHashSet()
                    : [];
                lock (_gate) Commands.RemoveAll(c => done.Contains(c.Id));
                string list;
                lock (_gate)
                    list = string.Join(",", Commands.Select(c => $"{{\"id\":\"{c.Id}\",\"type\":\"{c.Type}\",\"appId\":{c.AppId},\"gameName\":\"Portal\"}}"));
                return ("200 OK", "application/json", Utf8($"{{\"ok\":true,\"commands\":[{list}]}}"));

            case "POST /api/bridge/library":
                return ("200 OK", "application/json", Utf8("{\"ok\":true}"));

            case "GET /api/bridge/exe":
                return ("200 OK", "application/octet-stream", Binary);

            default:
                return ("404 Not Found", "application/json", Utf8("{}"));
        }
    }

    private static string Quote(string? value) => value is null ? "null" : "\"" + value + "\"";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _accept; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}

internal sealed record RecordedRequest(string Method, string Target, Dictionary<string, string> Headers, string Body);
