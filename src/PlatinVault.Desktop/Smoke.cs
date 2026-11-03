using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop;

/// <summary>
/// End-to-end self test of an installed (or built) program behind <c>--smoke &lt;folder&gt; --test-root &lt;folder&gt;</c>: starts the real
/// program in the test root (its SQLite database in the test data folder, server on a free loopback port, window far off-screen, no tray icon,
/// no hotkey, game starts only recorded), then checks: server answers /api/version and /api/branding (host "desktop"), the UI renders in
/// WebView2 (picture of the window), sound may play without a click (autoplay), the in-game overlay (the app's /overlay page in a
/// transparent click-through window, visual hosting) loads, shows a test unlock, a full-screen popup and the platinum moment (each saved
/// as a picture from the off-screen window, checked for a transparent background), hides itself again and costs nothing while hidden
/// (CPU of the WebView2 processes measured), a backup is written; finally it quits like the user would and checks that the database was closed cleanly.
/// Writes smoke-report.txt and the pictures into the folder; exit code 0 = everything passed.
/// </summary>
internal static class Smoke
{
    public static Task<int> RunAsync(DesktopEnvironment env, string output)
    {
        Directory.CreateDirectory(output);
        var report = new StringBuilder();
        var failures = 0;
        void Check(bool ok, string what)
        {
            report.AppendLine((ok ? "OK    " : "FAIL  ") + what);
            if (!ok) failures++;
        }

        var instance = AppInstance.TryAcquire(env.Layout);
        if (instance is null)
        {
            Console.Out.WriteLine("Another test instance runs in this test root.");
            return Task.FromResult(1);
        }

        var app = new DesktopApp(env, instance);
        _ = RunChecksAsync();
        Application.Run(app);

        // After the program quit: the clean stop closed the database (WAL folded back into the file) and made its last backup
        var database = Path.Combine(env.Data.Database, Companion.Api.Configuration.DatabaseOptions.FileName);
        Check(File.Exists(database) && !File.Exists(database + "-wal"), "Datenbank sauber geschlossen (platinvault.db, keine -wal-Datei)");
        var backups = Directory.Exists(Path.Combine(env.Data.Backups, "recent")) ? Directory.GetFiles(Path.Combine(env.Data.Backups, "recent"), "platinvault-*.db").Length : 0;
        Check(backups >= 1, $"Sicherungen in backups\\recent: {backups}");

        report.AppendLine(failures == 0 ? "SMOKE PASSED" : $"SMOKE FAILED ({failures})");
        File.WriteAllText(Path.Combine(output, "smoke-report.txt"), report.ToString(), new UTF8Encoding(false));
        Console.Out.Write(report.ToString());
        return Task.FromResult(failures == 0 ? 0 : 1);

        async Task RunChecksAsync()
        {
            try
            {
                await app.Started;
                var server = app.Server;
                Check(server is not null, "Datenbank geöffnet und Server gestartet");
                if (server is null)
                {
                    SaveControl(app.Window, Path.Combine(output, "start-error.png"));
                    return;
                }

                using var http = new HttpClient { BaseAddress = server.BaseUri, Timeout = TimeSpan.FromSeconds(20) };
                var version = await http.GetFromJsonAsync<JsonElement>("api/version");
                Check(version.TryGetProperty("version", out var v) && v.GetString() is { Length: > 0 }, $"GET /api/version -> {version}");
                var branding = await http.GetFromJsonAsync<JsonElement>("api/branding");
                Check(branding.TryGetProperty("host", out var host) && host.GetString() == "desktop", $"GET /api/branding host = {(branding.TryGetProperty("host", out var h) ? h.GetString() : "?")}");
                using (var foreign = new HttpRequestMessage(HttpMethod.Get, "api/version"))
                {
                    foreign.Headers.Host = "evil.example";
                    using var answer = await http.SendAsync(foreign);
                    Check((int)answer.StatusCode == 400, $"Fremder Host-Header wird abgewiesen (DNS-Rebinding-Schutz): HTTP {(int)answer.StatusCode}");
                }

                var core = app.Window.Core;
                Check(core is not null, "WebView2 bereit");
                if (core is null) return;
                // A test run is never heard: the window is muted before anything can play (sound tests below play into the mute)
                Check(core.IsMuted, "Programmfenster stumm geschaltet (Testlauf, nichts ist zu hören)");

                var rendered = false;
                for (var i = 0; i < 60 && !rendered; i++)
                {
                    var children = await core.ExecuteScriptAsync("document.querySelector('#root') ? document.querySelector('#root').children.length : 0");
                    rendered = int.TryParse(children, out var count) && count > 0;
                    if (!rendered) await Task.Delay(500);
                }
                Check(rendered, "React-Oberfläche im Fenster gerendert");
                await Task.Delay(2500); // fonts, data and animations settle for the picture
                await using (var png = File.Create(Path.Combine(output, "app-window.png")))
                {
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
                }

                // The own title bar: on top, visible, the page right below it and never over it, the start screen's brand look gone
                var bar = app.Window.TitleBar;
                var web = app.Window.WebSurface;
                Check(bar.Visible && bar.Top == 0 && bar.Width == app.Window.ClientSize.Width, $"Titelleiste sichtbar oben über die ganze Breite ({bar.Bounds})");
                Check(web is not null && web.Visible && web.Top == bar.Bottom && !web.Bounds.IntersectsWith(bar.Bounds),
                    $"Seite (WebView2) direkt unter der Titelleiste, nicht darüber ({web?.Bounds})");
                Check(!bar.BrandMode, "Titelleiste im Farbschema der App (Startbildschirm-Look beendet)");
                SaveControl(bar, Path.Combine(output, "title-bar.png"));

                // Autoplay: an unmuted element (volume 0, so nothing is heard) may play without any click on the page
                await core.ExecuteScriptAsync("(()=>{const a=new Audio('/sounds/kristall/gold.mp3');a.volume=0;a.play().then(()=>{window.__pvAutoplay='played';a.pause();}).catch(e=>{window.__pvAutoplay='blocked:'+e.name;});})()");
                var autoplay = "null";
                for (var i = 0; i < 20 && autoplay is "null" or "\"\""; i++)
                {
                    await Task.Delay(250);
                    autoplay = await core.ExecuteScriptAsync("window.__pvAutoplay ?? null");
                }
                Check(autoplay == "\"played\"", $"Ton ohne Klick (Autoplay): {autoplay}");

                // "Mit Steam anmelden" stays in the window: the start page sets the state cookie in the WINDOW's cookie store and Steam's
                // sign-in page loads in the window (not in the default browser); a return to the callback carries that cookie, so the
                // server gets past the state check (it then fails later only because there is no real Steam answer here)
                async Task<string> NavigateAsync(string url)
                {
                    var done = new TaskCompletionSource();
                    void OnDone(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult();
                    core.NavigationCompleted += OnDone;
                    core.Navigate(url);
                    await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                    core.NavigationCompleted -= OnDone;
                    return core.Source;
                }
                var origin = server.BaseUri.GetLeftPart(UriPartial.Authority);
                var steamPage = await NavigateAsync($"{origin}/api/steam/openid/start?origin={Uri.EscapeDataString(origin)}");
                var cookies = await core.CookieManager.GetCookiesAsync($"{origin}/api/steam/openid/callback");
                // The value "state|origin" is URL-encoded in the cookie (ASP.NET Core escapes it when setting it)
                var raw = cookies.FirstOrDefault(c => c.Name == "pv_steam_openid")?.Value;
                var state = raw is null ? null : Uri.UnescapeDataString(raw).Split('|')[0];
                Check(state is { Length: > 0 }, "Steam-Anmeldung: Einmal-Cookie im Cookie-Speicher des Fensters gesetzt");
                var onSteam = Uri.TryCreate(steamPage, UriKind.Absolute, out var steamUri) && steamUri.Host == "steamcommunity.com";
                report.AppendLine((onSteam ? "OK    " : "INFO  ") + $"Steam-Anmeldeseite im Programmfenster geladen: {steamPage}" +
                                  (onSteam ? string.Empty : " (ohne Internet nicht prüfbar - kein Fehler)"));
                var back = await NavigateAsync($"{origin}/api/steam/openid/callback?state={Uri.EscapeDataString(state ?? string.Empty)}&openid.mode=id_res");
                Check(!back.Contains("reason=state", StringComparison.Ordinal) && back.Contains("steamLink=error", StringComparison.Ordinal),
                    $"Steam-Rückweg kommt im Fenster mit dem Cookie an (kein reason=state): {back}");
                Check(!app.Window.SteamLoginActive, "Steam-Anmeldung endet mit der Rückkehr in die App");
                await NavigateAsync(server.BaseUri.ToString());

                // The in-game overlay: the app's own /overlay page in the off-screen overlay window (never on the desktop in a test run)
                var overlay = app.Overlay;
                for (var i = 0; i < 80 && !overlay.Available; i++) await Task.Delay(250);
                Check(overlay.Available, $"Overlay-Seite geladen und bereit (visuelles Hosting, {overlay.CreationMilliseconds} ms)");
                Check(overlay.Core?.IsMuted == true, "Overlay stumm geschaltet");
                var overlayFolder = Path.Combine(env.Layout.TestRoot!, "overlay");
                async Task<string?> OverlayRunAsync(string trigger, string name, bool coversScreen = false)
                {
                    var before = Directory.Exists(overlayFolder) ? Directory.GetFiles(overlayFolder, "*.png").ToHashSet() : [];
                    using (var request = new HttpRequestMessage(HttpMethod.Post, trigger))
                    {
                        request.Headers.Add("X-Requested-With", "PlatinVault");
                        using var answer = await http.SendAsync(request);
                        Check(answer.IsSuccessStatusCode, $"{name}: ausgelöst ({trigger}) HTTP {(int)answer.StatusCode}");
                    }
                    string? picture = null;
                    for (var i = 0; i < 40 && picture is null; i++)
                    {
                        await Task.Delay(250);
                        picture = Directory.Exists(overlayFolder) ? Directory.GetFiles(overlayFolder, "*.png").FirstOrDefault(f => !before.Contains(f)) : null;
                    }
                    if (picture is null)
                    {
                        Check(false, $"{name}: kein Overlay-Bild ({overlayFolder})");
                        return null;
                    }
                    var target = Path.Combine(output, $"overlay-{name}.png");
                    File.Copy(picture, target, overwrite: true);
                    var (corner, opaque) = OverlayPixels(target);
                    if (coversScreen)
                    {
                        // Full-screen popup and platinum moment: the screen is dimmed, not covered - the game stays visible behind it
                        Check(corner is > 0 and < 255 && opaque > 0, $"{name}: Overlay-Bild mit durchscheinender Abdunklung (Ecke Alpha {corner} von 255, deckende Pixel {opaque}) -> {target}");
                    }
                    else
                    {
                        Check(corner == 0 && opaque > 0, $"{name}: Overlay-Bild mit durchsichtigem Hintergrund (Ecke Alpha {corner}, deckende Pixel {opaque}) -> {target}");
                    }
                    return target;
                }
                async Task<bool> WaitHiddenAsync(int seconds)
                {
                    for (var i = 0; i < seconds * 4 && overlay.IsPresenting; i++) await Task.Delay(250);
                    return !overlay.IsPresenting;
                }
                async Task SetPositionAsync(string position)
                {
                    using var put = new HttpRequestMessage(HttpMethod.Put, "api/settings") { Content = JsonContent.Create(new { popupPosition = position }) };
                    put.Headers.Add("X-Requested-With", "PlatinVault");
                    using var answer = await http.SendAsync(put);
                    Check(answer.IsSuccessStatusCode, $"Popup-Position {position} gespeichert: HTTP {(int)answer.StatusCode}");
                }

                var cpuStart = WebViewCpu(app);
                var cpuClock = System.Diagnostics.Stopwatch.StartNew();
                await OverlayRunAsync("api/live/test/gold", "card");
                Check(await WaitHiddenAsync(15), "Overlay nach dem Popup wieder versteckt (Seite meldet idle)");
                var busyCpu = WebViewCpu(app) - cpuStart;
                report.AppendLine($"INFO  CPU aller WebView2-Prozesse (Fenster + Overlay) während Popup + Ausblenden: {busyCpu.TotalMilliseconds:0} ms in {cpuClock.ElapsedMilliseconds} ms ({Environment.ProcessorCount} logische Kerne)");
                var idleStart = WebViewCpu(app);
                await Task.Delay(5000);
                var idleCpu = WebViewCpu(app) - idleStart;
                report.AppendLine($"INFO  CPU aller WebView2-Prozesse in Ruhe (Overlay versteckt): {idleCpu.TotalMilliseconds:0} ms in 5000 ms");

                await SetPositionAsync("fullscreen");
                await OverlayRunAsync("api/live/test/silver", "fullscreen", coversScreen: true);
                Check(await WaitHiddenAsync(15), "Overlay nach dem Vollbild-Popup wieder versteckt");
                await OverlayRunAsync("api/live/test/platinum?gameName=Elden%20Ring&total=42", "platinum", coversScreen: true);
                await SetPositionAsync("top-right");

                var recent = Path.Combine(env.Data.Backups, "recent");
                var found = false;
                for (var i = 0; i < 60 && !found; i++)
                {
                    found = Directory.Exists(recent) && Directory.GetFiles(recent, "platinvault-*.db").Length > 0;
                    if (!found) await Task.Delay(500);
                }
                Check(found, "Sicherung beim Start geschrieben");
            }
            catch (Exception ex)
            {
                Check(false, "Ausnahme: " + ex);
            }
            finally
            {
                await app.QuitAsync();
            }
        }
    }

    /// <summary>Alpha of the top-left pixel and the number of fully opaque pixels of an overlay picture.</summary>
    private static (int Corner, int Opaque) OverlayPixels(string path)
    {
        using var bitmap = new Bitmap(path);
        var opaque = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                if (bitmap.GetPixel(x, y).A == 255) opaque++;
            }
        }
        return (bitmap.GetPixel(2, 2).A, opaque);
    }

    /// <summary>CPU time of every WebView2 process of the program (browser, GPU, renderers of window and overlay).</summary>
    private static TimeSpan WebViewCpu(DesktopApp app)
    {
        var total = TimeSpan.Zero;
        if (app.Window.Environment is not { } environment) return total;
        foreach (var info in environment.GetProcessInfos())
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(info.ProcessId);
                total += process.TotalProcessorTime;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process ended meanwhile
            }
        }
        return total;
    }

    private static void SaveControl(Control control, string path)
    {
        using var bitmap = ControlSnapshot.Capture(control);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
