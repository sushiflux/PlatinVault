using PlatinVault.Versioning;

namespace Companion.Api.Services;

/// <summary>
/// Finds the customer changelogs (<c>CHANGELOG.md</c>, <c>CHANGELOG.beta.md</c>, <c>CHANGELOG.nightly.md</c>) that ship with every
/// PlatinVault artifact, so "What's new" works offline. Same layout as the licence documents (<see cref="LicenseFiles"/>): in Development
/// they are read straight from the repository root; the Docker image, the Windows program folder and the Linux package keep them in
/// <c>docs/</c> below the application folder.
/// </summary>
public sealed class ChangelogFiles
{
    public const string FileName = "CHANGELOG.md";
    public const string BetaFileName = "CHANGELOG.beta.md";
    public const string NightlyFileName = "CHANGELOG.nightly.md";

    private readonly string[] _roots;

    public ChangelogFiles(IWebHostEnvironment environment)
    {
        // Development: the repository root is two levels above src/Companion.Api (same convention as HandbookFiles and LicenseFiles)
        var repository = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
        var docs = Path.Combine(AppContext.BaseDirectory, "docs");
        _roots = environment.IsDevelopment() ? [repository, docs] : [docs];
    }

    /// <summary>For tests: look in exactly these folders.</summary>
    internal ChangelogFiles(IEnumerable<string> roots) => _roots = [.. roots];

    public static string FileNameFor(VersionKind kind) => kind switch
    {
        VersionKind.Beta => BetaFileName,
        VersionKind.Nightly => NightlyFileName,
        _ => FileName,
    };

    /// <summary>The path of the shipped changelog for <paramref name="kind"/> (falling back to <c>CHANGELOG.md</c>), or null when it is not there.</summary>
    public string? Find(VersionKind kind = VersionKind.Release)
    {
        var preferred = FileNameFor(kind);
        return _roots.Select(root => Path.Combine(root, preferred)).FirstOrDefault(File.Exists)
            ?? (preferred != FileName ? _roots.Select(root => Path.Combine(root, FileName)).FirstOrDefault(File.Exists) : null);
    }

    /// <summary>The shipped changelog text (HTML comments, which are notes to the owner, removed), or null when the file is missing or unreadable.</summary>
    public string? ReadText(VersionKind kind = VersionKind.Release)
    {
        if (Find(kind) is not { } path) return null;
        try
        {
            return LicenseFiles.ReadShippedText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
