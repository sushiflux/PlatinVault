using Companion.Api.Data;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>Why a platform choice for a manual game was refused (<see cref="GamePlatforms.Validate"/>); the text is <see cref="ServerTexts.Platforms.Error"/>.</summary>
public enum GamePlatformError
{
    None,
    MissingPlatform,
    UnknownPlatform,
    SteamNotManual,
    VariantRequired,
    UnknownVariant,
    VariantNotAllowed,
}

/// <summary>A valid, normalised platform choice (lower-case ids; Variant only for PlayStation).</summary>
public sealed record GamePlatformChoice(string Platform, string? Variant);

/// <summary>Result of <see cref="GamePlatforms.Validate"/>: either Choice (Ok) or Error with the refused value.</summary>
public sealed record GamePlatformValidation(GamePlatformChoice? Choice, GamePlatformError Error, string? Value = null)
{
    public bool Ok => Choice is not null;

    /// <summary>The complete reason in the language of the request (null when Ok).</summary>
    public Msg? Message => Ok ? null : ServerTexts.Platforms.Error(Error, Value);
}

/// <summary>
/// The platforms a game can be played on (owner decision 2026-11-19). A game from the Steam library is always "steam"; a game added by hand
/// has one of the other ids and never a Steam AppID. PlayStation has the variants "ps5" / "ps4" because their trophy lists are separate
/// (a PS4 game played on a PS5 earns the PS4 list). Nintendo Switch has no trophies or achievements (guide only).
/// Ids are stable API values (database column <c>games.Platform</c>, DTOs, the frontend's <c>utils/platforms.ts</c>) - never rename one.
/// </summary>
public static class GamePlatforms
{
    public const string Steam = "steam";
    public const string PlayStation = "playstation";
    public const string Xbox = "xbox";
    public const string Epic = "epic";
    public const string Gog = "gog";
    public const string Nintendo = "nintendo";
    public const string Ubisoft = "ubisoft";
    public const string Ea = "ea";
    public const string BattleNet = "battlenet";

    public const string Ps5 = "ps5";
    public const string Ps4 = "ps4";

    /// <summary>Value of <c>kind</c> in the trophy list: PlayStation calls them trophies, every other platform achievements.</summary>
    public const string KindTrophies = "trophies";
    public const string KindAchievements = "achievements";

    /// <summary>Every platform id in display order.</summary>
    public static readonly IReadOnlyList<string> Ids = [Steam, PlayStation, Xbox, Epic, Gog, Nintendo, Ubisoft, Ea, BattleNet];

    /// <summary>The platforms a game can be added by hand for (Steam games come from the Steam library).</summary>
    public static readonly IReadOnlyList<string> ManualIds = [.. Ids.Where(id => id != Steam)];

    private static readonly IReadOnlyList<string> PlayStationVariants = [Ps5, Ps4];

    /// <summary>The variants of a platform (PlayStation: ps5, ps4); empty for every other one.</summary>
    public static IReadOnlyList<string> VariantsOf(string? platform) => Normalize(platform) == PlayStation ? PlayStationVariants : [];

    /// <summary>The known id for a value (trimmed, case ignored), else null.</summary>
    public static string? Normalize(string? platform)
    {
        var value = platform?.Trim().ToLowerInvariant();
        return value is not null && Ids.Contains(value) ? value : null;
    }

    public static bool IsKnown(string? platform) => Normalize(platform) is not null;

    /// <summary>A missing value counts as Steam (rows from before the column existed are "steam" anyway).</summary>
    public static bool IsSteam(string? platform) => string.IsNullOrWhiteSpace(platform) || Normalize(platform) == Steam;

    public static bool IsSteam(GameEntity game) => IsSteam(game.Platform);

    /// <summary>A game added by hand: its trophies live in <c>game_trophies</c> and are ticked by hand.</summary>
    public static bool IsManual(GameEntity game) => !IsSteam(game);

    /// <summary>Whether the platform has trophies / achievements at all (Nintendo Switch has none).</summary>
    public static bool HasTrophies(string? platform) => Normalize(platform) is { } id && id != Nintendo;

    /// <summary>"trophies" for PlayStation, else "achievements".</summary>
    public static string TrophyKind(string? platform) => Normalize(platform) == PlayStation ? KindTrophies : KindAchievements;

    /// <summary>
    /// Whether the platform grades its trophies with a platinum trophy (PlayStation). There the platinum moment is the platinum trophy;
    /// everywhere else it is 100 % of all trophies (see <see cref="TrophyProgress"/>).
    /// </summary>
    public static bool HasPlatinumGrade(string? platform) => Normalize(platform) == PlayStation;

    /// <summary>The readable name ("PlayStation 5", "Xbox", "Epic Games", ...). Proper names, the same in every language.</summary>
    public static string DisplayName(string? platform, string? variant = null) => (Normalize(platform), variant?.Trim().ToLowerInvariant()) switch
    {
        (PlayStation, Ps5) => "PlayStation 5",
        (PlayStation, Ps4) => "PlayStation 4",
        (PlayStation, _) => "PlayStation",
        (Xbox, _) => "Xbox",
        (Epic, _) => "Epic Games",
        (Gog, _) => "GOG",
        (Nintendo, _) => "Nintendo Switch",
        (Ubisoft, _) => "Ubisoft Connect",
        (Ea, _) => "EA app",
        (BattleNet, _) => "Battle.net",
        _ => "Steam",
    };

    /// <summary>
    /// Checks the platform choice of a game added by hand: a known platform other than Steam; a variant (ps5/ps4) exactly for PlayStation.
    /// Values are trimmed and compared without case; the choice comes back normalised.
    /// </summary>
    public static GamePlatformValidation Validate(string? platform, string? variant)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return new(null, GamePlatformError.MissingPlatform);
        }
        var id = Normalize(platform);
        if (id is null)
        {
            return new(null, GamePlatformError.UnknownPlatform, platform.Trim());
        }
        if (id == Steam)
        {
            return new(null, GamePlatformError.SteamNotManual, id);
        }

        var variants = VariantsOf(id);
        var givenVariant = string.IsNullOrWhiteSpace(variant) ? null : variant.Trim().ToLowerInvariant();
        if (variants.Count == 0)
        {
            return givenVariant is null
                ? new(new GamePlatformChoice(id, null), GamePlatformError.None)
                : new(null, GamePlatformError.VariantNotAllowed, id);
        }
        if (givenVariant is null)
        {
            return new(null, GamePlatformError.VariantRequired, id);
        }
        return variants.Contains(givenVariant)
            ? new(new GamePlatformChoice(id, givenVariant), GamePlatformError.None)
            : new(null, GamePlatformError.UnknownVariant, variant!.Trim());
    }
}
