using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.Interop;
using Loadstone.UI.Tests.Windows;
using Shouldly;
using Xunit;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Tests.Input;

/// <summary>
/// These drive the real RegisterHotKey against a chord no application is likely to own, because the
/// only interesting answers - Registered, InUse, and a chord that is genuinely gone again - come from
/// Windows itself and cannot be faked. Every registrar here is disposed, so no chord outlives the test.
/// </summary>
public class HotkeyRegistrarTests
{
    // Ctrl+Alt+Shift+Win+F23: a chord no application is likely to own.
    private static readonly Shortcut Obscure = new(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win, VirtualKeys.F1 + 22);

    [WpfFact]
    public void Register_FreeChord_IsRegistered()
    {
        using var registrar = new HotkeyRegistrar();
        registrar.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Register_ChordOwnedByAnotherRegistration_IsInUse()
    {
        using var first = new HotkeyRegistrar();
        using var second = new HotkeyRegistrar();
        first.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);

        var status = second.Register(WindowCommand.LeftHalf, Obscure);
        status.State.ShouldBe(HotkeyState.InUse);
        status.Win32Error.ShouldBe(1409);
    }

    [WpfFact]
    public void Unregister_FreesTheChord()
    {
        using var first = new HotkeyRegistrar();
        using var second = new HotkeyRegistrar();
        first.Register(WindowCommand.LeftHalf, Obscure);
        first.Unregister(WindowCommand.LeftHalf).ShouldBe(HotkeyStatus.Unbound);
        second.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Unregister_ACommandHoldingNothing_IsUnbound()
    {
        using var registrar = new HotkeyRegistrar();
        registrar.Unregister(WindowCommand.LeftHalf).ShouldBe(HotkeyStatus.Unbound);
    }

    [WpfFact]
    public void Register_SameCommandAgain_ReplacesTheChord()
    {
        using var registrar = new HotkeyRegistrar();
        using var other = new HotkeyRegistrar();
        registrar.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);
        registrar.Register(WindowCommand.LeftHalf, Obscure with { VirtualKey = VirtualKeys.F1 + 21 }).ShouldBe(HotkeyStatus.Registered);
        other.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Register_TwoCommands_HoldTwoChordsAtOnce()
    {
        // The command is the key now, so two commands must not collide the way two callers sharing an
        // id would: releasing one may not take the other's chord with it.
        using var registrar = new HotkeyRegistrar();
        using var other = new HotkeyRegistrar();
        var second = Obscure with { VirtualKey = VirtualKeys.F1 + 23 };
        registrar.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);
        registrar.Register(WindowCommand.RightHalf, second).ShouldBe(HotkeyStatus.Registered);

        registrar.Unregister(WindowCommand.LeftHalf).ShouldBe(HotkeyStatus.Unbound);

        other.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);
        other.Register(WindowCommand.RightHalf, second).State.ShouldBe(HotkeyState.InUse);
    }

    [WpfFact]
    public void Register_APostedHotkey_RaisesHotkeyPressedForThatCommand()
    {
        using var registrar = new HotkeyRegistrar();
        WindowCommand? raised = null;
        registrar.HotkeyPressed += command => raised = command;
        registrar.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);

        User32.PostMessage(registrar.Handle, NativeConstants.WM_HOTKEY, (nint)WindowCommand.LeftHalf, 0).ShouldBeTrue();
        TestWindow.Pump();
        raised.ShouldBe(WindowCommand.LeftHalf);

        raised = null;
        User32.PostMessage(registrar.Handle, NativeConstants.WM_HOTKEY, 999, 0).ShouldBeTrue();
        TestWindow.Pump();
        raised.ShouldBeNull();
    }

    [WpfFact]
    public void Register_WhenTheOldChordCannotBeReleased_DoesNotTakeTheNewOne()
    {
        using var registrar = new HotkeyRegistrar();
        using var other = new HotkeyRegistrar();
        registrar.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);

        // Drop it in Windows without telling the registrar, so UnregisterHotKey fails while the
        // registrar still believes it holds the id. Registering anyway would report the new chord
        // and, on Vista and later, leave the old one live as well.
        User32.UnregisterHotKey(registrar.Handle, (int)WindowCommand.LeftHalf).ShouldBeTrue();

        var replacement = Obscure with { VirtualKey = VirtualKeys.F1 + 21 };
        var status = registrar.Register(WindowCommand.LeftHalf, replacement);
        status.State.ShouldBe(HotkeyState.NotReleased);

        other.Register(WindowCommand.LeftHalf, replacement).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Dispose_FreesEveryChord()
    {
        var registrar = new HotkeyRegistrar();
        registrar.Register(WindowCommand.LeftHalf, Obscure);
        registrar.Dispose();
        using var other = new HotkeyRegistrar();
        other.Register(WindowCommand.LeftHalf, Obscure).ShouldBe(HotkeyStatus.Registered);
    }
}
