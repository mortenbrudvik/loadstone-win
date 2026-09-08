using Loadstone.Core.Commands;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Commands;

public class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Left", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Left)]
    [InlineData("ctrl+alt+left", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Left)]
    [InlineData("Control+Alt+U", KeyModifiers.Control | KeyModifiers.Alt, 0x55)]
    [InlineData("Ctrl+Alt+Win+Right", KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win, VirtualKeys.Right)]
    [InlineData("Win+Ctrl+Right", KeyModifiers.Control | KeyModifiers.Win, VirtualKeys.Right)]
    [InlineData("Ctrl+Alt+Enter", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Enter)]
    [InlineData("Ctrl+Alt+Backspace", KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Backspace)]
    [InlineData("Shift+F24", KeyModifiers.Shift, VirtualKeys.F1 + 23)]
    [InlineData("F5", KeyModifiers.None, VirtualKeys.F1 + 4)]
    [InlineData("Ctrl+Alt+3", KeyModifiers.Control | KeyModifiers.Alt, 0x33)]
    [InlineData("Ctrl+Alt+Vk0xBA", KeyModifiers.Control | KeyModifiers.Alt, 0xBA)]
    public void TryParse_AcceptsNamesCaseInsensitively(string text, KeyModifiers modifiers, int virtualKey)
    {
        Shortcut.TryParse(text, out var shortcut).ShouldBeTrue();
        shortcut!.Modifiers.ShouldBe(modifiers);
        shortcut.VirtualKey.ShouldBe(virtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Foo+A")]
    [InlineData("Ctrl+Alt+NoSuchKey")]
    [InlineData("Ctrl+Alt+Vk0xZZ")]
    public void TryParse_RejectsMalformedText(string text)
    {
        Shortcut.TryParse(text, out var shortcut).ShouldBeFalse();
        shortcut.ShouldBeNull();
    }

    [Fact]
    public void TryParse_NullText_IsFalse()
    {
        Shortcut.TryParse(null, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("win+ctrl+right", "Ctrl+Win+Right")]
    [InlineData("shift+alt+ctrl+win+a", "Ctrl+Alt+Shift+Win+A")]
    [InlineData("ctrl+alt+vk0xba", "Ctrl+Alt+Vk0xBA")]
    [InlineData("F12", "F12")]
    public void ToString_IsCanonical(string text, string canonical)
    {
        Shortcut.Parse(text).ToString().ShouldBe(canonical);
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse_ForEveryNamedKey()
    {
        var names = new[]
        {
            "Backspace", "Tab", "Enter", "Space", "PageUp", "PageDown", "End", "Home", "Left", "Up",
            "Right", "Down", "Insert", "Delete", "A", "Z", "0", "9", "F1", "F24",
        };
        foreach (var name in names)
        {
            var text = "Ctrl+Alt+" + name;
            Shortcut.TryParse(text, out var shortcut).ShouldBeTrue(text);
            shortcut!.ToString().ShouldBe(text);
        }
    }

    [Theory]
    [InlineData("Ctrl+Alt+Left", "Ctrl+Alt+←")]
    [InlineData("Ctrl+Alt+Right", "Ctrl+Alt+→")]
    [InlineData("Ctrl+Alt+Up", "Ctrl+Alt+↑")]
    [InlineData("Ctrl+Alt+Down", "Ctrl+Alt+↓")]
    [InlineData("Ctrl+Alt+Enter", "Ctrl+Alt+↵")]
    [InlineData("Ctrl+Alt+Backspace", "Ctrl+Alt+⌫")]
    [InlineData("Ctrl+Alt+U", "Ctrl+Alt+U")]
    public void DisplayText_UsesGlyphsForArrowsEnterAndBackspace(string text, string display)
    {
        Shortcut.Parse(text).DisplayText.ShouldBe(display);
    }

    [Fact]
    public void Parse_Malformed_Throws()
    {
        Should.Throw<FormatException>(() => Shortcut.Parse("Ctrl+"));
    }

    [Fact]
    public void Shortcuts_AreValues()
    {
        Shortcut.Parse("Ctrl+Alt+E").ShouldBe(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, 0x45));
    }

    [Theory]
    [InlineData(VirtualKeys.ShiftKey)]
    [InlineData(VirtualKeys.ControlKey)]
    [InlineData(VirtualKeys.MenuKey)]
    [InlineData(VirtualKeys.LWin)]
    [InlineData(VirtualKeys.RWin)]
    [InlineData(VirtualKeys.LMenu)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0x100)]
    public void Constructor_RejectsAKeyThatCannotEndAChord(int virtualKey)
    {
        // ToString is the settings format: new Shortcut(mods, 0) wrote "Ctrl+Alt+Vk0x00", which TryParse
        // then refused, so the command came back unbound on the next launch with nothing logged.
        Should.Throw<ArgumentOutOfRangeException>(
            () => new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, virtualKey));
    }

    [Theory]
    [InlineData("Ctrl+Vk0x10")]
    [InlineData("Ctrl+Alt+Vk0x11")]
    [InlineData("Ctrl+Alt+Vk0xA0")]
    [InlineData("Ctrl+Alt+Vk0x00")]
    [InlineData("Ctrl+Alt+Vk0x100")]
    public void TryParse_RejectsAKeyThatCannotEndAChord(string text)
    {
        // Hand-edited settings reach this path, so the constructor's guard must be answered with false
        // here rather than thrown out of a TryParse.
        Shortcut.TryParse(text, out var shortcut).ShouldBeFalse(text);
        shortcut.ShouldBeNull();
    }

    [Theory]
    [InlineData("Ctrl+Alt+A", true)]
    [InlineData("Shift+Backspace", true)]
    [InlineData("F5", true)]
    [InlineData("Shift+F24", true)]
    [InlineData("A", false)]
    [InlineData("Backspace", false)]
    public void IsUsable_RequiresModifiers_ExceptOnAFunctionKey(string text, bool usable)
    {
        // A hand-edited "restore": "Backspace" parses fine, and RegisterHotKey would then swallow
        // Backspace in every app on the machine.
        Shortcut.Parse(text).IsUsable.ShouldBe(usable, text);
    }

    [Fact]
    public void IsBindable_RejectsModifiersAndCodesOutsideOneToFF()
    {
        VirtualKeys.IsBindable(0).ShouldBeFalse();
        VirtualKeys.IsBindable(0x100).ShouldBeFalse();
        VirtualKeys.IsBindable(VirtualKeys.ShiftKey).ShouldBeFalse();
        VirtualKeys.IsBindable(VirtualKeys.Backspace).ShouldBeTrue();
        VirtualKeys.IsBindable(0xFF).ShouldBeTrue();
    }

    [Fact]
    public void IsModifier_RecognisesEveryModifierCode()
    {
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
        {
            VirtualKeys.IsModifier(vk).ShouldBeTrue($"0x{vk:X2}");
        }

        VirtualKeys.IsModifier(0x41).ShouldBeFalse();
    }
}
