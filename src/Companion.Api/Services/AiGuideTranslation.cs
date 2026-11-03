using System.Text.Json;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;

namespace Companion.Api.Services;

// Translating an existing guide (AI, no web search). The unit of work is a list of plain sentences, see GuideTextExtractor.
public sealed partial class AiGuideGeneratorService
{
    // A request carries about this much source text (the answer is about as long): well inside every provider's output limit
    private const int TranslationChunkChars = 6000;
    private const int TranslationChunkItems = 100;

    /// <summary>The settings the translation uses (the same stored provider choice and models as the research).</summary>
    public Task<SettingsResponseDto> GetSettingsAsync(CancellationToken ct) => settingsStore.GetAsync(ct);

    /// <summary>
    /// Translates distinct texts of one chapter. Returns source text -> translation for EVERY text, or throws: a translation with
    /// holes is never returned. Chunks run in parallel through <paramref name="gate"/> (at most three calls at once, like the
    /// research); which provider takes a chunk, and what happens when it cannot be used, is decided by <paramref name="run"/>
    /// (the stored AI selection: Standard-KI / mixed, automatic fallback).
    /// </summary>
    public async Task<Dictionary<string, string>> TranslateTextsAsync(
        string gameTitle,
        string chapterTitle,
        IReadOnlyList<string> texts,
        string sourceLanguage,
        string targetLanguage,
        AiRun run,
        SettingsResponseDto settings,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (texts.Count == 0)
        {
            return result;
        }

        var chunks = new List<List<string>>();
        var current = new List<string>();
        var size = 0;
        foreach (var text in texts)
        {
            if (current.Count > 0 && (size + text.Length > TranslationChunkChars || current.Count >= TranslationChunkItems))
            {
                chunks.Add(current);
                current = [];
                size = 0;
            }
            current.Add(text);
            size += text.Length;
        }
        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        var translated = await Task.WhenAll(chunks.Select((chunk, index) =>
            TranslateChunkAsync(gameTitle, chapterTitle, chunk, sourceLanguage, targetLanguage, run, settings, gate, index, ct)));
        foreach (var part in translated)
        {
            foreach (var (source, target) in part)
            {
                result[source] = target;
            }
        }
        return result;
    }

    private async Task<Dictionary<string, string>> TranslateChunkAsync(
        string gameTitle,
        string chapterTitle,
        List<string> chunk,
        string sourceLanguage,
        string targetLanguage,
        AiRun run,
        SettingsResponseDto settings,
        SemaphoreSlim gate,
        int chunkIndex,
        CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        var missing = Enumerable.Range(0, chunk.Count).ToList();

        // Round 0 asks for everything; a round only repeats what the model skipped or left empty (two repeats at most)
        for (var round = 0; round < 3 && missing.Count > 0; round++)
        {
            var prompt = BuildTranslationPrompt(gameTitle, sourceLanguage, targetLanguage, missing.Select(i => (i, chunk[i])).ToList());
            JsonDocument? answer = null;
            Exception? firstError = null;

            // Quota / key / outage: the run hands the request to the next provider by itself. An unreadable answer is not a reason to
            // switch - it is simply asked once more.
            for (var attempt = 0; attempt < 2 && answer is null; attempt++)
            {
                await gate.WaitAsync(ct);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string? usedProvider = null;
                try
                {
                    answer = await run.ExecuteAsync(run.SpreadFor(chunkIndex + round), async provider =>
                    {
                        usedProvider = provider;
                        var raw = await CallProviderAsync(provider, prompt, settings, ct, research: false);
                        return JsonDocument.Parse(ExtractJsonObject(raw));
                    }, ct);
                    logger.LogInformation("Übersetzung {Chapter}: {Provider}, {Count} Texte in {Seconds:0.0} s", chapterTitle, usedProvider, chunk.Count, watch.Elapsed.TotalSeconds);
                }
                // A cut-off answer (output limit) is unreadable as well: asked once more, never a reason to switch provider
                catch (Exception ex) when (ex is LocalizedException or JsonException or AiProviderException { IsTruncated: true })
                {
                    logger.LogWarning("Übersetzung ({Provider}) lieferte keine lesbare Antwort: {Message}", usedProvider, ex.Message);
                    firstError ??= ex;
                }
                finally
                {
                    gate.Release();
                }
            }

            if (answer is null)
            {
                throw firstError ?? new LocalizedException(ServerTexts.Ai.AnswerNotJson);
            }

            using (answer)
            {
                if (answer.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) && missing.Contains(id) &&
                            item.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String &&
                            textEl.GetString() is { } text && !string.IsNullOrWhiteSpace(text))
                        {
                            result[id] = text.Trim();
                        }
                    }
                }
            }
            missing = missing.Where(i => !result.ContainsKey(i)).ToList();
        }

        if (missing.Count > 0)
        {
            throw new LocalizedException(ServerTexts.Translation.Incomplete(chapterTitle, missing.Count));
        }
        return chunk.Select((text, i) => (text, result[i])).ToDictionary(p => p.text, p => p.Item2, StringComparer.Ordinal);
    }

    private static string LanguageName(string language) => language == Lang.English ? "English" : "German";

    private static string BuildTranslationPrompt(string gameTitle, string sourceLanguage, string targetLanguage, List<(int Id, string Text)> items)
    {
        var source = LanguageName(sourceLanguage);
        var target = LanguageName(targetLanguage);
        var json = JsonSerializer.Serialize(items.Select(i => new { id = i.Id, text = i.Text }));
        return $$"""
            You translate the texts of a video game completion guide for the game "{{gameTitle}}" from {{source}} into {{target}}.
            You receive a JSON list of items {"id": number, "text": "..."}. Translate the "text" of every item.

            Rules:
            - Keep meaning, tone and level of detail. Write naturally, like an experienced player of that language, with the established gaming terms of {{target}}.
            - Proper names of the game (characters, places, items, weapons, skills, enemies, bosses, achievements): use their OFFICIAL {{target}} names if you know them; otherwise leave the name unchanged. Never invent names.
              A bilingual pattern like "Name (Original)" becomes just the official {{target}} name; if both are the same, write it once.
            - Keep unchanged: numbers, units, percentages, URLs, emoji, icon ids, HTML tags, Markdown, line breaks, leading chapter numbers such as "3. " and all text in <angle brackets> or {braces}.
            - Do not add, remove, merge, split, reorder, shorten or explain anything. Every id must appear exactly once in your answer. Identical source texts must get identical translations.
            - Texts can be very short (a table cell, a category such as "Missable") - translate them too, consistently.

            Answer ONLY with a valid JSON object, no Markdown, no text before or after it:
            {"items":[{"id":0,"text":"<translation>"}]}

            Items:
            {{json}}
            """;
    }
}
