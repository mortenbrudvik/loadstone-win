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

    /// <summary>
    /// The toggle drives a command rather than a property setter (see
    /// <see cref="SettingsViewModel.SetLaunchAtLoginCommand"/>), so exercising the on/off paths here
    /// goes through the command rather than assigning <see cref="SettingsViewModel.LaunchAtLogin"/>
    /// directly.
    /// </summary>
    [Fact]
    public async Task LaunchAtLogin_On_EnablesRegistration()
    {
        _startup.Status.Returns(StartupStatus.NotRegistered, StartupStatus.Enabled);
        _startup.EnableAsync().Returns(true);
        var vm = NewViewModel();
        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);
        await _startup.Received(1).EnableAsync();
        vm.LaunchAtLogin.ShouldBeTrue();
        vm.LaunchAtLoginMessage.ShouldBe("");
    }

    [Fact]
    public async Task LaunchAtLogin_Off_DisablesRegistration()
    {
        _startup.Status.Returns(StartupStatus.Enabled, StartupStatus.NotRegistered);
        _startup.DisableAsync().Returns(true);
        var vm = NewViewModel();
        await vm.SetLaunchAtLoginCommand.ExecuteAsync(false);
        await _startup.Received(1).DisableAsync();
        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldBe("");
    }

    /// <summary>
    /// The registry refusing the write is reported, not thrown, so the rollback is the view model's
    /// job: re-read the registry and say so. Symptom if this regresses: the toggle stays on while
    /// nothing is registered, and the user is told nothing.
    /// </summary>
    [Fact]
    public async Task LaunchAtLogin_WhenTheRegistryWriteFails_RollsBackAndExplains()
    {
        _startup.Status.Returns(StartupStatus.NotRegistered);
        _startup.EnableAsync().Returns(false);
        var vm = NewViewModel();

        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);

        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldContain("refused");
        await _startup.Received(1).EnableAsync();
    }

    /// <summary>
    /// The toggle drives a command rather than a property setter because the packaged implementation
    /// awaits a WinRT call, and blocking on one from the STA UI thread risks deadlock.
    /// </summary>
    [Fact]
    public async Task SetLaunchAtLogin_WhenTheWriteSucceeds_LeavesNoMessage()
    {
        _startup.EnableAsync().Returns(true);
        _startup.Status.Returns(StartupStatus.Enabled);
        var vm = NewViewModel();

        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);

        vm.LaunchAtLogin.ShouldBeTrue();
        vm.LaunchAtLoginMessage.ShouldBeEmpty();
    }

    /// <summary>
    /// Symptom if this regresses: the switch stays where the user put it while nothing was written,
    /// which is the exact silent lie the packaged Run key produced and this whole change exists to stop.
    /// </summary>
    [Fact]
    public async Task SetLaunchAtLogin_WhenTheWriteFails_RollsBackAndExplains()
    {
        _startup.EnableAsync().Returns(false);
        _startup.Status.Returns(StartupStatus.NotRegistered);
        var vm = NewViewModel();

        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);

        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(StartupStatus.DisabledByPolicy, false)]
    [InlineData(StartupStatus.EnabledByPolicy, true)]
    public void RefreshStartup_ForAPolicyState_TicksCorrectlyAndSaysWhy(StartupStatus status, bool ticked)
    {
        _startup.Status.Returns(status);
        var vm = NewViewModel();

        vm.LaunchAtLogin.ShouldBe(ticked);
        vm.LaunchAtLoginMessage.ShouldContain("policy");
    }

    /// <summary>
    /// PackagedStartupRegistration.Status is a snapshot taken during InitializeAsync, not a live
    /// query, so a user who disables Loadstone in Task Manager while it is running would still see
    /// the toggle switched on. Opening Settings is the point that has to re-fetch it; this proves the
    /// re-initialize happens before the status the window shows is read, not just that both happen.
    /// </summary>
    [Fact]
    public async Task RefreshStartupAsync_ReInitializesBeforeReReadingStatus()
    {
        _startup.Status.Returns(StartupStatus.Enabled, StartupStatus.NotRegistered);
        var vm = NewViewModel();
        vm.LaunchAtLogin.ShouldBeTrue();

        await vm.RefreshStartupAsync();

        await _startup.Received(1).InitializeAsync();
        vm.LaunchAtLogin.ShouldBeFalse();
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
