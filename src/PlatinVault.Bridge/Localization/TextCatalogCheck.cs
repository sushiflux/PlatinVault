namespace PlatinVault.Bridge;

/// <summary>
/// Test aid behind the hidden <c>--test-texts</c> option: checks the whole text catalogue of the Windows bridge (its own
/// <see cref="BridgeTexts"/> and the shared <see cref="CoreTexts"/>) with the shared <see cref="TextCatalog"/> check.
/// Exit code 0 = fine, 1 = problems (listed), so a script can gate on it.
/// </summary>
internal static class TextCatalogCheck
{
    public static int Run(BridgeEnvironment env) =>
        TextCatalog.Run([typeof(BridgeTexts), typeof(CoreTexts)], new Dictionary<Type, object> { [typeof(BridgeEnvironment)] = env });
}
