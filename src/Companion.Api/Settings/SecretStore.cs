using System.Security.Cryptography;
using System.Text;
using Companion.Api.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Companion.Api.Settings;

/// <summary>
/// Encrypts third-party API keys (Gemini, Claude, Steam, Twitch) at rest using ASP.NET Core DataProtection.
/// Stored in DataDir/secrets, never in plaintext or in git.
/// </summary>
public sealed class SecretStore
{
    // Shared with KeyRingProtection, which reads the old key ring once during the switch to DPAPI
    public const string ApplicationName = "PlatinVault";
    public const string Purpose = "CompanionVault.Secrets.v1";

    private readonly IDataProtector _protector;
    private readonly string _secretsDirectory;

    public SecretStore(IDataProtectionProvider provider, IOptions<CompanionOptions> options)
    {
        _protector = provider.CreateProtector(Purpose);
        _secretsDirectory = options.Value.ResolvedSecretsDir;
        Directory.CreateDirectory(_secretsDirectory);
        KeyRingProtection.RestrictToOwner(_secretsDirectory);
    }

    public string? GetSecret(string name)
    {
        var path = GetPath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plainBytes = _protector.Unprotect(protectedBytes);
            var value = Encoding.UTF8.GetString(plainBytes).Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void SetSecret(string name, string? value)
    {
        var path = GetPath(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return;
        }

        var plainBytes = Encoding.UTF8.GetBytes(value.Trim());
        var protectedBytes = _protector.Protect(plainBytes);
        File.WriteAllBytes(path, protectedBytes);
    }

    public string GetHint(string name) => MaskHint(GetSecret(name));

    /// <summary>All stored secrets as encrypted bytes (name without extension); only for the one-time key ring migration.</summary>
    internal Dictionary<string, byte[]> ReadAllRaw() =>
        Directory.GetFiles(_secretsDirectory, "*.bin")
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), File.ReadAllBytes);

    /// <summary>Writes encrypted bytes back unchanged (rollback of the key ring migration).</summary>
    internal void WriteAllRaw(Dictionary<string, byte[]> secrets)
    {
        foreach (var (name, bytes) in secrets)
        {
            File.WriteAllBytes(GetPath(name), bytes);
        }
    }

    /// <summary>Masked form of a secret that is safe to show (first and last four characters, or only dots for short values).</summary>
    public static string MaskHint(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return string.Empty;
        }

        return secret.Length <= 8
            ? "••••••••"
            : $"{secret[..4]}••••{secret[^4..]}";
    }

    private string GetPath(string name) => Path.Combine(_secretsDirectory, $"{name}.bin");
}

