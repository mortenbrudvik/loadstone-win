using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class KeyboardLayoutProbeTests
{
    private readonly KeyboardLayoutProbe _probe = new();

    [Fact]
    public void CharacterTypedBy_ArrowKeys_IsNull()
    {
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Left)).ShouldBeNull();
    }

    [Fact]
    public void CharacterTypedBy_WithoutBothCtrlAndAlt_IsNull()
    {
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control, 'E')).ShouldBeNull();
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Alt, 'E')).ShouldBeNull();
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win, 'E')).ShouldBeNull();
    }

    [Fact]
    public void CharacterTypedBy_Letter_IsNullOrAPrintableCharacter()
    {
        // Layout dependent: "€" on Norwegian and German layouts, null on US. Never a control character.
        var result = _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, 'E'));
        if (result is not null)
        {
            result.ShouldNotBeEmpty();
            result.All(c => !char.IsControl(c)).ShouldBeTrue();
        }
    }
}
