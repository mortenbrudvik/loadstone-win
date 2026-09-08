namespace Loadstone.UI.Interop;

/// <summary>The one sound Loadstone makes, so every "that command did nothing" path is audibly the same.</summary>
internal static class Feedback
{
    public static void Beep() => NativeMethods.User32.MessageBeep(NativeConstants.MB_OK);
}
