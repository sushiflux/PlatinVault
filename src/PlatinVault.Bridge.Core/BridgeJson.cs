using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatinVault.Bridge;

/// <summary>
/// Source-generated JSON metadata for everything the bridge sends or receives. Reflection-based serialization is
/// disabled in the csproj (trimmed exe), so every serialized type must be listed here. camelCase on the wire; null
/// properties (locked achievements without an unlock time) are omitted.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BridgeStateDto))]
[JsonSerializable(typeof(BridgeReplyDto))]
[JsonSerializable(typeof(BridgePollRequestDto))]
[JsonSerializable(typeof(BridgePollReplyDto))]
[JsonSerializable(typeof(BridgeLibraryDto))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext
{
}

/// <summary>Same, for the small <c>bridge.json</c> config file (indented because people open it in an editor).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StoredBridgeConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}
