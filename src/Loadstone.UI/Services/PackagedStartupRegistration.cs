using Loadstone.Core.Platform;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;

namespace Loadstone.UI.Services;

/// <summary>
/// Launch at login for the packaged build, over the package's <c>windows.startupTask</c> extension.
/// </summary>
/// <remarks>
/// The HKCU Run key cannot be used here and, worse, does not fail: MSIX copy-on-writes the value into
/// a private per-package hive that Explorer never reads, then merges that hive on read, so the write
/// succeeds, the status reads back Enabled, and nothing runs at logon. This class is the supported
/// mechanism. It gives up one thing in exchange: once the user disables the task in Task Manager or
/// Settings the state is <see cref="StartupTaskState.DisabledByUser"/> and
/// <see cref="StartupTask.RequestEnableAsync"/> will not override them, so
/// <see cref="EnableAsync"/> reports false and the view model tells the user where to change it.
/// <para>
/// <see cref="Status"/> reflects the task as of the last <see cref="InitializeAsync"/> call, not a
/// live cross-process query: <see cref="StartupTask.State"/> is read off the cached object, and an
/// external change - the user flipping the switch in Task Manager while Loadstone keeps running -
/// may not be visible until <see cref="InitializeAsync"/> runs again, which it is safe to do, since
/// it only re-fetches and reassigns the cached task. The unpackaged registry path has no such gap
/// because it re-reads the registry on every <see cref="Status"/> get; closing it here would mean
/// making <see cref="Status"/> asynchronous, which every caller depends on it not being.
/// </para>
/// </remarks>
internal sealed class PackagedStartupRegistration : IStartupRegistration
{
    /// <summary>Must equal the TaskId in the manifest's uap5:StartupTask element, or GetAsync answers null.</summary>
    internal const string TaskId = "LoadstoneStartup";

    private readonly ILogger<PackagedStartupRegistration> _logger;

    /// <summary>
    /// The startup task from the latest <see cref="InitializeAsync"/>. Null until that first fetch
    /// and when a fetch fails. A later <see cref="InitializeAsync"/> replaces it.
    /// </summary>
    private StartupTask? _task;

    public PackagedStartupRegistration(ILogger<PackagedStartupRegistration> logger) => _logger = logger;

    /// <summary>
    /// Synchronous by design. <see cref="StartupTask.State"/> is a plain property on an object we
    /// already hold, so the only asynchronous work is the one-time fetch in
    /// <see cref="InitializeAsync"/>; the Settings constructor and the tray must never await.
    /// </summary>
    public StartupStatus Status
    {
        get
        {
            // A local, not the field: InitializeAsync is safely re-callable and reassigns _task, which
            // can now land between the null check and the dereference - ShowSettings queues a refresh
            // that re-initializes while a write may be in flight.
            var task = _task;
            if (task is null)
            {
                return StartupStatus.Unknown;
            }

            try
            {
                return Map(task.State);
            }
            catch (Exception ex)
            {
                // _task.State is a proxy call into an out-of-process broker and can throw (e.g. if
                // the broker has gone since InitializeAsync succeeded). This must never throw: it is
                // read from the Settings constructor and on every tray click with no handler above it.
                _logger.LogError(ex, "could not read the startup task state");
                return StartupStatus.Unknown;
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _task = await StartupTask.GetAsync(TaskId);
        }
        catch (Exception ex)
        {
            // Nothing here may throw: this runs on the dispatcher during startup with no handler over
            // it, and the app must still tile windows when its login item cannot be read.
            //
            // Unlike RegistryStartupRegistration, this class catches Exception broadly rather than
            // filtering to a known set: the WinRT/COM exception surface behind StartupTask is
            // open-ended (broker RPC failures surface as any HRESULT), and every method here has the
            // same never-throw obligation, so a narrower filter would just be a second way to escape.
            _logger.LogError(ex, "could not read the startup task {TaskId}", TaskId);
        }
    }

    public async Task<bool> EnableAsync()
    {
        // A local, for the reason given on Status.
        var task = _task;
        if (task is null)
        {
            return false;
        }

        try
        {
            // Answers the state it ended in, which is DisabledByUser when the user has overruled us.
            return await task.RequestEnableAsync() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "could not enable the startup task");
            return false;
        }
    }

    public Task<bool> DisableAsync()
    {
        // A local, for the reason given on Status.
        var task = _task;
        if (task is null)
        {
            return Task.FromResult(false);
        }

        try
        {
            task.Disable();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "could not disable the startup task");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// No discard arm on purpose, as with the switches over Tile and WindowCommand: a StartupTaskState
    /// member added by a future Windows SDK fails the build here rather than reading as NotRegistered
    /// at run time. CS8524 is suppressed project-wide.
    /// </summary>
    internal static StartupStatus Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => StartupStatus.NotRegistered,
        StartupTaskState.Enabled => StartupStatus.Enabled,
        StartupTaskState.DisabledByUser => StartupStatus.DisabledInTaskManager,
        StartupTaskState.DisabledByPolicy => StartupStatus.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => StartupStatus.EnabledByPolicy,
    };
}
