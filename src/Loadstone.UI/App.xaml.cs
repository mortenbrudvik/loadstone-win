using System.Windows;
using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.UI.DependencyInjection;
using Loadstone.UI.Input;
using Loadstone.UI.Services;
using Loadstone.UI.Tray;
using Loadstone.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Wpf.Ui.Appearance;

namespace Loadstone.UI;

public partial class App : Application
{
    /// <summary>
    /// How long to wait for the async file target to drain. The fatal handlers need it because an
    /// unhandled exception kills the process without running NLog's own shutdown flush, so the one
    /// line that explains the crash would sit in the buffer and be lost.
    /// </summary>
    private static readonly TimeSpan LogFlushTimeout = TimeSpan.FromSeconds(2);

    private IHost? _host;
    private Mutex? _mutex;
    private IDisposable? _showSettingsListener;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = SingleInstance.TryAcquire();
        if (_mutex is null)
        {
            // Another Loadstone is running: ask it to show Settings and leave.
            SingleInstance.SignalShowSettings();
            Shutdown();
            return;
        }

        // A bare HostBuilder, not Host.CreateDefaultBuilder: the app has no configuration, and the
        // default builder would read an appsettings.json out of whatever directory Loadstone was
        // launched from. What the hosting infrastructure is allowed to write to the log is decided in
        // LoggingSetup, because NLog does not honour the filters configured here.
        _host = new HostBuilder()
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureContainer<ContainerBuilder>(builder => builder.RegisterModule<AppModule>())
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddNLog(LoggingSetup.Build());
            })
            .Build();
        _host.Start();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        InstallExceptionHandlers();
        ApplicationThemeManager.ApplySystemTheme();

        var services = _host.Services;
        var settings = services.GetRequiredService<ISettingsService>();
        var nativeSnap = services.GetRequiredService<NativeSnap>();
        var shell = services.GetRequiredService<ShellActions>();
        shell.OpenSettingsAction = ShowSettings;
        shell.QuitAction = Shutdown;

        // Order matters, as on the Mac: native snap off before the first drag can happen, monitors last.
        nativeSnap.Follow();
        services.GetRequiredService<RegistryStartupRegistration>().RefreshPathIfStale();
        services.GetRequiredService<TrayController>();
        services.GetRequiredService<HotkeyCenter>().Start();
        var drag = services.GetRequiredService<DragSnapMonitor>();
        drag.Start();
        _showSettingsListener = SingleInstance.ListenForShowSettings(() => Dispatcher.BeginInvoke(ShowSettings));

        _logger.LogInformation(
            "launched: version {Version} dragSnapping={Drag} monitoring={Monitoring} nativeSnapSuppressed={Suppressed} startup={Startup}",
            typeof(App).Assembly.GetName().Version,
            settings.Current.DragSnappingEnabled,
            drag.IsMonitoring,
            nativeSnap.IsSuppressing,
            services.GetRequiredService<IStartupRegistration>().Status);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            var services = _host.Services;

            // First of all, so a second launch arriving during shutdown cannot ask for a window from a
            // container that is on its way out.
            BestEffort("stop listening for a second launch", () => _showSettingsListener?.Dispose());

            var nativeSnap = services.GetRequiredService<NativeSnap>();
            nativeSnap.StopFollowing();

            // Section 7 promises the user's edge snapping back after a quit, so nothing above
            // Restore may cost them that: each teardown step is best-effort and Restore is the
            // one call the finally guarantees.
            try
            {
                BestEffort("stop the drag monitor", () => services.GetRequiredService<DragSnapMonitor>().Stop());
                BestEffort("release the hotkeys", () => services.GetRequiredService<HotkeyCenter>().Dispose());
                BestEffort("close the hotkey window", () => services.GetRequiredService<IHotkeyRegistrar>().Dispose());
                BestEffort("remove the tray icon", () => services.GetRequiredService<TrayController>().Dispose());
            }
            finally
            {
                BestEffort("restore Windows edge snapping", nativeSnap.Restore);
                _logger?.LogInformation("exiting");
                NLog.LogManager.Flush(LogFlushTimeout);
            }

            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        NLog.LogManager.Shutdown();
        base.OnExit(e);
    }

    private void ShowSettings() => _host?.Services.GetRequiredService<SettingsWindow>().ShowAndActivate();

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
            _logger?.LogCritical(args.Exception, "unhandled exception on the UI thread");
            RestoreNativeSnapQuietly();
            NLog.LogManager.Flush(LogFlushTimeout);
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _logger?.LogCritical(args.ExceptionObject as Exception, "unhandled exception");
            RestoreNativeSnapQuietly();
            NLog.LogManager.Flush(LogFlushTimeout);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "unobserved task exception");
            args.SetObserved();
        };
    }

    private void RestoreNativeSnapQuietly()
    {
        try
        {
            _host?.Services.GetService<NativeSnap>()?.Restore();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "could not restore Windows edge snapping while crashing");
        }
    }
}
