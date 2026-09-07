# Loadstone for Windows: design

Date: 2026-09-07
Status: approved design, ready for an implementation plan
Reference implementation: the macOS app at `C:\code\projects\loadstone` (Loadstone 0.2.1, Swift 6)

## 1. Purpose and scope

Loadstone for Windows is a tray utility that snaps the focused window into halves, corners, thirds, and two-thirds of the screen, from global hotkeys or by dragging a window to a screen edge. It is a port of the macOS Loadstone with the same commands, the same drag zones, the same restore semantics, and the same defaults translated to Windows chords.

### 1.1 Feature parity with Loadstone 0.2.1

| Feature | macOS | Windows |
| --- | --- | --- |
| Halves, corners, thirds, two-thirds, maximize | yes | yes |
| Center (keeps size), one-shot Restore | yes | yes |
| Drag to snap with a blue preview, portrait zones | yes | yes |
| Next / previous display in reading order | yes | yes |
| Editable global shortcuts | KeyboardShortcuts package | own recorder, `RegisterHotKey` |
| Launch at login | `SMAppService` | HKCU Run key |
| Switch off the OS's own drag tiling while running, with crash recovery | `com.apple.WindowManager` defaults | `SystemParametersInfo` dock-moving and snap-sizing flags |
| Accessibility permission, Relaunch | yes | not needed, dropped |
| Homebrew cask | yes | dropped; Inno Setup installer instead |

### 1.2 Windows-only additions

These come almost free from the Windows APIs and are in scope:

- A shortcut another application already owns is reported next to its recorder in Settings ("In use by another app") and in the startup log. The Mac library swallows that failure.
- A Ctrl+Alt chord that types a character on the active keyboard layout (AltGr, for example Ctrl+Alt+E types € on Norwegian) is still bound, and Settings shows "Types € on your keyboard layout" beside it.
- Launching the exe while Loadstone is running opens Settings in the running instance.
- The launch-at-login row shows when Task Manager has disabled the entry and offers to open Startup Apps.

### 1.3 Non-goals

Custom zone layouts or grids, per-application rules, saved window layouts, MSIX, winget, code signing, auto-update, arm64, telemetry, and any Mac-only concept (Accessibility trust, relaunch). The Mac app has none of the first group either.

## 2. Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| UI stack | Pure WPF with WPF-UI, per the desktop template | The whole UI is a tray menu and a two-tab settings window; the hybrid template's WebView2, Node build, and six projects buy nothing here |
| Aero Snap while running | Switch off Windows' drag-to-edge docking while Loadstone's drag snapping is on; restore on exit; recover after a crash | Mirrors the Mac; without it two previews show and both systems snap on release. Win+Arrow and Snap Layouts are untouched because they hang off a different flag |
| Win32 layer | Self-contained `LibraryImport` declarations in the UI project | A public repo must build with `dotnet build` alone; the private `WinInputHooks` and `WindowManagementApi` packages need a token and are built around modifier-plus-drag |
| Drag detection | Windows' own move-loop events (`EVENT_SYSTEM_MOVESIZESTART` / `END`) plus cursor polling during a drag | Windows already reports when a window is being moved; a text selection never enters the move loop. No low-level hook, no need to cancel the OS drop |
| AltGr collisions | Bind anyway and warn in Settings | Keeps Mac parity for muscle memory; the user rebinds once if they type that character |
| Tray icon library | H.NotifyIcon.Wpf | Works without a hosting window; proven in the Snap project |
| Coordinates | Physical pixels, top-left origin, y down, throughout | Matches every Win32 API once the process is Per-Monitor V2 aware; no coordinate flip layer exists |

## 3. Architecture

Data flows one way, as on the Mac:

```
hotkey (WM_HOTKEY) ──┐
tray menu item ──────┴─▶ CommandDispatcher ─▶ WindowDirector.Perform ─┐
drag (WinEvent) ───────▶ DragSnapMonitor ───▶ WindowDirector.Snap ────┴─▶ IMovableWindow (Win32Window) ─▶ SetWindowPos
                                                     ▲
                                     Display snapshot (EnumDisplayMonitors)
```

Input components resolve which window and which display; the director decides the frame; `Win32Window` writes it.

### 3.1 Solution layout

```
Loadstone.sln
Directory.Build.props            net10.0-windows10.0.26100, Nullable, ImplicitUsings, TreatWarningsAsErrors
.editorconfig                    template conventions (file-scoped namespaces, 4 spaces C#, 2 spaces XAML, CRLF)
.github/workflows/ci.yml
docs/                            README diagrams, this spec
src/Loadstone.Core/              pure: geometry, commands, director, drag tracker, settings, seams. No Win32, no WPF
src/Loadstone.UI/                WPF app, exe name Loadstone.exe
tests/Loadstone.Core.Tests/
tests/Loadstone.UI.Tests/
tools/icons/                     script that regenerates the .ico assets
```

Later phase (installer): `version.json`, `build/`, `installer/`, `scripts/version-manager.ps1` per the wpf-installer skill.

Dependency direction: `Loadstone.UI → Loadstone.Core`. Tests reference their subject project only.

### 3.2 Packages

| Project | Packages |
| --- | --- |
| Core | `Microsoft.Extensions.Logging.Abstractions` only; JSON through the BCL's `System.Text.Json` |
| UI | WPF-UI, CommunityToolkit.Mvvm, Autofac, Autofac.Extensions.DependencyInjection, Microsoft.Extensions.Hosting, H.NotifyIcon.Wpf, NLog, NLog.Extensions.Logging |
| Tests | xunit.v3, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, NSubstitute, Shouldly, coverlet.collector; UI tests add an STA fact attribute package (see 10.2) |

Exact versions are pinned by the implementation plan.

### 3.3 Core project layout

```
Loadstone.Core/
  Geometry/   Tile.cs, SnapZones.cs, Layout.cs, Display.cs, ScreenGeometry.cs, Rect.cs, Point.cs
  Commands/   WindowCommand.cs, CommandSection.cs, CommandOutcome.cs, Shortcut.cs, VirtualKeys.cs
  Windows/    IMovableWindow.cs, WindowIdentity.cs, WindowDirector.cs
  Drag/       DragSnapTracker.cs, SnapTarget.cs
  Settings/   AppSettings.cs, ISettingsService.cs, SettingsService.cs
  Platform/   NativeSnap.cs, INativeSnapSettings.cs, IStartupRegistration.cs, StartupStatus.cs
```

The folder is `Platform`, not `System`, because a `Loadstone.Core.System` namespace would shadow the BCL's `System` namespace inside Core.

`Rect` and `Point` are small integer value records so Core does not depend on `System.Drawing` or WPF.

### 3.4 UI project layout

```
Loadstone.UI/
  App.xaml, App.xaml.cs          host bootstrap, startup order, single instance, exception handlers
  app.manifest                   dpiAwareness PerMonitorV2
  DependencyInjection/AppModule.cs
  Interop/                       NativeConstants.cs, NativeStructs.cs, NativeMethods.User32.cs, NativeMethods.Dwmapi.cs, NativeMethods.Shcore.cs
  Windows/                       Win32Window.cs, FocusedWindow.cs, WindowStyles.cs (IsManageable), Displays.cs, CommandDispatcher.cs
  Input/                         HotkeyCenter.cs, HotkeyRegistrar.cs, KeyboardLayoutProbe.cs, IMoveSizeEvents.cs, WinEventMoveSizeEvents.cs, DragSnapMonitor.cs, SnapPreview.xaml(.cs)
  Services/                      SystemParametersNativeSnapSettings.cs, RegistryStartupRegistration.cs, SingleInstance.cs
  Tray/                          TrayController.cs, TrayMenuBuilder.cs
  ViewModels/                    SettingsViewModel.cs, ShortcutsViewModel.cs, ShortcutRowViewModel.cs
  Views/                         SettingsWindow.xaml(.cs)
  Controls/                      ShortcutRecorder.cs, RecorderKeyHandler.cs
  Assets/                        loadstone.ico, tray-light.ico, tray-dark.ico
```

All Win32 calls happen on the UI thread, the counterpart of the Mac's main actor. Nothing in `Interop/` is called from anywhere but `Windows/`, `Input/`, and `Services/`.

## 4. Core

### 4.1 Geometry

`Tile` has the fourteen cases of the Mac enum: `LeftHalf`, `RightHalf`, `TopHalf`, `BottomHalf`, `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, `LeftThird`, `CenterThird`, `RightThird`, `LeftTwoThirds`, `RightTwoThirds`, `Maximize`.

`Tile.Frame(workArea)` lays the tile into a work area `(x, y, w, h)` in y-down coordinates. Floor the left and top pieces and give the remainder to the right and bottom ones, so complementary tiles meet exactly on odd sizes and `LeftTwoThirds` ends where `RightThird` starts:

```
halfW = floor(w / 2)        halfH = floor(h / 2)
thirdW = floor(w / 3)       twoThirdsW = 2 * thirdW

LeftHalf       (x,            y,           halfW,          h)
RightHalf      (x + halfW,    y,           w - halfW,      h)
TopHalf        (x,            y,           w,              halfH)
BottomHalf     (x,            y + halfH,   w,              h - halfH)
TopLeft        (x,            y,           halfW,          halfH)
TopRight       (x + halfW,    y,           w - halfW,      halfH)
BottomLeft     (x,            y + halfH,   halfW,          h - halfH)
BottomRight    (x + halfW,    y + halfH,   w - halfW,      h - halfH)
LeftThird      (x,            y,           thirdW,         h)
CenterThird    (x + thirdW,   y,           thirdW,         h)
RightThird     (x + 2*thirdW, y,           w - 2*thirdW,   h)
LeftTwoThirds  (x,            y,           twoThirdsW,     h)
RightTwoThirds (x + thirdW,   y,           w - thirdW,     h)
Maximize       workArea
```

`SnapZones.TileAt(point, screenFrame, scale)` maps a cursor against the full monitor frame (not the work area). `scale = dpi / 96`, `edge = 16 * scale`, `corner = 140 * scale`, both rounded to whole pixels.

1. If the point is outside the frame inflated by `edge` on every side, no tile. Overshooting an outer edge by up to `edge` still counts.
2. Clamp the point into the frame and measure `fromLeft`, `fromRight`, `fromTop`, `fromBottom`.
3. Corner squares of side `corner` win: `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`.
4. Within `edge` of the top: `Maximize`.
5. Portrait frame (height > width): within `edge` of the left or right edge, split along the height into `t = fromTop / height`: `t < 1/3` top corner, `t < 2/3` the side third, else bottom corner. Within `edge` of the bottom: `LeftHalf` if the point is left of the frame's horizontal middle, else `RightHalf`.
6. Landscape: left band `LeftHalf`, right band `RightHalf`, bottom band the third under the point (`t = fromLeft / width`: `< 1/3` left, `< 2/3` center, else right).

`Layout.Centered(current, workArea)` keeps the size clamped to the work area and centers it. `Layout.Mapped(current, source, destination)` keeps the same relative origin and size across displays; a degenerate source yields the whole destination.

`Display` is a value record of `Frame`, `WorkArea`, and `Dpi` (an integer, 96 at 100 percent). `ScreenGeometry`:

- `DisplayContaining(point, displays)`: exact containment first, then a 1 px tolerance so a cursor resting on the outer top or right edge still resolves. Null when on no display.
- `DisplayContaining(rect, displays)`: the display under the rect's center.
- `Neighbor(display, delta, displays)`: reading order, x ascending then y ascending (top before bottom), wrapping at both ends with a Euclidean modulo. Returns the display itself when it is the only one. Null if the display is not in the list.

### 4.2 Commands

`WindowCommand` is the single source of truth for the hotkey list, the tray menu, and the Shortcuts tab. It is a closed set: the fourteen tiles plus `Center`, `Restore`, `NextDisplay`, `PreviousDisplay`, in that order. Each command has an `Id`, a `Title`, a `Section`, and a `DefaultShortcut`. Adding a tile without covering all four fails to compile, and a test pins the list.

Ids are the settings keys and never change once shipped. They match the Mac ids exactly: `leftHalf`, `rightHalf`, `topHalf`, `bottomHalf`, `topLeft`, `topRight`, `bottomLeft`, `bottomRight`, `leftThird`, `centerThird`, `rightThird`, `leftTwoThirds`, `rightTwoThirds`, `maximize`, `center`, `restore`, `nextDisplay`, `previousDisplay`.

Sections, in menu order: Halves, Corners, Thirds, Window (maximize, center, restore), Displays.

Defaults (Control-Option on the Mac becomes Ctrl+Alt, Command becomes Win):

| Command | Default |
| --- | --- |
| Left / right / top / bottom half | Ctrl+Alt+Left / Right / Up / Down |
| Top left / top right corner | Ctrl+Alt+U / I |
| Bottom left / bottom right corner | Ctrl+Alt+J / K |
| Left / center / right third | Ctrl+Alt+D / F / G |
| Left / right two thirds | Ctrl+Alt+E / T |
| Maximize | Ctrl+Alt+Enter |
| Center | Ctrl+Alt+C |
| Restore | Ctrl+Alt+Backspace |
| Next / previous display | Ctrl+Alt+Win+Right / Left |

`CommandOutcome` is the Mac's set: `Moved`, `FrameUnreadable`, `NoDisplay`, `NoOtherDisplay`, `NothingToRestore`, `Rejected(int win32Error)`.

### 4.3 Shortcuts

`Shortcut` is a value of `Modifiers` (flags: Control, Alt, Shift, Win) and a virtual-key code. Text form, used in settings.json and shown in the UI: modifiers in the fixed order `Ctrl+Alt+Shift+Win+` followed by a key name. Key names: `A`–`Z`, `0`–`9`, `F1`–`F24`, `Left`, `Right`, `Up`, `Down`, `Enter`, `Backspace`, `Space`, `Tab`, `Home`, `End`, `PageUp`, `PageDown`, `Insert`, `Delete`, and `Vk0xNN` for any other virtual key. Parsing is case-insensitive and rejects a chord with no key or with an unknown name; formatting is canonical. The UI renders arrows and Enter and Backspace as glyphs (← → ↑ ↓ ↵ ⌫).

### 4.4 Windows and the director

`WindowIdentity` is `(nint Hwnd, int Pid)`. Windows reuses handles after a window closes, so an entry is valid only while the handle is a window owned by the same process.

`IMovableWindow` is what the director needs: `Identity`, `Frame` (the visible rectangle, null when unreadable), and `SetFrame(Rect) → CommandOutcome` returning `Moved` or `Rejected`.

`WindowDirector`:

- `Perform(command, window)` runs a command on a window using the display under the window's center, or the primary display when the window is on no display after a disconnect, so it can be brought back. The display list comes from the delegate injected at construction (production: `Displays.Snapshot`), as on the Mac.
- `Snap(tile, window, display)` is the drag path: the display the gesture ended on is passed explicitly, because a wide window straddling two displays belongs to the one under its center, not the one under the pointer.
- The restore ledger maps `WindowIdentity` to the frame the window had before Loadstone first touched it. It is written only after a window accepts a frame, never before, so a refused write or a command that never ran leaves nothing for a later Restore. `Restore` writes the remembered frame and drops the entry only once that write is accepted. `Restore` itself never records.
- Before every command the ledger drops entries whose identity is no longer alive, via an injected `Func<WindowIdentity, bool>`; the UI answers with `IsWindow` plus a process id check. This replaces the Mac's process-quit observer.
- `NextDisplay` / `PreviousDisplay` map the frame from the current display's work area to the neighbor's with `Layout.Mapped`; one display gives `NoOtherDisplay`.
- The director does not resolve windows or report outcomes; it cannot, because both need Win32. `CommandDispatcher` in the UI project does that (5.5).

Accepted limitation, shared with the Mac: the ledger stores a frame, not a window state. A window that was OS-maximized before Loadstone touched it restores to a normal-state window filling the work area.

### 4.5 Drag tracker

`DragSnapTracker<TWindow>` is a pure state machine. The monitor feeds it the facts it cannot know and gets back what to preview and what to commit.

States: `Idle`, `Tracking(window, baseline)`, `Armed(window, baseline)`, `Disarmed`.

- `Begin(window, baselineFrame, displays)`: `Idle → Tracking`.
- `Update(cursor, currentFrame) → SnapTarget?`: a null frame changes nothing and returns null. A size different from the baseline means a resize: `→ Disarmed` for the rest of the drag, return null. In `Tracking`, an origin different from the baseline means the window is really being moved: `→ Armed`. In `Armed`, return the zone under the cursor on the display under the cursor, scaled by that display's DPI, or null when the cursor is in no zone. `Tracking` and `Disarmed` always return null, so the preview shows only once the window has moved.
- `End(cursor, finalFrame) → (window, SnapTarget)?`: always returns to `Idle`. Returns null from `Idle` or `Disarmed`, when the final frame is null, when the final frame equals the baseline (Escape cancelled the drag, or nothing moved), or when its size differs from the baseline. Otherwise it returns the zone under the cursor, if any. This last rule intentionally does not require `Armed`: a drag shown as an outline only moves the frame when the loop ends, and that drop still snaps.

`SnapTarget` is `(Tile, Display)`.

### 4.6 Settings model

`AppSettings` is the JSON document at `%LOCALAPPDATA%\Loadstone\settings.json`:

```json
{
  "dragSnappingEnabled": true,
  "shortcuts": {
    "leftTwoThirds": "Ctrl+Alt+Shift+D",
    "center": ""
  },
  "nativeSnapPreviousValues": {
    "dockMoving": true,
    "snapSizing": true
  }
}
```

- `dragSnappingEnabled` defaults to true when absent (opt-out, as on the Mac).
- `shortcuts` holds only customized commands, keyed by command id. An absent id means the default; an empty string means the user cleared it.
- `nativeSnapPreviousValues` is the crash-recovery marker described in 7.1. Absent when Loadstone is not running or exited cleanly.
- Launch at login is not stored here. It is read from the registry each time, as the Mac reads `SMAppService`.

`SettingsService` loads once at startup and writes atomically: serialize to `settings.json.tmp`, then move over the target. A file that fails to parse is copied to `settings.json.corrupt`, logged, and replaced by defaults on the next write. Every change from the UI is saved immediately; there is no Save button.

### 4.7 Seams

Core owns the interfaces the UI implements, all with obvious fakes for tests: `IMovableWindow`, `INativeSnapSettings` (read and write one named flag), `IStartupRegistration` (`IsEnabled`, `Enable(exePath)`, `Disable()`, `Status`), `ISettingsService`. The director takes its display list and liveness check as delegates. The drag tracker is generic over the window type.

## 5. Windows layer

### 5.1 Interop

`Interop/` follows the template: constants, structs, and `LibraryImport` partial classes split by DLL. About twenty-five functions, most adapted from the user's WindowsAppManagerMcp declarations: window queries (`GetForegroundWindow`, `GetAncestor`, `IsWindow`, `IsWindowVisible`, `IsIconic`, `IsZoomed`, `GetWindowRect`, `GetWindowLongPtr`, `GetClassName`, `GetWindowThreadProcessId`, `GetWindowPlacement`, `ShowWindow`, `SetWindowPos`), DWM (`DwmGetWindowAttribute` for extended frame bounds and cloaking), monitors (`EnumDisplayMonitors`, `GetMonitorInfo`, `MonitorFromPoint`, `GetDpiForMonitor`, `GetDpiForWindow`), input (`RegisterHotKey`, `UnregisterHotKey`, `GetCursorPos`, `ToUnicodeEx`, `MapVirtualKeyEx`, `GetKeyboardLayout`), events (`SetWinEventHook`, `UnhookWinEvent`), system (`SystemParametersInfo`, `MessageBeep`, `SetProcessDpiAwarenessContext` for the test host).

### 5.2 Win32Window

`Win32Window` implements `IMovableWindow` and is the only type that knows about invisible borders and DPI, the role `AXWindow` plays on the Mac.

- `Frame` reads `DWMWA_EXTENDED_FRAME_BOUNDS`, the rectangle the user sees. `GetWindowRect` includes the invisible resize borders (about 7 px per side at 100 percent), which is why naive tiling leaves gaps between tiles. Falls back to `GetWindowRect` if the DWM call fails. Null when the handle is no longer a window or both calls fail.
- `SetFrame(target)`:
  1. If the window is minimized or maximized, `ShowWindow(SW_RESTORE)` first; `SetWindowPos` misbehaves on those states.
  2. If the window has no `WS_THICKFRAME` style, it is fixed-size: keep its current size and center it on the target. A window larger than the tile overhangs it rather than being resized, because `SetWindowPos` would resize a window that has no sizing frame and break its layout.
  3. Compute the border insets as the difference between `GetWindowRect` and the extended frame bounds, and expand the target by them so the visible frame lands on the target.
  4. `SetWindowPos(hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE)`. A false return is `Rejected` with `GetLastError`. The common case is a window of an elevated process, which UIPI blocks with access denied.
  5. Read the frame back. If it differs from the target, write once more with the same expanded rectangle. This is the DPI dance: moving a per-monitor-aware window to a monitor with a different scale makes it resize itself on `WM_DPICHANGED`, and the second write lands once it lives there. It is the Windows form of the Mac's size, position, size sequence.
  6. Return `Moved` when the write was accepted, even if the app clamped the size to its minimum. Only a refused write is an error, as on the Mac. The final frame is logged at debug level when it differs from the target.
- Maximize sets the frame to the work area and leaves the window in the normal state. The OS maximize on title-bar double-click is unaffected.

### 5.3 Focused window

`FocusedWindow.Resolve()` takes `GetForegroundWindow`, walks to the root with `GetAncestor(GA_ROOT)`, and applies `WindowStyles.IsManageable`, a pure predicate over a snapshot of style, extended style, class name, visibility, cloaking, and owning process:

- is a window and is visible,
- is not cloaked (`DWMWA_CLOAKED`, which hides windows on other virtual desktops and suspended UWP frames),
- is not owned by Loadstone's own process,
- class is not one of `Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`,
- has `WS_CAPTION` or `WS_THICKFRAME`,
- does not have `WS_EX_TOOLWINDOW`.

The same predicate gates the drag path, so tool windows and Loadstone's own windows never snap.

### 5.4 Displays

`Displays.Snapshot()` enumerates monitors into `Display` values with the monitor rectangle, the work area (excludes the taskbar), and the DPI from `GetDpiForMonitor`. Primary first. Empty when nothing is attached, which the director reports as `NoDisplay`.

### 5.5 Command dispatch

`CommandDispatcher` is the counterpart of the Mac director's no-argument `perform(_:)`. It has two entry points: `Run(command)` resolves the focused window through `FocusedWindow.Resolve()`, and `Run(command, hwnd)` wraps a window captured earlier (the tray menu path). Both wrap the handle in a `Win32Window`, call `WindowDirector.Perform`, and report the outcome: `Moved` is silent; every other outcome beeps with `MessageBeep` and logs one line with the command id, the outcome, the process id, and the Win32 error when there is one. A focused window that fails `IsManageable`, or no foreground window at all, beeps and logs "skipped" with the reason. There are no dialogs.

## 6. Input

### 6.1 Hotkeys

`HotkeyRegistrar` owns a message-only window (`HwndSource` parented to `HWND_MESSAGE`) and calls `RegisterHotKey` with `MOD_NOREPEAT` plus the chord's modifiers, one id per command. `WM_HOTKEY` fires on key down, the Windows convention; the Mac fires on key up. Registration returns a per-command status: `Registered`, `Unbound` (cleared or invalid), `InUse` (the API failed with `ERROR_HOTKEY_ALREADY_REGISTERED`, 1409), or `Failed(win32Error)` for any other failure. Every non-registered status is logged.

`HotkeyCenter` sits above it: it resolves each command's effective shortcut (custom, else default), registers all of them at startup, logs the effective binding and status per command like the Mac's startup log, re-registers a single command when its shortcut changes, and exposes `Suspend()` / `Resume()` used while a recorder is recording so pressing the current chord records instead of moving a window. A hotkey routes to `CommandDispatcher.Run(command)`, which acts on the focused window (5.5).

`KeyboardLayoutProbe.TypesCharacter(shortcut)` asks `ToUnicodeEx` for the active layout with Ctrl and Alt down whether the chord produces a character. It runs for every Ctrl+Alt binding at registration and whenever the recorder commits one. A true answer is logged and surfaces in Settings as "Types € on your keyboard layout", with the character it found. The binding is still made.

### 6.2 Drag to snap

`IMoveSizeEvents` is the seam: `Started(hwnd)` and `Ended(hwnd)` events plus `Install()` / `Uninstall()`. `WinEventMoveSizeEvents` implements it with one `SetWinEventHook` covering `EVENT_SYSTEM_MOVESIZESTART` (0x000A) through `EVENT_SYSTEM_MOVESIZEEND` (0x000B), flags `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`, installed from the UI thread so callbacks are delivered through its message loop. The callback delegate is held in a field for the life of the hook so the GC cannot collect it.

`DragSnapMonitor` follows the drag setting: installed while `dragSnappingEnabled` is true, uninstalled (hook removed, tracker reset, preview hidden) when it is false. It owns a `DispatcherTimer` at 16 ms and the `SnapPreview`.

- `Started(hwnd)`: resolve the root and check `IsManageable`; ignore otherwise. Take a display snapshot, read the window's frame as the baseline, `tracker.Begin`, start the timer.
- Timer tick: read `GetCursorPos` and the window's frame, `tracker.Update`, show the preview over `tile.Frame(display.WorkArea)` or hide it.
- `Ended(hwnd)`: stop the timer, hide the preview, read the cursor and the final frame, `tracker.End`. If it returns a target, `director.Snap(tile, window, display)`. Anything but `Moved` beeps and logs. The commit runs after the OS has finished its move loop, so Loadstone's frame is the last write.
- Any event for a different handle while a drag is in progress resets the tracker.

Every display has its own zones, including the edge it shares with a neighbor. The cursor passes straight through a shared edge, so the README keeps the Mac's advice to slow down there and release while the preview is showing.

### 6.3 Snap preview

`SnapPreview` is a WPF window: no style, transparent, no taskbar entry, topmost, not activated on show, hit-test invisible. After the handle exists it adds `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`. It is positioned with `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE | SWP_SHOWWINDOW)` in physical pixels, because WPF's own placement is wrong on mixed-DPI desks, a lesson recorded in Snap. The content is a rounded rectangle, 10 px radius, filled with the system accent blue at 22 percent and stroked 2 px at 85 percent, inset 6 px from the tile so it reads as a preview. It is only ever hidden, never closed.

Topmost is asserted by Loadstone's own process on every show, never requested on the window from elsewhere. The spike found that Windows refuses a topmost request made by a background process on another process's window, but honors one a process makes on its own window after the window exists, whether plain or overlay-styled.

## 7. System side effects

### 7.1 Windows edge snapping

`NativeSnap` in Core mirrors the Mac's `NativeTiling` over `INativeSnapSettings`. The managed flags are data:

| Flag | Get | Set | Registry mirror |
| --- | --- | --- | --- |
| `DockMoving` | `SPI_GETDOCKMOVING` 0x0090 | `SPI_SETDOCKMOVING` 0x0091 | `HKCU\Control Panel\Desktop\DockMoving` |
| `SnapSizing` | `SPI_GETSNAPSIZING` 0x008E | `SPI_SETSNAPSIZING` 0x008F | `HKCU\Control Panel\Desktop\SnapSizing` |

`SystemParametersNativeSnapSettings` writes with `SPIF_UPDATEINIFILE | SPIF_SENDCHANGE` so the value persists and Explorer is told. For these two actions the boolean travels in `uiParam` and `pvParam` must be zero; passing the value in `pvParam` with `uiParam` zero silently writes FALSE. Reads take a pointer to a BOOL in `pvParam`. Both were established on the development machine (Windows 11 25H2, build 26200) on 2026-09-07; see 10.4.

Behavior:

- `Apply(dragSnappingEnabled: true)`: if the marker is present in settings, an earlier run ended without restoring, so apply the stored values first and clear the marker; capturing before that would record Loadstone's own false as "what the user had". Then capture the current value of every managed flag, write them to the marker, set every flag to false, read each back, and log an error for any that did not stick. Capturing happens once per run.
- `Apply(dragSnappingEnabled: false)` and exit: write the captured values back, clear the marker. A flag whose read failed at capture time is never written.
- Only while Loadstone's own drag snapping is on. Turning the setting off restores Windows' behavior immediately. The Mac does this unconditionally; tying it to the setting is the one deliberate difference.

The spike in 10.4 confirmed that these two flags are sufficient for the edge, corner, and top-edge behaviors, so the managed list is final. The top-edge Snap Layouts flyout (Explorer's `EnableSnapAssistFlyout`) could not be observed because it is already off on the development machine; Loadstone does not manage it, and the README tells users where to turn it off if it appears while dragging to the top.

The README's troubleshooting entry tells users where to put the setting back by hand: Settings › System › Multitasking › Snap windows (`ms-settings:multitasking`).

### 7.2 Launch at login

`RegistryStartupRegistration` writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Loadstone`, data the quoted path of the running exe. `Disable` deletes the value. `Status` reads `…\Explorer\StartupApproved\Run`, value `Loadstone`: absent or first byte 0x02 means enabled, 0x03 means Task Manager disabled it. That status surfaces in Settings as "Disabled in Task Manager › Startup apps" with a button that opens `ms-settings:startupapps`, the counterpart of the Mac's approval message.

At launch, if the Run value exists and points somewhere other than the running exe (an installer moved it), it is rewritten. A failed write rolls the toggle back and shows the error, with the same re-entrancy guard the Mac's `isApplyingLoginItem` needed.

## 8. UI

### 8.1 Tray

`TrayController` creates an H.NotifyIcon `TaskbarIcon` in code with no hosting window. Tooltip "Loadstone". Right click opens the menu; left click opens Settings (Windows convention; the Mac opens the menu on click). Two icon variants of the split-rectangle glyph follow the taskbar theme, read from `HKCU\…\Themes\Personalize\SystemUsesLightTheme` and refreshed on `WM_SETTINGCHANGE`.

`TrayMenuBuilder` is pure and tested: one item per command in `WindowCommand.Sections` order with a separator after each section, each showing its effective chord as gesture text, then "Settings…", "Loadstone Help", a separator, and "Quit Loadstone". Help opens `https://github.com/mortenbrudvik/loadstone-win#readme`.

Opening a tray menu makes Loadstone the foreground application, so the controller captures the foreground window in the menu's pre-open event and menu commands run against that captured window through `CommandDispatcher.Run(command, hwnd)`. Chord text in the menu updates when shortcuts change.

### 8.2 Settings window

A WPF-UI `FluentWindow` titled "Loadstone Settings", Mica backdrop, 520 by 560 initially with a 480 by 480 minimum, created once and hidden on close, shown and activated by the tray or by a second instance. Two tabs.

General:

- Toggle "Snap windows by dragging to screen edges" bound to `dragSnappingEnabled`.
- A caption, visible while that toggle is on: "Windows' own edge snapping is off while Loadstone runs."
- Toggle "Launch at login" bound to `IStartupRegistration`, with the status caption and an "Open Startup Apps" button when Task Manager has disabled it, and the error caption when a write failed.
- About: App "Loadstone", Version from the assembly.

Shortcuts:

- Groups titled Halves, Corners, Thirds, Window, Displays.
- One row per command: title, `ShortcutRecorder`, and a status caption that reads "In use by another app", "Could not register (error N)", or "Types € on your keyboard layout" when one applies, empty otherwise.
- "Restore defaults" at the bottom clears every customization.

`SettingsViewModel`, `ShortcutsViewModel`, and `ShortcutRowViewModel` use CommunityToolkit.Mvvm source generators. Views have no logic in code-behind. Every change persists immediately through `ISettingsService` and re-registers hotkeys through `HotkeyCenter`.

### 8.3 Shortcut recorder

`ShortcutRecorder` is a custom control with a two-way `Shortcut?` dependency property and an `IsRecording` state. Click or keyboard focus starts recording and shows "Press a shortcut". `RecorderKeyHandler` is a pure class that turns a key event (modifier state plus key) into one of: `Commit(shortcut)`, `Clear`, `Cancel`, or `Ignore`:

- a non-modifier key with at least one of Ctrl, Alt, Shift, Win commits;
- `F1`–`F24` commit with no modifier;
- Escape cancels; Backspace or Delete with no modifier clears;
- a lone modifier or any other combination is ignored.

A clear button sits at the control's right edge. While any recorder is recording, `ShortcutsViewModel` calls `HotkeyCenter.Suspend()`; commit or cancel calls `Resume()`. The control is focusable by Tab and exposes an automation name.

### 8.4 Theme

WPF-UI theme and control dictionaries in `App.xaml`, following the system theme with live switching. All colors through dynamic resources, per the template and the recorded WPF-UI pitfalls.

## 9. Application lifecycle

- `App.xaml` has no `StartupUri`; `ShutdownMode` is `OnExplicitShutdown`. `OnStartup` builds the Autofac `IHost` per the template and resolves the singletons in the Mac's order: settings, native snap applied for the drag setting, tray, hotkeys, drag monitor. `OnExit` uninstalls the drag monitor, unregisters hotkeys, restores native snap, and disposes the tray icon.
- Single instance: a named mutex `Loadstone-SingleInstance`. A second instance sets a named auto-reset event `Loadstone-ShowSettings` and exits; the running instance waits on that event from the thread pool and opens Settings on the dispatcher when it fires. A broadcast window message would not do: message-only windows never receive broadcasts, and Loadstone has no other window that is always alive.
- Unhandled exceptions on the dispatcher, the app domain, and unobserved tasks are logged as fatal, native snap is restored best-effort, and the process ends. The marker covers the case where even that fails.
- Logging: NLog behind `ILogger<T>`, async file target, daily files `Loadstone-<yyyy-MM-dd>.log` under `%LOCALAPPDATA%\Loadstone\Logs`, 14 files kept, 10 MB archive threshold. The startup summary at Info level lists drag state, hook state, native-snap state, and the effective binding and status per command. Categories are the logger type names; the README's "reading the log" section points at the folder.
- Beep: `MessageBeep(MB_OK)`.

## 10. Testing

xUnit v3, NSubstitute, Shouldly. Test-driven throughout. Test names follow `MethodName_Scenario_ExpectedResult`. Fixtures use dimensions that do not divide by 2 or 3 (1201 by 901, negative-origin secondary displays, 144 and 192 DPI) on purpose; the Mac found a real rounding bug that way.

### 10.1 Loadstone.Core.Tests

- `TileFrameTests`: every tile against odd work areas; complementary tiles sum exactly; two-thirds meets the opposite third.
- `SnapZonesTests`: priority (corner beats top beats sides beats bottom), overshoot inside and outside the padding, portrait side thirds and bottom halves, DPI scaling of the bands.
- `ScreenGeometryTests`: containment and the 1 px tolerance, center-of-rect, reading order with stacked and negative-origin displays, wrap, single display, unknown display.
- `LayoutTests`: centered clamps and centers; mapped keeps a half a half across differently sized displays; degenerate source.
- `WindowDirectorTests` with a fake window: ledger recorded after acceptance only, first frame kept across many commands, one-shot restore, refused restore keeps the entry, dead identities pruned, drag snap uses the given display, next and previous display, one display gives `NoOtherDisplay`, no displays gives `NoDisplay`.
- `DragSnapTrackerTests`: no preview before the window moves, arm on origin change, resize disarms for the drag, end equal to baseline commits nothing, outline drag commits at end, zone resolved on the display under the cursor, reset on begin.
- `WindowCommandTests`: pins ids, order, sections, titles, and defaults.
- `ShortcutTests`: parse and format round trip for every key name, canonical modifier order, case-insensitivity, rejection of empty or unknown, `Vk0x` fallback.
- `NativeSnapTests` over a fake `INativeSnapSettings`: capture then off, restore, marker written and cleared, unclean-exit recovery applies the marker before capturing, a failed read is never written, restore followed by suppress captures the values current at that time.
- `SettingsServiceTests` on a temp directory: defaults when missing, round trip, atomic write leaves no temp file, corrupt file backed up and replaced.
- `StartupStatusTests`: pure decoding of the Startup Approved bytes. The registry wrapper itself is covered in UI tests.

### 10.2 Loadstone.UI.Tests

Tests that touch real windows run on an STA thread through an STA fact attribute (the `Xunit.StaFact` package, if its current release supports xUnit v3; otherwise a small STA thread helper written in the test project). The plan verifies which.

- `Win32WindowTests` against a real window the test creates and owns, the counterpart of the Mac's `AXWindowTests` but runnable in CI: visible frame excludes the invisible borders, border insets match, `SetFrame` lands within 1 px, fixed-size window keeps its size and is centered, a maximized window is restored before placement, a destroyed window reads as unreadable.
- `WindowStylesTests`: `IsManageable` over style snapshots (tool window, desktop classes, own process, cloaked, no caption).
- `HotkeyRegistrarTests`: register and unregister, a second registration of the same chord in the same process reports `InUse`, `Suspend` releases and `Resume` re-registers.
- `RecorderKeyHandlerTests`: every rule in 8.3.
- `TrayMenuBuilderTests`: menu mirrors `WindowCommand.Sections`, gesture text follows the effective shortcut, fixed items at the end.
- `ShortcutsViewModelTests`, `SettingsViewModelTests` with substitutes: a change persists and re-registers; recording suspends and resumes hotkeys; startup toggle rollback on failure.
- `RegistryStartupRegistrationTests` against a test-only key path injected into the service, so the user's real Run key is never touched.

### 10.3 What is not unit tested

The WinEvent hook, the preview window's visuals, and the settings window's XAML are exercised by running the app, as the Mac's monitors and views are.

### 10.4 Spike: Windows edge snapping flags (done 2026-09-07)

Run during planning on the development machine (Windows 11 25H2, build 26200; primary display 3840 by 2160 at 150 percent with a second display to its left at 100 percent). A throwaway script created a test window, dragged it by its title bar with injected mouse input, and read back its rectangle, once with the flags as found and once with `DockMoving` and `SnapSizing` set to false; it restored both flags afterwards.

| Drop | Flags on | Flags off |
| --- | --- | --- |
| Left edge | snapped to the left half | left where dropped |
| Top-left corner | snapped to the top-left quarter | left where dropped |
| Top edge | maximized | left where dropped, not maximized |

Two more facts came out of it and are carried into the design: the boolean for these actions travels in `uiParam` (7.1), and a background process cannot make another process's window topmost but can make its own window topmost once it exists (6.3). The Snap Layouts flyout was not observed because it is off on that machine. The results are recorded in `CLAUDE.md` by the documentation task.

## 11. Continuous integration

`.github/workflows/ci.yml`, on push and manual dispatch, `windows-latest`: set up .NET 10, `dotnet build -c Release` (warnings are errors), `dotnet test -c Release --no-build` for both test projects, upload the test results directory on failure, and cancel an in-progress run for the same branch when a new push arrives, like the Mac workflow. GitHub's hosted Windows runners provide an interactive desktop, so the real-window tests are expected to run there; the first CI run confirms it, and if a test cannot run on the runner it is skipped there by an environment check, as the Mac skips its Accessibility tests.

## 12. Distribution

A later phase, implemented through the wpf-installer skill after the app works end to end:

- `version.json` as the single version source, applied to the csproj by `scripts/version-manager.ps1`. First Windows release is 0.1.0.
- `build/build.ps1` publishes self-contained, single-file, `win-x64`, ReadyToRun, trimming off.
- `build/build-installer.ps1` runs Inno Setup 6.3 or newer to produce `Loadstone-<version>-Setup.exe`: per-user install, no admin, Start Menu shortcut, closes a running Loadstone before upgrading (the native-snap marker makes that safe), uninstall removes the Run key value.
- Releases are GitHub Releases carrying the installer. x64 only.

## 13. Documentation

- `README.md` follows the Mac README's structure: what it is, features, default shortcuts (Windows chords), drag-to-snap zones, install (download the installer, run it), build from source (`dotnet build`, `dotnet test`), troubleshooting, how it works, name, license. Troubleshooting covers: a window that will not move (elevated process, fixed-size, app refused), a shortcut that is ignored (in use by another app, AltGr character on your layout), dragging that does not snap (title bar, shared edges), Restore does nothing (one-shot), edge snapping left off after a crash and where to put it back, reading the log.
- `docs/snap-zones.svg` is copied from the Mac repo unchanged (it is platform neutral). `docs/layouts.svg` is regenerated with Windows chords.
- `CLAUDE.md` from the wpf-scaffold template, plus an architecture section carrying the invariants over from the Mac: command ids never change, the floor rule and why fixtures are odd, place twice for DPI, the visible frame versus the outer rectangle, the native-snap marker and why recovery runs before capture, the ledger written after acceptance, the drag tracker's rules, and the spike's outcome.
- `LICENSE`: MIT, © 2026 Morten Brudvik.

## 14. Risks and how the plan handles them

| Risk | Handling |
| --- | --- |
| The two snap flags do not suppress every drag-to-edge behavior on Windows 11 | Resolved by the spike (10.4): edge, corner, and top-edge behaviors are suppressed. Only the Snap Layouts flyout is unverified, and the README covers it |
| A background process cannot make a window topmost | Resolved by the spike: it can for its own window. The preview asserts topmost from Loadstone's process on every show (6.3) |
| Some application never enters the standard move loop, so no drag events arrive for it | Accepted; hotkeys still work for it. Chrome, Electron, WPF, WinForms, UWP frames, and Win32 apps all use the standard loop |
| An STA fact attribute for xUnit v3 is unavailable | Small STA thread helper in the UI test project |
| GitHub's Windows runner cannot create windows | Environment check skips the real-window tests there, mirroring the Mac's CI skip |
| Per-monitor DPI edge cases on exotic desks | Place-twice plus tolerance logging; the Snap project's experience says this is enough |
