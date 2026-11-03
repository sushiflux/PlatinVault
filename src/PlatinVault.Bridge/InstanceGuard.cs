using System.Diagnostics;

namespace PlatinVault.Bridge;

/// <summary>
/// Makes sure only one bridge runs per Windows user session and lets other processes talk to the running one through
/// named events: "show your status window" (a second start of the exe, with an acknowledgement so the caller can tell
/// a current bridge from an old one without this feature) and "quit" (installer, updater and uninstaller).
/// </summary>
internal sealed class InstanceGuard : IDisposable
{
    private readonly BridgeEnvironment _env;
    private readonly EventWaitHandle _showEvent;
    private readonly EventWaitHandle _ackEvent;
    private readonly EventWaitHandle _quitEvent;
    private const uint AsfwAny = 0xFFFFFFFF;

    private readonly ManualResetEvent _stopListening = new(false);

    private Mutex? _mutex;
    private Thread? _listener;

    public InstanceGuard(BridgeEnvironment env)
    {
        _env = env;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, env.ShowStatusEventName);
        _ackEvent = new EventWaitHandle(false, EventResetMode.AutoReset, env.ShowAckEventName);
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, env.QuitEventName);
    }

    /// <summary>Takes the single-instance mutex; false when another bridge of this user session already runs.</summary>
    public bool TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, _env.MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    /// <summary>True when some bridge holds the mutex (without taking it).</summary>
    public bool IsAnotherInstanceRunning()
    {
        if (!Mutex.TryOpenExisting(_env.MutexName, out var existing)) return false;
        existing.Dispose();
        return true;
    }

    /// <summary>
    /// Asks the running bridge to show its status window. Returns false when nobody answered within the timeout, which
    /// means the running bridge is an old build without windows.
    /// </summary>
    public bool RequestShowStatus(TimeSpan timeout)
    {
        // This process was just started by the user and may bring windows to the front; let the running bridge do so as well.
        AllowSetForegroundWindow(AsfwAny);
        _ackEvent.Reset();
        _showEvent.Set();
        return _ackEvent.WaitOne(timeout);
    }

    /// <summary>Tells the running bridge to exit and waits until it has released the mutex. False on timeout.</summary>
    public bool RequestQuitAndWait(TimeSpan timeout)
    {
        if (!IsAnotherInstanceRunning()) return true;

        _quitEvent.Set();
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (!IsAnotherInstanceRunning()) return true;
            Thread.Sleep(150);
        }
        _quitEvent.Reset();
        return !IsAnotherInstanceRunning();
    }

    /// <summary>
    /// Last resort for an old bridge that does not understand "quit": ends all other bridge processes of this Windows
    /// session. Never used in test runs, which share the process name with a real bridge.
    /// </summary>
    public static int KillOtherInstances()
    {
        var killed = 0;
        var self = Environment.ProcessId;
        var session = Process.GetCurrentProcess().SessionId;

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(BridgeEnvironment.ExeFileName)))
        {
            using (process)
            {
                if (process.Id == self || process.SessionId != session) continue;
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                    killed++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    BridgeLog.Warn(BridgeTexts.Program.ProcessNotEnded(process.Id, ex.Message));
                }
            }
        }
        return killed;
    }

    /// <summary>Starts waiting for "show status" and "quit" requests; the callbacks run on the listener thread.</summary>
    public void StartListening(Action onShowStatus, Action onQuit)
    {
        // A request nobody consumed (e.g. a quit sent to an instance that had just exited) must not hit the next instance.
        _showEvent.Reset();
        _quitEvent.Reset();

        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _quitEvent, _stopListening };
            while (true)
            {
                switch (WaitHandle.WaitAny(handles))
                {
                    case 0:
                        onShowStatus();
                        _ackEvent.Set();
                        break;
                    case 1:
                        onQuit();
                        break;
                    default:
                        return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "PlatinVault.Bridge.InstanceEvents",
        };
        _listener.Start();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    public void Dispose()
    {
        _stopListening.Set();
        _listener?.Join(1000);
        _mutex?.Dispose();
        _showEvent.Dispose();
        _ackEvent.Dispose();
        _quitEvent.Dispose();
        _stopListening.Dispose();
    }
}
