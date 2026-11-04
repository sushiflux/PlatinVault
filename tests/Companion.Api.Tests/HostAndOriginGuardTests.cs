using System.Net;
using System.Net.WebSockets;
using Companion.Api.Configuration;
using Companion.Api.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Companion.Api.Tests;

/// <summary>
/// Cross-site WebSocket hijacking (a foreign page listening to the live hub) and DNS rebinding (a foreign domain pointed at the server):
/// the pure rules of <see cref="HubOriginGuard"/> and <see cref="HostGuard"/>, and both against the real server on a loopback port.
/// </summary>
public sealed class HostAndOriginGuardTests
{
    private static readonly string[] NoOrigins = [];

    // ---- hub origin: rules ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, "http", "127.0.0.1:5233", true)]                       // no Origin: not a browser (bridge, scripts)
    [InlineData("", "http", "127.0.0.1:5233", true)]
    [InlineData("http://127.0.0.1:43120", "http", "127.0.0.1:43120", true)]  // Windows program: WebView2 on the server's own origin
    [InlineData("http://192.168.1.20:5233", "http", "192.168.1.20:5233", true)] // Docker/Unraid opened by LAN IP
    [InlineData("http://tower:5233", "http", "TOWER:5233", true)]            // host names compare case-insensitively
    [InlineData("http://[::1]:5233", "http", "[::1]:5233", true)]
    [InlineData("http://example.test", "http", "example.test:80", true)]     // default port written or not
    [InlineData("https://evil.example", "http", "127.0.0.1:5233", false)]
    [InlineData("http://192.168.1.20:8080", "http", "192.168.1.20:5233", false)] // another app on the same box
    [InlineData("https://192.168.1.20:5233", "http", "192.168.1.20:5233", false)] // other scheme
    [InlineData("null", "http", "127.0.0.1:5233", false)]                    // sandboxed frame / file page
    [InlineData("http://127.0.0.1:5230", "http", "127.0.0.1:5231", false)]   // loopback, but outside Development
    public void The_hub_accepts_only_its_own_origin(string? origin, string scheme, string host, bool allowed) =>
        Assert.Equal(allowed, HubOriginGuard.IsAllowed(origin, scheme, host, null, null, development: false, NoOrigins));

    [Fact]
    public void In_development_every_loopback_origin_is_accepted_for_the_vite_proxies()
    {
        // vite.config.ts proxies /hubs with changeOrigin: origin localhost:5230, host localhost:5231 (desktop dev: 5236 -> 5234)
        Assert.True(HubOriginGuard.IsAllowed("http://localhost:5230", "http", "localhost:5231", null, null, development: true, NoOrigins));
        Assert.True(HubOriginGuard.IsAllowed("http://127.0.0.1:5236", "http", "127.0.0.1:5234", null, null, development: true, NoOrigins));
        Assert.True(HubOriginGuard.IsAllowed("http://platinvault.localhost:5230", "http", "localhost:5231", null, null, development: true, NoOrigins));
        Assert.False(HubOriginGuard.IsAllowed("http://evil.example:5230", "http", "localhost:5231", null, null, development: true, NoOrigins));
    }

    [Fact]
    public void Behind_a_reverse_proxy_the_forwarded_scheme_and_host_count_and_a_configured_origin_is_accepted()
    {
        // TLS ends at the proxy, which passes the public scheme/host on
        Assert.True(HubOriginGuard.IsAllowed("https://pv.example.com", "http", "pv.example.com", "https", null, false, NoOrigins));
        Assert.True(HubOriginGuard.IsAllowed("https://pv.example.com", "http", "platinvault:8080", "https, http", "pv.example.com", false, NoOrigins));
        Assert.False(HubOriginGuard.IsAllowed("https://pv.example.com", "http", "platinvault:8080", null, null, false, NoOrigins));

        var configured = HubOriginGuard.ReadAllowedOrigins(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [HubOriginGuard.EnvironmentVariable] = "https://pv.example.com/, not an origin",
            [HubOriginGuard.AllowedOriginsKey + ":0"] = "HTTPS://Other.Example:8443",
        }).Build());
        Assert.Equal(["https://pv.example.com:443", "https://other.example:8443"], configured);
        Assert.True(HubOriginGuard.IsAllowed("https://pv.example.com", "http", "platinvault:8080", null, null, false, configured));
        Assert.True(HubOriginGuard.IsAllowed("https://other.example:8443", "http", "platinvault:8080", null, null, false, configured));
        Assert.False(HubOriginGuard.IsAllowed("https://other.example", "http", "platinvault:8080", null, null, false, configured));
    }

    // ---- hub origin: real server ---------------------------------------------------------------------------------------------

    private static HttpRequestMessage Negotiate(string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/achievements/negotiate?negotiateVersion=1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        return request;
    }

    private static async Task<HttpStatusCode?> WebSocketStatusAsync(Uri baseAddress, string? origin)
    {
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        if (origin is not null) socket.Options.SetRequestHeader("Origin", origin);
        var uri = new UriBuilder(baseAddress) { Scheme = "ws", Path = "/hubs/achievements" }.Uri;
        try
        {
            await socket.ConnectAsync(uri, CancellationToken.None);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            return HttpStatusCode.SwitchingProtocols;
        }
        catch (WebSocketException)
        {
            return socket.HttpStatusCode;
        }
    }

    [Fact]
    public async Task A_foreign_origin_is_refused_for_negotiate_and_websocket_and_the_own_origin_works()
    {
        await using var server = await TestServer.StartAsync(null);
        var own = server.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        Assert.Equal(HttpStatusCode.Forbidden, (await server.Client.SendAsync(Negotiate("https://evil.example"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await WebSocketStatusAsync(server.Client.BaseAddress!, "https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, await WebSocketStatusAsync(server.Client.BaseAddress!, "null"));

        Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(Negotiate(own))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(Negotiate(null))).StatusCode);
        Assert.Equal(HttpStatusCode.SwitchingProtocols, await WebSocketStatusAsync(server.Client.BaseAddress!, own));
        Assert.Equal(HttpStatusCode.SwitchingProtocols, await WebSocketStatusAsync(server.Client.BaseAddress!, null));

        // The rest of the API is not affected by the hub check
        var api = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        api.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(api)).StatusCode);
    }

    [Fact]
    public async Task A_configured_origin_and_in_development_a_loopback_origin_are_accepted()
    {
        await using var server = await TestServer.StartAsync(null, b =>
        {
            b.Environment.EnvironmentName = Environments.Development;
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [HubOriginGuard.EnvironmentVariable] = "https://pv.example.com" });
        });

        Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(Negotiate("http://localhost:5230"))).StatusCode);
        Assert.Equal(HttpStatusCode.SwitchingProtocols, await WebSocketStatusAsync(server.Client.BaseAddress!, "http://127.0.0.1:5236"));
        Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(Negotiate("https://pv.example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await server.Client.SendAsync(Negotiate("https://evil.example"))).StatusCode);
    }

    // ---- host guard: rules -----------------------------------------------------------------------------------------------------

    private static HostGuard.Rules Rules(params (string Key, string? Value)[] values) =>
        HostGuard.ReadRules(new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build())!;

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.5")]
    [InlineData("[::1]")]
    [InlineData("[fe80::1]")]
    [InlineData("::1")]
    [InlineData("localhost")]
    [InlineData("platinvault.localhost")]
    [InlineData("tower")]
    [InlineData("TOWER.")]
    [InlineData("tower.local")]
    [InlineData("nas.lan")]
    [InlineData("server.home.arpa")]
    [InlineData("tower.fritz.box")]
    [InlineData("TOWER.FRITZ.BOX")]
    [InlineData("box.internal")]
    [InlineData("")]
    public void Lan_names_and_ip_literals_pass_without_any_setup(string host) => Assert.True(HostGuard.IsAllowed(host, Rules()));

    [Theory]
    [InlineData("evil.example")]
    [InlineData("rebind.attacker.com")]
    [InlineData("tower.local.attacker.com")]
    [InlineData("192.168.1.20.nip.io")]
    [InlineData("pv.example.com")]
    public void A_public_domain_is_refused_by_default(string host) => Assert.False(HostGuard.IsAllowed(host, Rules()));

    [Fact]
    public void Configured_hosts_and_wildcards_are_accepted_and_star_switches_the_check_off()
    {
        var rules = Rules((HostGuard.EnvironmentVariable, "pv.example.com; *.home.example.net,https://other.example:8443"), (HostGuard.ConfigKey, "third.example:5233"));
        Assert.True(HostGuard.IsAllowed("pv.example.com", rules));
        Assert.True(HostGuard.IsAllowed("PV.Example.com", rules));
        Assert.True(HostGuard.IsAllowed("a.home.example.net", rules));
        Assert.True(HostGuard.IsAllowed("b.a.home.example.net", rules));
        Assert.False(HostGuard.IsAllowed("home.example.net", rules)); // a wildcard means the names below it, like ASP.NET Core's AllowedHosts
        Assert.True(HostGuard.IsAllowed("other.example", rules));
        Assert.True(HostGuard.IsAllowed("third.example", rules));
        Assert.False(HostGuard.IsAllowed("evil.example", rules));

        Assert.Null(HostGuard.ReadRules(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [HostGuard.EnvironmentVariable] = "*" }).Build()));
    }

    [Fact]
    public void The_windows_program_and_the_linux_package_keep_their_own_host_filtering()
    {
        // DesktopServer and deploy/linux/run.sh set ASP.NET Core's AllowedHosts to loopback: the guard then does not run at all
        Assert.Null(HostGuard.ReadRules(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1;localhost" }).Build()));
        Assert.Null(HostGuard.ReadRules(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1;localhost;[::1]" }).Build()));
        Assert.NotNull(HostGuard.ReadRules(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*" }).Build()));
        Assert.NotNull(HostGuard.ReadRules(new ConfigurationBuilder().Build()));
    }

    // ---- host guard: real server ---------------------------------------------------------------------------------------------

    private static HttpRequestMessage WithHost(string host, string path = "/api/version")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        return request;
    }

    [Fact]
    public async Task The_server_refuses_a_foreign_host_with_a_message_that_names_the_variable()
    {
        await using var server = await TestServer.StartAsync(null, b =>
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*", [HostGuard.EnvironmentVariable] = "*.home.example.net" }));

        var refused = await server.Client.SendAsync(WithHost("rebind.attacker.com:5233"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("text/plain", refused.Content.Headers.ContentType?.MediaType);
        var text = await refused.Content.ReadAsStringAsync();
        Assert.Contains("\"rebind.attacker.com\"", text);
        Assert.Contains("PLATINVAULT_ALLOWED_HOSTS=rebind.attacker.com", text);
        Assert.Contains("DNS-Rebinding", text);
        Assert.Contains("DNS rebinding", text);
        // The hub and the API behind it are never reached
        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.SendAsync(WithHost("rebind.attacker.com", "/hubs/achievements/negotiate"))).StatusCode);

        foreach (var host in new[] { "192.168.1.20:5233", "[::1]:5233", "localhost:5231", "tower:5233", "tower.local", "pv.home.example.net:443" })
        {
            Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(WithHost(host))).StatusCode);
        }
    }

    [Fact]
    public async Task With_a_host_list_of_its_own_the_server_uses_aspnet_host_filtering_unchanged()
    {
        // The shape of the Windows program and the Linux package
        await using var server = await TestServer.StartAsync(null, b =>
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1;localhost" }));

        Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(WithHost("127.0.0.1"))).StatusCode);
        var refused = await server.Client.SendAsync(WithHost("tower:5233"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.DoesNotContain("PLATINVAULT_ALLOWED_HOSTS", await refused.Content.ReadAsStringAsync());
    }
}
