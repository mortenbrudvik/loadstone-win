# Testing a Loadstone beta

Loadstone is a tray app that snaps windows into halves, corners and thirds — from the keyboard, or
by dragging a window to a screen edge. This page is for people trying a beta build before it reaches
the Microsoft Store.

## Before you install

**Do not run Setup as administrator.** Just double-click it. Right-clicking and choosing "Run as
administrator" makes Loadstone refuse to start normally afterwards, and if you approve the prompt as
a different administrator account, it also saves your settings under that account instead of yours.
If you have already done it, quit Loadstone from its tray icon. If the prompt only asked you to
click Yes, start it again from the Start menu. If it asked for someone else's password, Loadstone
was installed under that account, so there is nothing in your Start menu to start — double-click
Setup and install it again.

**If you already have Loadstone from the Microsoft Store**, uninstall it first, or at least turn off
its "Launch at login". Only one Loadstone can run at a time, and with two of them set to start at
sign-in the one that loses the race opens the other's Settings window at every sign-in.

**If you build Loadstone from source** and have "Launch at login" switched on there, turn it off
before installing. Windows remembers the old location, and the installed copy will not take it over
while the old files are still on disk.

Installing also closes any Loadstone that is currently running, including one started from a code
editor or a debugger.

## Installing

Windows will show a blue **"Windows protected your PC"** box, because this installer is not signed
with a certificate. That is expected for a beta. Click **More info**, then **Run anyway**.

Loadstone installs for you only — no administrator password, nothing added for other people on the
machine. It goes into your own program folder and adds a Start menu entry.

When Setup finishes, Loadstone starts and its Settings window opens by itself. That is intentional
on a first install: drag-to-snap is off until you turn it on, because switching it on also changes
one of Windows' own settings.

**Where the tray icon went.** Loadstone has no window of its own — it lives in the tray, next to the
clock. Windows hides icons it has not seen before, so look under the **^** arrow at the left end of
the tray. Drag it out onto the tray to keep it visible.

## Updating

Download the newer Setup and run it. It closes the running Loadstone, replaces it, and starts it
again. Your settings and shortcuts are kept.

Running an *older* Setup over a newer install is refused, with a message saying so.

## When something goes wrong

Send back both of these:

- The app log: press <kbd>Win</kbd>+<kbd>R</kbd>, paste `%LOCALAPPDATA%\Loadstone\Logs` and press
  Enter. Take the newest file. (Settings has an **Open log folder** button that goes to the same
  place.)
- The installer log, if the problem was during install: press <kbd>Win</kbd>+<kbd>R</kbd>, paste
  `%TEMP%` and press Enter, and take the newest `Setup Log*.txt`.

Please say which build you were running — search the app log for the line containing `launched:`,
which names the version and the folder it started from.

## Uninstalling

Settings → Apps → Installed apps → Loadstone → Uninstall. That removes the program and the "start
at sign-in" entry.

Two things it deliberately leaves behind:

- **Your settings, shortcuts and logs**, at `%LOCALAPPDATA%\Loadstone`. They are kept so that
  reinstalling a newer beta does not lose your shortcut customizations. To clear them, delete that
  folder yourself.
- **Windows' own drag-to-edge snapping stays off** if you had Loadstone's drag snapping switched on,
  until you sign out and back in. Loadstone turns that Windows setting off while it is running, and
  an uninstall does not get the chance to turn it back on. Signing out and back in restores it.
  Settings → System → Multitasking will show **Snap windows** as already on the whole time —
  Loadstone changes the setting only for the current session, without changing what Windows has
  recorded — so that switch will not tell you whether snapping is really on.
