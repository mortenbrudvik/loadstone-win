using System.IO;
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
using Loadstone.UI.ViewModels;
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

        // NLog's own log, before the handshake below and long before the host: it is the one channel
        // that needs no logger, and the handshake is the one place a failure used to leave no trace.
        LoggingSetup.EnableInternalLog();

        var showSettings = TryBecomeTheOnlyInstance();
        if (showSettings is null)
        {
            Shutdown();
            return;
        }

        // Before the host, so it covers the part of startup that takes time; after the single-instance
        // handshake, so a second launch that only asks the first to open Settings never flashes it. There
        // is no logger yet, so a failure is held here and written once there is, and startup goes on
        // without the splash.
        StartupSplash? splash = null;
        Exception? splashFailure = null;
        try
        {
            splash = StartupSplash.Show();
        }
        catch (Exception ex)
        {
            splashFailure = ex;
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
        if (splashFailure is not null)
        {
            _logger.LogWarning(splashFailure, "could not show the splash screen; starting without it");
        }

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

        // Order matters, as on the Mac: whatever Follow() settles on has to be in place before the
        // first drag can happen, monitors last. Not unconditional suppression - DragSnappingEnabled
        // defaults to false, so on an ordinary first run Follow() turns nothing off and only puts back
        // what an unclean exit left off. It is the run where consent is already given that needs this
        // order.
        nativeSnap.Follow();
        services.GetRequiredService<TrayController>().Start();
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
            "launched: version {Version} path={Path} packaged={Packaged} dragSnapping={Drag} monitoring={Monitoring} nativeSnapSuppressed={Suppressed}",
            typeof(App).Assembly.GetName().Version,
            Environment.ProcessPath,
            services.GetRequiredService<IPackageIdentity>().IsPackaged,
            settings.Current.DragSnappingEnabled,
            drag.IsMonitoring,
            nativeSnap.IsSuppressing);

        // Flush it, then look for it. Everything the user could ever be told about this app goes
        // through NLog, and NLog reports nothing when it cannot write, so the answer cannot come
        // back this way: VerifyWritable puts it in NLog's internal log under the temp directory,
        // which is the one place a later diagnosis can read it from.
        NLog.LogManager.Flush(LogFlushTimeout);
        LoggingSetup.VerifyWritable(logLengthBeforeLaunch);

        // Package identity has to be asked over an asynchronous WinRT call and OnStartup cannot await:
        // the dispatcher does not pump until it returns (see the single-instance note above), so an
        // await here would let a queued second launch build Settings from a half-finished container.
        // Queue it instead. The Settings window is only ever built on a user action, which cannot
        // happen before this lands.
        var splashClosed = splash?.CloseAsync(_logger) ?? Task.CompletedTask;
        Defer(() => InitializeStartupAsync(services, settings, splashClosed));
    }

    /// <summary>
    /// Queues asynchronous work on the dispatcher and gives a fault the treatment a synchronous throw
    /// on the UI thread gets: the async lambda posts its exception back to the dispatcher, where
    /// <see cref="DispatcherUnhandledException"/> logs it as fatal, restores the snap flags and lets
    /// the process end. The discarded-task idiom this replaces carried the fault to
    /// <see cref="TaskScheduler.UnobservedTaskException"/>, which fires from the finalizer - so a
    /// first-run Settings failure was logged only if the GC happened to run before exit, and usually
    /// not at all.
    /// </summary>
    private void Defer(Func<Task> work) => Dispatcher.BeginInvoke(async () => await work());

    /// <summary>
    /// The deferred half of startup. Not everything below is guaranteed not to throw:
    /// <see cref="PackagedStartupRegistration.InitializeAsync"/> catches broadly by design, but
    /// <see cref="RegistryStartupRegistration.InitializeAsync"/> only catches registry refusals and
    /// lets a genuine bug through, and <see cref="ShowSettings"/> builds a window. A throw here
    /// reaches the dispatcher's fatal policy through <see cref="Defer"/>, the same as it would from a
    /// tray click.
    /// </summary>
    private async Task InitializeStartupAsync(IServiceProvider services, ISettingsService settings, Task splashClosed)
    {
        var startup = services.GetRequiredService<IStartupRegistration>();
        await startup.InitializeAsync();
        services.GetRequiredService<ILogger<App>>().LogInformation("launch at login: {Startup}", startup.Status);

        // First run opens Settings so the drag-snapping toggle and its explanation are in front of the
        // user before anything changes a Windows setting — and so a tray-only app has a window a Store
        // reviewer can reach from the Start menu. Here rather than in OnStartup, so the launch-at-login
        // row shows a state that has actually been read. ShowSettings uses Show(), never ShowDialog():
        // a modal would run a nested dispatcher loop and defeat the ordering OnStartup depends on.
        if (settings.IsFirstRun)
        {
            // Once the splash has gone: it is topmost, and would otherwise sit over the very window it
            // hands over to. StartupSplash.CloseAsync never faults, so this cannot strand the first run.
            await splashClosed;
            ShowSettings();
        }
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

            // Spec 7.1, "Windows edge snapping", promises the user's edge snapping back after a quit, so nothing above
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

            // Dispose only. Nothing is hosted, so there is nothing for StopAsync to stop, and it was
            // the one blocking wait on the UI thread in the tree: safe while Hosting awaits with
            // ConfigureAwait(false) and no hosted service marshals back here, and a wait for
            // HostOptions.ShutdownTimeout the day one does. Wrapped as well: a throw here used to lose
            // everything after it, including the buffered tail of the log - the part most likely to
            // explain the throw.
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

    /// <summary>
    /// Queues a re-read of the packaged startup status before showing the window. Resolved from the
    /// container rather than through the window, because the queued refresh must reach the same
    /// singleton <see cref="SettingsViewModel"/> instance the window is bound to, not a copy of it -
    /// see <see cref="SettingsViewModel.RefreshStartupAsync"/> for why the re-read exists at all.
    /// Queued through <see cref="Defer"/>, like <see cref="InitializeStartupAsync"/>: showing the
    /// window must stay synchronous. On a single open, the queued refresh lands before the user can
    /// touch the toggle, since nothing pumps the dispatcher between here and
    /// <see cref="SettingsWindow.ShowAndActivate"/> returning - but nothing here checks whether the
    /// window is already showing, so a second trigger (a tray click while Settings is already open)
    /// queues a second refresh on top of whatever the user is doing. That is exactly why
    /// <see cref="SettingsViewModel.LaunchAtLoginActionMessage"/> is a separate slot from
    /// <see cref="SettingsViewModel.LaunchAtLoginMessage"/>: a redundant refresh here must not be able
    /// to erase feedback about a write the user just made.
    /// </summary>
    private void ShowSettings()
    {
        if (_host is not { } host)
        {
            return;
        }

        var services = host.Services;
        var settings = services.GetRequiredService<SettingsViewModel>();
        Defer(settings.RefreshStartupAsync);
        services.GetRequiredService<SettingsWindow>().ShowAndActivate();
    }

    /// <summary>
    /// Takes the single-instance mutex and returns the event to listen on, or asks the Loadstone
    /// that already holds it to show Settings and returns null, meaning this process must leave.
    /// </summary>
    /// <remarks>
    /// The handshake is guarded because both halves of it open kernel objects by name, and both throw
    /// UnauthorizedAccessException when an instance running at another integrity level owns them with
    /// a DACL this process may not open for full control. There is nothing the user could act on, so
    /// a refused handshake leaves quietly rather than running a second tiler over the first one's
    /// hotkeys - but not silently: there is no logger yet, so the refusal goes to NLog's internal
    /// log, enabled before this runs, where a later diagnosis of "Loadstone refuses to start" can
    /// find it. Only the documented refusals are caught; a genuine bug still crashes visibly.
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
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            NLog.Common.InternalLogger.Error(ex, "the single-instance handshake was refused; this launch is leaving without starting. An instance running at another integrity level (an elevated one, say) owns the named mutex or event.");
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
