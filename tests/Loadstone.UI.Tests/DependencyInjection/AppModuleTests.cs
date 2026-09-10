using Autofac;
using Loadstone.Core.Platform;
using Loadstone.UI.DependencyInjection;
using Loadstone.UI.Services;
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
}
