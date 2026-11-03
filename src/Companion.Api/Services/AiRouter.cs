using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;

namespace Companion.Api.Services;

/// <summary>
/// The one place that decides which AI provider does the work: it reads the stored selection (mode, Standard-KI, fallback switch),
/// the saved keys and what <see cref="AiProviderHealth"/> knows, and hands every AI action an <see cref="AiRun"/>.
/// </summary>
public sealed class AiRouter(SettingsStore settings, SecretStore secrets, AiProviderHealth health)
{
    private AiRun? _lastRun;

    /// <summary>
    /// What the AI action of this request did so far (who answered, every change of provider) - also after a failure, so an error
    /// answer can still say "Gemini quota used up - continuing with Claude" before it reports why Claude failed too.
    /// </summary>
    public AiUsage? LastUsage => _lastRun?.Usage();

    public static string SecretOf(string provider) => provider switch
    {
        AiProvider.Claude => SettingsStore.SecretClaude,
        AiProvider.OpenAi => SettingsStore.SecretOpenAi,
        _ => SettingsStore.SecretGemini
    };

    public bool HasKey(string provider) => !string.IsNullOrWhiteSpace(secrets.GetSecret(SecretOf(provider)));

    /// <summary>The providers that have a saved key, in the canonical order.</summary>
    public IReadOnlyList<string> ProvidersWithKey() => [.. AiProvider.All.Where(HasKey)];

    public string Fingerprint(string provider) => AiKeys.Fingerprint(secrets.GetSecret(SecretOf(provider)));

    /// <summary>
    /// Starts an AI action.
    /// </summary>
    /// <param name="research">True when the action needs live web search (guides, lore); false for plain generation (translation).</param>
    /// <param name="providerOverride">The provider chosen for this one action (the chooser dialog, the one-off switch); legacy "auto"/"both"/blank = no choice.</param>
    /// <param name="background">An action nobody is waiting at (translation job, achievement texts): never asks, always may switch provider.</param>
    public async Task<AiRun> BeginAsync(bool research, string? providerOverride, bool background, CancellationToken ct)
    {
        var selection = await settings.GetAiSelectionAsync(ct);
        var run = Plan(selection, ProvidersWithKey(), research, providerOverride, background, health, Fingerprint);
        _lastRun = run;
        return run;
    }

    /// <summary>
    /// The decision itself, free of storage so it can be tested: which provider(s) a new action uses and whether it may switch.
    /// Default mode: the one-off choice, else the Standard-KI, else the first provider with a key. Mixed mode: chapters spread over
    /// every provider with a key (research: only those with web search, unless nothing else is there; providers that are paused right now
    /// are left out while the fallback is on). Ask mode: the choice the player made is used and nothing switches by itself - except
    /// for a background action, which nobody can be asked for.
    /// </summary>
    public static AiRun Plan(
        AiSelection selection,
        IReadOnlyList<string> keyed,
        bool research,
        string? providerOverride,
        bool background,
        AiProviderHealth health,
        Func<string, string> fingerprint)
    {
        if (keyed.Count == 0)
        {
            throw new LocalizedException(ServerTexts.Ai.NoProviderKey);
        }

        var chosen = AiProvider.Normalize(providerOverride);
        if (chosen is not null && !keyed.Contains(chosen))
        {
            throw new LocalizedException(ServerTexts.Ai.NoKey(chosen));
        }

        // "Ask before every request" never switches by itself: the player picks again. A background job nobody can ask still may.
        var autoFallback = selection.Mode == AiSelectionMode.Ask ? background : selection.AutoFallback;

        if (chosen is null && selection.Mode == AiSelectionMode.Mixed && !background)
        {
            // Research prefers the providers that can search the web; OpenAI joins only when it is the only key (this integration has no web search for it)
            var searchCapable = keyed.Where(AiProvider.HasWebSearch).ToList();
            var spread = research && searchCapable.Count > 0 ? searchCapable : keyed.ToList();
            if (autoFallback)
            {
                var healthy = spread.Where(p => health.Current(p, fingerprint(p)) is null).ToList();
                if (healthy.Count > 0)
                {
                    spread = healthy;
                }
            }
            return new AiRun(AiSelectionMode.Mixed, spread[0], spread, autoFallback, keyed, research, health, fingerprint);
        }

        var primary = chosen ?? DefaultProviderOf(selection, keyed, research);
        return new AiRun(AiSelectionMode.Default, primary, [primary], autoFallback, keyed, research, health, fingerprint);
    }

    /// <summary>The Standard-KI when it still has a key, else the first provider that has one (OpenAI last when the request researches).</summary>
    internal static string DefaultProviderOf(AiSelection selection, IReadOnlyList<string> keyed, bool research)
    {
        if (selection.DefaultProvider is { } preferred && keyed.Contains(preferred))
        {
            return preferred;
        }
        return research ? keyed.OrderBy(p => AiProvider.HasWebSearch(p) ? 0 : 1).First() : keyed[0];
    }

    /// <summary>What the settings and the AI buttons need: the selection, every provider with its key, model and current state.</summary>
    public async Task<AiStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var selection = await settings.GetAiSelectionAsync(ct);
        var current = await settings.GetAsync(ct);
        var keyed = ProvidersWithKey();

        var providers = AiProvider.All.Select(id =>
        {
            var hasKey = keyed.Contains(id);
            var state = hasKey ? health.Current(id, Fingerprint(id)) : null;
            var model = id switch
            {
                AiProvider.Claude => current.ClaudeModel,
                AiProvider.OpenAi => current.OpenAiModel,
                _ => current.GeminiModel
            };
            return new AiProviderStatusDto(
                id,
                hasKey,
                model,
                AiProvider.HasWebSearch(id),
                state is null ? "ok" : state.Kind.ToId(),
                state?.Since,
                state?.Until,
                state?.Text.ToString());
        }).ToList();

        // The provider a plain action would use right now: the Standard-KI, unless it is paused and the fallback is on
        string? active = null;
        if (keyed.Count > 0 && selection.Mode != AiSelectionMode.Mixed)
        {
            active = DefaultProviderOf(selection, keyed, research: true);
            var autoFallback = selection.Mode != AiSelectionMode.Ask && selection.AutoFallback;
            if (autoFallback && health.Current(active, Fingerprint(active)) is not null)
            {
                active = keyed.FirstOrDefault(p => p != active && health.Current(p, Fingerprint(p)) is null) ?? active;
            }
        }

        var defaultProvider = keyed.Count > 0 ? DefaultProviderOf(selection, keyed, research: true) : null;
        return new AiStatusDto(selection.Mode, defaultProvider, selection.AutoFallback, active, providers);
    }

    /// <summary>"Reset" for one provider; false when the id is not a provider.</summary>
    public bool ResetProvider(string id)
    {
        var provider = AiProvider.Normalize(id);
        if (provider is null)
        {
            return false;
        }
        health.Reset(provider);
        return true;
    }
}
