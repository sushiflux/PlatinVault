using System.Text.Json;

namespace PlatinVault.Bridge;

/// <summary>Validated server address and token the bridge runs with.</summary>
internal sealed record BridgeSettings(Uri Server, string Token)
{
    public const string ServerEnvVar = "PLATINVAULT_SERVER";
    public const string TokenEnvVar = "PLATINVAULT_TOKEN";

    /// <summary>Endpoint of section 4 of the spec; the server URL always ends with '/' so a sub path survives.</summary>
    public Uri StateEndpoint => new(Server, "api/bridge/state");

    /// <summary>Where the server offers the current bridge exe (self-update).</summary>
    public Uri ExeEndpoint => new(Server, "api/bridge/exe");

    /// <summary>Where the bridge sends the library facts of the Steam client (release date, ratings, size on disk) for the dashboard sort orders.</summary>
    public Uri LibraryEndpoint => new(Server, "api/bridge/library");

    /// <summary>Where the bridge asks for commands ("start this game") and reports how they ended.</summary>
    public Uri CommandsEndpoint => new(Server, "api/bridge/commands");

    /// <summary>Server address without the trailing slash, for display and for saving.</summary>
    public string ServerText => Server.GetLeftPart(UriPartial.Path).TrimEnd('/');

    /// <summary>Never expose more than the last 4 characters of the token.</summary>
    public string TokenHint => TokenHintOf(Token);

    public static string TokenHintOf(string token) => token.Length >= 12 ? "***" + token[^4..] : "***";

    /// <summary>
    /// Resolves server and token with the precedence command line &gt; environment &gt; bridge.json next to the exe
    /// &gt; bridge.json in %APPDATA% and validates them. Pure function (environment and file contents are injected) so
    /// the rules are easy to test.
    /// </summary>
    public static bool TryResolve(CliOptions cli, Func<string, string?> getEnvironmentVariable,
        StoredBridgeConfig? portable, StoredBridgeConfig? user, out BridgeSettings? settings, out string? error)
    {
        settings = null;

        var serverText = FirstNonBlank(cli.Server, getEnvironmentVariable(ServerEnvVar), portable?.Server, user?.Server);
        var tokenText = FirstNonBlank(cli.Token, getEnvironmentVariable(TokenEnvVar), portable?.Token, user?.Token);

        if (serverText is null)
        {
            error = BridgeTexts.Config.NoServer(ServerEnvVar);
            return false;
        }

        if (tokenText is null)
        {
            error = BridgeTexts.Config.NoToken(TokenEnvVar);
            return false;
        }

        if (!TryParseServer(serverText, out var server, out error)) return false;

        if (!IsValidToken(tokenText))
        {
            error = BridgeTexts.Config.TokenInvalidChars;
            return false;
        }

        settings = new BridgeSettings(server!, tokenText);
        error = null;
        return true;
    }

    /// <summary>The server/token candidates by precedence, unvalidated - used to prefill the setup window.</summary>
    public static (string? Server, string? Token) ResolveRaw(CliOptions cli, Func<string, string?> getEnvironmentVariable,
        StoredBridgeConfig? portable, StoredBridgeConfig? user) =>
        (FirstNonBlank(cli.Server, getEnvironmentVariable(ServerEnvVar), portable?.Server, user?.Server),
         FirstNonBlank(cli.Token, getEnvironmentVariable(TokenEnvVar), portable?.Token, user?.Token));

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return null;
    }

    internal static bool TryParseServer(string text, out Uri? server, out string? error)
    {
        server = null;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            // The typed text is echoed in full. The message slot of the setup window keeps its size and scrolls when the text is long
            error = BridgeTexts.Config.InvalidServer(text);
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = BridgeTexts.Config.ServerHasCredentials;
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = BridgeTexts.Config.ServerHasQuery;
            return false;
        }

        // Normalize to "<scheme>://<authority><path>/" so that Uri(base, "api/...") appends instead of replacing.
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" };
        server = builder.Uri;
        error = null;
        return true;
    }

    // Tokens are base64url, but only reject what can certainly never work in an Authorization header.
    internal static bool IsValidToken(string token) => token.All(c => c is > (char)32 and < (char)127);
}

/// <summary>Content of a <c>bridge.json</c> (same shape next to the exe and in <c>%APPDATA%\PlatinVault</c>).</summary>
internal sealed class StoredBridgeConfig
{
    public string? Server { get; set; }
    public string? Token { get; set; }

    /// <summary>"de" or "en": the language of the PlatinVault server, written by the server's ZIP, the setup window and the running bridge.</summary>
    public string? Language { get; set; }
}

internal static class BridgeConfigFile
{
    /// <summary>Directory of the running exe (the portable config, the update files and the package live there).</summary>
    public static string ExeDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>Portable config: the server-generated <c>bridge.json</c> shipped in the zip next to the exe.</summary>
    public static string PortablePath => Path.Combine(ExeDirectory, "bridge.json");

    /// <summary>Loads a config file; null when there is none or it is unreadable (warning text in <paramref name="warning"/>).</summary>
    public static StoredBridgeConfig? Load(string path, out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(path), ConfigJsonContext.Default.StoredBridgeConfig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = BridgeTexts.Config.Unreadable(path, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Where <c>--save</c> writes: an existing portable config next to the exe is updated in place (it beats the per-user
    /// file, so saving elsewhere would silently have no effect), otherwise the per-user file.
    /// </summary>
    public static string SaveTargetPath(BridgeEnvironment env) => File.Exists(PortablePath) ? PortablePath : env.UserConfigPath;

    /// <param name="language">The language to remember (see <see cref="BridgeLang.Remembered"/>); null leaves the key out.</param>
    public static void Save(string path, BridgeSettings settings, string? language)
    {
        Write(path, new StoredBridgeConfig { Server = settings.ServerText, Token = settings.Token, Language = language });
    }

    /// <summary>
    /// Stores the language in the config the bridge reads (same target as <see cref="SaveTargetPath"/>) and keeps server and token
    /// as they are. A config that cannot be read is left alone: rewriting it from nothing would destroy the token.
    /// If the folder next to the exe is read-only, the per-user config takes the language instead.
    /// </summary>
    public static void RememberLanguage(BridgeEnvironment env, string language)
    {
        var target = SaveTargetPath(env);
        var existing = Load(target, out var warning);
        if (warning is not null) return;

        existing ??= new StoredBridgeConfig();
        existing.Language = language;

        try
        {
            Write(target, existing);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException &&
                                   !string.Equals(target, env.UserConfigPath, StringComparison.OrdinalIgnoreCase))
        {
            BridgeLog.Debug(BridgeTexts.Config.NotWritable(target, ex.Message));
            var user = Load(env.UserConfigPath, out var userWarning) ?? new StoredBridgeConfig();
            if (userWarning is not null) return;
            user.Language = language;
            Write(env.UserConfigPath, user);
        }
    }

    private static void Write(string path, StoredBridgeConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.StoredBridgeConfig);

        // Write to a temp file first so a crash can never leave a half-written config behind.
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
