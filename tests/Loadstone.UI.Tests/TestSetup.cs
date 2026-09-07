using System.Runtime.CompilerServices;
using Loadstone.UI.Interop;

namespace Loadstone.UI.Tests;

internal static class TestSetup
{
    /// <summary>
    /// The test host is not DPI aware by default, which would make every rectangle it reads a scaled
    /// lie on a 150 percent display. Opt in to Per-Monitor V2 before any window exists.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeMethods.User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
