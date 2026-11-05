using System.Net;
using System.Text.Json;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatinVault.Versioning;

namespace Companion.Api.Tests;

/// <summary>
/// "What's new" after an update: cutting the entry out of the customer changelog (release and beta headings, both languages), the
/// rules that decide whether the window opens, the two endpoints (including the CSRF header) and that the changelog is part of every artifact.
/// </summary>
public sealed class WhatsNewTests
{
    private const string Beta = "β";

    private static readonly string Changelog = string.Join('\n',
        "# Changelog",
        "",
        "**[English](#english) · [Deutsch](#deutsch)**",
        "",
        "---",
        "",
        "<a name=\"english\"></a>",
        "## English",
        "",
        $"## v2026.11.18 (9.7263 {Beta}) · Unraid, Windows",
        "",
        "### ✨ New",
        "",
        "- **Pinned:** pin achievements.",
        "",
        "## v2026.11.17 (24.19071)",
        "",
        "Short summary.",
        "",
        "### 🔧 Fix",
        "",
        "- Fixed a thing.",
        "",
        "---",
        "",
        "<a name=\"deutsch\"></a>",
        "## Deutsch",
        "",
        $"## v2026.11.18 (9.7263 {Beta}) · Unraid, Windows",
        "",
        "### ✨ Neu",
        "",
        "- **Angeheftet:** Erfolge anheften.",
        "",
        "## v2026.11.17 (24.19071)",
        "",
        "Kurze Zusammenfassung.",
        "",
        "### 🔧 Behoben",
        "",
        "- Etwas behoben.",
        "",
        "---");

    // ---- cutting the entry ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_beta_entry_is_found_by_its_readable_version_with_the_platforms_kept_in_both_languages()
    {
        var english = ChangelogEntry.Extract(Changelog, $"v2026.11.18 (9.7263 {Beta})", "en");
        var german = ChangelogEntry.Extract(Changelog, $"v2026.11.18 (9.7263 {Beta})", "de");

        Assert.NotNull(english);
        Assert.StartsWith($"## v2026.11.18 (9.7263 {Beta}) · Unraid, Windows", english);
        Assert.Contains("pin achievements", english);
        Assert.DoesNotContain("Angeheftet", english);
        Assert.DoesNotContain("v2026.11.17", english);

        Assert.NotNull(german);
        Assert.Contains("Erfolge anheften", german);
        Assert.DoesNotContain("pin achievements", german);
    }

    [Fact]
    public void A_release_entry_ends_before_the_next_entry_and_drops_the_trailing_rule()
    {
        var english = ChangelogEntry.Extract(Changelog, "v2026.11.17 (24.19071)", "en");
        var german = ChangelogEntry.Extract(Changelog, "v2026.11.17 (24.19071)", "de");

        Assert.Equal("## v2026.11.17 (24.19071)\n\nShort summary.\n\n### 🔧 Fix\n\n- Fixed a thing.", english);
        Assert.Equal("## v2026.11.17 (24.19071)\n\nKurze Zusammenfassung.\n\n### 🔧 Behoben\n\n- Etwas behoben.", german);
    }

    [Fact]
    public void A_release_version_does_not_match_a_longer_heading_and_an_unknown_version_gives_nothing()
    {
        // "(24.19071)" must not match "(24.190719)" and a prefix of a heading is no heading
        var text = "<a name=\"english\"></a>\n## v2026.11.17 (24.190719)\n\n- other\n";
        Assert.Null(ChangelogEntry.Extract(text, "v2026.11.17 (24.19071)", "en"));
        Assert.Null(ChangelogEntry.Extract(Changelog, "v2026.11.99 (1.1)", "en"));
        Assert.Null(ChangelogEntry.Extract(Changelog, "v2026.11.17", "en"));
    }

    [Fact]
    public void Windows_line_endings_work_and_a_missing_language_section_falls_back_to_the_other_language()
    {
        var crlf = Changelog.Replace("\n", "\r\n");
        var entry = ChangelogEntry.Extract(crlf, "v2026.11.17 (24.19071)", "en");
        Assert.NotNull(entry);
        Assert.DoesNotContain('\r', entry);

        var englishOnly = "<a name=\"english\"></a>\n## v2026.11.17 (24.19071)\n\n- Only English.\n";
        Assert.Null(ChangelogEntry.Extract(englishOnly, "v2026.11.17 (24.19071)", "de"));
        Assert.Contains("Only English", ChangelogEntry.ForLanguage(englishOnly, "v2026.11.17 (24.19071)", "de"));
        Assert.Null(ChangelogEntry.ForLanguage(englishOnly, "v2026.11.99 (1.1)", "de"));
    }

    [Fact]
    public void The_shipped_changelog_has_an_entry_for_its_newest_version_in_both_languages()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md"));
        var heading = text.Split('\n').First(l => l.StartsWith("## v", StringComparison.Ordinal)).TrimEnd('\r')[3..];
        var version = heading.Contains(" · ", StringComparison.Ordinal) ? heading[..heading.IndexOf(" · ", StringComparison.Ordinal)] : heading;

        Assert.NotNull(ChangelogEntry.Extract(text, version, "en"));
        Assert.NotNull(ChangelogEntry.Extract(text, version, "de"));
    }

    // ---- the decision --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(VersionKind.Release, "v2", "v1", null, true, true)]    // update: another version stored
    [InlineData(VersionKind.Beta, "v2", "v1", null, true, true)]
    [InlineData(VersionKind.Release, "v2", "v2", null, true, false)]   // already shown
    [InlineData(VersionKind.Release, "v2", "v1", null, false, false)]  // no entry
    [InlineData(VersionKind.Dev, "v2", "v1", null, true, false)]       // dev build
    [InlineData(VersionKind.Release, "v2", null, "v1", true, true)]    // nothing stored, older installation
    [InlineData(VersionKind.Release, "v2", null, "v2", true, true)]    // nothing stored, migration overwrote the schema version
    [InlineData(VersionKind.Release, "v2", null, null, true, false)]   // nothing at all: fresh install
    public void Show_follows_the_rules(VersionKind kind, string running, string? stored, string? schema, bool hasEntry, bool expected) =>
        Assert.Equal(expected, WhatsNew.ShouldShow(kind, running, stored, schema, hasEntry));

    private static RunningVersion Running(string display, VersionKind kind = VersionKind.Release) => new(display, display, kind, null);

    private static async Task SetAsync(CompanionDbContext db, string key, string? value)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (value is null)
        {
            if (row is not null) db.AppSettings.Remove(row);
        }
        else if (row is null)
        {
            db.AppSettings.Add(new AppSettingEntity { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_fresh_database_stores_the_version_silently_and_shows_nothing()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        var running = Running("v2026.11.17 (24.19071)");

        var answer = await WhatsNew.GetAsync(db, running, Changelog, "en");

        Assert.False(answer.Show);
        Assert.NotNull(answer.Notes);
        Assert.Equal("v2026.11.17 (24.19071)", await db.AppSettings.Where(s => s.Key == WhatsNew.LastVersionKey).Select(s => s.Value).SingleAsync());
    }

    [Fact]
    public async Task An_older_installation_sees_the_window_once_and_marking_it_seen_closes_it_for_good()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SetAsync(db, DatabaseInitializer.SchemaVersionKey, "v2026.11.14 (15.116022)");
        var running = Running($"v2026.11.18 (9.7263 {Beta})");

        var first = await WhatsNew.GetAsync(db, running, Changelog, "de");
        Assert.True(first.Show);
        Assert.Contains("Erfolge anheften", first.Notes);

        await WhatsNew.MarkSeenAsync(db, running);
        var second = await WhatsNew.GetAsync(db, running, Changelog, "de");
        Assert.False(second.Show);
        Assert.NotNull(second.Notes);

        // The next update shows again
        var next = await WhatsNew.GetAsync(db, Running("v2026.11.17 (24.19071)"), Changelog, "en");
        Assert.True(next.Show);
    }

    [Fact]
    public async Task A_version_without_an_entry_and_a_dev_build_show_nothing_and_a_dev_build_writes_nothing()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        await SetAsync(db, WhatsNew.LastVersionKey, "v2026.11.10 (1.1)");

        Assert.False((await WhatsNew.GetAsync(db, Running("v2026.11.99 (1.1)"), Changelog, "en")).Show);

        var dev = await WhatsNew.GetAsync(db, Running("v2026.11.17 (24.19071-dev)", VersionKind.Dev), Changelog, "en");
        Assert.False(dev.Show);
        Assert.Null(dev.Notes);
        await WhatsNew.MarkSeenAsync(db, Running("v2026.11.17 (24.19071-dev)", VersionKind.Dev));
        Assert.Equal("v2026.11.10 (1.1)", await db.AppSettings.Where(s => s.Key == WhatsNew.LastVersionKey).Select(s => s.Value).SingleAsync());
    }

    [Fact]
    public async Task The_migration_start_stores_the_version_for_a_fresh_database_only()
    {
        using var scratch = new SqliteScratch();
        await SqliteDatabase.PrepareAsync(scratch.DatabaseFile, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var services = new ServiceCollection()
            .AddSingleton<AppVersion>()
            .AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(new FakeEnvironment())
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new CompanionOptions()))
            .AddScoped(_ => scratch.Context())
            .BuildServiceProvider();

        await DatabaseInitializer.MigrateAsync(services, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await using var db = scratch.Context();
        var shown = services.GetRequiredService<AppVersion>().Running.Display;
        Assert.Equal(shown, await db.AppSettings.Where(s => s.Key == WhatsNew.LastVersionKey).Select(s => s.Value).SingleAsync());

        // An existing database (migrations already applied) is never stamped by the start
        await SetAsync(db, WhatsNew.LastVersionKey, null);
        await DatabaseInitializer.MigrateAsync(services, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.False(await db.AppSettings.AnyAsync(s => s.Key == WhatsNew.LastVersionKey));
    }

    private sealed class FakeEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Companion.Api";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // ---- the endpoints -------------------------------------------------------------------------------------------------------------

    private static async Task<(TestServer Server, string Display, string Folder)> StartAsync(string? channel = null, bool withEntry = true)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pv-whatsnew-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var server = await TestServer.StartAsync(null, b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Companion:ReleaseChannel"] = channel });
            b.Services.AddSingleton(new ChangelogFiles([folder]));
        });
        var display = server.Services.GetRequiredService<AppVersion>().Running.Display;
        if (withEntry)
        {
            var text = $"<a name=\"english\"></a>\n## English\n\n## {display} · Unraid\n\n### ✨ New\n\n- English note.\n\n---\n\n<a name=\"deutsch\"></a>\n## Deutsch\n\n## {display} · Unraid\n\n### ✨ Neu\n\n- Deutsche Notiz.\n";
            await File.WriteAllTextAsync(Path.Combine(folder, "CHANGELOG.md"), text);
        }
        server.Client.DefaultRequestHeaders.Add("X-Requested-With", "PlatinVault");
        return (server, display, folder);
    }

    private static async Task SetStoredAsync(TestServer server, string? stored)
    {
        using var scope = server.Services.CreateScope();
        await SetAsync(scope.ServiceProvider.GetRequiredService<CompanionDbContext>(), WhatsNew.LastVersionKey, stored);
    }

    private static async Task<JsonElement> GetAsync(TestServer server, string language)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/whats-new");
        request.Headers.AcceptLanguage.ParseAdd(language);
        using var response = await server.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task The_endpoints_follow_the_whole_flow_fresh_install_update_seen()
    {
        var (server, display, folder) = await StartAsync();
        await using var _ = server;
        try
        {
            // A fresh start stored the running version: nothing opens, the notes stay available for the version display
            var fresh = await GetAsync(server, "en");
            Assert.False(fresh.GetProperty("show").GetBoolean());
            Assert.Equal(display, fresh.GetProperty("version").GetString());
            Assert.Contains("English note", fresh.GetProperty("notes").GetString());

            // An update: the stored version is an older one
            await SetStoredAsync(server, "v2026.1.1 (1.1)");
            var update = await GetAsync(server, "de");
            Assert.True(update.GetProperty("show").GetBoolean());
            Assert.Contains("Deutsche Notiz", update.GetProperty("notes").GetString());

            // Seen needs the CSRF header
            using var bare = new HttpClient { BaseAddress = server.Client.BaseAddress };
            using var refused = await bare.PostAsync("/api/whats-new/seen", null);
            Assert.False(refused.IsSuccessStatusCode);
            Assert.True((await GetAsync(server, "de")).GetProperty("show").GetBoolean());

            using var seen = await server.Client.PostAsync("/api/whats-new/seen", null);
            Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
            Assert.False((await GetAsync(server, "de")).GetProperty("show").GetBoolean());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Without_an_entry_or_as_a_dev_build_the_window_never_opens()
    {
        var (noEntry, _, folderA) = await StartAsync(withEntry: false);
        await using (noEntry)
        {
            await SetStoredAsync(noEntry, "v2026.1.1 (1.1)");
            var answer = await GetAsync(noEntry, "en");
            Assert.False(answer.GetProperty("show").GetBoolean());
            Assert.Equal(JsonValueKind.Null, answer.GetProperty("notes").ValueKind);
        }
        Directory.Delete(folderA, recursive: true);

        var (dev, _, folderB) = await StartAsync(channel: "dev");
        await using (dev)
        {
            await SetStoredAsync(dev, "v2026.1.1 (1.1)");
            var answer = await GetAsync(dev, "en");
            Assert.False(answer.GetProperty("show").GetBoolean());
            Assert.Equal(JsonValueKind.Null, answer.GetProperty("notes").ValueKind);
            using var seen = await dev.Client.PostAsync("/api/whats-new/seen", null);
            Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
            using var scope = dev.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            Assert.Equal("v2026.1.1 (1.1)", await db.AppSettings.Where(s => s.Key == WhatsNew.LastVersionKey).Select(s => s.Value).SingleAsync());
        }
        Directory.Delete(folderB, recursive: true);
    }

    // ---- the changelog ships with every artifact -----------------------------------------------------------------------------------

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dockerfile"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void The_changelog_is_part_of_the_image_the_windows_program_folder_and_the_linux_package()
    {
        var root = RepositoryRoot();

        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        Assert.Contains("COPY CHANGELOG.md ./docs/", dockerfile);

        // .dockerignore excludes *.md at the top level; without this exception the COPY above would fail the image build
        var ignore = File.ReadAllLines(Path.Combine(root, ".dockerignore")).Select(l => l.Trim()).ToList();
        Assert.Contains("!CHANGELOG.md", ignore);
        Assert.True(ignore.IndexOf("!CHANGELOG.md") > ignore.IndexOf("*.md"), "the exception must come after the *.md rule");

        Assert.Contains("'docs\\CHANGELOG.md'", File.ReadAllText(Path.Combine(root, "scripts", "build-windows.ps1")));
        Assert.Contains("$app/docs/CHANGELOG.md", File.ReadAllText(Path.Combine(root, "scripts", "build-linux.sh")));
        Assert.Contains("app/docs/CHANGELOG.md", File.ReadAllText(Path.Combine(root, "scripts", "test-linux-package.sh")));
        Assert.True(File.Exists(Path.Combine(root, "CHANGELOG.md")));
    }

    [Fact]
    public void The_server_finds_the_changelog_in_the_docs_folder_like_the_licence_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pv-changelog-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Null(new ChangelogFiles([folder]).ReadText());
            File.WriteAllText(Path.Combine(folder, "CHANGELOG.md"), "<!-- note to the owner -->\n## v1 (1.1)\n");
            var text = new ChangelogFiles([folder]).ReadText();
            Assert.NotNull(text);
            Assert.DoesNotContain("note to the owner", text);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
