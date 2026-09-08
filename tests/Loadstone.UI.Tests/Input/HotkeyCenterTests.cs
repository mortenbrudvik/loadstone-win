using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class HotkeyCenterTests
{
    private readonly IHotkeyRegistrar _registrar = Substitute.For<IHotkeyRegistrar>();
    private readonly ICommandDispatcher _dispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IKeyboardLayoutProbe _probe = Substitute.For<IKeyboardLayoutProbe>();
    private readonly InMemorySettings _settings = new();

    public HotkeyCenterTests()
    {
        // Unregister is deliberately left unstubbed: a substitute answers default(HotkeyStatus), which
        // is Unbound, and that is what a command holding no chord should report.
        _registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        _probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
    }

    private HotkeyCenter NewCenter() => new(_registrar, _settings, _dispatcher, _probe, NullLogger<HotkeyCenter>.Instance);

    [Fact]
    public void Start_RegistersEveryCommandWithItsDefault()
    {
        NewCenter().Start();
        foreach (var command in WindowCommands.All)
        {
            _registrar.Received(1).Register(command, command.DefaultShortcut());
        }

        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void Start_Twice_RegistersOnce()
    {
        var center = NewCenter();
        center.Start();
        center.Start();
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void EffectiveShortcut_PrefersACustomization_AndTreatsEmptyAsUnbound()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        _settings.Current.Shortcuts["center"] = "";
        var center = NewCenter();

        center.EffectiveShortcut(WindowCommand.LeftHalf).ShouldBe(Shortcut.Parse("Ctrl+Alt+Shift+Left"));
        center.EffectiveShortcut(WindowCommand.Center).ShouldBeNull();
        center.EffectiveShortcut(WindowCommand.RightHalf).ShouldBe(WindowCommand.RightHalf.DefaultShortcut());
        center.IsCustomized(WindowCommand.LeftHalf).ShouldBeTrue();
        center.IsCustomized(WindowCommand.RightHalf).ShouldBeFalse();
    }

    [Fact]
    public void Start_UnboundCommand_IsUnregisteredAndReportedUnbound()
    {
        _settings.Current.Shortcuts["center"] = "";
        var center = NewCenter();
        center.Start();

        center.StatusOf(WindowCommand.Center).ShouldBe(HotkeyStatus.Unbound);
        _registrar.DidNotReceive().Register(WindowCommand.Center, Arg.Any<Shortcut>());
        _registrar.Received().Unregister(WindowCommand.Center);
    }

    [Fact]
    public void Start_StoredTextThatDoesNotParse_IsTreatedAsUnbound()
    {
        // The symptom: settings.json is hand-editable and survives upgrades, so junk like
        // "Ctrl+Nonsense" reaches the center as readily as anything the recorder wrote. Falling back to
        // the default would bind a chord the file says the user does not want, and letting the parse
        // throw would take the tray with it at startup.
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Nonsense";
        var center = NewCenter();
        center.Start();

        center.EffectiveShortcut(WindowCommand.LeftHalf).ShouldBeNull();
        center.StatusOf(WindowCommand.LeftHalf).ShouldBe(HotkeyStatus.Unbound);
        _registrar.DidNotReceive().Register(WindowCommand.LeftHalf, Arg.Any<Shortcut>());
        _registrar.Received().Unregister(WindowCommand.LeftHalf);
    }

    [Fact]
    public void Start_StoredTextWithoutAModifier_IsRejectedRatherThanRegistered()
    {
        // The symptom: "restore": "Backspace" parses perfectly well, and RegisterHotKey would then hand
        // Backspace to Loadstone system-wide, in every application, with no way left to type it -
        // including in the Settings window that would undo it. Only F1..F24 may go bare.
        _settings.Current.Shortcuts["restore"] = "Backspace";
        var center = NewCenter();
        center.Start();

        center.EffectiveShortcut(WindowCommand.Restore).ShouldBeNull();
        center.StatusOf(WindowCommand.Restore).ShouldBe(HotkeyStatus.Unbound);
        _registrar.DidNotReceive().Register(WindowCommand.Restore, Arg.Any<Shortcut>());
        _registrar.Received().Unregister(WindowCommand.Restore);
    }

    [Fact]
    public void Start_StoredTextThatIsABareFunctionKey_IsRegistered()
    {
        _settings.Current.Shortcuts["restore"] = "F13";
        var center = NewCenter();
        center.Start();

        center.EffectiveShortcut(WindowCommand.Restore).ShouldBe(Shortcut.Parse("F13"));
        _registrar.Received(1).Register(WindowCommand.Restore, Shortcut.Parse("F13"));
    }

    [Fact]
    public void Start_RecordsInUseStatus()
    {
        _registrar.Register(WindowCommand.TopLeft, WindowCommand.TopLeft.DefaultShortcut()).Returns(HotkeyStatus.InUse(1409));
        var center = NewCenter();
        center.Start();
        center.StatusOf(WindowCommand.TopLeft).State.ShouldBe(HotkeyState.InUse);
        center.StatusOf(WindowCommand.TopRight).State.ShouldBe(HotkeyState.Registered);
    }

    [Fact]
    public void Start_RecordsLayoutWarnings()
    {
        _probe.CharacterTypedBy(WindowCommand.LeftTwoThirds.DefaultShortcut()).Returns("€");
        var center = NewCenter();
        center.Start();
        center.LayoutWarning(WindowCommand.LeftTwoThirds).ShouldBe("€");
        center.LayoutWarning(WindowCommand.LeftHalf).ShouldBeNull();
    }

    [Fact]
    public void SetShortcut_PersistsAndReregistersThatCommand()
    {
        var center = NewCenter();
        center.Start();
        _registrar.ClearReceivedCalls();
        var raised = 0;
        center.BindingsChanged += (_, _) => raised++;

        var custom = Shortcut.Parse("Ctrl+Alt+Shift+Left");
        center.SetShortcut(WindowCommand.LeftHalf, custom);

        _settings.Current.Shortcuts["leftHalf"].ShouldBe("Ctrl+Alt+Shift+Left");
        _registrar.Received(1).Register(WindowCommand.LeftHalf, custom);
        center.EffectiveShortcut(WindowCommand.LeftHalf).ShouldBe(custom);
        raised.ShouldBe(1);
    }

    [Fact]
    public void SetShortcut_Null_ClearsTheBinding()
    {
        var center = NewCenter();
        center.Start();
        center.SetShortcut(WindowCommand.LeftHalf, null);
        _settings.Current.Shortcuts["leftHalf"].ShouldBe("");
        center.StatusOf(WindowCommand.LeftHalf).ShouldBe(HotkeyStatus.Unbound);
    }

    [Fact]
    public void SetShortcut_Null_WhenWindowsKeepsTheChord_ReportsNotReleased()
    {
        // Clearing a binding is not the same as being rid of it: UnregisterHotKey can refuse, and the
        // row has to say so rather than show the cheerful "unbound" of a chord that is still live.
        var center = NewCenter();
        center.Start();
        _registrar.Unregister(WindowCommand.LeftHalf).Returns(HotkeyStatus.NotReleased(5));

        center.SetShortcut(WindowCommand.LeftHalf, null);

        center.StatusOf(WindowCommand.LeftHalf).State.ShouldBe(HotkeyState.NotReleased);
        center.StatusOf(WindowCommand.LeftHalf).Win32Error.ShouldBe(5);
    }

    [Fact]
    public void SetShortcut_BackToTheDefault_RemovesTheCustomization()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        var center = NewCenter();
        center.Start();
        center.SetShortcut(WindowCommand.LeftHalf, WindowCommand.LeftHalf.DefaultShortcut());
        _settings.Current.Shortcuts.ContainsKey("leftHalf").ShouldBeFalse();
    }

    [Fact]
    public void ResetAll_ClearsEveryCustomization()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        _settings.Current.Shortcuts["center"] = "";
        var center = NewCenter();
        center.Start();
        center.ResetAll();
        _settings.Current.Shortcuts.ShouldBeEmpty();
        center.StatusOf(WindowCommand.Center).ShouldBe(HotkeyStatus.Registered);
    }

    [Fact]
    public void Suspend_UnregistersAll_AndResume_Reregisters()
    {
        var center = NewCenter();
        center.Start();
        _registrar.ClearReceivedCalls();

        center.Suspend();
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Unregister)).ShouldBe(WindowCommands.All.Count);
        _registrar.ClearReceivedCalls();

        center.Resume();
        _registrar.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register)).ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void Suspend_WhenWindowsRefusesToReleaseAChord_ReportsItNotReleased()
    {
        // The symptom: Suspend runs while the recorder is listening, so a chord Windows would not give
        // back is still live - pressing it moves a window instead of being recorded - and the row would
        // otherwise keep reading "registered" for the one command the user needs to see.
        _registrar.Unregister(WindowCommand.LeftHalf).Returns(HotkeyStatus.NotReleased(5));
        var center = NewCenter();
        center.Start();
        var raised = 0;
        center.BindingsChanged += (_, _) => raised++;

        center.Suspend();

        center.StatusOf(WindowCommand.LeftHalf).State.ShouldBe(HotkeyState.NotReleased);
        center.StatusOf(WindowCommand.LeftHalf).Win32Error.ShouldBe(5);
        center.StatusOf(WindowCommand.RightHalf).State.ShouldBe(HotkeyState.Registered);
        raised.ShouldBe(1);
    }

    [Fact]
    public void Suspend_WhenEveryChordComesBack_LeavesTheRowsAlone()
    {
        var center = NewCenter();
        center.Start();
        var raised = 0;
        center.BindingsChanged += (_, _) => raised++;

        center.Suspend();

        center.StatusOf(WindowCommand.LeftHalf).State.ShouldBe(HotkeyState.Registered);
        raised.ShouldBe(0);
    }

    [Fact]
    public void HotkeyPressed_RunsThatCommand()
    {
        var center = NewCenter();
        center.Start();

        _registrar.HotkeyPressed += Raise.Event<Action<WindowCommand>>(WindowCommand.LeftHalf);

        _dispatcher.Received(1).Run(WindowCommand.LeftHalf);
    }

    [Fact]
    public void HotkeyPressed_WhileSuspended_IsIgnored()
    {
        var center = NewCenter();
        center.Start();
        center.Suspend();
        _registrar.HotkeyPressed += Raise.Event<Action<WindowCommand>>(WindowCommand.LeftHalf);
        _dispatcher.DidNotReceive().Run(Arg.Any<WindowCommand>());
    }
}
