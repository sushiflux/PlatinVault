using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;
using PlatinVault.Versioning;

namespace Companion.Api.Services;

/// <summary>Answer of <c>GET /api/whats-new</c>: the running version (readable form), whether the app should open the window by itself, and the notes.</summary>
/// <param name="Notes">The changelog entry of the running version in the request language (Markdown, heading line included); null when there is none or this is a dev build.</param>
public sealed record WhatsNewDto(string Version, bool Show, string? Notes);

/// <summary>
/// Cuts one version's entry out of the customer changelog. The same rules as scripts/release-notes.sh (which builds the GitHub release
/// body): the English section is announced by an anchor <c>&lt;a name="english"&gt;</c>, the German one by <c>&lt;a name="deutsch"&gt;</c>;
/// an entry is the <c>## &lt;version&gt;</c> heading (optionally followed by <c> · &lt;platforms&gt;</c>) and everything up to the next
/// <c>## </c> heading or the next anchor, without a trailing <c>---</c> rule and blank lines.
/// </summary>
public static class ChangelogEntry
{
    private const string PlatformSeparator = " · ";

    /// <summary>The entry of this version in this language section, or null when the section has none.</summary>
    public static string? Extract(string markdown, string readableVersion, string language)
    {
        var wanted = language == Lang.German ? "deutsch" : "english";
        var section = false;
        var taking = false;
        var found = false;
        var lines = new List<string>();

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (IsAnchor(line, out var anchor))
            {
                section = anchor == wanted;
                taking = false;
                continue;
            }
            if (taking && line.StartsWith("## ", StringComparison.Ordinal)) taking = false;
            if (section && !found && line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..];
                if (heading == readableVersion || heading.StartsWith(readableVersion + PlatformSeparator, StringComparison.Ordinal))
                {
                    taking = true;
                    found = true;
                }
            }
            if (taking) lines.Add(line);
        }

        if (!found) return null;
        while (lines.Count > 0 && (lines[^1].Length == 0 || lines[^1] == "---")) lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines);
    }

    /// <summary>The entry in the requested language; when that section has none, the one of the other language (never nothing while any exists).</summary>
    public static string? ForLanguage(string markdown, string readableVersion, string language)
    {
        var primary = Lang.NormalizeOr(language);
        return Extract(markdown, readableVersion, primary)
            ?? Extract(markdown, readableVersion, primary == Lang.German ? Lang.English : Lang.German);
    }

    private static bool IsAnchor(string line, out string name)
    {
        name = string.Empty;
        foreach (var candidate in new[] { "english", "deutsch" })
        {
            if (line.StartsWith($"<a name=\"{candidate}\"", StringComparison.Ordinal) || line.StartsWith($"<a id=\"{candidate}\"", StringComparison.Ordinal))
            {
                name = candidate;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// "What's new" after an update (owner's rule 2026-11-04): on the first start after an update the app opens a window with the changelog
/// entry of the version that now runs, once per version. State is one <c>app_settings</c> row, <see cref="LastVersionKey"/>, holding the
/// readable version that was last shown or acknowledged. Browser and Windows program share one database, so whichever opens first shows it.
/// <para>The window opens ("Show") exactly when ALL of these hold:</para>
/// <list type="number">
/// <item>this is not a dev build or the Development environment (<see cref="VersionKind.Dev"/>);</item>
/// <item>the running readable version differs from the stored one;</item>
/// <item>the shipped changelog has an entry for the running version;</item>
/// <item>the database is not fresh: with nothing stored yet, an existing <c>app.schema_version</c> row means an older installation (an update
/// from a build that did not know this feature). A fresh install is told apart at startup - <c>DatabaseInitializer</c> calls
/// <see cref="RecordFreshInstallAsync"/> when the database had no applied migrations - and stores the version silently, so the onboarding
/// is the only welcome. (Comparing <c>app.schema_version</c> with the running version alone cannot work: an update that applies a migration
/// overwrites that row in the same start, before the first request.)</item>
/// </list>
/// </summary>
public static class WhatsNew
{
    public const string LastVersionKey = "whatsnew.last_version";

    /// <summary>The pure decision (see the class summary). <paramref name="schemaVersion"/> is the stored <c>app.schema_version</c>, null when there is none.</summary>
    public static bool ShouldShow(VersionKind kind, string running, string? stored, string? schemaVersion, bool hasEntry)
    {
        if (kind == VersionKind.Dev || !hasEntry) return false;
        if (stored is not null) return !string.Equals(stored, running, StringComparison.Ordinal);
        // Nothing stored: an older installation shows it once, a database without any version record is fresh
        return schemaVersion is not null;
    }

    public static async Task<WhatsNewDto> GetAsync(CompanionDbContext db, RunningVersion running, string? changelog, string language, CancellationToken ct = default)
    {
        if (running.Kind == VersionKind.Dev) return new WhatsNewDto(running.Display, false, null);

        var notes = changelog is null ? null : ChangelogEntry.ForLanguage(changelog, running.Display, language);
        var stored = await ReadAsync(db, LastVersionKey, ct);
        var schema = stored is null ? await ReadAsync(db, DatabaseInitializer.SchemaVersionKey, ct) : null;

        // No record of either: nothing to compare with, so this is a fresh install - remember the version, show nothing
        if (stored is null && schema is null)
        {
            await RecordFreshInstallAsync(db, running.Display, ct);
            return new WhatsNewDto(running.Display, false, notes);
        }
        return new WhatsNewDto(running.Display, ShouldShow(running.Kind, running.Display, stored, schema, notes is not null), notes);
    }

    /// <summary>Marks the running version as shown. A dev build never writes anything.</summary>
    public static async Task MarkSeenAsync(CompanionDbContext db, RunningVersion running, CancellationToken ct = default)
    {
        if (running.Kind == VersionKind.Dev) return;
        await WriteAsync(db, running.Display, overwrite: true, ct);
    }

    /// <summary>A fresh installation: stores the version if nothing is stored yet (never overwrites), so it is not offered as "new".</summary>
    public static Task RecordFreshInstallAsync(CompanionDbContext db, string display, CancellationToken ct = default) =>
        WriteAsync(db, display, overwrite: false, ct);

    private static Task<string?> ReadAsync(CompanionDbContext db, string key, CancellationToken ct) =>
        db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => (string?)s.Value).FirstOrDefaultAsync(ct);

    private static async Task WriteAsync(CompanionDbContext db, string value, bool overwrite, CancellationToken ct)
    {
        // Two servers can share one SQLite file: when the insert loses the race, read again and decide on what the other one wrote
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == LastVersionKey, ct);
            if (row is null)
            {
                db.AppSettings.Add(new AppSettingEntity { Key = LastVersionKey, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
            }
            else if (overwrite && row.Value != value)
            {
                row.Value = value;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                return;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
