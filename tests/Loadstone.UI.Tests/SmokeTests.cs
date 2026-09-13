using System.Windows;
using System.Windows.Threading;
using Autofac;
using Autofac.Core;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.UI.DependencyInjection;
using Loadstone.UI.Tray;
using Loadstone.UI.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

public class SmokeTests
{
    [WpfFact]
    public void WpfFact_RunsOnStaThread_WithDispatcher()
    {
        Thread.CurrentThread.GetApartmentState().ShouldBe(ApartmentState.STA);
        Dispatcher.CurrentDispatcher.ShouldNotBeNull();
        var window = new Window { Width = 100, Height = 100, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.IsVisible.ShouldBeTrue();
        window.Close();
    }

    [Fact]
    public void TestHost_OptedInToPerMonitorV2Dpi_BeforeAnyWindowExisted()
    {
        TestSetup.IsPerMonitorAwareV2.ShouldBeTrue(
            "the test host could not opt in to Per-Monitor V2 DPI awareness, so every window frame it measures is in logical pixels; "
            + "expect the real-window tests to be wrong by the display's scale factor rather than to say anything about DPI");
    }

    /// <summary>
    /// Resolves everything the container knows how to build without a tray icon or the application's
    /// resource dictionaries. Nothing here asserts behaviour: it is the whole class of startup crash
    /// the container hides until launch - a service whose registration was never added, or a
    /// constructor that throws - caught in a second instead of by running the app.
    /// </summary>
    [WpfFact]
    public void AppModule_ResolvesEveryServiceThatDoesNotNeedAWindow()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new AppModule());

        // The generic host supplies logging in production; the module takes it as given.
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();

        // The two registrations that would otherwise reach the user's own state: the real settings
        // service reads %LOCALAPPDATA%\Loadstone\settings.json, and the real registration reads the
        // live Run key. Everything else below is the production registration, unchanged.
        builder.RegisterInstance(new InMemorySettings()).As<ISettingsService>();
        builder.RegisterInstance(Substitute.For<IStartupRegistration>()).As<IStartupRegistration>();

        using var container = builder.Build();

        // The one the harness cannot build: SettingsWindow needs the dictionaries App.xaml merges at
        // application level, and SettingsWindowTests supplies those and shows the real window.
        // TrayController is resolved here like everything else: its constructor builds the menu and
        // the icon object, and only Start() - which nothing here calls - puts the icon in the user's
        // tray and hooks the static SystemEvents.
        var cannotBeBuiltHere = new[] { typeof(SettingsWindow) };

        foreach (var serviceType in container.ComponentRegistry.Registrations
                     .SelectMany(registration => registration.Services)
                     .OfType<TypedService>()
                     .Select(typed => typed.ServiceType)
                     .Distinct())
        {
            if (cannotBeBuiltHere.Contains(serviceType))
            {
                continue;
            }

            container.Resolve(serviceType).ShouldNotBeNull($"{serviceType.Name} could not be resolved from AppModule");
        }
    }
}
