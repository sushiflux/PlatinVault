using System.Collections.Concurrent;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// What is known about a provider that failed lately. <see cref="Until"/> is when it is tried again by itself (null = only after the
/// key was replaced or the state was reset). <see cref="Text"/> is the explanation of the failure in both languages.
/// </summary>
public sealed record AiProviderState(string Provider, AiFailureKind Kind, DateTimeOffset Since, DateTimeOffset? Until, string KeyFingerprint, Msg Text);

/// <summary>
/// Remembers providers that are out of quota, have a rejected key or are unavailable, so a guide with twenty chapters does not ask
/// the exhausted provider twenty times (and a new action starts with the provider that works). In memory on purpose: after a restart
/// the first request simply finds out again. Quota: the provider's Retry-After, else one hour. Rejected key: until the key is
/// replaced. Unavailable: two minutes.
/// </summary>
public sealed class AiProviderHealth(TimeProvider? clock = null)
{
    public static readonly TimeSpan DefaultQuotaCooldown = TimeSpan.FromHours(1);
    public static readonly TimeSpan UnavailableCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromHours(24);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, AiProviderState> _states = new();

    /// <summary>The state while it still applies; null when the provider is fine, the pause is over or the key was replaced.</summary>
    public AiProviderState? Current(string provider, string keyFingerprint)
    {
        if (!_states.TryGetValue(provider, out var state))
        {
            return null;
        }

        var expired = state.KeyFingerprint != keyFingerprint ||
                      (state.Until is { } until && until <= _clock.GetUtcNow());
        if (expired)
        {
            _states.TryRemove(new KeyValuePair<string, AiProviderState>(provider, state));
            return null;
        }
        return state;
    }

    /// <summary>Notes a failure that is a reason to switch provider (<see cref="AiFailureKind.Other"/> is ignored).</summary>
    public void Report(string provider, AiProviderException failure, string keyFingerprint)
    {
        if (!failure.Kind.SwitchesProvider())
        {
            return;
        }

        var now = _clock.GetUtcNow();
        TimeSpan? pause = failure.Kind switch
        {
            AiFailureKind.InvalidKey => null,
            AiFailureKind.Unavailable => Clamp(failure.RetryAfter ?? UnavailableCooldown),
            _ => Clamp(failure.RetryAfter ?? DefaultQuotaCooldown)
        };

        // The same problem again keeps its original "since", so "quota used up since 10:12" stays true while it lasts
        var since = _states.TryGetValue(provider, out var previous) && previous.Kind == failure.Kind && previous.KeyFingerprint == keyFingerprint
            ? previous.Since
            : now;
        _states[provider] = new AiProviderState(provider, failure.Kind, since, pause is null ? null : now + pause.Value, keyFingerprint, failure.Text);
    }

    /// <summary>A request worked: whatever was noted before no longer holds.</summary>
    public void MarkOk(string provider) => _states.TryRemove(provider, out _);

    /// <summary>"Reset" in the settings: forget the state, the next request tries the provider again. True when there was one.</summary>
    public bool Reset(string provider) => _states.TryRemove(provider, out _);

    private static TimeSpan Clamp(TimeSpan value) => value < MinCooldown ? MinCooldown : value > MaxCooldown ? MaxCooldown : value;
}
