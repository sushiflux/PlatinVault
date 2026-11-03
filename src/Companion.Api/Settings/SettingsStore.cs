using System.Globalization;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Companion.Api.Settings;

/// <summary>Manages database-backed operator settings, achievement notes, and encrypted API keys.</summary>
public sealed class SettingsStore(CompanionDbContext db, SecretStore secrets)
{
    /// <summary>The former single AI choice ("auto" / "gemini" / "claude" / "openai"). Read once to migrate it into the AI selection, then removed.</summary>
    public const string KeyLegacyProvider = "ai.provider";

    /// <summary>How the AI is chosen: "default" (one Standard-KI), "mixed" (chapters spread over all providers) or "ask" (every action asks).</summary>
    public const string KeyAiSelectionMode = "ai.selection_mode";

    /// <summary>The Standard-KI ("gemini" / "claude" / "openai"). Absent = the first provider that has a key.</summary>
    public const string KeyAiDefaultProvider = "ai.default_provider";

    /// <summary>Continue with the next provider when the chosen one is out of quota, has a rejected key or is unavailable. On by default.</summary>
    public const string KeyAiAutoFallback = "ai.auto_fallback";
    public const string KeyGeminiModel = "ai.gemini_model";
    public const string KeyClaudeModel = "ai.claude_model";
    public const string KeyOpenAiModel = "ai.openai_model";
    public const string KeySteamUserId = "steam.user_id";

    public const string KeyAutoDetectGame = "live.auto_detect_game";
    public const string KeyPlaySounds = "live.play_sounds";
    public const string KeySoundVolume = "live.sound_volume";
    public const string KeySoundTheme = "live.sound_theme";
    public const string KeyPopupPosition = "live.popup_position";

    /// <summary>
    /// Where the trophy popup appears (user decision 2026-11-03): "app" = only in the app / the browser, "overlay" = only in the game
    /// (the Windows program's overlay), "both" (default). Without the Windows program there is no overlay and the effect is always "app".
    /// </summary>
    public const string KeyPopupTarget = "live.popup_target";
    /// <summary>Steam language name for achievement texts ("german" / "english"). Not edited on its own: it follows <see cref="KeyUiLanguage"/>.</summary>
    public const string KeyLanguage = "live.language";

    /// <summary>The language of the whole app ("de" / "en"). Absent until the user has chosen one on first start (the UI then shows its language picker).</summary>
    public const string KeyUiLanguage = "ui.language";
    public const string KeyAutoTranslate = "live.auto_translate";

    /// <summary>Optional feature: the spoiler-heavy "Story & Lore" tab per game. Off unless switched on in the settings.</summary>

    public const string SecretGemini = "gemini_api_key";
    public const string SecretClaude = "claude_api_key";
    public const string SecretOpenAi = "openai_api_key";
    public const string SecretSteam = "steam_api_key";

    /// <summary>Client-ID and Client-Secret of the Twitch developer app; the same app serves Twitch live streams and IGDB.</summary>
    public const string SecretTwitchClientId = "twitch_client_id";
    public const string SecretTwitchClientSecret = "twitch_client_secret";

    /// <summary>Live Twitch streams per game. On by default; has an effect only while the Twitch credentials are stored.</summary>
    public const string KeyTwitchStreams = "features.twitch_streams";

    /// <summary>
    /// Personal notes on achievements (stored as "note.{appId}.{apiName}"). On by default. Switching it off only hides the notes in the UI:
    /// the stored notes are never deleted, so they come back when it is switched on again.
    /// </summary>
    public const string KeyAchievementNotes = "features.achievement_notes";

    /// <summary>
    /// Every open window switches to the game page (Live trophies) when a game of the vault is started. On by default; the switch is
    /// evaluated by the browser at the moment a start is detected, so a change takes effect without a restart.
    /// </summary>
    public const string KeyAutoSwitchGame = "features.auto_switch_game";

    /// <summary>
    /// "Beta-Versionen erhalten" (user decision 2026-11-02, every platform): off (default) = the update check offers only releases, on =
    /// betas and releases, always the newest. Switching it off never downgrades: a running beta stays until a newer release exists.
    /// </summary>
    public const string KeyBetaUpdates = "updates.beta_channel";

    /// <summary>"Nach Updates suchen" (<see cref="UpdateCheckInterval"/> id, default daily).</summary>
    public const string KeyUpdateCheckInterval = "updates.check_interval";

    /// <summary>The research building blocks the player ticked, as JSON (see <see cref="ResearchBlocks"/>); absent = nothing ticked = default behaviour.</summary>
    public const string KeyResearchBlocks = "ai.research_blocks";

    /// <summary>The former free-text research template. Gone since the building blocks; a leftover row is removed when the blocks are saved.</summary>
    private const string KeyLegacyResearchTemplate = "ai.research_template";

    /// <summary>Client-IDs and secrets are short alphanumeric strings; anything else is a copy error (spaces, line breaks, quotes).</summary>
    public static bool IsPlausibleTwitchCredential(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9_\-]{8,128}$");

    /// <summary>Trophy sound themes shipped by the frontend (public/sounds/&lt;theme&gt;/). Earlier versions stored "ps5" or "steam".</summary>
    public static readonly string[] SoundThemes = ["kristall", "orchester", "spieluhr"];

    public const string DefaultSoundTheme = "kristall";

    /// <summary>
    /// Where the live achievement popup appears: the nine screen spots plus "fullscreen" (the whole window is taken over for a few seconds).
    /// The frontend has the same list in <c>NotificationPosition</c> (api/types.ts).
    /// </summary>
    public static readonly string[] PopupPositions =
    [
        "top-left", "top-center", "top-right",
        "center-left", "center", "center-right",
        "bottom-left", "bottom-center", "bottom-right",
        "fullscreen",
    ];

    public const string DefaultPopupPosition = "top-right";

    /// <summary>The three choices of "Trophäen-Popup anzeigen"; the frontend has the same list in <c>POPUP_TARGETS</c> (utils/popupTarget.ts).</summary>
    public static readonly string[] PopupTargets = ["app", "overlay", "both"];

    public const string DefaultPopupTarget = "both";

    /// <summary>The known popup target for a submitted or stored value (trimmed, case-insensitive), or null when it is not one of <see cref="PopupTargets"/>.</summary>
    public static string? NormalizePopupTarget(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is not null && PopupTargets.Contains(v) ? v : null;
    }

    /// <summary>The stored popup target ("both" when nothing or an unknown value is stored); the Windows program reads it for every unlock.</summary>
    public static async Task<string> ReadPopupTargetAsync(CompanionDbContext db, CancellationToken ct = default)
    {
        var stored = await db.AppSettings.AsNoTracking().Where(x => x.Key == KeyPopupTarget).Select(x => x.Value).FirstOrDefaultAsync(ct);
        return NormalizePopupTarget(stored) ?? DefaultPopupTarget;
    }

    /// <summary>Whether a popup target value is stored at all (the Windows program migrates its former overlay switch only when not).</summary>
    public static Task<bool> HasPopupTargetAsync(CompanionDbContext db, CancellationToken ct = default) =>
        db.AppSettings.AsNoTracking().AnyAsync(x => x.Key == KeyPopupTarget, ct);

    /// <summary>Stores the popup target (the tray switch "Overlay" of the Windows program uses this; the app saves it with the other settings).</summary>
    public async Task SetPopupTargetAsync(string target, CancellationToken ct = default)
    {
        var normalized = NormalizePopupTarget(target) ?? throw new SettingsValidationException(ServerTexts.Settings.PopupTargetInvalid(target));
        await UpsertSettingAsync(KeyPopupTarget, normalized, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The known popup position for a submitted or stored value (trimmed, case-insensitive), or null when it is not one of <see cref="PopupPositions"/>.</summary>
    public static string? NormalizePopupPosition(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is not null && PopupPositions.Contains(v) ? v : null;
    }

    /// <summary>Languages the app speaks. One choice drives UI texts, formats, Steam achievement texts and (later) guides and server messages.</summary>
    /// <remarks>The list is languages.json in the repository root, shared with the frontend (see <see cref="LanguageRegistry"/>).</remarks>
    public static IReadOnlyList<string> UiLanguages => LanguageRegistry.Codes;

    /// <summary>The Steam language name for achievement texts that belongs to an app language ("de" = german, "en" = english, from languages.json).</summary>
    public static string SteamLanguageFor(string uiLanguage) => LanguageRegistry.SteamLanguageFor(uiLanguage);

    /// <summary>The stored app language, or null when it is missing or not a known language (a hand-edited bad value counts as "not chosen yet").</summary>
    public static string? NormalizeUiLanguage(string? value) => LanguageRegistry.Find(value)?.Code;

    /// <summary>Maps any stored or submitted value to a known sound theme; unknown and legacy values select the default.</summary>
    public static string NormalizeSoundTheme(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is not null && SoundThemes.Contains(v) ? v : DefaultSoundTheme;
    }

    public async Task<SettingsResponseDto> GetAsync(CancellationToken ct = default)
    {
        var all = await db.AppSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var geminiModel = all.GetValueOrDefault(KeyGeminiModel, AiGuideGeneratorService.DefaultGeminiModel);
        var claudeModel = all.GetValueOrDefault(KeyClaudeModel, "claude-sonnet-4-5");
        var openAiModel = all.GetValueOrDefault(KeyOpenAiModel, "gpt-4o");

        var steamUserId = all.GetValueOrDefault(KeySteamUserId, string.Empty);
        if (string.IsNullOrWhiteSpace(steamUserId))
        {
            steamUserId = TryDetectLocalSteamId64() ?? string.Empty;
        }

        var autoDetect = !all.TryGetValue(KeyAutoDetectGame, out var ad) || !bool.TryParse(ad, out var adb) || adb;
        var playSounds = !all.TryGetValue(KeyPlaySounds, out var ps) || !bool.TryParse(ps, out var psb) || psb;
        var soundVolume = all.TryGetValue(KeySoundVolume, out var sv) && double.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out var svd)
            ? Math.Clamp(svd, 0.0, 1.0)
            : 0.15;
        var soundTheme = NormalizeSoundTheme(all.GetValueOrDefault(KeySoundTheme));
        // A hand-edited unknown value falls back to the default instead of reaching the UI
        var popupPosition = NormalizePopupPosition(all.GetValueOrDefault(KeyPopupPosition)) ?? DefaultPopupPosition;
        var popupTarget = NormalizePopupTarget(all.GetValueOrDefault(KeyPopupTarget)) ?? DefaultPopupTarget;
        var uiLanguage = NormalizeUiLanguage(all.GetValueOrDefault(KeyUiLanguage));
        var language = all.GetValueOrDefault(KeyLanguage, "german");
        var autoTranslate = !all.TryGetValue(KeyAutoTranslate, out var at) || !bool.TryParse(at, out var atb) || atb;

        var geminiKey = secrets.GetSecret(SecretGemini);
        var claudeKey = secrets.GetSecret(SecretClaude);
        var openAiKey = secrets.GetSecret(SecretOpenAi);
        var steamKey = secrets.GetSecret(SecretSteam);
        var twitchClientId = secrets.GetSecret(SecretTwitchClientId);
        var twitchClientSecret = secrets.GetSecret(SecretTwitchClientSecret);
        var twitchStreams = !all.TryGetValue(KeyTwitchStreams, out var ts) || !bool.TryParse(ts, out var tsb) || tsb;
        var achievementNotes = !all.TryGetValue(KeyAchievementNotes, out var an) || !bool.TryParse(an, out var anb) || anb;
        var autoSwitchGame = !all.TryGetValue(KeyAutoSwitchGame, out var asg) || !bool.TryParse(asg, out var asgb) || asgb;
        var betaUpdates = ParseBetaUpdates(all.GetValueOrDefault(KeyBetaUpdates));
        var selection = await ResolveAiSelectionAsync(all, ct);
        var keyedProviders = AiProvider.All.Where(HasProviderKey).ToList();
        var effectiveDefault = selection.DefaultProvider is { } stored && keyedProviders.Contains(stored)
            ? stored
            : keyedProviders.FirstOrDefault();
        var researchBlocks = ResearchBlocks.Parse(all.GetValueOrDefault(KeyResearchBlocks));

        return new SettingsResponseDto(
            GeminiModel: geminiModel,
            ClaudeModel: claudeModel,
            OpenAiModel: openAiModel,
            SteamUserId: steamUserId,
            AutoDetectGame: autoDetect,
            PlaySounds: playSounds,
            SoundVolume: soundVolume,
            SoundTheme: soundTheme,
            PopupPosition: popupPosition,
            Language: language,
            UiLanguage: uiLanguage,
            AutoTranslate: autoTranslate,
            HasGeminiKey: !string.IsNullOrEmpty(geminiKey),
            GeminiKeyHint: secrets.GetHint(SecretGemini),
            HasClaudeKey: !string.IsNullOrEmpty(claudeKey),
            ClaudeKeyHint: secrets.GetHint(SecretClaude),
            HasOpenAiKey: !string.IsNullOrEmpty(openAiKey),
            OpenAiKeyHint: secrets.GetHint(SecretOpenAi),
            HasSteamKey: !string.IsNullOrEmpty(steamKey),
            SteamKeyHint: secrets.GetHint(SecretSteam),
            HasTwitchKey: !string.IsNullOrEmpty(twitchClientId) && !string.IsNullOrEmpty(twitchClientSecret),
            TwitchKeyHint: SecretStore.MaskHint(twitchClientId),
            TwitchStreamsEnabled: twitchStreams,
            AchievementNotesEnabled: achievementNotes,
            AiSelectionMode: selection.Mode,
            AiDefaultProvider: effectiveDefault,
            AiAutoFallback: selection.AutoFallback,
            ResearchBlocks: ResearchBlocks.ToDto(researchBlocks),
            ResearchBlocksPolicy: ResearchBlocks.Policy(),
            AutoSwitchGameEnabled: autoSwitchGame,
            BetaUpdatesEnabled: betaUpdates,
            PopupTarget: popupTarget,
            DashboardSortFavorites: DashboardSort.Resolve(all.GetValueOrDefault(DashboardSort.KeyFavorites), favorites: true),
            DashboardSortAll: DashboardSort.Resolve(all.GetValueOrDefault(DashboardSort.KeyAll), favorites: false)
        );
    }

    private bool HasProviderKey(string provider) => !string.IsNullOrWhiteSpace(secrets.GetSecret(AiRouter.SecretOf(provider)));

    private bool _aiSelectionMigrationTried;

    /// <summary>
    /// The stored AI selection. A database that still holds only the former single <c>ai.provider</c> value is migrated here, once:
    /// "auto" becomes Standard-KI = the first provider that has a key, a fixed provider becomes Standard-KI = that provider; the
    /// new rows are written and the old one removed, so adding a key later does not silently change the Standard-KI.
    /// </summary>
    private async Task<AiSelection> ResolveAiSelectionAsync(Dictionary<string, string> all, CancellationToken ct)
    {
        all.TryGetValue(KeyAiSelectionMode, out var mode);
        all.TryGetValue(KeyAiDefaultProvider, out var defaultProvider);
        all.TryGetValue(KeyLegacyProvider, out var legacy);
        bool? autoFallback = all.TryGetValue(KeyAiAutoFallback, out var af) && bool.TryParse(af, out var afb) ? afb : null;

        var firstKeyed = AiProvider.All.FirstOrDefault(HasProviderKey);
        var selection = AiSelection.Resolve(mode, defaultProvider, autoFallback, legacy, firstKeyed);

        var needsMigration = AiSelectionMode.Normalize(mode) is null && !string.IsNullOrWhiteSpace(legacy);
        if (needsMigration && !_aiSelectionMigrationTried)
        {
            _aiSelectionMigrationTried = true;
            try
            {
                await UpsertSettingAsync(KeyAiSelectionMode, selection.Mode, ct);
                if (selection.DefaultProvider is not null)
                {
                    await UpsertSettingAsync(KeyAiDefaultProvider, selection.DefaultProvider, ct);
                }
                await RemoveSettingAsync(KeyLegacyProvider, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A parallel request migrated at the same moment: the rows are there, the selection read above is the same
                db.ChangeTracker.Clear();
            }
        }
        return selection;
    }

    /// <summary>The stored AI selection (mode, Standard-KI, fallback switch) - what the router decides with.</summary>
    public async Task<AiSelection> GetAiSelectionAsync(CancellationToken ct = default)
    {
        var all = await db.AppSettings.AsNoTracking().Where(x => x.Key.StartsWith("ai.")).ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        return await ResolveAiSelectionAsync(all, ct);
    }

    /// <summary>The research building blocks the AI prompts use (empty = nothing ticked = default behaviour).</summary>
    public async Task<ResearchBlocks.Selection> GetResearchBlocksAsync(CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == KeyResearchBlocks, ct);
        return ResearchBlocks.Parse(row?.Value);
    }

    /// <summary>The switch "Beta-Versionen erhalten" (off unless stored as true).</summary>
    public async Task<bool> GetBetaUpdatesEnabledAsync(CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == KeyBetaUpdates, ct);
        return ParseBetaUpdates(row?.Value);
    }

    /// <summary>How often the server looks for updates by itself (daily when nothing or an unknown value is stored).</summary>
    public async Task<UpdateCheckInterval> GetUpdateCheckIntervalAsync(CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == KeyUpdateCheckInterval, ct);
        return UpdateCheckIntervals.TryParse(row?.Value) ?? UpdateCheckIntervals.Default;
    }

    // Default OFF: a missing or unreadable value means "releases only"
    private static bool ParseBetaUpdates(string? value) => bool.TryParse(value, out var on) && on;

    /// <summary>True when the Twitch credentials are stored and the live-stream feature is not switched off.</summary>
    public async Task<bool> IsTwitchStreamsActiveAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(secrets.GetSecret(SecretTwitchClientId)) || string.IsNullOrEmpty(secrets.GetSecret(SecretTwitchClientSecret)))
        {
            return false;
        }

        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == KeyTwitchStreams, ct);
        return row is null || !bool.TryParse(row.Value, out var enabled) || enabled;
    }

    public async Task<SettingsResponseDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        // Validate before anything is written, so a typo in the Twitch fields does not leave the other settings half saved
        foreach (var (label, value) in new[] { ("Client-ID", request.TwitchClientId), ("Client-Secret", request.TwitchClientSecret) })
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsPlausibleTwitchCredential(value.Trim()))
            {
                throw new SettingsValidationException(ServerTexts.Settings.TwitchCredentialInvalid(label));
            }
        }

        // The app language must be one of the supported ones; checked before anything is written. The message names both languages,
        // because the user may not understand the one the rest of the messages are in.
        string? uiLanguage = null;
        if (request.UiLanguage is not null)
        {
            uiLanguage = NormalizeUiLanguage(request.UiLanguage);
            if (uiLanguage is null)
            {
                throw new SettingsValidationException(ServerTexts.Settings.LanguageUnsupported(request.UiLanguage));
            }
        }

        // The popup position must be one of the known spots (blank = keep what is stored)
        string? popupPosition = null;
        if (!string.IsNullOrWhiteSpace(request.PopupPosition))
        {
            popupPosition = NormalizePopupPosition(request.PopupPosition)
                            ?? throw new SettingsValidationException(ServerTexts.Settings.PopupPositionInvalid(request.PopupPosition));
        }

        // The popup target must be one of the three choices (null = keep what is stored)
        string? popupTarget = null;
        if (request.PopupTarget is not null)
        {
            popupTarget = NormalizePopupTarget(request.PopupTarget)
                          ?? throw new SettingsValidationException(ServerTexts.Settings.PopupTargetInvalid(request.PopupTarget));
        }

        // The update check interval must be one of the known ids (null = keep what is stored)
        string? updateInterval = null;
        if (request.UpdateCheckInterval is not null)
        {
            updateInterval = UpdateCheckIntervals.TryParse(request.UpdateCheckInterval) is { } parsed
                ? UpdateCheckIntervals.ToId(parsed)
                : throw new SettingsValidationException(ServerTexts.Settings.UpdateIntervalInvalid(request.UpdateCheckInterval));
        }

        // The dashboard sort orders are validated before anything is written, too (null = keep what is stored)
        string? sortFavorites = null, sortAll = null;
        if (request.DashboardSortFavorites is not null)
        {
            sortFavorites = DashboardSort.Normalize(request.DashboardSortFavorites, favorites: true)
                            ?? throw new SettingsValidationException(ServerTexts.Settings.DashboardSortInvalid(request.DashboardSortFavorites, favorites: true));
        }
        if (request.DashboardSortAll is not null)
        {
            sortAll = DashboardSort.Normalize(request.DashboardSortAll, favorites: false)
                      ?? throw new SettingsValidationException(ServerTexts.Settings.DashboardSortInvalid(request.DashboardSortAll, favorites: false));
        }

        // The AI selection and the research blocks are validated like the Twitch values: before anything is written
        string? aiMode = null;
        if (request.AiSelectionMode is not null)
        {
            aiMode = AiSelectionMode.Normalize(request.AiSelectionMode)
                     ?? throw new SettingsValidationException(ServerTexts.Settings.AiSelectionModeInvalid(request.AiSelectionMode));
        }
        string? aiDefaultProvider = null;
        if (request.AiDefaultProvider is not null)
        {
            aiDefaultProvider = AiProvider.Normalize(request.AiDefaultProvider)
                                ?? throw new SettingsValidationException(ServerTexts.Settings.AiProviderInvalid(request.AiDefaultProvider));
        }
        if (request.ResearchBlocks is not null && ResearchBlocks.Validate(request.ResearchBlocks) is { } blocksError)
        {
            throw new SettingsValidationException(blocksError);
        }

        // null = keep what is stored; a blank value means "the default"
        if (aiMode is not null)
        {
            await UpsertSettingAsync(KeyAiSelectionMode, aiMode, ct);
            // The former single choice is superseded by the explicit selection
            await RemoveSettingAsync(KeyLegacyProvider, ct);
        }
        if (aiDefaultProvider is not null)
            await UpsertSettingAsync(KeyAiDefaultProvider, aiDefaultProvider, ct);
        if (request.AiAutoFallback.HasValue)
            await UpsertSettingAsync(KeyAiAutoFallback, request.AiAutoFallback.Value.ToString(), ct);
        if (request.GeminiModel is not null)
            await UpsertSettingAsync(KeyGeminiModel, string.IsNullOrWhiteSpace(request.GeminiModel) ? AiGuideGeneratorService.DefaultGeminiModel : request.GeminiModel.Trim(), ct);
        if (request.ClaudeModel is not null)
            await UpsertSettingAsync(KeyClaudeModel, string.IsNullOrWhiteSpace(request.ClaudeModel) ? "claude-sonnet-4-5" : request.ClaudeModel.Trim(), ct);
        if (request.OpenAiModel is not null)
            await UpsertSettingAsync(KeyOpenAiModel, string.IsNullOrWhiteSpace(request.OpenAiModel) ? "gpt-4o" : request.OpenAiModel.Trim(), ct);

        if (request.SteamUserId is not null)
        {
            await UpsertSettingAsync(KeySteamUserId, request.SteamUserId.Trim(), ct);
        }

        if (request.AutoDetectGame.HasValue)
            await UpsertSettingAsync(KeyAutoDetectGame, request.AutoDetectGame.Value.ToString(), ct);
        if (request.PlaySounds.HasValue)
            await UpsertSettingAsync(KeyPlaySounds, request.PlaySounds.Value.ToString(), ct);
        if (request.SoundVolume.HasValue)
            await UpsertSettingAsync(KeySoundVolume, Math.Clamp(request.SoundVolume.Value, 0.0, 1.0).ToString(CultureInfo.InvariantCulture), ct);
        if (!string.IsNullOrWhiteSpace(request.SoundTheme))
            await UpsertSettingAsync(KeySoundTheme, NormalizeSoundTheme(request.SoundTheme), ct);
        if (popupPosition is not null)
            await UpsertSettingAsync(KeyPopupPosition, popupPosition, ct);
        if (popupTarget is not null)
            await UpsertSettingAsync(KeyPopupTarget, popupTarget, ct);
        var languageChanged = false;
        if (uiLanguage is not null)
        {
            // One language choice: the Steam achievement language follows the app language
            var previousLanguage = NormalizeUiLanguage((await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == KeyUiLanguage, ct))?.Value);
            languageChanged = previousLanguage != uiLanguage;
            await UpsertSettingAsync(KeyUiLanguage, uiLanguage, ct);
            await UpsertSettingAsync(KeyLanguage, SteamLanguageFor(uiLanguage), ct);
        }
        if (request.AutoTranslate.HasValue)
            await UpsertSettingAsync(KeyAutoTranslate, request.AutoTranslate.Value.ToString(), ct);
        if (request.TwitchStreamsEnabled.HasValue)
            await UpsertSettingAsync(KeyTwitchStreams, request.TwitchStreamsEnabled.Value.ToString(), ct);
        if (request.AchievementNotesEnabled.HasValue)
            await UpsertSettingAsync(KeyAchievementNotes, request.AchievementNotesEnabled.Value.ToString(), ct);
        if (request.AutoSwitchGameEnabled.HasValue)
            await UpsertSettingAsync(KeyAutoSwitchGame, request.AutoSwitchGameEnabled.Value.ToString(), ct);
        if (request.BetaUpdatesEnabled.HasValue)
            await UpsertSettingAsync(KeyBetaUpdates, request.BetaUpdatesEnabled.Value.ToString(), ct);
        if (updateInterval is not null)
            await UpsertSettingAsync(KeyUpdateCheckInterval, updateInterval, ct);
        if (sortFavorites is not null)
            await UpsertSettingAsync(DashboardSort.KeyFavorites, sortFavorites, ct);
        if (sortAll is not null)
            await UpsertSettingAsync(DashboardSort.KeyAll, sortAll, ct);

        // An empty selection removes the row: nothing ticked is the default behaviour, which then keeps following improvements of the app
        if (request.ResearchBlocks is not null)
        {
            var json = ResearchBlocks.Serialize(ResearchBlocks.FromDto(request.ResearchBlocks));
            if (json is null)
                await RemoveSettingAsync(KeyResearchBlocks, ct);
            else
                await UpsertSettingAsync(KeyResearchBlocks, json, ct);
            await RemoveSettingAsync(KeyLegacyResearchTemplate, ct);
        }

        await db.SaveChangesAsync(ct);

        if (uiLanguage is not null)
        {
            RequestLanguage.InvalidateStored();
        }
        if (languageChanged)
        {
            // Cards keep texts from Steam in the old language (review summary, release date): mark them stale so the next dashboard
            // load refreshes them in the new one. Only a refresh marker - nothing is deleted.
            await db.Games.ExecuteUpdateAsync(s => s.SetProperty(g => g.FactsUpdatedAt, (DateTimeOffset?)null), ct);
        }

        if (request.ClearGeminiKey) secrets.SetSecret(SecretGemini, null);
        else if (!string.IsNullOrWhiteSpace(request.GeminiApiKey)) secrets.SetSecret(SecretGemini, request.GeminiApiKey);

        if (request.ClearClaudeKey) secrets.SetSecret(SecretClaude, null);
        else if (!string.IsNullOrWhiteSpace(request.ClaudeApiKey)) secrets.SetSecret(SecretClaude, request.ClaudeApiKey);

        if (request.ClearOpenAiKey) secrets.SetSecret(SecretOpenAi, null);
        else if (!string.IsNullOrWhiteSpace(request.OpenAiApiKey)) secrets.SetSecret(SecretOpenAi, request.OpenAiApiKey);

        if (request.ClearSteamKey) secrets.SetSecret(SecretSteam, null);
        else if (!string.IsNullOrWhiteSpace(request.SteamApiKey)) secrets.SetSecret(SecretSteam, request.SteamApiKey);

        // Client-ID and Client-Secret can be replaced separately (blank = keep); clearing removes both
        if (request.ClearTwitchKey)
        {
            secrets.SetSecret(SecretTwitchClientId, null);
            secrets.SetSecret(SecretTwitchClientSecret, null);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.TwitchClientId)) secrets.SetSecret(SecretTwitchClientId, request.TwitchClientId);
            if (!string.IsNullOrWhiteSpace(request.TwitchClientSecret)) secrets.SetSecret(SecretTwitchClientSecret, request.TwitchClientSecret);
        }

        return await GetAsync(ct);
    }

    /// <summary>Stores the SteamID64 confirmed by "Mit Steam anmelden" or reported by the Steam bridge.</summary>
    public async Task SetSteamUserIdAsync(string steamId64, CancellationToken ct = default)
    {
        await UpsertSettingAsync(KeySteamUserId, steamId64.Trim(), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The explicitly stored SteamID64 (empty when none was saved; no registry fallback).</summary>
    public async Task<string> GetStoredSteamUserIdAsync(CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == KeySteamUserId, ct);
        return row?.Value ?? string.Empty;
    }

    public async Task<Dictionary<string, string>> GetNotesForAppAsync(uint appId, CancellationToken ct = default)
    {
        var prefix = $"note.{appId}.";
        var rows = await db.AppSettings
            .AsNoTracking()
            .Where(x => x.Key.StartsWith(prefix))
            .ToListAsync(ct);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var apiName = row.Key[prefix.Length..];
            result[apiName] = row.Value;
        }
        return result;
    }

    public async Task SaveNoteAsync(uint appId, string apiName, string note, CancellationToken ct = default)
    {
        var key = $"note.{appId}.{apiName.Trim()}";
        var existing = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (string.IsNullOrWhiteSpace(note))
        {
            if (existing is not null)
            {
                db.AppSettings.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        if (existing is null)
        {
            db.AppSettings.Add(new AppSettingEntity
            {
                Key = key,
                Value = note.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Value = note.Trim();
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public static string? TryDetectLocalSteamId64()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            var activeUserObj = key?.GetValue("ActiveUser");
            if (activeUserObj is not null && long.TryParse(activeUserObj.ToString(), out var activeUser) && activeUser > 0)
            {
                const long steam64Base = 76561197960265728L;
                return (steam64Base + activeUser).ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // Ignore registry access failures on non-Steam environments
        }
        return null;
    }

    private async Task RemoveSettingAsync(string key, CancellationToken ct)
    {
        var existing = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing is not null) db.AppSettings.Remove(existing);
    }

    private async Task UpsertSettingAsync(string key, string value, CancellationToken ct)
    {
        var existing = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing is null)
        {
            db.AppSettings.Add(new AppSettingEntity { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}

/// <summary>A submitted setting is not acceptable; the message is shown to the user as is, in the language of the request.</summary>
public sealed class SettingsValidationException(Msg text) : Exception(text.De)
{
    public Msg Text { get; } = text;

    public override string Message => Text.ToString();
}
