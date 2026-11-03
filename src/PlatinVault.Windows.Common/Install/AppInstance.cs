using System.IO.Pipes;
using System.Text;

namespace PlatinVault.Windows.Install;

/// <summary>
/// Single instance of the Windows program and its tiny command channel: a named mutex says "PlatinVault runs" and a named pipe takes
/// one-word commands - "show" (a second start brings the window to the front), "guide" (hotkey of a second start), "quit" (the
/// installer and the uninstaller ask the running program to back up, stop its database and exit before they replace or remove it).
/// Both names are per user session ("Local\") and carry the test suffix in a test run.
/// </summary>
public sealed class AppInstance : IDisposable
{
    public const string Show = "show";
    public const string Quit = "quit";
    public const string Guide = "guide";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _listener;

    /// <summary>How often the listener had to start again after an error (test aid).</summary>
    public int Failures { get; private set; }

    private AppInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Takes the instance; null when another copy already runs.</summary>
    public static AppInstance? TryAcquire(InstallLayout layout)
    {
        var mutex = new Mutex(initiallyOwned: false, layout.MutexName);
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous copy crashed: the mutex is ours now
        }
        return new AppInstance(mutex);
    }

    public static bool IsRunning(InstallLayout layout)
    {
        if (!Mutex.TryOpenExisting(layout.MutexName, out var existing)) return false;
        using (existing)
        {
            try
            {
                if (existing.WaitOne(0))
                {
                    existing.ReleaseMutex();
                    return false;
                }
                return true;
            }
            catch (AbandonedMutexException)
            {
                existing.ReleaseMutex();
                return false;
            }
        }
    }

    /// <summary>Sends a command to the running copy. False when none answers within the timeout.</summary>
    public static bool Send(InstallLayout layout, string command, TimeSpan timeout)
    {
        try
        {
            // CurrentUserOnly: only connects to a pipe created by this same user (never another account's program)
            using var pipe = new NamedPipeClientStream(".", layout.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect((int)timeout.TotalMilliseconds);
            var bytes = Encoding.UTF8.GetBytes(command + "\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            var buffer = new byte[16];
            var read = pipe.Read(buffer, 0, buffer.Length);
            return read > 0;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Asks a running copy to quit and waits until it is gone. True when nothing runs (anymore).</summary>
    public static bool RequestQuitAndWait(InstallLayout layout, TimeSpan timeout)
    {
        if (!IsRunning(layout)) return true;
        Send(layout, Quit, TimeSpan.FromSeconds(5));
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (!IsRunning(layout)) return true;
            Thread.Sleep(250);
        }
        return !IsRunning(layout);
    }

    /// <summary>Listens for commands on a background thread; <paramref name="handle"/> runs on that thread (marshal to the UI yourself).</summary>
    public void Listen(InstallLayout layout, Action<string> handle)
    {
        _listener = new Thread(() =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    // CurrentUserOnly: other Windows accounts on this PC can neither connect nor create a pipe of the same name for us
                    using var server = new NamedPipeServerStream(layout.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    server.WaitForConnectionAsync(_stop.Token).GetAwaiter().GetResult();
                    var buffer = new byte[64];
                    var read = server.Read(buffer, 0, buffer.Length);
                    var command = Encoding.UTF8.GetString(buffer, 0, read).Trim().ToLowerInvariant();
                    server.Write("ok\n"u8);
                    server.Flush();
                    if (command is Show or Quit or Guide) handle(command);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // A client that went away mid-command, or the name is held by someone else: never crash, never spin - wait, listen again
                    Failures++;
                    if (_stop.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500))) break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "PlatinVault command pipe",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        // The listener uses the token until it has seen the cancellation: let it end before the token source goes away
        _listener?.Join(TimeSpan.FromSeconds(3));
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* not owned by this thread any more */ }
        _mutex.Dispose();
        if (_listener is null || !_listener.IsAlive) _stop.Dispose();
    }
}
