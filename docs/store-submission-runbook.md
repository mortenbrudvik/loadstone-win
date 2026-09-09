# First Store submission: what has to happen, in what order

The packaging work is done — `build/build-msix.ps1` produces a verified `.msix`. What remains is the
one-time Partner Center work that no API or CLI can do, and it has a trap in the ordering.

Everything after this first submission can be automated with `msstore` and
`microsoft/microsoft-store-apppublisher@v1.1`. See "After it is live" at the bottom.

## The trap: reserve the name *before* building the package you submit

`packaging/msix/AppxManifest.xml` currently ships placeholders:

```xml
Name="PUBLISHER-PLACEHOLDER.Loadstone"
Publisher="CN=PUBLISHER-PLACEHOLDER"
```

Partner Center assigns the real values **when the name is reserved**, and the package must already
carry them when it is uploaded — a package with placeholders is rejected on identity mismatch. So the
order is:

1. Reserve the name in Partner Center.
2. Read the four identity values off **Product management → Product identity**.
3. Put them in `C:\code\projects\loadstone-win\packaging\msix\AppxManifest.xml`, then rebuild:
   ```powershell
   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File C:\code\projects\loadstone-win\build\build-msix.ps1
   ```
4. *Then* upload `C:\code\projects\loadstone-win\installer-output\Loadstone-1.0.0-x64.msix`.

`build-msix.ps1` prints a warning while the placeholders are still there. Do not ignore it.

## Three things missing from this repo

These block a submission and none of them is Partner Center work:

| Missing | Why it blocks | What it needs |
| --- | --- | --- |
| **Privacy policy URL** | Store policy 10.5.1 makes one mandatory for desktop products *regardless of what is collected* | A publicly reachable page saying Loadstone collects and transmits nothing. This repo is **private**, so GitHub Pages will not serve from it as-is — either make a small public repo for the page, or host it anywhere with a stable URL |
| ~~At least one screenshot~~ **done** | Every listing requires one | `docs/store/screenshots/01-general.png` and `02-shortcuts.png`, 1920×1080 PNG. Captured from the real app at 150% scaling. Note the window uses a Mica backdrop, so its tint carries a trace of the desktop wallpaper behind it — recapture over a plain wallpaper if a neutral tint is wanted |
| **Identity values** | See the trap above | Only obtainable by reserving the name first |

## What to do in Partner Center

Sign in at <https://partner.microsoft.com/dashboard>. Account is Individual, publisher display name
**Morten Brudvik**.

### 1. Reserve the name

**Overview → New product → MSIX or PWA app.** Enter `Loadstone`, check availability, reserve.

If `Loadstone` is taken, **stop and ask** — the name appears in `AppxManifest.xml`, the README and
the app's own UI, and changing it is a code change, not a listing choice. Do not silently reserve a
variant like "Loadstone Window Manager".

A reservation expires after three months if unused.

### 2. Record the identity values — this is the most important output

**Product management → Product identity.** Copy all four **exactly**, character for character; they
are case- and punctuation-sensitive:

- `Package/Identity/Name`
- `Package/Identity/Publisher`
- `Package/Properties/PublisherDisplayName`
- the **Store ID** (12 characters, e.g. `9NBLGGH4R315`) — this is the ProductId that CI automation
  will need later

Bring these back before doing anything else. Steps 3 onward need the rebuilt package.

### 3. Upload the rebuilt package

**Packages.** Upload:

```
C:\code\projects\loadstone-win\installer-output\Loadstone-1.0.0-x64.msix
```

~83 MB (83,328,183 bytes). The path does not change between builds — `build-msix.ps1` overwrites it,
so **the file sitting there right now is the placeholder build**. Upload only the one rebuilt *after*
the manifest was updated with the real identity values.

To be certain which build is on disk, check the manifest inside it before uploading:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead('C:\code\projects\loadstone-win\installer-output\Loadstone-1.0.0-x64.msix')
$e = $z.GetEntry('AppxManifest.xml')
$r = New-Object System.IO.StreamReader($e.Open())
if ($r.ReadToEnd() -match 'PLACEHOLDER') { 'PLACEHOLDER BUILD - do not upload' } else { 'identity set - safe to upload' }
$r.Dispose(); $z.Dispose()
```

Note that `installer-output/` is gitignored, so this file exists only on the build machine — it is
not in the repository and not on GitHub.

The package is **unsigned on purpose**. The Store re-signs it. Do not sign it, and do not accept any
prompt to.

### 4. Store listing

- **Name:** Loadstone
- **Short description:** Snap any window into place: halves, corners, and thirds from the keyboard or by dragging to an edge.
- **Description:** draw from the Features list in `README.md`. Keep the honest notes — that drag
  snapping is off until enabled, and what it does to Windows' own edge snapping.
- **Required disclosure**, per policy 10.1.1 (important limitations): state that **Loadstone cannot
  move windows of elevated processes** (Task Manager, an admin terminal). That is UIPI, not a bug,
  and PowerToys documents the same limitation.
- **Screenshots:** the ones captured above.
- **Privacy policy URL:** the page from the table above.

### 5. Age rating

**Age ratings** → complete the IARC questionnaire. Loadstone has no content, no ads, no data
collection, no user interaction. Answer honestly; the result should be the lowest rating.

### 6. Restricted capability justification — read this one before pasting

**Submission options → Restricted capabilities.** The package declares `rescap:runFullTrust`, which
is mandatory for any packaged Win32 app and triggers human review.

This text is a statement to Microsoft under Morten's publisher account. **It should be reviewed by
Morten before submission, not pasted by an agent unsupervised.** It is accurate as written:

> Packaged Win32 WPF desktop application running at medium integrity level. It uses documented user32
> APIs: `RegisterHotKey` for user-configured keyboard shortcuts, an out-of-context `SetWinEventHook`
> to observe window move and size events (no DLL injection), `SetWindowPos` to reposition the window
> the user selected, and `SystemParametersInfo` to toggle Windows' own drag-to-edge snapping with
> explicit user consent. No code injection and no input interception.

### 7. Pricing, availability, properties

**Morten's decisions, not an agent's.** Free; markets and release timing as he wants them. Category:
Utilities & tools (Developer tools is defensible too).

### 8. Submit

Then wait. A first submission declaring a restricted capability takes longer than a routine update,
because the justification is read by a person.

## Do not

- **Do not sign the package.** The Store re-signs; a signature whose subject does not match the
  Partner Center publisher fails upload validation.
- **Do not invent identity values.** They come from Partner Center and nowhere else.
- **Do not reserve a different name** without asking — it is a code change.
- **Do not paste a justification other than the text above.** It describes what the code actually
  does, and it was written against the API list.
- **Do not change pricing, markets, or the age rating answers** on anyone's behalf.

## Bring back

1. The four identity values, verbatim.
2. The Store ID / ProductId.
3. Anything Partner Center rejected or warned about, quoted exactly rather than summarised.

## After it is live

Only then does automation work — the submission API creates each new submission as *a copy of the
last published one*, so a published baseline has to exist. Once it does:

- `msstore` CLI (`microsoft/msstore-cli`, actively maintained, still labelled preview)
- `microsoft/microsoft-store-apppublisher@v1.1` in GitHub Actions
  (`microsoft/store-submission` is **archived** — do not start there)
- Four secrets: tenant ID, client ID, client secret, seller ID. Needs an Entra app registration with
  the **Manager** role, associated under Account settings → Tenants.
- Ordering gotcha: `msstore publish` deletes the pending draft and recreates it from the last
  published submission, **discarding staged metadata**. Publish first, edit metadata after.
- Updates through the CLI/Action are supported for **free products only**, which fits.
- The capability approval carries forward — no need to redo the justification unless a *new*
  restricted capability is declared.
