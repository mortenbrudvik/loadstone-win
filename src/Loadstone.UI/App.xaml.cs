using System.Windows;
using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.UI.DependencyInjection;
using Loadstone.UI.Input;
using Loadstone.UI.Interop;
using Loadstone.UI.Services;
using Loadstone.UI.Tray;
using Loadstone.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NLog.Extensions.Logging;
using Wpf.Ui.Appearance;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI;

public partial class App : Application
{
    /// <summary>
    /// How long to wait for the async file target to drain. The fatal handlers need it because an
    /// unhandled exception kills the process without running NLog's own shutdown flush, so the one
    /// line that explains the crash would sit in the buffer and be lost.
    /// </summary>
    private static readonly TimeSpan LogFlushTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long an off-thread restore waits for the UI thread to take it. Short on purpose: the UI
    /// thread is often the one that has just died, and the process is on its way out either way.
    /// </summary>
    private static readonly TimeSpan RestoreHandoffTimeout = TimeSpan.FromSeconds(1);

    private IHost? _host;
    private Mutex? _mutex;
    private IDisposable? _showSettingsListener;
    private ISettingsService? _settings;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // First of everything, and null-safe on both the host and the logger it has not built yet.
        // Below this line sit the two named kernel objects, the container and the host, and all of
        // it used to run bare: a first instance at another integrity level owns the mutex with a
        // DACL this process cannot open for full control, and the user who double-clicked Loadstone
        // to open Settings got an unhandled-exception dialog and an empty log instead.
        InstallExceptionHandlers();

        var showSettings = TryBecomeTheOnlyInstance();
        if (showSettings is null)
        {
            Shutdown();
            return;
        }

        // A bare HostBuilder, not Host.CreateDefaultBuilder: the app has no configuration, and the
        // default builder would read an appsettings.json out of whatever directory Loadstone was
        // launched from. What the hosting infrastructure is allowed to write to the log is decided in
        // LoggingSetup, because NLog does not honour the filters configured here.
        var host = new HostBuilder()
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureContainer<ContainerBuilder>(builder => builder.RegisterModule<AppModule>())
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddNLog(LoggingSetup.Build());
            })
            .Build();
        _host = host;
        host.Start();
        _logger = host.Services.GetRequiredService<ILogger<App>>();
        ApplicationThemeManager.ApplySystemTheme();

        // Listening starts here rather than at the end of startup. The dispatcher does not pump
        // until OnStartup returns, so a second launch arriving while the tray and the hooks are
        // still being built is queued and runs against a finished container.
        _showSettingsListener = SingleInstance.ListenForShowSettings(() => Dispatcher.BeginInvoke(ShowSettings), showSettings);

        var services = host.Services;
        var settings = services.GetRequiredService<ISettingsService>();
        var nativeSnap = services.GetRequiredService<NativeSnap>();
        var shell = services.GetRequiredService<ShellActions>();
        shell.OpenSettingsAction = ShowSettings;
        shell.QuitAction = Shutdown;

        // A logoff or a restart ends the process without ever reaching OnExit.
        SystemEvents.SessionEnding += RestoreOnSessionEnd;

        // Order matters, as on the Mac: native snap off before the first drag can happen, monitors last.
        nativeSnap.Follow();
        // Blocking here is temporary. Task 6 queues this onto the dispatcher, once an implementation
        // that actually awaits is registered.
        services.GetRequiredService<IStartupRegistration>().InitializeAsync().GetAwaiter().GetResult();
        services.GetRequiredService<TrayController>();
        services.GetRequiredService<HotkeyCenter>().Start();
        var drag = services.GetRequiredService<DragSnapMonitor>();
        drag.Start();

        // Only now that both halves have had their say: the suppression is Loadstone's to keep only
        // while its replacement is actually running.
        _settings = settings;
        settings.Changed += OnSettingsChanged;
        SyncNativeSnapToDragMonitor();

        // The launch line doubles as the probe, and the size of the file before it is written is the
        // other half of that probe: on a second launch the same day, the file the earlier run wrote
        // is already there and not empty, so only a change in it proves this run's own line landed.
        var logLengthBeforeLaunch = LoggingSetup.CurrentLogLength();
        _logger.LogInformation(
            "launched: version {Version} dragSnapping={Drag} monitoring={Monitoring} nativeSnapSuppressed={Suppressed} startup={Startup}",
            typeof(App).Assembly.GetName().Version,
            settings.Current.DragSnappingEnabled,
            drag.IsMonitoring,
            nativeSnap.IsSuppressing,
            services.GetRequiredService<IStartupRegistration>().Status);

        // Flush it, then look for it. Everything the user could ever be told about this app goes
        // through NLog, and NLog reports nothing when it cannot write, so the answer cannot come
        // back this way: VerifyWritable puts it in NLog's internal log under the temp directory,
        // which is the one place a later diagnosis can read it from.
        NLog.LogManager.Flush(LogFlushTimeout);
        LoggingSetup.VerifyWritable(logLengthBeforeLaunch);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BestEffort("stop watching for the end of the session", () => { SystemEvents.SessionEnding -= RestoreOnSessionEnd; });

        if (_settings is { } settings)
        {
            BestEffort("stop reconciling drag snapping", () => { settings.Changed -= OnSettingsChanged; });
        }

        if (_host is { } host)
        {
            var services = host.Services;

            // First of all, so a second launch arriving during shutdown cannot ask for a window from a
            // container that is on its way out.
            BestEffort("stop listening for a second launch", () => _showSettingsListener?.Dispose());

            // Section 7 promises the user's edge snapping back after a quit, so nothing above
            // Restore may cost them that: each teardown step is best-effort and Restore is the
            // one call the finally guarantees. Both bookends live inside the protected region for
            // the same reason - resolving NativeSnap and stopping the follow used to sit above the
            // try, where one throw skipped the very step the finally exists for.
            NativeSnap? nativeSnap = null;
            try
            {
                // GetService, not GetRequiredService: a container that cannot hand it over leaves a
                // null the finally can live with, where an exception would carry the same failure
                // past every step below.
                nativeSnap = services.GetService<NativeSnap>();
                BestEffort("stop following the drag-snapping setting", () => nativeSnap?.StopFollowing());
                BestEffort("stop the drag monitor", () => services.GetRequiredService<DragSnapMonitor>().Stop());
                BestEffort("release the hotkeys", () => services.GetRequiredService<HotkeyCenter>().Dispose());
                BestEffort("close the hotkey window", () => services.GetRequiredService<IHotkeyRegistrar>().Dispose());
                BestEffort("remove the tray icon", () => services.GetRequiredService<TrayController>().Dispose());
            }
            finally
            {
                BestEffort("restore Windows edge snapping", () => nativeSnap?.Restore());
                _logger?.LogInformation("exiting");
                NLog.LogManager.Flush(LogFlushTimeout);
            }

            // Wrapped as well: a throw out of either of these used to lose everything after it,
            // including the buffered tail of the log - the part most likely to explain the throw.
            BestEffort("stop the host", () => host.StopAsync().GetAwaiter().GetResult());
            BestEffort("dispose the host", host.Dispose);
        }

        var mutex = _mutex;
        BestEffort("release the single-instance mutex", () =>
        {
            mutex?.ReleaseMutex();
            mutex?.Dispose();
        });
        BestEffort("shut NLog down", NLog.LogManager.Shutdown);
        base.OnExit(e);
    }

    private void ShowSettings() => _host?.Services.GetRequiredService<SettingsWindow>().ShowAndActivate();

    /// <summary>
    /// Takes the single-instance mutex and returns the event to listen on, or asks the Loadstone
    /// that already holds it to show Settings and returns null, meaning this process must leave.
    /// </summary>
    /// <remarks>
    /// The whole handshake is guarded because both halves of it open kernel objects by name, and
    /// both throw UnauthorizedAccessException when an instance running at another integrity level
    /// owns them with a DACL this process may not open for full control. There is no logger yet and
    /// nothing the user could act on, so a failed handshake leaves quietly rather than running a
    /// second tiler over the first one's hotkeys.
    /// </remarks>
    private EventWaitHandle? TryBecomeTheOnlyInstance()
    {
        EventWaitHandle? showSettings = null;
        try
        {
            // The event before the mutex: see SingleInstance.OpenShowSettingsEvent for why the
            // order is the whole point.
            showSettings = SingleInstance.OpenShowSettingsEvent();
            _mutex = SingleInstance.TryAcquire();
            if (_mutex is not null)
            {
                return showSettings;
            }

            // Another Loadstone is running: hand it our foreground rights, ask it to show Settings,
            // and leave. Without the handover the first instance is a background process with no
            // right to SetForegroundWindow, so the window it opens lands behind whatever the user
            // is looking at and its taskbar button flashes instead. The tray path never needed it -
            // the shell grants foreground rights to the owner of a notification icon the user has
            // just clicked - which is why only this path is broken.
            User32.AllowSetForegroundWindow(NativeConstants.ASFW_ANY);
            showSettings.Set();
        }
        catch (Exception)
        {
        }

        showSettings?.Dispose();
        return null;
    }

    /// <summary>
    /// Windows' edge snapping is Loadstone's to turn off only while Loadstone can actually replace
    /// it. <see cref="NativeSnap"/> suppresses because the setting says drag snapping is on;
    /// <see cref="DragSnapMonitor"/> gives up quietly when SetWinEventHook refuses the hook. Wired
    /// to the same setting and not to each other, that pair leaves a window dragged to an edge doing
    /// nothing at all - Windows' snap off because Loadstone turned it off, Loadstone's off because
    /// the hook never installed - which reads to the user as a broken Windows. So the suppression
    /// follows the hook rather than the setting: no hook, no suppression.
    /// </summary>
    private void SyncNativeSnapToDragMonitor()
    {
        if (_host is not { } host)
        {
            return;
        }

        var services = host.Services;
        var nativeSnap = services.GetRequiredService<NativeSnap>();
        if (services.GetRequiredService<ISettingsService>().Current.DragSnappingEnabled
            && !services.GetRequiredService<DragSnapMonitor>().IsMonitoring)
        {
            // Stop following first: a later Changed would otherwise put the flags straight back.
            nativeSnap.StopFollowing();
            if (nativeSnap.IsSuppressing)
            {
                _logger?.LogWarning(
                    "the move-size hook is not installed, so a dragged window cannot be snapped; giving Windows' own edge snapping back rather than leaving the user with neither");
                nativeSnap.Restore();
            }

            return;
        }

        // The hook is in, or drag snapping is off: NativeSnap follows the setting again. Follow does
        // nothing while it already does.
        nativeSnap.Follow();
    }

    /// <summary>
    /// Queued rather than run inline. NativeSnap's suppression and DragSnapMonitor's hook both move
    /// on this one event, and the order of an invocation list is not ours to depend on - the
    /// reconcile has to see the state both of them have finished settling.
    /// </summary>
    private void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(SyncNativeSnapToDragMonitor);

    /// <summary>
    /// A logoff or restart never reaches <see cref="OnExit"/>. The crash marker would put the flags
    /// back at the next launch, but a user who logs off and then uninstalls never gets that launch
    /// and keeps Windows' edge snapping off for good. Restore is idempotent, so the OnExit that may
    /// still follow costs nothing.
    /// </summary>
    private void RestoreOnSessionEnd(object? sender, SessionEndingEventArgs e)
    {
        BestEffort("restore Windows edge snapping", RestoreNativeSnap);
        NLog.LogManager.Flush(LogFlushTimeout);
    }

    /// <summary>Runs one teardown step; a failure is logged and the rest of the exit carries on.</summary>
    private void BestEffort(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "could not {Step} while exiting", what);
        }
    }

    /// <summary>Log as fatal, give Windows its edge snapping back on a best-effort basis, and let the process end.</summary>
    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Fatal(args.Exception, "unhandled exception on the UI thread");
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Fatal(args.ExceptionObject as Exception, "unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "unobserved task exception");
            args.SetObserved();
        };
    }

    /// <summary>
    /// The crash path's teardown: say what happened, give Windows its edge snapping back, and get
    /// the tail of the log onto disk before the process dies. Deliberately not
    /// <see cref="BestEffort"/> - that message is hard-coded to "while exiting" and a crash is not
    /// an exit.
    /// </summary>
    private void Fatal(Exception? ex, string message)
    {
        _logger?.LogCritical(ex, message);
        try
        {
            RestoreNativeSnap();
        }
        catch (Exception restoreFailed)
        {
            _logger?.LogError(restoreFailed, "could not restore Windows edge snapping while crashing");
        }

        NLog.LogManager.Flush(LogFlushTimeout);
    }

    /// <summary>
    /// Restores on the UI thread wherever it can still be reached. AppDomain.UnhandledException
    /// fires on the faulting thread and SessionEnding on the system-events thread, while Restore can
    /// reach ISettingsService.Changed and through it DragSnapMonitor.Uninstall, whose DispatcherTimer
    /// and SnapPreview are both thread-affine. No live path joins those two ends today, but only by
    /// the coincidence of two states that move independently, so this does not lean on it.
    /// </summary>
    /// <remarks>
    /// The handoff is bounded and falls back to the calling thread: the UI thread is often the one
    /// that has just died, and a restore on the wrong thread is worth more to the user than the
    /// edge snapping they would otherwise not get back until the next launch.
    /// </remarks>
    private void RestoreNativeSnap()
    {
        var restore = () => _host?.Services.GetService<NativeSnap>()?.Restore();
        if (Dispatcher.CheckAccess() || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            restore();
            return;
        }

        var handoff = Dispatcher.BeginInvoke(DispatcherPriority.Send, restore);
        if (handoff.Wait(RestoreHandoffTimeout) == DispatcherOperationStatus.Completed)
        {
            return;
        }

        // Abort succeeds only while the work has not started; anything else is already running on
        // the UI thread and must not be raced.
        if (handoff.Abort())
        {
            restore();
        }
    }
}
