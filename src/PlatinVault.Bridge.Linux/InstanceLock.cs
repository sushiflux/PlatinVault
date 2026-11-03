namespace PlatinVault.Bridge;

/// <summary>
/// One bridge per user: the running service (or a manual <c>run</c>) holds an exclusive lock on a file in the state folder, so a second one
/// never pushes the same state twice or starts a game twice. The lock is the operating system's file lock (flock on Linux), so it vanishes
/// with the process, whatever way it ends - no stale lock files.
/// </summary>
internal sealed class InstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private InstanceLock(FileStream stream) => _stream = stream;

    /// <summary>The lock, or null when another bridge holds it.</summary>
    public static InstanceLock? TryAcquire(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return new InstanceLock(new FileStream(path, options));
        }
        catch (IOException)
        {
            // .NET takes an advisory lock for FileShare.None: a second opener fails with an IOException
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
