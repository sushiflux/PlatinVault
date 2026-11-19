namespace Companion.Api.Services;

/// <summary>
/// The tier (Bronze / Silver / Gold / Platinum - the colour and sound of the popup, the tier filters) of a trophy of a manual game.
/// One rule for the whole app (coordinator decision D4):
/// <list type="number">
/// <item>PlayStation: the trophy's own grade (platinum / gold / silver / bronze).</item>
/// <item>Else, when a source states the share of players: the Steam scale (<see cref="SteamLiveTrackerService.CalculateRarityTier"/>).</item>
/// <item>Xbox without a share: gamerscore bands (see <see cref="ForGamerscore"/>).</item>
/// <item>Otherwise Bronze.</item>
/// </list>
/// Only a PlayStation platinum grade is ever tier Platinum (the Steam scale has no Platinum).
/// </summary>
public static class TrophyTiers
{
    // Grades as stored in game_trophies.Grade (PlayStation only)
    public const string Platinum = "platinum";
    public const string Gold = "gold";
    public const string Silver = "silver";
    public const string Bronze = "bronze";

    public static readonly IReadOnlyList<string> Grades = [Platinum, Gold, Silver, Bronze];

    // Tiers as the live UI knows them (RarityTier in the frontend)
    public const string TierPlatinum = "Platinum";
    public const string TierGold = "Gold";
    public const string TierSilver = "Silver";
    public const string TierBronze = "Bronze";

    /// <summary>The known grade for a value (trimmed, case ignored), else null.</summary>
    public static string? NormalizeGrade(string? grade)
    {
        var value = grade?.Trim().ToLowerInvariant();
        return value is not null && Grades.Contains(value) ? value : null;
    }

    /// <summary>
    /// The tier of one trophy. <paramref name="name"/> / <paramref name="description"/> feed the Steam scale's rule that a "complete chapter N"
    /// achievement is always Bronze (only used on the share-based path, as for Steam games).
    /// </summary>
    public static string For(string? platform, string? grade, int? points, double? rarityPercent, string? name = null, string? description = null)
    {
        if (GamePlatforms.Normalize(platform) == GamePlatforms.PlayStation && NormalizeGrade(grade) is { } ownGrade)
        {
            return ownGrade switch
            {
                Platinum => TierPlatinum,
                Gold => TierGold,
                Silver => TierSilver,
                _ => TierBronze,
            };
        }
        if (rarityPercent is { } share && double.IsFinite(share) && share is >= 0 and <= 100)
        {
            return SteamLiveTrackerService.CalculateRarityTier(share, name, description);
        }
        if (GamePlatforms.Normalize(platform) == GamePlatforms.Xbox && points is { } gamerscore)
        {
            return ForGamerscore(gamerscore);
        }
        return TierBronze;
    }

    /// <summary>
    /// Xbox without a stated share: 50 gamerscore or more = Gold, 20-49 = Silver, below 20 = Bronze. These bands are our own choice
    /// (coordinator decision D4), not an Xbox rule - Xbox has no tiers; they only give big achievements a bigger popup.
    /// </summary>
    public static string ForGamerscore(int gamerscore) => gamerscore switch
    {
        >= 50 => TierGold,
        >= 20 => TierSilver,
        _ => TierBronze,
    };
}
