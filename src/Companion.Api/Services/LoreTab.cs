namespace Companion.Api.Services;

/// <summary>The optional "Story & Lore" tab (spoilers): one tab per game, found by its fixed slug, always sorted last.</summary>
public static class LoreTab
{
    public const string Slug = "story-lore";
    public const string Title = "Story & Lore";
    public const string Icon = "mdi:book-open-page-variant";
    public const int SortOrder = 900;
}
