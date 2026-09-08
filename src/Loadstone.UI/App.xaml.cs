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

        _host = Host.CreateDefaultBuilder()
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
        var dragSnapping = settings.Current.DragSnappingEnabled;
        nativeSnap.Apply(dragSnapping);

        // Follow the toggle only when it actually flips. Every NativeSnap call parks or clears its
        // marker in settings, which raises Changed; reacting to that would re-enter Apply and
        // re-suppress the flags Restore had just put back, on exit and while crashing alike.
        settings.Changed += (_, _) =>
        {
            if (settings.Current.DragSnappingEnabled == dragSnapping)
            {
                return;
            }

            dragSnapping = settings.Current.DragSnappingEnabled;
            nativeSnap.Apply(dragSnapping);
        };
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
            services.GetRequiredService<DragSnapMonitor>().Stop();
            services.GetRequiredService<HotkeyCenter>().Dispose();
            services.GetRequiredService<IHotkeyRegistrar>().Dispose();
            services.GetRequiredService<NativeSnap>().Restore();
            services.GetRequiredService<TrayController>().Dispose();
            _showSettingsListener?.Dispose();
            _logger?.LogInformation("exiting");
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        NLog.LogManager.Shutdown();
        base.OnExit(e);
    }

    private void ShowSettings() => _host?.Services.GetRequiredService<SettingsWindow>().ShowAndActivate();

    /// <summary>Log as fatal, give Windows its edge snapping back on a best-effort basis, and let the process end.</summary>
    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.LogCritical(args.Exception, "unhandled exception on the UI thread");
            RestoreNativeSnapQuietly();
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _logger?.LogCritical(args.ExceptionObject as Exception, "unhandled exception");
            RestoreNativeSnapQuietly();
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
