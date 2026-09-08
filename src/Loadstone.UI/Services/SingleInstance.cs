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

    /// <summary>Opens the show-Settings event, creating it when no instance holds one yet.</summary>
    /// <remarks>
    /// The first instance must open this before it takes the mutex. A named kernel object
    /// lives only while a handle to it is open, and <see cref="SignalShowSettings"/> holds the only
    /// other one and closes it straight after setting it: an event the first instance opens after
    /// that signal is a brand new, unsignalled object and the launch that asked for Settings is
    /// silently lost. Opening it first means anything that finds the mutex taken is signalling an
    /// object that already exists.
    /// </remarks>
    public static EventWaitHandle OpenShowSettingsEvent(string eventName = DefaultEventName) =>
        new(initialState: false, EventResetMode.AutoReset, eventName);

    public static void SignalShowSettings(string eventName = DefaultEventName)
    {
        using var handle = OpenShowSettingsEvent(eventName);
        handle.Set();
    }

    /// <summary>Calls <paramref name="onSignal"/> on a thread-pool thread each time another instance signals. Dispose to stop listening.</summary>
    public static IDisposable ListenForShowSettings(Action onSignal, string eventName = DefaultEventName) =>
        ListenForShowSettings(onSignal, OpenShowSettingsEvent(eventName));

    /// <summary>The same, on a handle the caller opened earlier; the listener owns it from here.</summary>
    public static IDisposable ListenForShowSettings(Action onSignal, EventWaitHandle handle)
    {
        var registration = ThreadPool.RegisterWaitForSingleObject(
            handle, (_, _) => Signal(onSignal), null, Timeout.Infinite, executeOnlyOnce: false);
        return new Listener(registration, handle);
    }

    /// <summary>
    /// Guards the pool thread. An exception escaping a <see cref="ThreadPool"/> callback ends the
    /// process, and the one that actually happens is InvalidOperationException out of
    /// Dispatcher.BeginInvoke: between Shutdown() starting and this listener being disposed, a
    /// second launch would kill the first instance from a pool thread mid-exit - before Restore has
    /// given the user their edge snapping back. There is nothing to say about it either, since the
    /// only thing that request can be told is that Loadstone is already leaving.
    /// </summary>
    private static void Signal(Action onSignal)
    {
        try
        {
            onSignal();
        }
        catch (Exception)
        {
        }
    }

    private sealed class Listener(RegisteredWaitHandle registration, EventWaitHandle handle) : IDisposable
    {
        /// <summary>Long enough for a callback that only queues work on the dispatcher; short enough not to hold the exit.</summary>
        private static readonly TimeSpan CallbackDrainTimeout = TimeSpan.FromSeconds(1);

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Unregister with a wait object rather than null: null returns while a callback may
            // still be running on the pool, and the line below closes the handle that callback was
            // registered on. The wait object is signalled once every callback has finished, and the
            // wait is bounded because a wait that cannot end would hold the exit short of Restore.
            using var drained = new ManualResetEvent(false);
            if (registration.Unregister(drained))
            {
                drained.WaitOne(CallbackDrainTimeout);
            }

            handle.Dispose();
        }
    }
}
