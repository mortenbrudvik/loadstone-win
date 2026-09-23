using Autofac;
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.Core.Windows;
using Loadstone.UI.DependencyInjection;
using Loadstone.UI.Services;
using Loadstone.UI.Tests.Windows;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.DependencyInjection;

public sealed class AppModuleTests
{
    /// <summary>
    /// The one decision package identity makes. Getting it backwards is silent in both directions:
    /// the registry implementation inside a package writes to a hive Explorer never reads, and the
    /// packaged implementation outside one has no startup task to fetch.
    /// </summary>
    [Theory]
    [InlineData(false, typeof(RegistryStartupRegistration))]
    [InlineData(true, typeof(PackagedStartupRegistration))]
    public void Load_RegistersTheStartupImplementation_ThatMatchesPackageIdentity(bool packaged, Type expected)
    {
        var identity = Substitute.For<IPackageIdentity>();
        identity.IsPackaged.Returns(packaged);

        var builder = new ContainerBuilder();
        builder.RegisterModule(new AppModule(identity));

        // The generic host supplies logging in production; the module takes it as given.
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();

        using var container = builder.Build();

        container.Resolve<IStartupRegistration>().ShouldBeOfType(expected);
    }

    /// <summary>
    /// The probe is registered, not just consulted: the launch log names the channel, and a second
    /// <c>new PackageIdentity()</c> in App would put a second answer to the same question in the app.
    /// </summary>
    [Fact]
    public void Load_RegistersThePackageIdentityProbe_SoTheLaunchLineCanNameTheChannel()
    {
        var identity = Substitute.For<IPackageIdentity>();
        identity.IsPackaged.Returns(true);

        var builder = new ContainerBuilder();
        builder.RegisterModule(new AppModule(identity));
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();

        using var container = builder.Build();

        container.Resolve<IPackageIdentity>().ShouldBeSameAs(identity);
    }

    /// <summary>
    /// Turning the toggle off has to reach the director immediately. Perform also re-reads the
    /// setting, so a missing subscription still steps back on the next command; what it cannot do
    /// is collapse when the toggle goes off and back on with no command in between.
    /// </summary>
    [WpfFact]
    public void Load_DirectorFollowsRestoreOneMoveAtATime()
    {
        var settings = new InMemorySettings();
        var identity = Substitute.For<IPackageIdentity>();
        identity.IsPackaged.Returns(false);

        var builder = new ContainerBuilder();
        builder.RegisterModule(new AppModule(identity));
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(settings).As<ISettingsService>();

        using var container = builder.Build();
        // Before any Update: a failed override here would be the real settings file.
        container.Resolve<ISettingsService>().ShouldBeSameAs(settings);

        // A fake handle is pruned as dead before the next command, which would make Restore look
        // empty for a reason that has nothing to do with the setting. A real window stays alive.
        using var test = new TestWindow();
        var window = Win32Window.For(test.Handle).ShouldNotBeNull();
        var director = container.Resolve<WindowDirector>();
        var start = window.ReadFrame().ShouldNotBeNull();

        settings.Update(s => s.RestoreOneMoveAtATime = true);
        director.Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();
        var left = window.ReadFrame().ShouldNotBeNull();

        director.Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();
        LandedOn(window.ReadFrame(), left);

        settings.Update(s => s.RestoreOneMoveAtATime = false);
        settings.Update(s => s.RestoreOneMoveAtATime = true);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();
        LandedOn(window.ReadFrame(), start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    private static void LandedOn(Rect? actual, Rect expected)
    {
        var landed = actual.ShouldNotBeNull();
        Math.Abs(landed.X - expected.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Y - expected.Y).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Width - expected.Width).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Height - expected.Height).ShouldBeLessThanOrEqualTo(1);
    }
}
