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
        _registrar.Register(Arg.Any<int>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        _probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
    }

    private HotkeyCenter NewCenter() => new(_registrar, _settings, _dispatcher, _probe, NullLogger<HotkeyCenter>.Instance);

    [Fact]
    public void Start_RegistersEveryCommandWithItsDefault()
    {
        NewCenter().Start();
        foreach (var command in WindowCommands.All)
        {
            _registrar.Received(1).Register(Arg.Any<int>(), command.DefaultShortcut());
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
        _registrar.DidNotReceive().Register(Arg.Any<int>(), WindowCommand.Center.DefaultShortcut());
    }

    [Fact]
    public void Start_RecordsInUseStatus()
    {
        _registrar.Register(Arg.Any<int>(), WindowCommand.TopLeft.DefaultShortcut()).Returns(HotkeyStatus.InUse(1409));
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
        _registrar.Received(1).Register(Arg.Any<int>(), custom);
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
    public void HotkeyPressed_RunsTheCommandWithThatId()
    {
        var center = NewCenter();
        center.Start();
        var leftHalfId = (int)_registrar.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IHotkeyRegistrar.Register) && Equals(c.GetArguments()[1], WindowCommand.LeftHalf.DefaultShortcut()))
            .GetArguments()[0]!;

        _registrar.HotkeyPressed += Raise.Event<Action<int>>(leftHalfId);

        _dispatcher.Received(1).Run(WindowCommand.LeftHalf);
    }

    [Fact]
    public void HotkeyPressed_WhileSuspended_IsIgnored()
    {
        var center = NewCenter();
        center.Start();
        center.Suspend();
        _registrar.HotkeyPressed += Raise.Event<Action<int>>(1);
        _dispatcher.DidNotReceive().Run(Arg.Any<WindowCommand>());
    }
}
