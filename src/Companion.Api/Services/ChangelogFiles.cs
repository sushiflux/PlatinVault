namespace Companion.Api.Services;

/// <summary>
/// Finds the customer changelog (<c>CHANGELOG.md</c>) that ships with every PlatinVault artifact, so "What's new" works offline.
/// Same layout as the licence documents (<see cref="LicenseFiles"/>): in Development it is read straight from the repository root;
/// the Docker image, the Windows program folder and the Linux package keep it in <c>docs/</c> below the application folder.
/// </summary>
public sealed class ChangelogFiles
{
    public const string FileName = "CHANGELOG.md";

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

    /// <summary>The path of the shipped changelog, or null when it is not there.</summary>
    public string? Find() => _roots.Select(root => Path.Combine(root, FileName)).FirstOrDefault(File.Exists);

    /// <summary>The shipped changelog text (HTML comments, which are notes to the owner, removed), or null when the file is missing or unreadable.</summary>
    public string? ReadText()
    {
        if (Find() is not { } path) return null;
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
