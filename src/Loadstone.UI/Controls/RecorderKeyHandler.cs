using Loadstone.Core.Commands;

namespace Loadstone.UI.Controls;

internal enum RecorderActionKind
{
    Ignore,
    Commit,
    Clear,
    Cancel,
}

internal readonly record struct RecorderAction(RecorderActionKind Kind, Shortcut? Shortcut = null);

/// <summary>The recorder's key rules, kept pure so they can be tested without a keyboard.</summary>
internal static class RecorderKeyHandler
{
    /// <param name="held">The modifier keys down at the moment of the key press.</param>
    /// <param name="virtualKey">The key pressed.</param>
    public static RecorderAction Handle(KeyModifiers held, int virtualKey)
    {
        // IsBindable rather than IsModifier alone: KeyInterop.VirtualKeyFromKey answers 0 for keys WPF
        // does not map, and with a modifier held a 0 would otherwise fall through to the Commit arm and
        // throw out of Shortcut's constructor in the middle of a key press. Refusing it here keeps that
        // constructor validation unreachable from the recorder.
        if (!VirtualKeys.IsBindable(virtualKey))
        {
            return new RecorderAction(RecorderActionKind.Ignore);
        }

        if (virtualKey == VirtualKeys.Escape)
        {
            return new RecorderAction(RecorderActionKind.Cancel);
        }

        if (held == KeyModifiers.None && virtualKey is VirtualKeys.Backspace or VirtualKeys.Delete)
        {
            return new RecorderAction(RecorderActionKind.Clear);
        }

        var isFunctionKey = virtualKey is >= VirtualKeys.F1 and <= VirtualKeys.F24;
        if (held == KeyModifiers.None && !isFunctionKey)
        {
            return new RecorderAction(RecorderActionKind.Ignore);
        }

        return new RecorderAction(RecorderActionKind.Commit, new Shortcut(held, virtualKey));
    }
}
