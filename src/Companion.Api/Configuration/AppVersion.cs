using Microsoft.Extensions.Options;
using PlatinVault.Versioning;

namespace Companion.Api.Configuration;

/// <summary>
/// Version from version.txt, written by the pre-commit hook on every commit: line 1 = vYear.Month.Commit, line 2 = the
/// five-segment build id vYear.Month.Commit.Prompts.ChangedLines of that commit. Both only change with a commit.
/// The readable form comes from <see cref="VersionFormat"/> (user decision 2026-11-02): release "v2026.11.12 (13.557)", beta
/// "v2026.11.12 (13.557 β)", development "v2026.11.12 (13.557-dev)". Read fresh on every call, so a commit shows without restart.
/// </summary>
public sealed class AppVersion(IWebHostEnvironment environment, IOptions<CompanionOptions> options)
{
    /// <summary>The version shown in the UI.</summary>
    public string Current
    {
        get
        {
            var (shortVersion, buildId) = Read();
            // The dev environment and a dev-build image (channel "dev") are always marked "-dev", so they can never be mistaken for a beta or a release
            var kind = environment.IsDevelopment() ? VersionKind.Dev : VersionFormat.KindFromChannel(options.Value.ReleaseChannel);
            return VersionFormat.Display(shortVersion, buildId, kind);
        }
    }

    /// <summary>
    /// The running version in every form the update check needs: the readable form, the technical tag, the kind (release / beta /
    /// development) and the comparable version. A development run compares as the release of its commit (it has no tag of its own).
    /// </summary>
    public RunningVersion Running
    {
        get
        {
            var (shortVersion, buildId) = Read();
            var kind = environment.IsDevelopment() ? VersionKind.Dev : VersionFormat.KindFromChannel(options.Value.ReleaseChannel);
            var tag = VersionFormat.Tag(shortVersion, buildId, kind);
            var comparable = ReleaseVersion.Parse(kind == VersionKind.Dev ? shortVersion : tag) ?? ReleaseVersion.Parse(shortVersion);
            return new RunningVersion(VersionFormat.Display(shortVersion, buildId, kind), tag, kind, comparable);
        }
    }

    private (string Short, string? BuildId) Read()
    {
        foreach (var candidate in VersionCandidates())
        {
            if (!File.Exists(candidate)) continue;
            var lines = File.ReadAllText(candidate)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 0)
            {
                return (lines[0], lines.Length > 1 ? lines[1] : null);
            }
        }

        return ("v2026.10.0", null);
    }

    private IEnumerable<string> VersionCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "version.txt");
        yield return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "version.txt"));
    }
}

/// <summary>The running version: readable form, technical tag, kind and (when the version file is readable) the comparable version.</summary>
public sealed record RunningVersion(string Display, string Tag, VersionKind Kind, ReleaseVersion? Version);
