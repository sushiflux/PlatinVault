using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>One change of provider during an action: <see cref="From"/> could not be used (<see cref="Reason"/>), <see cref="To"/> answered instead.</summary>
public sealed record AiSwitch(string From, string To, AiFailureKind Reason);

/// <summary>Which providers answered during an action and every change of provider on the way (shown to the player, never silent).</summary>
public sealed record AiUsage(IReadOnlyList<string> Providers, IReadOnlyList<AiSwitch> Switches, bool Research = false)
{
    public static AiUsage None { get; } = new([], []);
}

/// <summary>
/// One AI action (a new guide, "complete guide", a translation job ...) and its choice of providers, decided once when the action
/// starts (<see cref="AiRouter.BeginAsync"/>). Every request of the action goes through <see cref="ExecuteAsync{T}"/>, which
/// applies the automatic fallback: when the chosen provider is out of quota, has a rejected key or is unavailable, the request
/// continues with the next provider that has a key - and the change is recorded in <see cref="Usage"/>. Other failures (a bad or
/// unreadable answer) are not a reason to switch and pass through unchanged. A provider that failed once during the run is not asked
/// again by the run (a guide of twenty chapters does not hit an exhausted provider twenty times); across actions
/// <see cref="AiProviderHealth"/> remembers it for a while.
/// </summary>
public sealed class AiRun
{
    private readonly IReadOnlyList<string> _keyed;
    private readonly bool _research;
    private readonly AiProviderHealth _health;
    private readonly Func<string, string> _fingerprint;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, AiProviderException> _failures = [];
    private readonly List<string> _used = [];
    private readonly List<AiSwitch> _switches = [];

    public AiRun(
        string mode,
        string primary,
        IReadOnlyList<string> spread,
        bool autoFallback,
        IReadOnlyList<string> keyedProviders,
        bool research,
        AiProviderHealth health,
        Func<string, string> fingerprint)
    {
        Mode = mode;
        Primary = primary;
        Spread = spread;
        AutoFallback = autoFallback;
        _keyed = keyedProviders;
        _research = research;
        _health = health;
        _fingerprint = fingerprint;
    }

    /// <summary><see cref="AiSelectionMode.Default"/> (one provider writes it all) or <see cref="AiSelectionMode.Mixed"/> (spread over several).</summary>
    public string Mode { get; }

    /// <summary>The provider single requests (blueprint, one chapter) go to.</summary>
    public string Primary { get; }

    /// <summary>The providers chapters are handed out to, round-robin; just <see cref="Primary"/> unless the mode is mixed.</summary>
    public IReadOnlyList<string> Spread { get; }

    public bool AutoFallback { get; }

    /// <summary>The provider for the <paramref name="index"/>-th unit of work (chapter, chunk, lore part).</summary>
    public string SpreadFor(int index) => Spread[(int)((uint)index % (uint)Spread.Count)];

    public AiUsage Usage()
    {
        lock (_lock)
        {
            return new AiUsage([.. _used], [.. _switches], _research);
        }
    }

    /// <summary>
    /// Runs <paramref name="call"/> with <paramref name="preferred"/> and, when that provider cannot be used and the fallback is on,
    /// with the next ones. Throws the failure of the last provider tried when none worked (or the first failure when the fallback is off).
    /// </summary>
    public async Task<T> ExecuteAsync<T>(string preferred, Func<string, Task<T>> call, CancellationToken ct = default)
    {
        var candidates = Candidates(preferred);
        AiProviderException? lastFailure = null;
        string? switchedFrom = null;
        var switchedKind = AiFailureKind.Other;

        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var provider = candidates[i];
            var hasNext = i < candidates.Count - 1;

            // Failed earlier in this very run: not asked again
            AiProviderException? known;
            lock (_lock)
            {
                _failures.TryGetValue(provider, out known);
            }
            if (known is not null)
            {
                if (!AutoFallback)
                {
                    throw known;
                }
                lastFailure = known;
                switchedFrom ??= provider;
                switchedKind = known.Kind;
                continue;
            }

            // Paused after an earlier action: skipped when there is somewhere else to go (the last one is always tried)
            if (AutoFallback && hasNext && _health.Current(provider, _fingerprint(provider)) is { } paused)
            {
                switchedFrom ??= provider;
                switchedKind = paused.Kind;
                continue;
            }

            // The change is recorded when the replacement is asked, not only when it answers: if it fails as well (its own
            // error, e.g. a rejected request), the player still has to be told that the first AI was skipped
            if (switchedFrom is not null && switchedFrom != provider)
            {
                NoteSwitch(switchedFrom, provider, switchedKind);
            }

            try
            {
                var result = await call(provider).ConfigureAwait(false);
                _health.MarkOk(provider);
                NoteUsed(provider);
                return result;
            }
            catch (AiProviderException ex) when (ex.Kind.SwitchesProvider())
            {
                _health.Report(provider, ex, _fingerprint(provider));
                lock (_lock)
                {
                    _failures[provider] = ex;
                }
                lastFailure = ex;
                if (!AutoFallback)
                {
                    throw;
                }
                switchedFrom ??= provider;
                switchedKind = ex.Kind;
            }
        }

        throw lastFailure ?? (Exception)new LocalizedException(ServerTexts.Ai.NoProviderKey);
    }

    /// <summary>The preferred provider first, then the others in a fixed order (providers with web search before OpenAI when the request researches).</summary>
    private List<string> Candidates(string preferred)
    {
        var rest = _keyed.Where(p => p != preferred);
        if (_research)
        {
            rest = rest.OrderBy(p => AiProvider.HasWebSearch(p) ? 0 : 1);
        }
        var list = new List<string> { preferred };
        list.AddRange(rest);
        return list;
    }

    private void NoteUsed(string provider)
    {
        lock (_lock)
        {
            if (!_used.Contains(provider))
            {
                _used.Add(provider);
            }
        }
    }

    private void NoteSwitch(string from, string to, AiFailureKind kind)
    {
        lock (_lock)
        {
            if (!_switches.Any(s => s.From == from && s.To == to))
            {
                _switches.Add(new AiSwitch(from, to, kind));
            }
        }
    }
}
