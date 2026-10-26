using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class User32
    {
        internal delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

        internal delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

        [LibraryImport("user32.dll")]
        internal static partial nint GetForegroundWindow();

        /// <summary>
        /// Hands this process's foreground right to another one. A background process cannot call
        /// SetForegroundWindow for itself, so the second Loadstone instance donates its right - it was
        /// just launched by the user, so it holds one - before signalling the first to show Settings.
        /// </summary>
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AllowSetForegroundWindow(int dwProcessId);

        [LibraryImport("user32.dll")]
        internal static partial nint GetAncestor(nint hwnd, uint gaFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindow(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsZoomed(nint hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        // No SetLastError: ShowWindow's BOOL says the window *was previously visible*, not that the call
        // succeeded, so a false return is not a failure and GetLastError would carry stale noise.
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

        // x64 only: GetWindowLongPtrW does not exist in the 32-bit user32. Loadstone ships x64 only.
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int GetClassName(nint hWnd, [Out] char[] lpClassName, int nMaxCount);

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out POINT lpPoint);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

        /// <summary>When the message being handled was posted, in milliseconds since the system started; wraps every 49.7 days.</summary>
        [LibraryImport("user32.dll")]
        internal static partial int GetMessageTime();

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(nint hWnd, int id);

        /// <summary>Raises a WinEvent; the tests use it to reach the foreground hook without taking the foreground.</summary>
        [LibraryImport("user32.dll")]
        internal static partial void NotifyWinEvent(uint eventType, nint hwnd, int idObject, int idChild);

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnhookWinEvent(nint hWinEventHook);

        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SystemParametersInfoGet(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SystemParametersInfoSet(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool MessageBeep(uint uType);

        // No SetLastError: GetSystemMetrics returns 0 for an unknown index and sets no error.
        [LibraryImport("user32.dll")]
        internal static partial int GetSystemMetrics(int nIndex);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int ToUnicodeEx(uint wVirtKey, uint wScanCode, [In] byte[] lpKeyState, [Out] char[] pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

        [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
        internal static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

        [LibraryImport("user32.dll")]
        internal static partial nint GetKeyboardLayout(uint idThread);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetProcessDpiAwarenessContext(nint value);
    }
}
