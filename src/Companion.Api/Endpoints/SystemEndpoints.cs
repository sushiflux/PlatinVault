using System.Diagnostics;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Companion.Api.Endpoints;

/// <summary>One line of the system check: Status "ok", "warn", "error" or "off" (not used / not configured).</summary>
public sealed record SystemCheckDto(string Key, string Label, string Status, string Detail, int? LatencyMs);

/// <summary>SteamLocal = where Steam was looked for on this computer and what was found (the "Steam auf diesem Rechner" line in detail).</summary>
public sealed record SystemStatusDto(string Version, string Environment, DateTimeOffset CheckedAt, List<SystemCheckDto> Checks, SteamLocalStatusDto? SteamLocal = null);

/// <summary>
/// "System" tab of the settings: is the backend up, the database reachable and current, the data folder writable, and
/// which sources (local Steam, bridge, keys) are available. Works the same on the PC, a NAS, Unraid or a Linux server -
/// what does not apply there is reported as "off", not as an error. Never returns secrets, only whether they exist.
/// </summary>
public static class SystemEndpoints
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static void MapSystem(this RouteGroupBuilder api)
    {
        api.MapGet("/system/status", async (
            CompanionDbContext db,
            AppVersion version,
            IWebHostEnvironment environment,
            IOptions<CompanionOptions> options,
            SecretStore secrets,
            SteamLocalStatsService localStats,
            SteamLiveTrackerService liveTracker,
            SettingsStore settingsStore,
            TwitchAuthService twitchAuth,
            DatabaseBackupService backups,
            CancellationToken ct) =>
        {
            var checks = new List<SystemCheckDto>();
            var uptime = DateTimeOffset.UtcNow - StartedAt;
            checks.Add(new SystemCheckDto("backend", ServerTexts.SystemCheck.BackendLabel, "ok",
                ServerTexts.SystemCheck.BackendDetail(uptime, environment.EnvironmentName), null));

            checks.Add(await CheckDatabaseAsync(db, backups, ct));
            checks.Add(CheckDataDir(options.Value.DataDir));

            checks.Add(SteamLocalInfo.Check(localStats));

            var lastBridge = liveTracker.GetLastBridgeEventAt();
            checks.Add(lastBridge is null
                ? new SystemCheckDto("bridge", ServerTexts.SystemCheck.BridgeLabel, "off", ServerTexts.SystemCheck.BridgeNone, null)
                : DateTimeOffset.UtcNow - lastBridge < TimeSpan.FromMinutes(3)
                    ? new SystemCheckDto("bridge", ServerTexts.SystemCheck.BridgeLabel, "ok", ServerTexts.SystemCheck.BridgeLast(DateTimeOffset.UtcNow - lastBridge.Value), null)
                    : new SystemCheckDto("bridge", ServerTexts.SystemCheck.BridgeLabel, "warn", ServerTexts.SystemCheck.BridgeLastWarn(DateTimeOffset.UtcNow - lastBridge.Value), null));

            var hasSteamKey = !string.IsNullOrWhiteSpace(secrets.GetSecret(SettingsStore.SecretSteam));
            checks.Add(new SystemCheckDto("steam-key", ServerTexts.SystemCheck.SteamKeyLabel, hasSteamKey ? "ok" : "off",
                hasSteamKey ? ServerTexts.SystemCheck.SteamKeyOk : ServerTexts.SystemCheck.SteamKeyOff, null));

            var ai = new[] { (SettingsStore.SecretGemini, "Gemini"), (SettingsStore.SecretClaude, "Claude"), (SettingsStore.SecretOpenAi, "ChatGPT") }
                .Where(p => !string.IsNullOrWhiteSpace(secrets.GetSecret(p.Item1)))
                .Select(p => p.Item2)
                .ToList();
            checks.Add(new SystemCheckDto("ai", ServerTexts.SystemCheck.AiLabel, ai.Count > 0 ? "ok" : "off",
                ai.Count > 0 ? ServerTexts.SystemCheck.AiOk(string.Join(", ", ai)) : ServerTexts.SystemCheck.AiOff, null));

            checks.Add(await CheckTwitchAsync(twitchAuth, settingsStore, ct));

            return Results.Ok(new SystemStatusDto(version.Current, environment.EnvironmentName, DateTimeOffset.UtcNow, checks, SteamLocalInfo.Status(localStats)));
        });
    }

    // Twitch & IGDB share one developer app. "ok" = the stored Client-ID/Secret get a token; the secrets themselves never leave the server.
    private static async Task<SystemCheckDto> CheckTwitchAsync(TwitchAuthService twitchAuth, SettingsStore settingsStore, CancellationToken ct)
    {
        const string key = "twitch";
        var label = ServerTexts.SystemCheck.TwitchLabel;
        try
        {
            var check = await twitchAuth.CheckStoredCredentialsAsync(ct);
            if (!check.HasCredentials)
            {
                return new SystemCheckDto(key, label, "off", ServerTexts.SystemCheck.TwitchOff, null);
            }
            if (!check.TokenOk)
            {
                return new SystemCheckDto(key, label, "error", check.Error ?? ServerTexts.SystemCheck.TwitchNoToken, null);
            }

            var streamsActive = await settingsStore.IsTwitchStreamsActiveAsync(ct);
            return new SystemCheckDto(key, label, "ok",
                ServerTexts.SystemCheck.TwitchOk(streamsActive), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SystemCheckDto(key, label, "error", ServerTexts.SystemCheck.Error(ex.GetBaseException().Message), null);
        }
    }

    private static async Task<SystemCheckDto> CheckDatabaseAsync(CompanionDbContext db, DatabaseBackupService backups, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (!await db.Database.CanConnectAsync(ct))
            {
                return new SystemCheckDto("database", ServerTexts.SystemCheck.DatabaseLabel, "error", ServerTexts.SystemCheck.DatabaseUnreachable, null);
            }
            var latency = (int)watch.ElapsedMilliseconds;
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
            var games = await db.Games.CountAsync(ct);
            return pending > 0
                ? new SystemCheckDto("database", ServerTexts.SystemCheck.DatabaseLabel, "warn", ServerTexts.SystemCheck.DatabasePending(pending), latency)
                : new SystemCheckDto("database", ServerTexts.SystemCheck.DatabaseLabel, "ok",
                    ServerTexts.SystemCheck.DatabaseOk(games, backups.Latest() is { } latest ? DateTime.Now - latest.LastWriteTime : null), latency);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SystemCheckDto("database", ServerTexts.SystemCheck.DatabaseLabel, "error", ServerTexts.SystemCheck.Error(ex.GetBaseException().Message), null);
        }
    }

    // The data folder holds the encrypted keys and caches; it must be writable (on Unraid: the appdata mapping)
    private static SystemCheckDto CheckDataDir(string dataDir)
    {
        try
        {
            var probeDir = Path.Combine(dataDir, "cache");
            Directory.CreateDirectory(probeDir);
            var probe = Path.Combine(probeDir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new SystemCheckDto("data", ServerTexts.SystemCheck.DataLabel, "ok", ServerTexts.SystemCheck.DataOk, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SystemCheckDto("data", ServerTexts.SystemCheck.DataLabel, "error", ServerTexts.SystemCheck.DataNotWritable(ex.Message), null);
        }
    }
}
