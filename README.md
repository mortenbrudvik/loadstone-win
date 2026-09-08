# Loadstone for Windows

<p align="center">
  <strong>Snap any window into place.</strong><br>
  Halves, corners, and thirds — from the keyboard or by dragging to an edge.<br>
  A native tray app. No browser engine, no background service.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-blue" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-purple" alt=".NET 10">
  <img src="https://img.shields.io/badge/status-early-blue" alt="Early">
</p>

Loadstone sits in the tray and stays out of the way. When you need two documents side by side, a browser in a third, or a window parked in a corner, it moves the focused window to a precise tile of the display's work area — the taskbar is left uncovered. It is the Windows port of the [macOS Loadstone](https://github.com/mortenbrudvik/loadstone), with the same commands and the same defaults.

<p align="center">
  <img src="docs/layouts.svg" alt="Halves, corner quarters, and thirds with their default shortcuts" width="960">
</p>

## Features

- **Halves** — left, right, top, bottom
- **Corners** — four quarters
- **Thirds** — left, center, right, plus two-thirds
- **Maximize, center, restore** — restore puts the window back where it was before Loadstone first moved it
- **Drag to snap** — edges for halves, corners for quarters, top to maximize, bottom for thirds. Only a window that is actually being moved snaps; selecting text or resizing near an edge does nothing
- **Multi-display** — send a window to the next or previous display, walking displays left to right as they sit on the desk, with the window's size scaled for the display's DPI
- **Custom shortcuts** — Magnet-style defaults, all editable, with a warning when a chord is taken by another app or types a character on your keyboard layout
- **Launch at login** — optional, from Settings

Loadstone turns off Windows' own drag-to-edge snapping while its drag snapping is on, so the two don't fight, and turns it back on when it quits or when you switch drag snapping off. If Loadstone is killed before it can put the setting back, the next launch notices and restores it. Win+Arrow and Snap Layouts are untouched.

## Default shortcuts

All of these are Ctrl+Alt unless noted. Change them in **Settings → Shortcuts**.

| Layout | Shortcut |
| --- | --- |
| Left / right / top / bottom half | `Ctrl+Alt+←` `Ctrl+Alt+→` `Ctrl+Alt+↑` `Ctrl+Alt+↓` |
| Top left / top right corner | `Ctrl+Alt+U` `Ctrl+Alt+I` |
| Bottom left / bottom right corner | `Ctrl+Alt+J` `Ctrl+Alt+K` |
| Left / center / right third | `Ctrl+Alt+D` `Ctrl+Alt+F` `Ctrl+Alt+G` |
| Left / right two thirds | `Ctrl+Alt+E` `Ctrl+Alt+T` |
| Maximize | `Ctrl+Alt+Enter` |
| Center (keeps the current size) | `Ctrl+Alt+C` |
| Restore | `Ctrl+Alt+Backspace` |
| Next / previous display | `Ctrl+Alt+Win+→` `Ctrl+Alt+Win+←` |

The corner keys are a square on the keyboard:

```
U  I
J  K
```

On Norwegian, German, and most other European layouts, AltGr is Ctrl+Alt to Windows, so `Ctrl+Alt+E` also captures the `€` key while Loadstone runs. Settings shows "Types € on your keyboard layout" next to any such binding; rebind it if you type that character.

## Drag to snap

Drag a window by its title bar until the pointer reaches an edge or corner of the display. A blue preview shows the tile; let go to snap. Only a window that is actually moving snaps, so selecting text or dragging a slider to an edge does nothing.

<p align="center">
  <img src="docs/snap-zones.svg" alt="Drag zones: corners give quarters, the sides give halves, the top edge maximizes, the bottom edge gives thirds; on a portrait display the sides give corner, vertical third, corner and the bottom edge gives halves" width="960">
</p>

- **Corners** — a quarter. The corner zones are 140 px squares (scaled with the display) and win over the edges.
- **Left and right edges** — a half.
- **Top edge** — maximize.
- **Bottom edge** — a third, chosen by where along the edge you let go.
- **Portrait displays** — the sides give corner, vertical third, corner along their length, and the bottom edge gives halves.
- **More than one display** — every display has its own zones, including the edge it shares with a neighbor. The pointer does not stop at a shared edge the way it does at an outer one, so slow down there and let go while the preview is showing. The window snaps onto the display the pointer is on.

The edge zones are 16 px deep at 100% scaling and extend the same distance past an outer edge, so overshooting still counts.

## Install

Download `Loadstone-<version>-Setup.exe` from the [latest release](https://github.com/mortenbrudvik/loadstone-win/releases/latest) and run it. It installs for the current user, no administrator rights needed, and adds a Start Menu entry. Windows 10 or 11, x64.

Optional: **Settings → Launch at login**.

### Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/mortenbrudvik/loadstone-win.git
cd loadstone-win
dotnet build Loadstone.slnx
dotnet run --project src/Loadstone.UI
```

## Troubleshooting

Tray → Loadstone → **Loadstone Help** opens this page.

**One window will not move.** Windows of programs running as administrator cannot be moved by a program that isn't, and some windows are fixed-size. Loadstone beeps and logs the refusal; everything else keeps working. Fixed-size windows are centered in the tile instead of resized.

**A shortcut does nothing.** Check the Shortcuts tab: a chord held by another app is captioned "In use by another app". Close that app or rebind the chord. Graphics-driver utilities and file-manager tools commonly hold Ctrl+Alt+arrow chords, so they are the usual culprit. If the row instead says "Types € on your keyboard layout", the chord doubles as an AltGr character on your layout and captures it while Loadstone runs; rebind if you need the character.

**Dragging to an edge does not snap.** Drag by the title bar; the window has to move before Loadstone treats the gesture as a window drag. On the edge between two displays the pointer passes straight through, so slow down and release while the preview is showing. If Windows' own Snap Layouts flyout appears at the top edge, turn it off under Settings → System → Multitasking → Snap windows.

**Restore does nothing.** Restore is one-shot: it puts the window back where it was before Loadstone first moved it, then forgets. The next Loadstone command starts a new memory. The memory is also dropped when the window closes or Loadstone restarts.

**Next / previous display beeps.** Only one display is attached.

**Windows' own edge snapping is off after Loadstone crashed or was killed.** Loadstone turns it off while its drag snapping is on and back on when it quits normally; after an unclean exit, simply launching Loadstone again restores it. To put it back by hand, use Settings → System → Multitasking → Snap windows (`ms-settings:multitasking`).

**Launch at login is on but Loadstone does not start.** Task Manager → Startup apps may have it disabled; Settings shows "Disabled in Task Manager" with a button to open that page.

**Reading the log.** Loadstone writes daily logs to `%LOCALAPPDATA%\Loadstone\Logs`. The startup summary lists the effective binding and status of every shortcut.

## Build and test

```powershell
dotnet build Loadstone.slnx                                   # warnings are errors
dotnet test --solution Loadstone.slnx                         # every test project
dotnet test --project tests/Loadstone.Core.Tests/Loadstone.Core.Tests.csproj
dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-query "/*/*/Win32WindowTests/*"
dotnet run --project src/Loadstone.UI
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1   # regenerate the .ico assets
```

`global.json` opts the repo into the Microsoft.Testing.Platform runner (xunit.v3), so the classic `dotnet test Loadstone.sln` and `--filter`/`--logger` switches do not apply; use `--solution`/`--project`, `--filter-query`, and `--report-trx`.

## How it works

Loadstone is a tray-only WPF app. Shortcuts and drag gestures go to a small window director that:

1. Finds the window (the foreground window, or the one being dragged)
2. Reads its visible frame, which excludes Windows' invisible resize borders
3. Lays out a tile against the display's **work area**
4. Writes the frame, then writes it once more if the window resized itself on a display with a different DPI
5. Reports what the window answered: a refused write makes Loadstone beep and log

Drag snapping listens to Windows' own move-loop events instead of the mouse, so a text selection or a slider dragged to a screen edge is left alone, and the snap is applied after Windows has finished the drop.

## Name

A lodestone is a naturally magnetic rock. Loadstone is the window magnet.

## License

[MIT](LICENSE) © 2026 Morten Brudvik.
