# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Loadstone for Windows is a tray-only WPF window tiler (.NET 10, WPF-UI, Win32 through `LibraryImport`). It tiles the focused window into halves, corners, and thirds from global hotkeys or by dragging a window to a screen edge. It is the Windows port of the macOS Loadstone at `C:\code\projects\loadstone`, which remains the authority on behavior: same command ids, same tile math, same drag zones, same one-shot Restore. Architecture: **Pure WPF** (see `docs/superpowers/specs/2026-09-07-loadstone-win-design.md`).

## Commands

```powershell
dotnet build Loadstone.slnx                                   # warnings are errors
dotnet test --solution Loadstone.slnx                         # every test project
dotnet test --project tests/Loadstone.Core.Tests/Loadstone.Core.Tests.csproj
dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-query "/*/*/Win32WindowTests/*"
dotnet run --project src/Loadstone.UI
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1   # regenerate the .ico assets
```

`global.json` opts the repo into the Microsoft.Testing.Platform runner (xunit.v3), so the classic `dotnet test Loadstone.sln` and `--filter`/`--logger` switches do not apply; use `--solution`/`--project`, `--filter-query`, and `--report-trx`.

The 32-bit `pwsh` is first on PATH on the development machine; scripts that touch Win32 must run under the 64-bit one above. Real-window tests run under `[WpfFact]` and need a desktop session. They are expected to run on GitHub's windows-latest runner; if the first CI run cannot create windows, gate `TestWindow` on `GITHUB_ACTIONS` as the Mac skips its Accessibility tests.

Reading the app's log: `%LOCALAPPDATA%\Loadstone\Logs\Loadstone-<date>.log`. Settings: `%LOCALAPPDATA%\Loadstone\settings.json`.

## Architecture

Data flows one direction: input → `CommandDispatcher` / `DragSnapMonitor` → `WindowDirector` → `Win32Window` → `SetWindowPos`.

- **Core (`src/Loadstone.Core`)** has no Win32 and no WPF. `Tile.Frame` floors the left and top pieces and gives the remainder to the right and bottom ones so complementary tiles meet on odd sizes. `SnapZones` maps the cursor against the *full* monitor frame (tiles are laid into the *work area*) and scales the 16 px bands and 140 px corners with the display's DPI. `ScreenGeometry.Neighbor` walks displays in reading order (x, then y). `WindowDirector` keeps the restore ledger: written only after a window accepts a frame, one-shot Restore, dead identities pruned before every command. `DragSnapTracker` arms once the dragged window's origin changes with its size unchanged, disarms on a resize, and commits at the end unless the frame is back at its baseline (Escape). `NativeSnap` captures Windows' edge-snapping flags once, parks them in settings as a crash marker, and restores them; recovery runs before capture.
- **UI (`src/Loadstone.UI`)**. `Interop/` holds every Win32 declaration. `Win32Window` is the only type that knows about invisible borders and DPI: `Frame` is the DWM extended frame bounds, `SetFrame` restores a maximized or minimized window first, expands the target by the border insets, and writes once more when the frame lands off target after a DPI change. `HotkeyRegistrar` uses `RegisterHotKey` on a message-only window; `HotkeyCenter` resolves the effective chord per command and records in-use and keyboard-layout warnings. `WinEventMoveSizeEvents` hooks `EVENT_SYSTEM_MOVESIZESTART`/`END`; `DragSnapMonitor` samples the cursor at 60 Hz during a drag and commits after the OS finishes the drop. `SnapPreview` asserts topmost from its own process on every show. `TrayController` captures the foreground window before the menu opens.

### Invariants worth knowing before editing

- `WindowCommand` is the single source of truth for hotkeys, the tray menu, and the Shortcuts tab. Its ids (`leftHalf` … `previousDisplay`) are settings keys and match the Mac app; never rename one. `WindowCommandTests` pins the list. Switch expressions over `Tile` and `WindowCommand` have no discard arm on purpose, and CS8524 is suppressed so a new member fails the build until every switch covers it.
- Coordinates are physical pixels, y down, everywhere. The app manifest declares Per-Monitor V2; the test host opts in through a module initializer.
- `SystemParametersInfo` for `SPI_SETDOCKMOVING` and `SPI_SETSNAPSIZING` takes the BOOL in `uiParam` with `pvParam` zero. Passing it in `pvParam` silently writes FALSE (found the hard way on 2026-09-07).
- A background process cannot make another process's window topmost, but can make its own window topmost once it exists. The preview relies on the second fact; never ask another process's window to change z-order.
- Test fixtures use 1201 by 901 and negative-origin displays on purpose; even sizes hide rounding bugs.
- Every `ISettingsService.Changed` subscriber must be flip-detecting or idempotent: `NativeSnap` parks its crash marker through the same service, so writing the marker raises `Changed` and re-enters every handler.
- `RegistryStartupRegistration.Enable()` writes the Run value *and* deletes `StartupApproved\Run\Loadstone`. That is the one write outside the Run key, and it is deliberate: without it Task Manager's disable outlives re-enabling from Settings. `RefreshPathIfStale()` rewrites the Run value only when the path it holds is gone from disk, so a `dotnet run` build never steals an installed copy's login item.
- Every test that creates a window runs under `[WpfFact]`. Tests never touch the real Run key (they use keys under `Software\LoadstoneTests` and delete that parent when they leave it empty), the real settings file (temp directories), the production single-instance names (each test makes its own), or change the snap flags (they write back the value they read).
- `src/Loadstone.UI/Loadstone.UI.csproj` sets `AllowUnsafeBlocks` because `LibraryImport` source generation requires it (SYSLIB1062). Core does not and must not need it.
- `System.Windows.Rect`/`Point` clash with `Loadstone.Core.Geometry.Rect`/`Point`; files that import both alias the Core type (`using Rect = Loadstone.Core.Geometry.Rect;`), see `src/Loadstone.UI/Input/SnapPreview.cs`.
- Fixed-size windows (no `WS_THICKFRAME`) keep their size on a tile command and are centered on the target frame, overhanging it if larger (spec 5.2).
- xUnit analyzers run as errors: blocking waits in tests take `TestContext.Current.CancellationToken` (xUnit1051).

### Spike results (2026-09-07, Windows 11 25H2 build 26200)

With `DockMoving` and `SnapSizing` off, a window dragged to the left edge, a corner, or the top edge is left where it was dropped; with them on it snaps to the half, the quarter, or maximizes. The Snap Layouts flyout could not be observed (off on that machine) and is documented in the README instead of managed.
