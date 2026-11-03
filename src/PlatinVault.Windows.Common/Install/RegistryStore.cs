using System.Text.Json;
using Microsoft.Win32;

namespace PlatinVault.Windows.Install;

/// <summary>The few registry operations the installer and the program need, all under HKEY_CURRENT_USER.</summary>
public interface IRegistryStore
{
    string? GetString(string key, string name);

    void SetString(string key, string name, string value);

    void SetDword(string key, string name, int value);

    void DeleteValue(string key, string name);

    void DeleteKey(string key);

    bool KeyExists(string key);
}

/// <summary>The real HKEY_CURRENT_USER.</summary>
public sealed class WindowsRegistry : IRegistryStore
{
    public string? GetString(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name) as string;
    }

    public void SetString(string key, string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, value, RegistryValueKind.String);
    }

    public void SetDword(string key, string name, int value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKey(string key) => Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);

    public bool KeyExists(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k is not null;
    }
}

/// <summary>
/// Test stand-in for the registry: the same keys and values in a JSON file inside the test root, so a test installation never
/// writes a Run value, an uninstall entry or anything else into the real profile.
/// </summary>
public sealed class FileRegistry(string path) : IRegistryStore
{
    private readonly object _lock = new();

    public string Path { get; } = path;

    public string? GetString(string key, string name)
    {
        lock (_lock)
        {
            return Load().TryGetValue(key.ToLowerInvariant(), out var values) && values.TryGetValue(name, out var value) ? value : null;
        }
    }

    public void SetString(string key, string name, string value) => Update(key, values => values[name] = value);

    public void SetDword(string key, string name, int value) => Update(key, values => values[name] = value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void DeleteValue(string key, string name) => Update(key, values => values.Remove(name), createKey: false);

    public void DeleteKey(string key)
    {
        lock (_lock)
        {
            var all = Load();
            var prefix = key.ToLowerInvariant();
            foreach (var existing in all.Keys.Where(k => k == prefix || k.StartsWith(prefix + "\\", StringComparison.Ordinal)).ToList())
                all.Remove(existing);
            Save(all);
        }
    }

    public bool KeyExists(string key)
    {
        lock (_lock) return Load().ContainsKey(key.ToLowerInvariant());
    }

    private void Update(string key, Action<Dictionary<string, string>> change, bool createKey = true)
    {
        lock (_lock)
        {
            var all = Load();
            var id = key.ToLowerInvariant();
            if (!all.TryGetValue(id, out var values))
            {
                if (!createKey) return;
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                all[id] = values;
            }
            change(values);
            Save(all);
        }
    }

    private Dictionary<string, Dictionary<string, string>> Load()
    {
        if (!File.Exists(Path)) return [];
        var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(Path)) ?? [];
        return raw.ToDictionary(e => e.Key, e => new Dictionary<string, string>(e.Value, StringComparer.OrdinalIgnoreCase));
    }

    private void Save(Dictionary<string, Dictionary<string, string>> all)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
