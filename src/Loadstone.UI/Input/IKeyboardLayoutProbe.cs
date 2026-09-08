using Loadstone.Core.Commands;

namespace Loadstone.UI.Input;

internal interface IKeyboardLayoutProbe
{
    /// <summary>The character the chord would type on the active keyboard layout (AltGr), or null when it types nothing.</summary>
    string? CharacterTypedBy(Shortcut shortcut);
}
