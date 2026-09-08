using Loadstone.Core.Platform;
using Loadstone.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Windows.ApplicationModel;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public sealed class PackagedStartupRegistrationTests
{
    /// <summary>
    /// The whole packaged path reduces to this mapping plus three WinRT calls that need a real
    /// package. DisabledByUser deliberately lands on DisabledInTaskManager, which the Settings view
    /// already renders with an "Open Startup Apps" button — the states mean the same thing to a user.
    /// </summary>
    [Theory]
    [InlineData(StartupTaskState.Disabled, StartupStatus.NotRegistered)]
    [InlineData(StartupTaskState.Enabled, StartupStatus.Enabled)]
    [InlineData(StartupTaskState.DisabledByUser, StartupStatus.DisabledInTaskManager)]
    [InlineData(StartupTaskState.DisabledByPolicy, StartupStatus.DisabledByPolicy)]
    [InlineData(StartupTaskState.EnabledByPolicy, StartupStatus.EnabledByPolicy)]
    public void Map_TranslatesEveryStartupTaskState(StartupTaskState state, StartupStatus expected)
    {
        PackagedStartupRegistration.Map(state).ShouldBe(expected);
    }

    /// <summary>
    /// Status before InitializeAsync must be Unknown, not NotRegistered: initialization is queued
    /// onto the dispatcher and the Settings view model can be built before it lands. A ticked box the
    /// user cannot untick is a worse lie than an unticked one, and NotRegistered would claim the app
    /// is deliberately off when nothing has been read yet.
    /// </summary>
    [Fact]
    public void Status_BeforeInitialize_IsUnknown()
    {
        new PackagedStartupRegistration(NullLogger<PackagedStartupRegistration>.Instance).Status
            .ShouldBe(StartupStatus.Unknown);
    }

    /// <summary>The TaskId must match the manifest's uap5:StartupTask TaskId exactly or GetAsync returns null.</summary>
    [Fact]
    public void TaskId_MatchesTheManifest()
    {
        PackagedStartupRegistration.TaskId.ShouldBe("LoadstoneStartup");
    }
}
