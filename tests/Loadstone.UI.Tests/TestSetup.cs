using System.Runtime.CompilerServices;
using Loadstone.UI.Interop;

namespace Loadstone.UI.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Whether the opt-in below actually took. Windows refuses the call once any awareness is set, so
    /// this is recorded at the one moment the answer is available; <see cref="SmokeTests"/> asserts it.
    /// A silent failure has no symptom of its own - every real-window test simply measures logical
    /// pixels and reports frames off by the display's scale factor, with nothing in the diff saying DPI.
    /// </summary>
    internal static bool IsPerMonitorAwareV2 { get; private set; }

    /// <summary>
    /// The test host is not DPI aware by default, which would make every rectangle it reads a scaled
    /// lie on a 150 percent display. Opt in to Per-Monitor V2 before any window exists.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        IsPerMonitorAwareV2 = NativeMethods.User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
