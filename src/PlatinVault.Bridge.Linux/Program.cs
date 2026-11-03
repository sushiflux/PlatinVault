namespace PlatinVault.Bridge;

internal static class Program
{
    private static async Task<int> Main(string[] args) =>
        await LinuxApp.RunAsync(args, Environment.GetEnvironmentVariable, CancellationToken.None).ConfigureAwait(false);
}
