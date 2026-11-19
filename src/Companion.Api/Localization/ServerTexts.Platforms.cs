using Companion.Api.Services;

namespace Companion.Api.Localization;

/// <summary>
/// Texts about the platform of a game (part of <see cref="ServerTexts"/>, kept in its own file). Same rules as there: parameterised text = method,
/// fixed text = field, English line directly behind the German one. Platform names are proper names (<see cref="GamePlatforms.DisplayName"/>).
/// </summary>
public static partial class ServerTexts
{
    public static class Platforms
    {
        public static readonly Msg InvalidTitle = M("Plattform ungültig", "Invalid platform");

        public static readonly Msg MissingPlatform = M(
            "Bitte wähle aus, auf welcher Plattform du das Spiel spielst.",
            "Please choose the platform you play the game on.");

        public static Msg UnknownPlatform(string value) => M(
            $"„{value}“ ist keine bekannte Plattform. Möglich sind: {ManualList}.",
            $"“{value}” is not a known platform. Possible values: {ManualList}.");

        public static readonly Msg SteamNotManual = M(
            "Steam-Spiele kommen aus deiner Steam-Bibliothek: setze dort unter „Alle Spiele“ den Stern. Von Hand hinzufügen lassen sich Spiele der anderen Plattformen.",
            "Steam games come from your Steam library: set the star under “All games”. Games of the other platforms can be added by hand.");

        public static readonly Msg VariantRequired = M(
            "Bitte gib bei PlayStation an, ob du auf der PS5 oder der PS4 spielst (ps5 oder ps4) – die beiden Trophäenlisten sind getrennt.",
            "For PlayStation, please say whether you play the PS5 or the PS4 version (ps5 or ps4) – the two trophy lists are separate.");

        public static Msg UnknownVariant(string value) => M(
            $"„{value}“ ist keine PlayStation-Variante. Möglich sind: ps5, ps4.",
            $"“{value}” is not a PlayStation variant. Possible values: ps5, ps4.");

        public static Msg VariantNotAllowed(string platform) => M(
            $"Eine Variante (ps5/ps4) gibt es nur bei PlayStation; für {GamePlatforms.DisplayName(platform)} bitte keine angeben.",
            $"A variant (ps5/ps4) exists only for PlayStation; please leave it out for {GamePlatforms.DisplayName(platform)}.");

        /// <summary>The reason for a refused choice (<see cref="GamePlatforms.Validate"/>).</summary>
        public static Msg Error(GamePlatformError error, string? value) => error switch
        {
            GamePlatformError.MissingPlatform => MissingPlatform,
            GamePlatformError.UnknownPlatform => UnknownPlatform(value ?? string.Empty),
            GamePlatformError.SteamNotManual => SteamNotManual,
            GamePlatformError.VariantRequired => VariantRequired,
            GamePlatformError.UnknownVariant => UnknownVariant(value ?? string.Empty),
            GamePlatformError.VariantNotAllowed => VariantNotAllowed(value ?? string.Empty),
            _ => InvalidRequestTitle,
        };

        private static string ManualList => string.Join(", ", GamePlatforms.ManualIds);
    }
}
