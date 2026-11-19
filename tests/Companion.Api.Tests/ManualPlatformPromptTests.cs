using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// Every content prompt of a game added by hand carries the rule of ITS platform (and never the Steam rule): blueprint, chapter, walkthrough outline / part /
/// single, Story &amp; Lore, achievement tips, the manual blueprint / chapter / batch / lore prompts and the trophy-list prompt of both ways.
/// </summary>
public sealed class ManualPlatformPromptTests
{
    public static TheoryData<string, string> Platforms()
    {
        var data = new TheoryData<string, string>();
        foreach (var language in new[] { Lang.German, Lang.English })
        {
            foreach (var platform in new[] { "ps5", "ps4", "xbox", "epic", "nintendo" })
            {
                data.Add(platform, language);
            }
        }
        return data;
    }

    private static (GameEntity Game, List<GameTrophyEntity> Trophies) Fixture(string platform, string language) => platform switch
    {
        "ps5" => (ManualPromptFixtures.Ps5(language), ManualPromptFixtures.Ps5Trophies(ManualPromptFixtures.Ps5(language).Id)),
        "ps4" => (ManualPromptFixtures.Ps4(language), []),
        "xbox" => (ManualPromptFixtures.Xbox(language), ManualPromptFixtures.XboxTrophies(ManualPromptFixtures.Xbox(language).Id)),
        "epic" => (ManualPromptFixtures.Epic(language), []),
        _ => (ManualPromptFixtures.Nintendo(language), []),
    };

    private static IEnumerable<(string Name, string Prompt)> ContentPrompts(GameEntity game, List<GameTrophyEntity> trophies, string language)
    {
        var context = ManualPromptFixtures.Context(game, trophies, language);
        var outline = new AiGuideGeneratorService.WalkthroughOutline(["Wiki – https://example.org/wiki"], [new("Teil 1", "Anfang."), new("Teil 2", "Ende.")]);
        yield return ("blueprint", AiGuideGeneratorService.BuildBlueprintPrompt(context, null, []));
        yield return ("chapter", AiGuideGeneratorService.BuildChapterPrompt(context, "Bosse", "Alle Bosse", "etabliert", "bestiary"));
        yield return ("walkthrough single", AiGuideGeneratorService.BuildWalkthroughSinglePrompt(context, "Komplettlösung", "Das ganze Spiel", "etabliert"));
        yield return ("outline", AiGuideGeneratorService.BuildWalkthroughOutlinePrompt(context, "Das ganze Spiel", "etabliert", 12));
        yield return ("part", AiGuideGeneratorService.BuildWalkthroughPartPrompt(context, outline, 0, ["Waffen"]));
        yield return ("lore", AiGuideGeneratorService.BuildLorePrompt(context, AiGuideGeneratorService.LoreParts(language)[0]));
        yield return ("tips", AiGuideGeneratorService.BuildAchievementTipsPrompt(context));
        yield return ("manual blueprint", AiGuideGeneratorService.ComposeManualBlueprintPrompt(context, game, language));
        yield return ("manual chapter", AiGuideGeneratorService.ComposeManualChapterPrompt(context, game, language, "Bosse", "Alle Bosse", "dünn", "bestiary"));
        yield return ("manual walkthrough", AiGuideGeneratorService.ComposeManualChapterPrompt(context, game, language, "Komplettlösung", "Das ganze Spiel", "dünn", WalkthroughTab.TopicId));
        yield return ("manual lore", AiGuideGeneratorService.ComposeManualLorePrompt(context, game, language));
        yield return ("manual batch", AiGuideGeneratorService.ComposeManualBatchPrompt(context, game, language,
            [new BatchChapter(0, "K01", "Komplettlösung", "Das ganze Spiel", "walkthrough", "etabliert", null, null),
             new BatchChapter(1, "K02", "Bosse", "Alle Bosse", "bestiary", "etabliert", null, null)]));
        if (GamePlatforms.HasTrophies(game.Platform))
        {
            yield return ("trophies api", AiGuideGeneratorService.BuildTrophyListPrompt(context, game, new TrophyResearchInclude(true, true), trophies));
            yield return ("trophies abo", AiGuideGeneratorService.ComposeManualTrophyPrompt(context, game, new TrophyResearchInclude(true, false), trophies));
        }
        else
        {
            yield return ("facts api", AiGuideGeneratorService.BuildTrophyListPrompt(context, game, new TrophyResearchInclude(false, true), trophies));
        }
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void Every_content_prompt_carries_the_rule_of_the_platform_and_never_the_Steam_rule(string platform, string language)
    {
        var (game, trophies) = Fixture(platform, language);
        var rule = AiGuideGeneratorService.PlatformRules(language, ManualPromptFixtures.Context(game, trophies, language).Platform).Replace("\r\n", "\n");
        var steamRule = AiGuideGeneratorService.PlatformRules(language).Replace("\r\n", "\n");

        foreach (var (name, prompt) in ContentPrompts(game, trophies, language))
        {
            var text = prompt.Replace("\r\n", "\n");
            Assert.True(text.Contains(rule, StringComparison.Ordinal), $"{name} ({platform}, {language}) lacks the platform rule");
            Assert.False(text.Contains(steamRule, StringComparison.Ordinal), $"{name} ({platform}, {language}) carries the Steam rule");
            Assert.DoesNotContain("Steam-AppID", text);
            Assert.DoesNotContain("Steam AppID", text);
        }
    }

    [Fact]
    public void The_PlayStation_rules_keep_the_PS5_and_PS4_lists_apart()
    {
        var ps5 = AiGuideGeneratorService.PlatformRules(Lang.English, new GamePlatformContext(GamePlatforms.PlayStation, GamePlatforms.Ps5, 0, false));
        var ps4 = AiGuideGeneratorService.PlatformRules(Lang.German, new GamePlatformContext(GamePlatforms.PlayStation, GamePlatforms.Ps4, 0, false));

        Assert.Contains("never mix in trophies of the PS4 list", ps5);
        Assert.Contains("Trophäen der PS5-Liste", ps4);
    }

    [Fact]
    public void The_Nintendo_trophy_prompt_asks_for_facts_only()
    {
        var game = ManualPromptFixtures.Nintendo(Lang.English);
        var prompt = AiGuideGeneratorService.BuildTrophyListPrompt(ManualPromptFixtures.Context(game, [], Lang.English), game, new TrophyResearchInclude(false, true), []);

        Assert.Contains("\"facts\"", prompt);
        Assert.DoesNotContain("\"trophies\"", prompt);
        Assert.DoesNotContain("\"documented\"", prompt);
    }
}
