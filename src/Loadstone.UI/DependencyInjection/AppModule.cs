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
        builder.RegisterType<RegistryStartupRegistration>().As<IStartupRegistration>().SingleInstance();
        // Displays.Source and not Displays.Snapshot: the snapshot abandons the whole list when a
        // monitor cannot be read, which turns every hotkey into a beep, and a source bound to no
        // logger would leave the log the user is told to read with nothing in it about why.
        builder.Register(c => new WindowDirector(Displays.Source(c.Resolve<ILogger<WindowDirector>>()), WindowLiveness.IsAlive))
            .AsSelf().SingleInstance();

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
