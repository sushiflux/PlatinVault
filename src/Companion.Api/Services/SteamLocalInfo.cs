using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// What the server tells the settings and the system check about the Steam client of its own computer (see
/// <see cref="SteamLocalStatsService"/> and <see cref="SteamLocationProvider"/> in PlatinVault.SteamLocal). Enum values go out as
/// camelCase ids, the frontend has the same lists.
/// </summary>
public static class SteamLocalInfo
{
    /// <summary>PlatinVault runs on a Windows or Linux computer and reads Steam there: the field "Steam-Ordner" is useful.</summary>
    public const string ModeNative = "native";

    /// <summary>Inside a container: usually without Steam (Unraid, NAS); a mounted Steam folder works with Companion__Steam__Root.</summary>
    public const string ModeContainer = "container";

    /// <summary>Another platform (e.g. macOS): no local Steam reading.</summary>
    public const string ModeUnsupported = "unsupported";

    /// <summary>
    /// True inside a container. The official .NET container images set <c>DOTNET_RUNNING_IN_CONTAINER=true</c> (the PlatinVault image
    /// is built on mcr.microsoft.com/dotnet/aspnet).
    /// </summary>
    public static bool InContainer(Func<string, string?>? environment = null) =>
        string.Equals((environment ?? Environment.GetEnvironmentVariable)("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    public static string Mode(SteamPlatform platform, bool inContainer) => platform switch
    {
        SteamPlatform.Windows => ModeNative,
        SteamPlatform.Linux => inContainer ? ModeContainer : ModeNative,
        _ => ModeUnsupported,
    };

    /// <summary>"KnownPath" -> "knownPath" (the ids the frontend knows).</summary>
    public static string Id<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static SteamLocalSettingsDto Settings(SteamLocalStatsService local)
    {
        var location = local.Location;
        var chosen = location.Candidates.FirstOrDefault(c => c.Chosen);
        var mode = Mode(location.Platform, InContainer());
        return new SteamLocalSettingsDto(
            Id(location.Platform),
            mode,
            location.Found,
            location.Root,
            Id(location.Method),
            chosen?.Detail,
            location.Found && location.IsExperimental,
            local.LocationProvider.ConfiguredRoot,
            // The server itself reads Steam on the gaming PC (Windows program, native Linux package): the bridge is not needed there. A
            // container with a mounted Steam folder does not count: it usually lacks the host's process list for the running game.
            location.Found && mode == ModeNative);
    }

    public static SteamLocalStatusDto Status(SteamLocalStatsService local)
    {
        var location = local.Location;
        var account = location.Found ? local.GetActiveAccount().Method : SteamAccountMethod.None;
        return new SteamLocalStatusDto(
            Id(location.Platform),
            Mode(location.Platform, InContainer()),
            location.Found,
            location.Root,
            Id(location.Method),
            Id(location.Found || local.RunningGameMethod == RunningGameMethod.Registry ? local.RunningGameMethod : RunningGameMethod.None),
            Id(account),
            location.Found && location.IsExperimental,
            local.LocationProvider.ManualRoot,
            local.LocationProvider.ConfiguredRoot,
            location.Candidates
                .Select(c => new SteamRootCandidateDto(c.Path, Id(c.Method), c.Detail, c.ResolvedPath, c.Exists, c.HasSteamApps, c.IsDuplicate, c.Chosen, c.IsSteamRoot))
                .ToList());
    }

    /// <summary>The system check line "Steam auf diesem Rechner".</summary>
    public static SystemCheckDto Check(SteamLocalStatsService local)
    {
        const string key = "steam-local";
        var label = ServerTexts.SystemCheck.SteamLocalLabel;
        var location = local.Location;
        var inContainer = InContainer();
        var mode = Mode(location.Platform, inContainer);

        if (location.Root is { } root)
        {
            var detail = ServerTexts.SteamLocal.Found(root, location.Method, location.Candidates.FirstOrDefault(c => c.Chosen)?.Detail,
                location.IsExperimental, inContainer && local.RunningGameMethod == RunningGameMethod.ProcessList);
            // A folder set by hand that no longer works is worth a warning even when the search found another one
            if (UnusableManual(location) is { } unusable)
            {
                return new SystemCheckDto(key, label, "warn", Msg.Join(" · ", detail, unusable), null);
            }
            return new SystemCheckDto(key, label, "ok", detail, null);
        }

        if (UnusableManual(location) is { } manualProblem)
        {
            return new SystemCheckDto(key, label, "warn", manualProblem, null);
        }
        return new SystemCheckDto(key, label, "off", mode == ModeNative ? ServerTexts.SteamLocal.NotFoundNative : ServerTexts.SystemCheck.SteamLocalOff, null);
    }

    public static SteamRootCheckResponse CheckResponse(SteamRootCheck check, SteamPlatform platform) => new(
        check.IsValid,
        Id(check.Problem),
        check.IsValid ? null : ServerTexts.SteamLocal.Problem(check.Problem, check.Path, platform),
        check.Path,
        check.ResolvedPath);

    private static Msg? UnusableManual(SteamLocation location)
    {
        var manual = location.Candidates.FirstOrDefault(c => c.Method == SteamLocationMethod.Manual);
        if (manual is null || manual.Chosen) return null;
        var problem = manual.ResolvedPath is null || !manual.Exists
            ? SteamRootProblem.NotFound
            : !manual.HasSteamApps ? SteamRootProblem.NoSteamApps
            : manual.IsSteamRoot ? SteamRootProblem.None : SteamRootProblem.LibraryOnly;
        // Same folder as the one in use (reached through another candidate first): nothing to warn about
        return problem == SteamRootProblem.None ? null : ServerTexts.SteamLocal.ManualUnusable(manual.Path, ServerTexts.SteamLocal.Problem(problem, manual.Path, location.Platform));
    }
}
