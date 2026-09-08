namespace Loadstone.UI.Services;

/// <summary>
/// One Loadstone at a time. The first instance holds a named mutex; a second instance sets a named
/// event and exits, and the first opens Settings. A broadcast window message would not do: message-only
/// windows never receive broadcasts, and Loadstone has no other window that is always alive.
/// </summary>
internal static class SingleInstance
{
    // Names are parameters so the tests can use their own: on the production names a test run would
    // block a running Loadstone from starting, or pop its Settings window.
    public const string DefaultMutexName = "Loadstone-SingleInstance";
    public const string DefaultEventName = "Loadstone-ShowSettings";

    /// <summary>The mutex to hold for the life of the process, or null when another instance holds it.</summary>
    public static Mutex? TryAcquire(string mutexName = DefaultMutexName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    public static void SignalShowSettings(string eventName = DefaultEventName)
    {
        using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        handle.Set();
    }

    /// <summary>Calls <paramref name="onSignal"/> on a thread-pool thread each time another instance signals. Dispose to stop listening.</summary>
    public static IDisposable ListenForShowSettings(Action onSignal, string eventName = DefaultEventName)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        var registration = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
        return new Listener(registration, handle);
    }

    private sealed class Listener(RegisteredWaitHandle registration, EventWaitHandle handle) : IDisposable
    {
        public void Dispose()
        {
            registration.Unregister(null);
            handle.Dispose();
        }
    }
}
