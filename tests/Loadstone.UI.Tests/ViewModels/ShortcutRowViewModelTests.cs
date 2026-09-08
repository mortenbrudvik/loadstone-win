using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.ViewModels;

public class ShortcutRowViewModelTests
{
    private readonly IHotkeyRegistrar _registrar = Substitute.For<IHotkeyRegistrar>();
    private readonly IKeyboardLayoutProbe _probe = Substitute.For<IKeyboardLayoutProbe>();
    private readonly InMemorySettings _settings = new();
    private readonly HotkeyCenter _hotkeys;

    public ShortcutRowViewModelTests()
    {
        _registrar.Register(Arg.Any<int>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        _probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        _hotkeys = new HotkeyCenter(_registrar, _settings, Substitute.For<ICommandDispatcher>(), _probe, NullLogger<HotkeyCenter>.Instance);
        _hotkeys.Start();
    }

    [Fact]
    public void Construction_ShowsTheEffectiveShortcut()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        row.Title.ShouldBe("Left Half");
        row.Shortcut.ShouldBe(WindowCommand.LeftHalf.DefaultShortcut());
        row.Status.ShouldBe("");
    }

    [Fact]
    public void SettingTheShortcut_PersistsThroughTheHotkeyCenter()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        var custom = Shortcut.Parse("Ctrl+Alt+Shift+Left");
        row.Shortcut = custom;
        _settings.Current.Shortcuts["leftHalf"].ShouldBe("Ctrl+Alt+Shift+Left");
        _hotkeys.EffectiveShortcut(WindowCommand.LeftHalf).ShouldBe(custom);
    }

    [Fact]
    public void ClearingTheShortcut_Unbinds()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        row.Shortcut = null;
        _settings.Current.Shortcuts["leftHalf"].ShouldBe("");
    }

    [Fact]
    public void Recording_SuspendsAndResumesHotkeys()
    {
        var row = new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys);
        _registrar.ClearReceivedCalls();

        row.IsRecording = true;
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Unregister)).ShouldBe(WindowCommands.All.Count);
        _registrar.ClearReceivedCalls();

        row.IsRecording = false;
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void Status_ReportsInUse_Failed_AndLayoutWarnings()
    {
        _registrar.Register(Arg.Any<int>(), WindowCommand.TopLeft.DefaultShortcut()).Returns(HotkeyStatus.InUse(1409));
        _registrar.Register(Arg.Any<int>(), WindowCommand.TopRight.DefaultShortcut()).Returns(HotkeyStatus.Failed(87));
        _probe.CharacterTypedBy(WindowCommand.LeftTwoThirds.DefaultShortcut()).Returns("€");
        _hotkeys.ResetAll();

        new ShortcutRowViewModel(WindowCommand.TopLeft, _hotkeys).Status.ShouldBe("In use by another app");
        new ShortcutRowViewModel(WindowCommand.TopRight, _hotkeys).Status.ShouldBe("Could not register (error 87)");
        new ShortcutRowViewModel(WindowCommand.LeftTwoThirds, _hotkeys).Status.ShouldBe("Types € on your keyboard layout");
    }

    [Fact]
    public void Status_WhenAnotherRowHoldsTheSameChord_NamesThatRow()
    {
        var shared = WindowCommand.RightHalf.DefaultShortcut();
        _registrar.Register(Arg.Any<int>(), shared).Returns(HotkeyStatus.InUse(1409));
        _hotkeys.SetShortcut(WindowCommand.LeftHalf, shared);

        new ShortcutRowViewModel(WindowCommand.LeftHalf, _hotkeys).Status.ShouldBe("Also bound to Right Half");
    }

    /// <summary>
    /// The recorder commits the chord while the center is still suspended, so the status recorded then is
    /// the *old* chord's. Only Resume() registers the new one, and the row has to re-read it: this is the
    /// spec 1.2 "in use by another app" caption for a chord recorded in the Settings window.
    /// </summary>
    [Fact]
    public void CommittingAChordWhileRecording_ShowsTheRegistrationStatusOnceRecordingStops()
    {
        var vm = new ShortcutsViewModel(_hotkeys);
        var row = vm.Groups.SelectMany(g => g.Rows).First(r => r.Command == WindowCommand.LeftHalf);
        var taken = Shortcut.Parse("Ctrl+Alt+Shift+Left");
        _registrar.Register(Arg.Any<int>(), taken).Returns(HotkeyStatus.InUse(1409));

        row.IsRecording = true;
        row.Shortcut = taken;
        row.Status.ShouldBe("");

        row.IsRecording = false;

        row.Status.ShouldBe("In use by another app");
    }

    [Fact]
    public void ShortcutsViewModel_GroupsRowsBySection_AndRestoresDefaults()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        var vm = new ShortcutsViewModel(_hotkeys);
        vm.Groups.Select(g => g.Header).ShouldBe(["Halves", "Corners", "Thirds", "Window", "Displays"]);
        vm.Groups.Sum(g => g.Rows.Count).ShouldBe(WindowCommands.All.Count);

        vm.RestoreDefaultsCommand.Execute(null);
        _settings.Current.Shortcuts.ShouldBeEmpty();
        vm.Groups[0].Rows[0].Shortcut.ShouldBe(WindowCommand.LeftHalf.DefaultShortcut());
    }
}
