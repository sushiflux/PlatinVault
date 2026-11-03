using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;

namespace Companion.Api.Settings;

/// <summary>
/// Protection of the DataProtection key ring at rest. Choosing our own key folder switches off .NET's built-in encryption
/// of the key ring (learn.microsoft.com, "Key encryption at rest"), so the master key would sit next to the encrypted API
/// keys in plain form. On Windows the ring is encrypted with DPAPI (bound to this user account); on Linux (container,
/// Unraid) the key and secret folders are restricted to the app user.
/// </summary>
public static class KeyRingProtection
{
    private static readonly XNamespace DataProtectionNs = "http://schemas.asp.net/2015/03/dataProtection";

    /// <summary>Linux/macOS: only the owner (the app user) may open the folder - files inside are then unreachable for others.</summary>
    public static bool RestrictToOwner(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Windows, once: key files written before DPAPI was switched on still hold the master key unencrypted. The API keys are
    /// read with that old ring, the old key files are moved out, the API keys are encrypted again with a new, DPAPI-protected
    /// key and checked; only then are the old key files deleted. On any problem everything is put back as it was.
    /// Must run before anything else uses DataProtection (the key ring is loaded lazily on first use).
    /// </summary>
    public static void MigrateUnprotectedKeys(SecretStore secrets, string keysDirectory, ILogger logger)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(keysDirectory))
        {
            return;
        }

        var unprotectedKeys = Directory.GetFiles(keysDirectory, "key-*.xml").Where(IsUnprotected).ToList();
        if (unprotectedKeys.Count == 0)
        {
            return;
        }

        var legacyDirectory = keysDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "-legacy";
        var originalSecrets = secrets.ReadAllRaw();
        try
        {
            // 1. Read the API keys with a separate provider over a copy of the old ring
            Directory.CreateDirectory(legacyDirectory);
            foreach (var file in unprotectedKeys)
            {
                File.Copy(file, Path.Combine(legacyDirectory, Path.GetFileName(file)), overwrite: true);
            }

            var plain = new Dictionary<string, string>();
            using (var legacyServices = new ServiceCollection()
                       .AddDataProtection()
                       .PersistKeysToFileSystem(new DirectoryInfo(legacyDirectory))
                       .SetApplicationName(SecretStore.ApplicationName)
                       .Services.BuildServiceProvider())
            {
                var legacyProtector = legacyServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(SecretStore.Purpose);
                foreach (var (name, bytes) in originalSecrets)
                {
                    try
                    {
                        plain[name] = System.Text.Encoding.UTF8.GetString(legacyProtector.Unprotect(bytes)).Trim();
                    }
                    catch (CryptographicException)
                    {
                        // Already unreadable before the migration - left untouched
                    }
                }
            }

            // 2. Take the old key files out of the live ring, 3. encrypt again with a new DPAPI-protected key, 4. check
            foreach (var file in unprotectedKeys)
            {
                File.Delete(file);
            }
            foreach (var (name, value) in plain)
            {
                secrets.SetSecret(name, value);
            }
            var mismatch = plain.Where(p => secrets.GetSecret(p.Key) != p.Value).Select(p => p.Key).ToList();
            if (mismatch.Count > 0)
            {
                throw new InvalidOperationException($"Nach dem Neu-Verschlüsseln nicht lesbar: {string.Join(", ", mismatch)}");
            }
            if (Directory.GetFiles(keysDirectory, "key-*.xml").Any(IsUnprotected))
            {
                throw new InvalidOperationException("Der neue Schlüssel wurde nicht mit DPAPI geschützt.");
            }

            Directory.Delete(legacyDirectory, recursive: true);
            logger.LogInformation("Schlüsselbund jetzt mit Windows-DPAPI geschützt: {Count} API-Key(s) neu verschlüsselt, {Old} alte(r) Schlüssel entfernt",
                plain.Count, unprotectedKeys.Count);
        }
        catch (Exception ex)
        {
            // Put everything back: old key files into the ring, the API key files as they were
            foreach (var file in unprotectedKeys)
            {
                var copy = Path.Combine(legacyDirectory, Path.GetFileName(file));
                if (!File.Exists(file) && File.Exists(copy))
                {
                    File.Copy(copy, file);
                }
            }
            secrets.WriteAllRaw(originalSecrets);
            if (Directory.Exists(legacyDirectory))
            {
                Directory.Delete(legacyDirectory, recursive: true);
            }
            logger.LogError(ex, "Schlüsselbund konnte nicht auf DPAPI umgestellt werden - alter Zustand wiederhergestellt");
        }
    }

    // A key file still holding its master key in plain form carries the "requiresEncryption" marker on that element
    private static bool IsUnprotected(string path)
    {
        try
        {
            return XDocument.Load(path).Descendants()
                .Any(e => (string?)e.Attribute(DataProtectionNs + "requiresEncryption") == "true");
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return false;
        }
    }
}
