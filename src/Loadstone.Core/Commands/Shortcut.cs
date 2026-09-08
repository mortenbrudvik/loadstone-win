using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Loadstone.Core.Commands;

/// <summary>
/// A global hotkey chord. Text form: modifiers in the fixed order Ctrl, Alt, Shift, Win joined with
/// "+", then a key name from <see cref="VirtualKeys"/>, for example "Ctrl+Alt+Left".
/// </summary>
public sealed record Shortcut(KeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>
    /// The key the chord ends on. Rejected unless <see cref="VirtualKeys.IsBindable"/>, because
    /// <see cref="ToString"/> is the settings format and has to be total over everything that can be
    /// constructed: <c>new Shortcut(mods, 0)</c> would write "Ctrl+Alt+Vk0x00", which
    /// <see cref="TryParse"/> then refuses, so the command would silently come back unbound on the
    /// next launch. Throwing at construction turns that into a bug at the source instead.
    /// </summary>
    public int VirtualKey { get; init; } = VirtualKeys.IsBindable(VirtualKey)
        ? VirtualKey
        : throw new ArgumentOutOfRangeException(nameof(VirtualKey), VirtualKey, "A shortcut's key must be a non-modifier virtual-key code in 1..0xFF.");

    /// <summary>
    /// Whether this chord may actually be registered. It is the recorder's rule, stated once so the
    /// settings loader can apply it too: a hand-edited <c>"restore": "Backspace"</c> parses perfectly
    /// well, and RegisterHotKey would then swallow Backspace system-wide, in every app, with no way
    /// left to type it. Only F1..F24 may go bare; a modifier-only chord cannot even be constructed
    /// because the key itself must not be a modifier.
    /// </summary>
    public bool IsUsable =>
        Modifiers != KeyModifiers.None || VirtualKey is >= VirtualKeys.F1 and <= VirtualKeys.F24;

    public static bool TryParse(string? text, [NotNullWhen(true)] out Shortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "win":
                    modifiers |= KeyModifiers.Win;
                    break;
                default:
                    return false;
            }
        }

        // IsBindable is re-checked here rather than left to the constructor: "Ctrl+Vk0x10" names the
        // Shift key by code and would otherwise throw out of a TryParse, which a settings file full of
        // hand-edited text must never do.
        if (parts[^1].Length == 0 || !VirtualKeys.TryParse(parts[^1], out var vk) || !VirtualKeys.IsBindable(vk))
        {
            return false;
        }

        shortcut = new Shortcut(modifiers, vk);
        return true;
    }

    public static Shortcut Parse(string text) =>
        TryParse(text, out var shortcut) ? shortcut : throw new FormatException($"'{text}' is not a shortcut");

    /// <summary>The canonical text form, used as the settings value.</summary>
    public override string ToString() => Prefix() + VirtualKeys.NameOf(VirtualKey);

    /// <summary>The form shown to the user, with glyphs for arrows, Enter, and Backspace.</summary>
    public string DisplayText => Prefix() + VirtualKeys.DisplayName(VirtualKey);

    private string Prefix()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(KeyModifiers.Win)) sb.Append("Win+");
        return sb.ToString();
    }
}
