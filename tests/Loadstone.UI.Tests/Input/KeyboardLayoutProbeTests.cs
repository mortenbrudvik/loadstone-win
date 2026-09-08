using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Shouldly;
using Xunit;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Tests.Input;

public class KeyboardLayoutProbeTests
{
    /// <summary>
    /// What Ctrl+Alt+E types on each layout this test knows, keyed by HKL. These are the layouts'
    /// documented AltGr assignments, written down here rather than read back out of ToUnicodeEx, which
    /// is what keeps the assertion honest: a probe that answered "€" for every chord fails on the US
    /// entry, and one that answered null for everything fails on all the others.
    /// </summary>
    private static readonly Dictionary<uint, string?> CtrlAltEByLayout = new()
    {
        [0x04090409] = null, // United States: no AltGr layer at all
        [0x04140414] = "€",  // Norwegian
        [0x04070407] = "€",  // German
        [0x041D041D] = "€",  // Swedish
        [0x04060406] = "€",  // Danish
        [0x040B040B] = "€",  // Finnish
    };

    private readonly KeyboardLayoutProbe _probe = new();

    [Fact]
    public void CharacterTypedBy_CtrlAltE_IsWhatTheActiveLayoutTypes()
    {
        // The warning this class exists for. Ctrl+Alt is AltGr as far as Windows is concerned, so a
        // global Ctrl+Alt+E hotkey swallows the € key on the European layouts and costs nothing on US.
        // Which layout the machine runs is not ours to choose, but what each of them types is, so the
        // expectation is a lookup - the "is it null" decision is asserted, never accepted either way.
        var layout = ActiveLayout();
        if (!CtrlAltEByLayout.TryGetValue(layout, out var expected))
        {
            Assert.Skip($"no documented Ctrl+Alt+E answer for the active keyboard layout 0x{layout:X8}");
        }

        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, 'E')).ShouldBe(expected);
    }

    [Fact]
    public void CharacterTypedBy_ArrowKeys_IsNull()
    {
        // Arrow keys type nothing on every layout, which is why the default chords are built on them.
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Left)).ShouldBeNull();
    }

    [Fact]
    public void CharacterTypedBy_AChordThatTypesAControlCharacter_IsNull()
    {
        // Enter resolves to a control character on every layout, and a character the user cannot see
        // themselves losing is not worth a warning on the row.
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt, VirtualKeys.Enter)).ShouldBeNull();
    }

    [Fact]
    public void CharacterTypedBy_WithoutBothCtrlAndAlt_IsNull()
    {
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control, 'E')).ShouldBeNull();
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Alt, 'E')).ShouldBeNull();
        _probe.CharacterTypedBy(new Shortcut(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win, 'E')).ShouldBeNull();
    }

    /// <summary>
    /// The HKL the probe will consult: the foreground window's thread layout, or this thread's when no
    /// window is foreground. It mirrors the probe's own lookup on purpose - it picks which documented
    /// answer applies, and computes none of them.
    /// </summary>
    private static uint ActiveLayout()
    {
        var foreground = User32.GetForegroundWindow();
        var thread = foreground == 0 ? 0 : User32.GetWindowThreadProcessId(foreground, out _);
        return unchecked((uint)User32.GetKeyboardLayout(thread).ToInt64());
    }
}
