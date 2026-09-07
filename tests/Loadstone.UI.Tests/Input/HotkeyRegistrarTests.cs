using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class HotkeyRegistrarTests
{
    // A chord no application is likely to own.
    private static readonly Shortcut Obscure = new(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win, VirtualKeys.F1 + 22);

    [WpfFact]
    public void Register_FreeChord_IsRegistered()
    {
        using var registrar = new HotkeyRegistrar();
        registrar.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Register_ChordOwnedByAnotherRegistration_IsInUse()
    {
        using var first = new HotkeyRegistrar();
        using var second = new HotkeyRegistrar();
        first.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);

        var status = second.Register(1, Obscure);
        status.State.ShouldBe(HotkeyState.InUse);
        status.Win32Error.ShouldBe(1409);
    }

    [WpfFact]
    public void Unregister_FreesTheChord()
    {
        using var first = new HotkeyRegistrar();
        using var second = new HotkeyRegistrar();
        first.Register(1, Obscure);
        first.Unregister(1);
        second.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Register_SameIdAgain_ReplacesTheChord()
    {
        using var registrar = new HotkeyRegistrar();
        using var other = new HotkeyRegistrar();
        registrar.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
        registrar.Register(1, Obscure with { VirtualKey = VirtualKeys.F1 + 21 }).ShouldBe(HotkeyStatus.Registered);
        other.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }

    [WpfFact]
    public void Dispose_FreesEveryChord()
    {
        var registrar = new HotkeyRegistrar();
        registrar.Register(1, Obscure);
        registrar.Dispose();
        using var other = new HotkeyRegistrar();
        other.Register(1, Obscure).ShouldBe(HotkeyStatus.Registered);
    }
}
