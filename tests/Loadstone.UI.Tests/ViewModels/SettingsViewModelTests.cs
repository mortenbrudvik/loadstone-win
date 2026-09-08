using Loadstone.Core.Platform;
using Loadstone.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly InMemorySettings _settings = new();
    private readonly IStartupRegistration _startup = Substitute.For<IStartupRegistration>();

    private SettingsViewModel NewViewModel() => new(_settings, _startup, NullLogger<SettingsViewModel>.Instance);

    [Fact]
    public void Construction_ReadsTheDragSettingAndStartupStatus()
    {
        _settings.Current.DragSnappingEnabled = false;
        _startup.Status.Returns(StartupStatus.Enabled);
        var vm = NewViewModel();
        vm.DragSnappingEnabled.ShouldBeFalse();
        vm.LaunchAtLogin.ShouldBeTrue();
        vm.ShowOpenStartupApps.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldBe("");
    }

    [Fact]
    public void DragSnappingEnabled_Change_PersistsImmediately()
    {
        var vm = NewViewModel();
        vm.DragSnappingEnabled = false;
        _settings.Current.DragSnappingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void LaunchAtLogin_On_EnablesRegistration()
    {
        _startup.Status.Returns(StartupStatus.NotRegistered, StartupStatus.Enabled);
        _startup.EnableAsync().Returns(true);
        var vm = NewViewModel();
        vm.LaunchAtLogin = true;
        _startup.Received(1).EnableAsync();
        vm.LaunchAtLogin.ShouldBeTrue();
        vm.LaunchAtLoginMessage.ShouldBe("");
    }

    [Fact]
    public void LaunchAtLogin_Off_DisablesRegistration()
    {
        _startup.Status.Returns(StartupStatus.Enabled, StartupStatus.NotRegistered);
        _startup.DisableAsync().Returns(true);
        var vm = NewViewModel();
        vm.LaunchAtLogin = false;
        _startup.Received(1).DisableAsync();
        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldBe("");
    }

    /// <summary>
    /// The registry refusing the write is reported, not thrown, so the rollback is the view model's
    /// job: re-read the registry and say so. Symptom if this regresses: the toggle stays on while
    /// nothing is registered, and the user is told nothing.
    /// </summary>
    [Fact]
    public void LaunchAtLogin_WhenTheRegistryWriteFails_RollsBackAndExplains()
    {
        _startup.Status.Returns(StartupStatus.NotRegistered);
        _startup.EnableAsync().Returns(false);
        var vm = NewViewModel();

        vm.LaunchAtLogin = true;

        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldContain("refused");
        _startup.Received(1).EnableAsync();
    }

    [Fact]
    public void DisabledInTaskManager_ShowsTheMessageAndTheButton()
    {
        _startup.Status.Returns(StartupStatus.DisabledInTaskManager);
        var vm = NewViewModel();
        vm.LaunchAtLogin.ShouldBeTrue();
        vm.ShowOpenStartupApps.ShouldBeTrue();
        vm.LaunchAtLoginMessage.ShouldContain("Task Manager");
    }

    /// <summary>
    /// A policy or an ACL can deny the Run key outright, and this constructor runs on the UI thread
    /// with no handler over it. Symptom if the read ever throws again: opening Settings from the tray
    /// ends the process, taking the hotkeys and the user's edge snapping with it. Unknown is not off,
    /// so the box stays clear - a ticked box the user cannot untick is the worse lie.
    /// </summary>
    [Fact]
    public void Construction_WhenTheRegistryCannotBeRead_LeavesTheWindowUsable()
    {
        _startup.Status.Returns(StartupStatus.Unknown);

        var vm = Should.NotThrow(NewViewModel);

        vm.LaunchAtLogin.ShouldBeFalse();
        vm.ShowOpenStartupApps.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldContain("Could not read");
    }

    /// <summary>
    /// The About card shows a version, not a commit: the informational version carries a "+sha" suffix
    /// on any build with source link, and the split is the only thing that keeps it off the card.
    /// </summary>
    [Fact]
    public void Version_IsTheInformationalVersionWithoutItsBuildMetadata()
    {
        var version = NewViewModel().Version;

        version.ShouldNotBeNullOrWhiteSpace();
        version.ShouldNotContain("+");
        version.ShouldNotBe("?");
    }
}
