using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Companion.Api.Tests;

/// <summary>
/// The bridge downloads and the self-update answers of the real server for both bridges: the Windows ZIP (default, unchanged) and the
/// Linux tar.gz (os=linux) with its binary, scripts, generated bridge.json and the Unix modes the install needs. The bridge folders hold
/// small fake files, nothing is built.
/// </summary>
public sealed class BridgePackageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pv-bridgepkg-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly byte[] _linuxBinary = Encoding.ASCII.GetBytes("\u007fELF-fake-linux-binary");

    public BridgePackageTests()
    {
        var linux = Path.Combine(_folder, "bridge-linux");
        Directory.CreateDirectory(Path.Combine(linux, "package"));
        File.WriteAllBytes(Path.Combine(linux, "platinvault-bridge"), _linuxBinary);
        File.WriteAllText(Path.Combine(linux, "bridge-version.txt"), "1.0.7");
        // CRLF on purpose: a checkout on Windows must not break the scripts inside the tar
        File.WriteAllText(Path.Combine(linux, "package", "install.sh"), "#!/bin/sh\r\necho install\r\n");
        File.WriteAllText(Path.Combine(linux, "package", "README.txt"), "readme");

        var windows = Path.Combine(_folder, "bridge");
        Directory.CreateDirectory(windows);
        File.WriteAllBytes(Path.Combine(windows, "PlatinVault.Bridge.exe"), "MZ-fake-windows-exe"u8.ToArray());
        File.WriteAllText(Path.Combine(windows, "bridge-version.txt"), "1.0.3");
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder> Bundles(bool windows = true, bool linux = true) => builder =>
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Companion:BridgeDir"] = windows ? Path.Combine(_folder, "bridge") : Path.Combine(_folder, "nothing-windows"),
            ["Companion:BridgeLinuxDir"] = linux ? Path.Combine(_folder, "bridge-linux") : Path.Combine(_folder, "nothing-linux"),
        });

    private static HttpRequestMessage Post(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Requested-With", "PlatinVault");
        return request;
    }

    private static string Origin(TestServer server) => server.Client.BaseAddress!.ToString().TrimEnd('/') + "/";

    private static async Task<Dictionary<string, (UnixFileMode Mode, byte[] Data)>> ReadTarAsync(HttpContent content)
    {
        var entries = new Dictionary<string, (UnixFileMode, byte[])>();
        await using var gzip = new GZipStream(await content.ReadAsStreamAsync(), CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            if (entry.EntryType != TarEntryType.RegularFile && entry.EntryType != TarEntryType.V7RegularFile) continue;
            using var buffer = new MemoryStream();
            await entry.DataStream!.CopyToAsync(buffer);
            entries[entry.Name] = (entry.Mode, buffer.ToArray());
        }
        return entries;
    }

    [Fact]
    public async Task Linux_package_is_a_tar_gz_with_binary_scripts_and_a_private_bridge_json()
    {
        await using var server = await TestServer.StartAsync(desktop: null, Bundles());

        var response = await server.Client.SendAsync(Post($"/api/bridge/package?os=linux&origin={Uri.EscapeDataString(Origin(server))}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/gzip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("PlatinVault-Bridge-linux-x64.tar.gz", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

        var entries = await ReadTarAsync(response.Content);

        var binary = entries["PlatinVault-Bridge/platinvault-bridge"];
        Assert.Equal(_linuxBinary, binary.Data);
        Assert.True(binary.Mode.HasFlag(UnixFileMode.UserExecute) && binary.Mode.HasFlag(UnixFileMode.OtherExecute), "the binary must be executable");

        var install = entries["PlatinVault-Bridge/install.sh"];
        Assert.True(install.Mode.HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("#!/bin/sh\necho install\n", Encoding.UTF8.GetString(install.Data));

        Assert.False(entries["PlatinVault-Bridge/README.txt"].Mode.HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("1.0.7", Encoding.ASCII.GetString(entries["PlatinVault-Bridge/bridge-version.txt"].Data));

        // The configuration holds the token: readable by its owner only
        var config = entries["PlatinVault-Bridge/bridge.json"];
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, config.Mode);
        using var json = JsonDocument.Parse(config.Data);
        Assert.Equal(Origin(server).TrimEnd('/'), json.RootElement.GetProperty("server").GetString()!.TrimEnd('/'));
        Assert.True(json.RootElement.GetProperty("token").GetString()!.Length >= 16);
        Assert.Contains(json.RootElement.GetProperty("language").GetString(), new[] { "de", "en" });

        // No stray Windows files
        Assert.DoesNotContain(entries.Keys, name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_json_body_selects_the_operating_system_too()
    {
        await using var server = await TestServer.StartAsync(desktop: null, Bundles());

        var request = Post("/api/bridge/package");
        // A string body has a Content-Length, like the one the browser sends (the endpoint only reads small bodies with a known length)
        request.Content = new StringContent(JsonSerializer.Serialize(new { origin = Origin(server), os = "Linux" }), Encoding.UTF8, "application/json");
        var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/gzip", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Without_os_the_package_is_still_the_windows_zip()
    {
        await using var server = await TestServer.StartAsync(desktop: null, Bundles());

        var response = await server.Client.SendAsync(Post($"/api/bridge/package?origin={Uri.EscapeDataString(Origin(server))}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("PlatinVault.Bridge.exe"));
        Assert.NotNull(zip.GetEntry("bridge.json"));
        Assert.Null(zip.GetEntry("platinvault-bridge"));
    }

    [Fact]
    public async Task A_missing_linux_bridge_is_a_404_that_does_not_cost_the_token()
    {
        await using var server = await TestServer.StartAsync(desktop: null, Bundles(linux: false));

        var response = await server.Client.SendAsync(Post($"/api/bridge/package?os=linux&origin={Uri.EscapeDataString(Origin(server))}"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Linux", await response.Content.ReadAsStringAsync());

        var status = await server.Client.GetFromJsonAsync<JsonElement>("/api/bridge/token");
        Assert.False(status.GetProperty("hasToken").GetBoolean());
        Assert.Equal("1.0.3", status.GetProperty("bundledVersion").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("linuxBundledVersion").ValueKind);
    }

    [Fact]
    public async Task An_unknown_operating_system_is_refused()
    {
        await using var server = await TestServer.StartAsync(desktop: null, Bundles());

        var response = await server.Client.SendAsync(Post($"/api/bridge/package?os=beos&origin={Uri.EscapeDataString(Origin(server))}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("beos", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_token_status_announces_both_bundled_versions()
    {
        await using var server = await TestServer.StartAsync(desktop: null, Bundles());

        var status = await server.Client.GetFromJsonAsync<JsonElement>("/api/bridge/token");
        Assert.Equal("1.0.3", status.GetProperty("bundledVersion").GetString());
        Assert.Equal("1.0.7", status.GetProperty("linuxBundledVersion").GetString());
    }

    [Fact]
    public async Task The_state_answer_and_the_exe_download_follow_the_platform_of_the_bridge()
    {
        await using var server = await TestServer.StartAsync(desktop: null, Bundles());

        var created = await server.Client.SendAsync(Post("/api/bridge/token"));
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        async Task<JsonElement> PushAsync(object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/bridge/state") { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await server.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        // An older / the Windows bridge says nothing about its platform: Windows bundle
        var windows = await PushAsync(new { runningAppId = 0, achievements = Array.Empty<object>() });
        Assert.Equal("1.0.3", windows.GetProperty("latestBridgeVersion").GetString());

        var linux = await PushAsync(new { runningAppId = 0, achievements = Array.Empty<object>(), platform = "linux" });
        Assert.Equal("1.0.7", linux.GetProperty("latestBridgeVersion").GetString());
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(_linuxBinary));
        Assert.Equal(expectedHash, linux.GetProperty("bridgeSha256").GetString());

        var download = new HttpRequestMessage(HttpMethod.Get, "/api/bridge/exe?platform=linux");
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var file = await server.Client.SendAsync(download);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(_linuxBinary, await file.Content.ReadAsByteArrayAsync());

        var windowsDownload = new HttpRequestMessage(HttpMethod.Get, "/api/bridge/exe");
        windowsDownload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal("MZ-fake-windows-exe", Encoding.ASCII.GetString(await (await server.Client.SendAsync(windowsDownload)).Content.ReadAsByteArrayAsync()));
    }
}
