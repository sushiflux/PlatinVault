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
    /// Resolves server and token with the precedence command line &gt; environment &gt; bridge.json next to the program
    /// &gt; the per-user bridge.json and validates them. Pure function (environment and file contents are injected) so
    /// the rules are easy to test. <paramref name="linux"/> only selects the wording of the "not configured" errors.
    /// </summary>
    public static bool TryResolve(string? cliServer, string? cliToken, Func<string, string?> getEnvironmentVariable,
        StoredBridgeConfig? portable, StoredBridgeConfig? user, out BridgeSettings? settings, out string? error, bool linux = false)
    {
        settings = null;

        var serverText = FirstNonBlank(cliServer, getEnvironmentVariable(ServerEnvVar), portable?.Server, user?.Server);
        var tokenText = FirstNonBlank(cliToken, getEnvironmentVariable(TokenEnvVar), portable?.Token, user?.Token);

        if (serverText is null)
        {
            error = CoreTexts.Config.NoServer(ServerEnvVar, linux);
            return false;
        }

        if (tokenText is null)
        {
            error = CoreTexts.Config.NoToken(TokenEnvVar, linux);
            return false;
        }

        if (!TryParseServer(serverText, out var server, out error)) return false;

        if (!IsValidToken(tokenText))
        {
            error = CoreTexts.Config.TokenInvalidChars;
            return false;
        }

        settings = new BridgeSettings(server!, tokenText);
        error = null;
        return true;
    }

    /// <summary>The server/token candidates by precedence, unvalidated - used to prefill the setup window.</summary>
    public static (string? Server, string? Token) ResolveRaw(string? cliServer, string? cliToken, Func<string, string?> getEnvironmentVariable,
        StoredBridgeConfig? portable, StoredBridgeConfig? user) =>
        (FirstNonBlank(cliServer, getEnvironmentVariable(ServerEnvVar), portable?.Server, user?.Server),
         FirstNonBlank(cliToken, getEnvironmentVariable(TokenEnvVar), portable?.Token, user?.Token));

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
            error = CoreTexts.Config.InvalidServer(text);
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = CoreTexts.Config.ServerHasCredentials;
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = CoreTexts.Config.ServerHasQuery;
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

/// <summary>Content of a <c>bridge.json</c> (same shape next to the program and in the per-user config folder).</summary>
internal sealed class StoredBridgeConfig
{
    public string? Server { get; set; }
    public string? Token { get; set; }

    /// <summary>"de" or "en": the language of the PlatinVault server, written by the server's ZIP, the setup window and the running bridge.</summary>
    public string? Language { get; set; }

    /// <summary>Linux bridge only: the Steam folder chosen by hand (the automatic search did not find it). The Windows bridge ignores it.</summary>
    public string? SteamDir { get; set; }
}

/// <summary>
/// Reading and writing <c>bridge.json</c>. The location is the host's business (next to the program, in the per-user folder); this class
/// only knows how a config file is read and written. The file holds the access token, so on Unix it is created readable by its owner only.
/// </summary>
internal static class BridgeConfigFile
{
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
            warning = CoreTexts.Config.Unreadable(path, ex.Message);
            return null;
        }
    }

    /// <param name="language">The language to remember (see <see cref="BridgeLang.Remembered"/>); null leaves the key out.</param>
    public static void Save(string path, BridgeSettings settings, string? language)
    {
        Write(path, new StoredBridgeConfig { Server = settings.ServerText, Token = settings.Token, Language = language });
    }

    /// <summary>
    /// Stores the language in <paramref name="target"/> (the config the bridge reads and saves to) and keeps server and token as they are.
    /// A config that cannot be read is left alone: rewriting it from nothing would destroy the token. If <paramref name="target"/> is not
    /// writable (a read-only folder next to the program), the per-user config at <paramref name="userConfigPath"/> takes the language instead.
    /// </summary>
    public static void RememberLanguage(string target, string userConfigPath, string language)
    {
        var existing = Load(target, out var warning);
        if (warning is not null) return;

        existing ??= new StoredBridgeConfig();
        existing.Language = language;

        try
        {
            Write(target, existing);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException &&
                                   !string.Equals(target, userConfigPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            BridgeLog.Debug(CoreTexts.Config.NotWritable(target, ex.Message));
            var user = Load(userConfigPath, out var userWarning) ?? new StoredBridgeConfig();
            if (userWarning is not null) return;
            user.Language = language;
            Write(userConfigPath, user);
        }
    }

    internal static void Write(string path, StoredBridgeConfig config)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.StoredBridgeConfig);

        // Write to a temp file first so a crash can never leave a half-written config behind. On Unix the temp file is created with
        // 0600 right away (it holds the token), and the move keeps that mode for the final file.
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(temp, options))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            // A stale temp file keeps its old mode when it is truncated: set it explicitly before the token goes in
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            writer.Write(json);
        }
        File.Move(temp, path, overwrite: true);
    }
}
