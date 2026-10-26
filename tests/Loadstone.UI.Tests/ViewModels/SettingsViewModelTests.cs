using System.IO;
using Loadstone.Core.Platform;
using Loadstone.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly InMemorySettings _settings = new();
    private readonly IStartupRegistration _startup = Substitute.For<IStartupRegistration>();
    private readonly List<string> _opened = [];
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "LoadstoneTests", Guid.NewGuid().ToString("N"));
    private string _logDirectory;
    private bool _shellOpens = true;
    private int _beeps;

    public SettingsViewModelTests()
    {
        _logDirectory = Path.Combine(_scratch, "Logs");
    }

    /// <summary>
    /// The internal constructor, so no test ever hands a URI to the shell: through the public one,
    /// OpenLogFolder starts Explorer on the real log folder, which is exactly what a test run must
    /// not do.
    /// </summary>
    private SettingsViewModel NewViewModel() => new(
        _settings,
        _startup,
        NullLogger<SettingsViewModel>.Instance,
        uri =>
        {
            _opened.Add(uri);
            return _shellOpens;
        },
        () => _beeps++,
        _logDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

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
    public void Construction_WhenSettingsCouldNotBeRead_SaysSo()
    {
        _settings.CanSave = false;
        NewViewModel().SettingsFileMessage.ShouldBe("Settings could not be read, so changes this session stay in memory and will not be saved.");
    }

    [Fact]
    public void DragSnapping_WhenTheSaveFails_SaysTheChangeWillNotLast()
    {
        _settings.SaveSucceeds = false;
        var vm = NewViewModel();
        vm.DragSnappingEnabled = true;
        vm.SettingsFileMessage.ShouldBe("Settings could not be saved; this change applies until Loadstone quits.");
        _settings.Current.DragSnappingEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Construction_ReadsRestoreOneMoveAtATime()
    {
        _settings.Current.RestoreOneMoveAtATime = true;
        NewViewModel().RestoreOneMoveAtATime.ShouldBeTrue();
    }

    [Fact]
    public void RestoreOneMoveAtATime_Change_PersistsImmediately()
    {
        var vm = NewViewModel();
        vm.RestoreOneMoveAtATime.ShouldBeFalse();
        vm.RestoreOneMoveAtATime = true;
        _settings.Current.RestoreOneMoveAtATime.ShouldBeTrue();
    }

    [Fact]
    public void DragSnappingEnabled_Change_PersistsImmediately()
    {
        var vm = NewViewModel();
        vm.DragSnappingEnabled = false;
        _settings.Current.DragSnappingEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// The packaged launch-at-login write is an out-of-process call and the dispatcher keeps pumping
    /// input for the whole of it; nothing disables the drag switch, which belongs to a different
    /// command. One flag doing both echo suppression and write re-entrancy turned that click into a
    /// no-op - the switch rendered on, settings.json was never written, and since the view model is a
    /// singleton that never re-reads the setting, the two stayed apart for the lifetime of the
    /// process. The gate holds the write open so the interleaving is the test and not a race.
    /// </summary>
    [Fact]
    public async Task DragSnappingEnabled_ChangedWhileALaunchAtLoginWriteIsInFlight_StillPersists()
    {
        var gate = new TaskCompletionSource<bool>();
        _startup.EnableAsync().Returns(gate.Task);
        var vm = NewViewModel();
        vm.DragSnappingEnabled.ShouldBeFalse();

        var inFlight = vm.SetLaunchAtLoginCommand.ExecuteAsync(true);
        vm.DragSnappingEnabled = true;

        _settings.Current.DragSnappingEnabled.ShouldBeTrue();

        gate.SetResult(true);
        await inFlight;
        _settings.Current.DragSnappingEnabled.ShouldBeTrue();
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
    /// job: re-read the registry, tell the view, and say so. The subject is the notification and not
    /// the value, because the value was never wrong - a refused enable leaves LaunchAtLogin exactly
    /// where it started, so asserting on it cannot fail, and only a PropertyChanged moves the switch
    /// back from where the click put it. Symptom if this regresses: the toggle stays on while nothing
    /// is registered.
    /// </summary>
    [Fact]
    public async Task LaunchAtLogin_WhenTheRegistryWriteFails_RollsBackAndExplains()
    {
        _startup.Status.Returns(StartupStatus.NotRegistered);
        _startup.EnableAsync().Returns(false);
        var vm = NewViewModel();
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);

        notified.ShouldContain(nameof(SettingsViewModel.LaunchAtLogin));
        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginActionMessage.ShouldContain("refused");
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
        vm.LaunchAtLoginActionMessage.ShouldBeEmpty();
    }

    /// <summary>
    /// Symptom if this regresses: the switch stays where the user put it while nothing was written,
    /// which is the exact silent lie the packaged Run key produced and this whole change exists to stop.
    /// Asserted as a notification for the reason given on
    /// <see cref="LaunchAtLogin_WhenTheRegistryWriteFails_RollsBackAndExplains"/> - the value never
    /// moves on a refused enable, so it is the notification that carries the correction to the view.
    /// </summary>
    [Fact]
    public async Task SetLaunchAtLogin_WhenTheWriteFails_RollsBackAndExplains()
    {
        _startup.EnableAsync().Returns(false);
        _startup.Status.Returns(StartupStatus.NotRegistered);
        var vm = NewViewModel();
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);

        notified.ShouldContain(nameof(SettingsViewModel.LaunchAtLogin));
        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginActionMessage.ShouldNotBeNullOrWhiteSpace();
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
    /// A sequenced <c>Status.Returns(a, b)</c> would not prove this - it hands out <c>b</c> on the
    /// second call regardless of whether InitializeAsync was awaited first, so it could pass even if
    /// RefreshStartup ran first. Instead, InitializeAsync itself is what flips Status to the new
    /// value: the assertion can only pass if the await actually happened before RefreshStartup read
    /// Status.
    /// </summary>
    [Fact]
    public async Task RefreshStartupAsync_ReInitializesBeforeReReadingStatus()
    {
        _startup.Status.Returns(StartupStatus.Enabled);
        _startup.When(x => x.InitializeAsync()).Do(_ => _startup.Status.Returns(StartupStatus.NotRegistered));
        var vm = NewViewModel();
        vm.LaunchAtLogin.ShouldBeTrue();

        await vm.RefreshStartupAsync();

        await _startup.Received(1).InitializeAsync();
        vm.LaunchAtLogin.ShouldBeFalse();
    }

    /// <summary>
    /// Reproduces the race that <see cref="SettingsViewModel.LaunchAtLoginActionMessage"/> exists to
    /// close: <see cref="SettingsViewModel.RefreshStartupAsync"/> runs on every Settings-open trigger,
    /// including a redundant one while the window is already showing, because nothing checks that.
    /// Before the split, that queued refresh recomputed the single message property from Status alone
    /// and blanked the write-failure explanation while nothing about the failure had changed - the
    /// toggle read correctly but the reason it did not take vanished. Symptom if this regresses: the
    /// explanation for a refused write disappears the next time Settings is reopened.
    /// </summary>
    [Fact]
    public async Task RefreshStartupAsync_AfterAFailedWrite_DoesNotEraseTheFailureMessage()
    {
        _startup.EnableAsync().Returns(false);
        _startup.Status.Returns(StartupStatus.NotRegistered);
        var vm = NewViewModel();
        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);
        vm.LaunchAtLoginActionMessage.ShouldNotBeNullOrWhiteSpace();

        await vm.RefreshStartupAsync();

        vm.LaunchAtLoginActionMessage.ShouldNotBeNullOrWhiteSpace();
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

    /// <summary>
    /// NLog creates the log directory on first write, so on a run where logging failed - exactly when
    /// a user is sent looking - it is not there yet. The command creates it first, so the shell has
    /// something to open rather than beeping. The path itself is the literal %LOCALAPPDATA%\Loadstone\Logs
    /// in both channels - MSIX does not redirect file-system writes for this package - so the button
    /// is a convenience, not the only honest way to name it.
    /// </summary>
    [Fact]
    public void OpenLogFolder_CreatesTheDirectory_ThenHandsItToTheShell()
    {
        Directory.Exists(_logDirectory).ShouldBeFalse();

        NewViewModel().OpenLogFolderCommand.Execute(null);

        Directory.Exists(_logDirectory).ShouldBeTrue();
        _opened.ShouldBe([_logDirectory]);
        _beeps.ShouldBe(0);
    }

    /// <summary>
    /// A creation that is refused must not escape: a throw from a button click on the UI thread ends
    /// the process and takes the hotkeys and the user's edge snapping with it. The shell still gets
    /// the path, and answers for itself.
    /// </summary>
    [Fact]
    public void OpenLogFolder_StillHandsThePathToTheShell_WhenTheDirectoryCannotBeCreated()
    {
        _logDirectory = Path.Combine(_scratch, "not<a>directory", "Logs");

        Should.NotThrow(() => NewViewModel().OpenLogFolderCommand.Execute(null));

        _opened.ShouldBe([_logDirectory]);
    }

    /// <summary>
    /// The beep used to be the whole failure report, on the theory that a tray-only app has nowhere
    /// to show one. These two buttons live on the General tab of a window that is on screen, so the
    /// refusal is also explained there, where a screen reader can announce it. The path is in the
    /// message because the folder is what the user was after.
    /// </summary>
    [Fact]
    public void OpenLogFolder_WhenTheShellRefuses_BeepsAndExplains()
    {
        _shellOpens = false;

        var vm = NewViewModel();
        vm.OpenLogFolderCommand.Execute(null);

        _beeps.ShouldBe(1);
        vm.ShellActionMessage.ShouldContain(_logDirectory);
    }

    [Fact]
    public void OpenStartupApps_WhenTheShellRefuses_BeepsAndExplains()
    {
        _shellOpens = false;

        var vm = NewViewModel();
        vm.OpenStartupAppsCommand.Execute(null);

        _beeps.ShouldBe(1);
        vm.ShellActionMessage.ShouldContain("Startup Apps");
    }

    /// <summary>A later attempt that works must not leave the earlier refusal on screen.</summary>
    [Fact]
    public void OpenLogFolder_WhenTheShellOpensIt_ClearsAnEarlierRefusal()
    {
        _shellOpens = false;
        var vm = NewViewModel();
        vm.OpenLogFolderCommand.Execute(null);
        vm.ShellActionMessage.ShouldNotBeNullOrWhiteSpace();

        _shellOpens = true;
        vm.OpenLogFolderCommand.Execute(null);

        vm.ShellActionMessage.ShouldBeEmpty();
    }

    /// <summary>
    /// Under a policy the write is refused by design, so the switch is disabled with the reason
    /// beside it rather than left to snap back after a click. Unknown stays enabled: the read
    /// failed, and a write may still tell the user something the read could not.
    /// </summary>
    [Theory]
    [InlineData(StartupStatus.DisabledByPolicy, false)]
    [InlineData(StartupStatus.EnabledByPolicy, false)]
    [InlineData(StartupStatus.NotRegistered, true)]
    [InlineData(StartupStatus.Enabled, true)]
    [InlineData(StartupStatus.DisabledInTaskManager, true)]
    [InlineData(StartupStatus.Unknown, true)]
    public void RefreshStartup_LetsTheUserChangeLaunchAtLogin_UnlessAPolicyDecidesIt(StartupStatus status, bool canChange)
    {
        _startup.Status.Returns(status);

        NewViewModel().CanChangeLaunchAtLogin.ShouldBe(canChange);
    }

    [Fact]
    public void OpenStartupApps_HandsTheSettingsUriToTheShell()
    {
        NewViewModel().OpenStartupAppsCommand.Execute(null);

        _opened.ShouldBe(["ms-settings:startupapps"]);
        _beeps.ShouldBe(0);
    }
}
