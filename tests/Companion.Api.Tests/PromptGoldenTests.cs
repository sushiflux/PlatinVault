using System.Text;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The single-chapter and walkthrough prompts (real AI path and manual flow) must stay byte-identical when their shared parts are reused by the
/// batch prompt. The golden file holds the rendered prompts of a fixed context; line endings are normalised so a CRLF checkout does not matter.
/// To regenerate it on purpose, set PV_WRITE_GOLDEN to the target file path and run this test once - review the diff before committing.
/// </summary>
public sealed class PromptGoldenTests
{
    private static AiGuideGeneratorService.GameContext Context(string language, ResearchBlocks.Selection blocks) =>
        new("Suncraft", language == Lang.English ? "Steam AppID: 4078110\nGenres according to Steam: Simulation" : "Steam-AppID: 4078110\nGenres laut Steam: Simulation", [], [], blocks,
            ResearchBlocks.FillBase(language, "Suncraft", "Simulation", ResearchBlocks.PlatformText), language);

    private static string Render()
    {
        var text = new StringBuilder();
        var selections = new (string Name, ResearchBlocks.Selection Blocks)[]
        {
            ("empty", ResearchBlocks.Selection.Empty),
            ("compact", new ResearchBlocks.Selection("compact", ["missables"], "none", "tables")),
        };
        foreach (var language in new[] { Lang.German, Lang.English })
        {
            foreach (var (name, blocks) in selections)
            {
                var context = Context(language, blocks);
                foreach (var topic in new[] { "weapons", "roadmap" })
                {
                    text.Append($"\n=====[chapter {language} {name} {topic}]=====\n");
                    text.Append(AiGuideGeneratorService.BuildChapterPrompt(context, "Dorfausbau", "Alle Ausbaustufen des Dorfes", "dünn", topic));
                }
                text.Append($"\n=====[walkthrough {language} {name}]=====\n");
                text.Append(AiGuideGeneratorService.BuildWalkthroughSinglePrompt(context, "Komplettlösung", "Das ganze Spiel", "etabliert"));
                text.Append($"\n=====[manual chapter {language} {name}]=====\n");
                text.Append(AiGuideGeneratorService.ComposeManualChapterPrompt(context,
                    new() { Title = "Suncraft", SteamAppId = 4078110, Slug = "suncraft", ContentLanguage = language, Tabs = [] }, language, "Dorfausbau", "Alle Ausbaustufen", "dünn", "weapons"));
            }

            // The other content prompts (once per language, empty selection): outline, one part, lore, achievement tips and the batch prompt
            var plain = Context(language, ResearchBlocks.Selection.Empty) with
            {
                Achievements = [("ACH_FIRST", "Erster Schritt", "Beende das erste Kapitel."), ("ACH_SECRET", "Geheimnis", "")]
            };
            var outline = new AiGuideGeneratorService.WalkthroughOutline(["Wiki – https://example.org/wiki"],
                [new("Kapitel 1 – Das Dorf", "Ankunft im Dorf, erster Boss."), new("Kapitel 2 – Der Wald", "Wald, Schatztruhen.")]);
            text.Append($"\n=====[outline {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildWalkthroughOutlinePrompt(plain, "Das ganze Spiel", "etabliert", 12));
            text.Append($"\n=====[part {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildWalkthroughPartPrompt(plain, outline, 0, ["Waffen", "Sammelobjekte"]));
            text.Append($"\n=====[lore {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildLorePrompt(plain, AiGuideGeneratorService.LoreParts(language)[0]));
            text.Append($"\n=====[tips {language}]=====\n");
            text.Append(AiGuideGeneratorService.BuildAchievementTipsPrompt(plain));
            text.Append($"\n=====[batch {language}]=====\n");
            text.Append(AiGuideGeneratorService.ComposeManualBatchPrompt(plain,
                new() { Title = "Suncraft", SteamAppId = 4078110, Slug = "suncraft", ContentLanguage = language, Tabs = [] }, language,
                [new BatchChapter(0, "K01", "Komplettlösung", "Das ganze Spiel", "walkthrough", "etabliert", null, null),
                 new BatchChapter(1, "K02", "Dorfausbau", "Alle Ausbaustufen", "weapons", "etabliert", null, null)]));
        }
        return text.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void The_single_chapter_walkthrough_and_manual_prompts_are_unchanged()
    {
        var actual = Render();
        var target = Environment.GetEnvironmentVariable("PV_WRITE_GOLDEN");
        if (!string.IsNullOrEmpty(target))
        {
            File.WriteAllText(target, actual, new UTF8Encoding(false));
            return;
        }
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "prompts.txt")).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }
}
