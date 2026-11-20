using System.Text;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The prompts of games added by hand, pinned like the Steam prompts (Golden/prompts.txt stays untouched by them): a PS5 context, an Xbox chapter, a
/// Nintendo blueprint and the trophy-list prompt of the AI way and of the own subscription, in German and English. To regenerate on purpose set
/// PV_WRITE_GOLDEN_MANUAL to the target file path and run this test once - review the diff before committing.
/// </summary>
public sealed class PromptManualGoldenTests
{
    internal static string Render()
    {
        var text = new StringBuilder();
        foreach (var language in new[] { Lang.German, Lang.English })
        {
            var ps5 = ManualPromptFixtures.Ps5(language);
            var ps5Trophies = ManualPromptFixtures.Ps5Trophies(ps5.Id);
            var ps5Context = ManualPromptFixtures.Context(ps5, ps5Trophies, language);
            text.Append($"\n=====[ps5 context {language}]=====\n");
            text.Append(ps5Context.FactsBlock).Append('\n').Append(ps5Context.ResearchBase);
            text.Append($"\n=====[ps5 chapter {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildChapterPrompt(ps5Context, "Bosse", "Alle Bosse mit Taktik", "etabliert", "bestiary"));

            var xbox = ManualPromptFixtures.Xbox(language);
            var xboxContext = ManualPromptFixtures.Context(xbox, ManualPromptFixtures.XboxTrophies(xbox.Id), language,
                new ResearchBlocks.Selection("compact", ["missables"], "none", "tables"));
            text.Append($"\n=====[xbox chapter {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildChapterPrompt(xboxContext, "Leuchttürme", "Alle Leuchttürme", "dünn", "collectibles"));

            var nintendo = ManualPromptFixtures.Nintendo(language);
            var nintendoContext = ManualPromptFixtures.Context(nintendo, [], language);
            text.Append($"\n=====[nintendo blueprint {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildBlueprintPrompt(nintendoContext, null, []));

            text.Append($"\n=====[ps5 trophies api {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildTrophyListPrompt(ps5Context, ps5, new TrophyResearchInclude(true, false), ps5Trophies));

            var ps4 = ManualPromptFixtures.Ps4(language);
            var ps4Context = ManualPromptFixtures.Context(ps4, [], language);
            text.Append($"\n=====[ps4 trophies abo {language}]=====\n");
            text.Append(AiGuideGeneratorService.ComposeManualTrophyPrompt(ps4Context, ps4, new TrophyResearchInclude(true, false), []));

            var epic = ManualPromptFixtures.Epic(language);
            var epicContext = ManualPromptFixtures.Context(epic, [], language);
            text.Append($"\n=====[epic trophies and facts abo {language}]=====\n");
            text.Append(AiGuideGeneratorService.ComposeManualTrophyPrompt(epicContext, epic, new TrophyResearchInclude(true, true), []));
        }

        // Appended again: the one-step prompt of the subscription flow for a PS5 game with a researched trophy list and a Switch game without trophies
        foreach (var language in new[] { Lang.German, Lang.English })
        {
            var ps5 = ManualPromptFixtures.Ps5(language);
            text.Append($"\n=====[ps5 oneshot {language}]=====\n");
            text.Append(AiGuideGeneratorService.ComposeManualOneShotPrompt(ManualPromptFixtures.Context(ps5, ManualPromptFixtures.Ps5Trophies(ps5.Id), language), ps5, language));
            var nintendo = ManualPromptFixtures.Nintendo(language);
            text.Append($"\n=====[nintendo oneshot {language}]=====\n");
            text.Append(AiGuideGeneratorService.ComposeManualOneShotPrompt(ManualPromptFixtures.Context(nintendo, [], language), nintendo, language));
        }
        return text.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void The_prompts_of_games_added_by_hand_are_unchanged()
    {
        var actual = Render();
        var target = Environment.GetEnvironmentVariable("PV_WRITE_GOLDEN_MANUAL");
        if (!string.IsNullOrEmpty(target))
        {
            File.WriteAllText(target, actual, new UTF8Encoding(false));
            return;
        }
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "prompts-manual.txt")).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }
}
