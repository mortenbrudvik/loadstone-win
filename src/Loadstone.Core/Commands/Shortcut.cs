using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Loadstone.Core.Commands;

/// <summary>
/// A global hotkey chord. Text form: modifiers in the fixed order Ctrl, Alt, Shift, Win joined with
/// "+", then a key name from <see cref="VirtualKeys"/>, for example "Ctrl+Alt+Left".
/// </summary>
public sealed record Shortcut(KeyModifiers Modifiers, int VirtualKey)
{
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

        if (parts[^1].Length == 0 || !VirtualKeys.TryParse(parts[^1], out var vk))
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
