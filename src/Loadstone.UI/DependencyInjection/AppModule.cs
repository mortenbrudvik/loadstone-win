using Autofac;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.Core.Windows;
using Loadstone.UI.Input;
using Loadstone.UI.Services;
using Loadstone.UI.Tray;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Views;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.DependencyInjection;

public sealed class AppModule : Module
{
    private readonly IPackageIdentity _identity;

    /// <summary>
    /// The probe is passed in rather than resolved: this module is configuring the container, so it
    /// cannot ask it a question, and a test needs to answer the question both ways.
    /// </summary>
    internal AppModule(IPackageIdentity identity) => _identity = identity;

    public AppModule() : this(new PackageIdentity())
    {
    }

    protected override void Load(ContainerBuilder builder)
    {
        // Core
        builder.Register(c => new SettingsService(SettingsService.DefaultPath, c.Resolve<ILogger<SettingsService>>()))
            .As<ISettingsService>().SingleInstance();
        builder.RegisterType<SystemParametersNativeSnapSettings>().As<INativeSnapFlags>().SingleInstance();

        // The crash marker keeps its own file rather than a settings key: see INativeSnapMarker.
        builder.Register(c => new FileNativeSnapMarker(FileNativeSnapMarker.DefaultPath, c.Resolve<ILogger<FileNativeSnapMarker>>()))
            .As<INativeSnapMarker>().SingleInstance();
        builder.RegisterType<NativeSnap>().AsSelf().SingleInstance();

        // Registered as well as consulted: the launch log line names the channel, and resolving the
        // same instance keeps one answer to the question rather than a second probe in App.
        builder.RegisterInstance(_identity).As<IPackageIdentity>().SingleInstance();

        // The one place package identity changes what the app does. Inside a package the Run key
        // write lands in a private hive that Explorer never reads and still reports success, so the
        // wrong choice here is invisible at run time.
        if (_identity.IsPackaged)
        {
            builder.RegisterType<PackagedStartupRegistration>().As<IStartupRegistration>().SingleInstance();
        }
        else
        {
            builder.RegisterType<RegistryStartupRegistration>().As<IStartupRegistration>().SingleInstance();
        }

        // Displays.Source(logger), not the logger-less Snapshot(). Both abandon the whole list when a
        // monitor cannot be read, which turns every hotkey into a beep. Source logs that abandon
        // under WindowDirector, which is where a reader of the log looks for it.
        // The director reads the toggle when a command runs, and again on Changed: turning it off
        // reduces each window's remembered stack to its oldest frame, which has to happen even if
        // the toggle goes back on before the next command. The window stays where it is until
        // Restore. The handler does not write settings.
        builder.Register(c =>
        {
            var settings = c.Resolve<ISettingsService>();
            var director = new WindowDirector(
                Displays.Source(c.Resolve<ILogger<WindowDirector>>()),
                WindowLiveness.IsAlive,
                () => settings.Current.RestoreOneMoveAtATime);
            settings.Changed += (_, _) => director.ApplyRestoreMode();
            return director;
        }).AsSelf().SingleInstance();

        // Input
        builder.RegisterType<CommandDispatcher>().As<ICommandDispatcher>().SingleInstance();
        builder.RegisterType<HotkeyRegistrar>().As<IHotkeyRegistrar>().SingleInstance();
        builder.RegisterType<KeyboardLayoutProbe>().As<IKeyboardLayoutProbe>().SingleInstance();
        builder.RegisterType<HotkeyCenter>().AsSelf().SingleInstance();
        builder.RegisterType<WinEventMoveSizeEvents>().As<IMoveSizeEvents>().SingleInstance();
        builder.RegisterType<SnapPreview>().As<ISnapPreview>().SingleInstance();
        builder.RegisterType<DragSnapMonitor>().AsSelf().SingleInstance();

        // Shell
        builder.RegisterType<ShellActions>().AsSelf().SingleInstance();
        builder.RegisterType<TrayController>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutsViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsWindow>().AsSelf().SingleInstance();
    }
}
