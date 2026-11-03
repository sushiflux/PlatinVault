using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>The file cannot be imported; <see cref="Message"/> is the explanation shown to the user (which field, why), in the language of the reader.</summary>
public sealed class GuideImportException(Msg text) : Exception(text.De)
{
    public Msg Text { get; } = text;

    public override string Message => Text.ToString();
}

/// <summary>A validated guide file: lengths are within the database limits, URLs are http(s) or empty, sections are normalized.</summary>
public sealed record ParsedGuide(
    string Title,
    string Subtitle,
    int? SteamAppId,
    string CoverUrl,
    string HeroBannerUrl,
    string AccentColor,
    string EstimatedHours,
    string MissablesInfo,
    string DifficultyInfo,
    string GenreTags,
    // "de" / "en": the language the guide text is written in (files without the field are German, as all files were before)
    string ContentLanguage,
    List<ParsedGuideTab> Tabs);

/// <summary>IsLore = the optional "story-lore" tab (the only slug an import keeps; every other tab slug is generated).</summary>
public sealed record ParsedGuideTab(string Title, string Icon, string SectionsJson, bool IsLore = false, string? GroupKey = null, bool IsWalkthrough = false);

/// <summary>
/// Reads and validates a "platinvault-guide" document (see GuideExportDocument). The file may come from anyone, so every
/// field is checked: type, length (database limits), URL scheme; sections go through the same normalization as AI output.
/// </summary>
public static class GuideImport
{
    public const string FormatName = "platinvault-guide";
    public const int SupportedFormatVersion = 1;
    public const int MaxTabs = 40;

    // Limits of the games / guide_tabs columns (CompanionDbContext)
    private const int MaxTitle = 240;
    private const int MaxSubtitle = 500;
    private const int MaxUrl = 1000;
    private const int MaxAccentColor = 40;
    private const int MaxShort = 240;
    private const int MaxEstimatedHours = 160;
    private const int MaxTabTitle = 240;
    private const int MaxTabIcon = 120;

    public static ParsedGuide Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new GuideImportException(ServerTexts.Import.RootNotObject);
        }

        ValidateHeader(root);

        if (!TryGet(root, "game", out var game) || game.ValueKind != JsonValueKind.Object)
        {
            throw new GuideImportException(ServerTexts.Import.GameMissing);
        }

        var title = OptionalText(game, "title", "game.title", MaxTitle);
        if (string.IsNullOrEmpty(title))
        {
            throw new GuideImportException(ServerTexts.Import.GameTitleMissing);
        }

        // Optional "game.contentLanguage" ("de" / "en"); files from before the field existed were all German. An unknown value
        // is not an error: the text language is only a hint for later additions to the guide.
        var language = TryGet(game, "contentLanguage", out var languageEl) && languageEl.ValueKind == JsonValueKind.String
            ? Lang.NormalizeOr(languageEl.GetString(), Lang.German)
            : Lang.German;
        var defaults = AiGuideGeneratorService.DefaultsFor(language);

        // "game.modules" of older export files is ignored: every game gets live achievements and the guide
        // Texts the normalizer makes up (an empty table caption) follow the guide's language, not the one of the request
        List<ParsedGuideTab> tabs;
        using (Lang.Use(language))
        {
            tabs = ParseTabs(root);
        }

        return new ParsedGuide(
            Title: title,
            Subtitle: OrDefault(OptionalText(game, "subtitle", "game.subtitle", MaxSubtitle), defaults.Subtitle),
            SteamAppId: ParseSteamAppId(game),
            CoverUrl: HttpUrlOrEmpty(OptionalText(game, "coverUrl", "game.coverUrl", int.MaxValue)),
            HeroBannerUrl: HttpUrlOrEmpty(OptionalText(game, "heroBannerUrl", "game.heroBannerUrl", int.MaxValue)),
            AccentColor: OrDefault(OptionalText(game, "accentColor", "game.accentColor", MaxAccentColor), "emerald"),
            EstimatedHours: OrDefault(OptionalText(game, "estimatedHours", "game.estimatedHours", MaxEstimatedHours), defaults.EstimatedHours),
            MissablesInfo: OrDefault(OptionalText(game, "missablesInfo", "game.missablesInfo", MaxShort), defaults.MissablesInfo),
            DifficultyInfo: OrDefault(OptionalText(game, "difficultyInfo", "game.difficultyInfo", MaxShort), defaults.DifficultyInfo),
            GenreTags: OrDefault(OptionalText(game, "genreTags", "game.genreTags", MaxShort), defaults.GenreTags),
            ContentLanguage: language,
            Tabs: tabs);
    }

    private static void ValidateHeader(JsonElement root)
    {
        if (!TryGet(root, "format", out var format) || format.ValueKind != JsonValueKind.String)
        {
            throw new GuideImportException(ServerTexts.Import.FormatMissing(FormatName));
        }
        if (format.GetString() != FormatName)
        {
            throw new GuideImportException(ServerTexts.Import.FormatWrong(Shorten(format.GetString() ?? string.Empty), FormatName));
        }

        if (!TryGet(root, "formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber))
        {
            throw new GuideImportException(ServerTexts.Import.FormatVersionMissing);
        }
        if (versionNumber > SupportedFormatVersion)
        {
            throw new GuideImportException(ServerTexts.Import.FormatVersionTooNew(versionNumber, SupportedFormatVersion));
        }
        if (versionNumber < SupportedFormatVersion)
        {
            throw new GuideImportException(ServerTexts.Import.FormatVersionInvalid(versionNumber, SupportedFormatVersion));
        }
    }

    private static List<ParsedGuideTab> ParseTabs(JsonElement root)
    {
        if (!TryGet(root, "tabs", out var tabsEl) || tabsEl.ValueKind == JsonValueKind.Null)
        {
            return [];
        }
        if (tabsEl.ValueKind != JsonValueKind.Array)
        {
            throw new GuideImportException(ServerTexts.Import.TabsNotList);
        }

        var count = tabsEl.GetArrayLength();
        if (count > MaxTabs)
        {
            throw new GuideImportException(ServerTexts.Import.TooManyTabs(count, MaxTabs));
        }

        var parsed = new List<(int Order, int Index, ParsedGuideTab Tab)>(count);
        var index = 0;
        foreach (var tabEl in tabsEl.EnumerateArray())
        {
            var path = $"tabs[{index}]";
            if (tabEl.ValueKind != JsonValueKind.Object)
            {
                throw new GuideImportException(ServerTexts.Import.TabNotObject(path));
            }

            var title = OptionalText(tabEl, "title", $"{path}.title", MaxTabTitle);
            if (string.IsNullOrEmpty(title))
            {
                throw new GuideImportException(ServerTexts.Import.TabTitleMissing(path));
            }

            var icon = OrDefault(OptionalText(tabEl, "icon", $"{path}.icon", MaxTabIcon), "mdi:compass-outline");

            var order = index + 1;
            if (TryGet(tabEl, "sortOrder", out var orderEl) && orderEl.ValueKind != JsonValueKind.Null)
            {
                if (orderEl.ValueKind != JsonValueKind.Number || !orderEl.TryGetInt32(out order))
                {
                    throw new GuideImportException(ServerTexts.Import.SortOrderInvalid(path, Shorten(title)));
                }
            }

            // The slug is only honoured for the optional lore tab and the walkthrough; every other slug is generated so imports can never collide
            var fileSlug = OptionalText(tabEl, "slug", $"{path}.slug", int.MaxValue);
            var isLore = fileSlug == LoreTab.Slug;
            var isWalkthrough = fileSlug == WalkthroughTab.Slug;
            if (isWalkthrough && parsed.Any(p => p.Tab.IsWalkthrough))
            {
                throw new GuideImportException(ServerTexts.Import.LoreTabTwice(WalkthroughTab.Slug, path));
            }
            if (isLore && parsed.Any(p => p.Tab.IsLore))
            {
                throw new GuideImportException(ServerTexts.Import.LoreTabTwice(LoreTab.Slug, path));
            }

            // An explicit "groupKey" of the file is kept when it names a known group; anything else = decided from the title later
            var groupKey = OptionalText(tabEl, "groupKey", $"{path}.groupKey", 32).ToLowerInvariant();
            parsed.Add((isLore ? int.MaxValue : isWalkthrough ? int.MinValue : order, index, new ParsedGuideTab(title, icon, NormalizeSections(tabEl, path, title), isLore, GuideGroups.IsValid(groupKey) ? groupKey : null, isWalkthrough)));
            index++;
        }

        return parsed.OrderBy(t => t.Order).ThenBy(t => t.Index).Select(t => t.Tab).ToList();
    }

    private static string NormalizeSections(JsonElement tabEl, string path, string tabTitle)
    {
        if (!TryGet(tabEl, "sections", out var sectionsEl) || sectionsEl.ValueKind == JsonValueKind.Null)
        {
            return "[]";
        }
        if (sectionsEl.ValueKind != JsonValueKind.Array)
        {
            throw new GuideImportException(ServerTexts.Import.SectionsNotList(path, Shorten(tabTitle)));
        }

        var normalized = GuideSectionNormalizer.Normalize(sectionsEl);
        if (normalized.Count == 0 && sectionsEl.GetArrayLength() > 0)
        {
            throw new GuideImportException(ServerTexts.Import.NoReadableSections(Shorten(tabTitle), path, sectionsEl.GetArrayLength()));
        }
        return normalized.ToJsonString();
    }

    private static int? ParseSteamAppId(JsonElement game)
    {
        if (!TryGet(game, "steamAppId", out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var id) || id <= 0)
        {
            throw new GuideImportException(ServerTexts.Import.SteamAppIdInvalid);
        }
        return id;
    }

    /// <summary>Optional text field: missing or null = empty; wrong type or too long = rejected with the field name.</summary>
    private static string OptionalText(JsonElement obj, string name, string path, int maxLength)
    {
        if (!TryGet(obj, name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }
        if (el.ValueKind != JsonValueKind.String)
        {
            throw new GuideImportException(ServerTexts.Import.FieldNotText(path));
        }

        var value = (el.GetString() ?? string.Empty).Trim();
        if (value.Length > maxLength)
        {
            throw new GuideImportException(ServerTexts.Import.FieldTooLong(path, value.Length, maxLength));
        }
        return value;
    }

    /// <summary>Only absolute http(s) URLs survive; anything else (javascript:, data:, relative, too long) becomes empty.</summary>
    private static string HttpUrlOrEmpty(string value) =>
        value.Length is > 0 and <= MaxUrl &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        !string.IsNullOrEmpty(uri.Host)
            ? value
            : string.Empty;

    private static string OrDefault(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Shorten(string value) => value.Length > 60 ? value[..60] + "…" : value;

    /// <summary>Case-insensitive property lookup, so a hand-edited file with "SteamAppId" works as well.</summary>
    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
