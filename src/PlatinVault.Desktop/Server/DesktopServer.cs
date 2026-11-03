using System.Net;
using System.Text;
using Companion.Api;
using Companion.Api.Desktop;
using PlatinVault.Desktop.Database;

namespace PlatinVault.Desktop.Server;

/// <summary>
/// The PlatinVault server inside the Windows program: the very same app as in Docker/Unraid (CompanionHost), configured for this PC -
/// Kestrel only on 127.0.0.1 (a stable port chosen once, the window's stored preferences belong to that address), its own SQLite file
/// and backups in the data folder of the program, only "127.0.0.1"/"localhost" as accepted Host headers (protects against DNS
/// rebinding from websites), and <see cref="IDesktopHost"/> registered so the server knows it runs next to Steam.
/// </summary>
internal sealed class DesktopServer : IAsyncDisposable
{
    private DesktopServer(WebApplication app, Uri baseUri)
    {
        App = app;
        BaseUri = baseUri;
    }

    public WebApplication App { get; }

    public Uri BaseUri { get; }

    public static async Task<DesktopServer> CreateAsync(DesktopEnvironment env, DesktopSettings settings, DatabaseHost database,
        IDesktopHost desktopHost, bool start, CancellationToken ct)
    {
        var port = LoopbackPorts.ChoosePort(env.Options.ServerPort, settings.ServerPort,
            busy => new DatabaseException(DesktopTexts.Dev.PortBusy(DesktopTexts.Dev.ServerPort, busy)), purpose: "server");
        // A fixed port (development run) is configuration, not state: it is not written to desktop.json
        if (env.Options.ServerPort is null && settings.ServerPort != port)
        {
            settings.ServerPort = port;
            settings.Save(env.Data.SettingsFile);
        }

        var binding = ServerBinding.From(settings);
        Directory.CreateDirectory(env.Data.Server);
        var builder = CompanionHost.CreateBuilder(new CompanionHostOptions
        {
            ContentRootPath = env.ContentRoot,
            WebRootPath = Directory.Exists(env.WebRoot) ? env.WebRoot : null,
            EnvironmentName = env.EnvironmentName,
            ConfigureBuilder = b =>
            {
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Companion:DataDir"] = env.Data.Server,
                    ["Companion:ReleaseChannel"] = env.ReleaseChannel,
                    ["Companion:BridgeDir"] = env.BridgeDir,
                    // Development and test runs never check for updates (the settings say so instead of showing a result)
                    ["Companion:Updates:Enabled"] = env.UpdateCheckAllowed ? "true" : "false",
                    ["AllowedHosts"] = binding.AllowedHosts,
                });
                b.Configuration.AddInMemoryCollection(DatabaseSettings(database));
                b.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(binding.Address, port));
                b.Logging.ClearProviders();
                b.Logging.AddProvider(new FileLoggerProvider(Path.Combine(env.Data.Logs, "server.log")));
                // Same quiet levels as the server's appsettings.json: no request or SQL chatter in the log
                b.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                b.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
                b.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
                b.Services.AddSingleton(desktopHost);
            },
        });

        var app = await CompanionHost.BuildAsync(builder, ct);
        if (start) await app.StartAsync(ct);
        // The program window always talks to the server over loopback, whatever address the server listens on
        return new DesktopServer(app, new Uri($"http://127.0.0.1:{port}/"));
    }

    /// <summary>
    /// The server's database and key settings (see <see cref="DatabaseHost.ServerSettings"/>): the SQLite file and the backup folder; in
    /// shared development mode the browser stack's file, key ring and encrypted API keys. The data folder with the caches stays the
    /// program's own (set above from env.Data.Server).
    /// </summary>
    internal static Dictionary<string, string?> DatabaseSettings(DatabaseHost database) => database.ServerSettings();

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await App.StopAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            DesktopLog.Warn($"Server wurde nicht sauber beendet: {ex.Message}");
        }
        await App.DisposeAsync();
    }
}

/// <summary>
/// Where the server listens. Today ALWAYS 127.0.0.1 (only this PC). The setting exists so the later opt-in phone/tablet access in the
/// home network (pairing by QR code / one-time token, firewall rule for private networks only) only has to
/// widen this one place: any other stored value is ignored with a log line until that feature exists.
/// </summary>
internal sealed record ServerBinding(IPAddress Address, string AllowedHosts)
{
    public static readonly ServerBinding Loopback = new(IPAddress.Loopback, "127.0.0.1;localhost");

    public static ServerBinding From(DesktopSettings settings)
    {
        if (IPAddress.TryParse(settings.ServerBindAddress, out var address) && IPAddress.IsLoopback(address)) return Loopback;
        DesktopLog.Warn($"Bindungsadresse '{settings.ServerBindAddress}' wird in dieser Version nicht unterstützt - der Server bleibt auf 127.0.0.1.");
        return Loopback;
    }
}

/// <summary>Minimal file logger for the server (the program has no console): one line per entry, rotated at 5 MB.</summary>
internal sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                DesktopLog.RotateIfNeeded(path);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the server
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {logLevel,-11} {category}: {formatter(state, exception)}";
            if (exception is not null) text += Environment.NewLine + exception;
            owner.Write(text);
        }
    }
}
