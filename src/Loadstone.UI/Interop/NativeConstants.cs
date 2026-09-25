namespace Loadstone.UI.Interop;

internal static class NativeConstants
{
    // Window styles
    public const long WS_BORDER = 0x00800000;
    public const long WS_CAPTION = 0x00C00000; // WS_BORDER | WS_DLGFRAME
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_MAXIMIZEBOX = 0x00010000;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    // GetAncestor
    public const uint GA_ROOT = 2;

    // ShowWindow
    public const int SW_RESTORE = 9;

    // SetWindowPos. HWND_TOPMOST is a negative pseudo-handle the z-order slot is named by, not a real
    // window handle and not an error return; SetWindowPos takes it in hWndInsertAfter.
    public static readonly nint HWND_TOPMOST = -1;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // DWM
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint DWMWA_CLOAKED = 14;

    // Monitors
    public const uint MONITORINFOF_PRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;
    // A DPI_AWARENESS_CONTEXT is an opaque handle, and the four predefined contexts are small negative
    // sentinels rather than allocated handles; -4 is the value, not a failure code.
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // ASFW_ANY (-1) lets any process take the foreground, not one named pid; the donor is exiting
    // immediately so it cannot know the running instance's pid without another lookup.
    public const int ASFW_ANY = -1;

    // Hotkeys
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    // HWND_MESSAGE is a negative pseudo-handle handed to CreateWindowEx as the parent (through
    // HwndSourceParameters.ParentWindow here) to ask for a message-only window; it names no real
    // window and is not an error return.
    public static readonly nint HWND_MESSAGE = -3;

    // WinEvents
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    // SystemParametersInfo. For the SET actions the BOOL travels in uiParam and pvParam is zero
    // (verified on Windows 11 25H2 on 2026-09-07; passing it in pvParam silently writes FALSE).
    public const uint SPI_GETSNAPSIZING = 0x008E;
    public const uint SPI_SETSNAPSIZING = 0x008F;
    public const uint SPI_GETDOCKMOVING = 0x0090;
    public const uint SPI_SETDOCKMOVING = 0x0091;
    public const uint SPIF_SENDCHANGE = 0x0002;

    // Keyboard
    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint TOUNICODE_NO_KEYBOARD_STATE_CHANGE = 0x0004;

    // MessageBeep
    public const uint MB_OK = 0x0000;

    // GetSystemMetrics. The shell draws a notification icon at the small-icon size, not the large one.
    public const int SM_CXICON = 11;
    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    // Package identity. GetCurrentPackageFullName answers with an error code rather than a BOOL:
    // this one means the process has no package, anything else (including ERROR_INSUFFICIENT_BUFFER
    // from a zero-length buffer) means it has one.
    public const int APPMODEL_ERROR_NO_PACKAGE = 15700;
}
