# Brief: first Microsoft Store submission for Loadstone

You are working in the Microsoft Partner Center web dashboard on behalf of Morten Brudvik. The app is
finished and packaged; what remains is the one-time submission work that no API or CLI can do.

**This cannot be completed in one pass.** It splits at a hard handback, because Partner Center
assigns identity values that must go *into the package* before the package is uploaded. Phase 1 ends
by reporting those values. Do not attempt Phase 2 until Morten confirms the package has been rebuilt.

---

## The app, in one paragraph

Loadstone is a free, tray-only window-tiling utility for Windows. It snaps the focused window into
halves, corners and thirds from global keyboard shortcuts or by dragging a window to a screen edge.
Publisher: **Morten Brudvik**. Individual account. Free product. x64 only. Windows 10 build 19041 or
later. It collects and transmits nothing.

---

## PHASE 1 — Reserve the name and report back

### 1. Reserve

**Overview → New product → MSIX or PWA app.** Name: `Loadstone`. Check availability, reserve.

> **If `Loadstone` is unavailable, STOP and report it.** Do not reserve a variant such as "Loadstone
> Window Manager" or "Loadstone Tiler". The name appears inside the app's own code, its window
> titles and its documentation — changing it is a code change that Morten has to make, not a
> listing choice you can make for him.

A reservation expires after three months if unused.

### 2. Collect the identity values

**Product management → Product identity.** Copy these **exactly** — character for character, case
and punctuation included. A single wrong character causes the upload in Phase 2 to be rejected:

| Field | Looks like |
| --- | --- |
| `Package/Identity/Name` | `12345SomePublisher.Loadstone` |
| `Package/Identity/Publisher` | `CN=ABCD1234-5678-...` |
| `Package/Properties/PublisherDisplayName` | usually the publisher display name |
| **Store ID** | 12 characters, e.g. `9NBLGGH4R315` |

### 3. Stop and report

Report all four values plus the Store ID, and anything Partner Center warned about. **Then stop.**

The package on Morten's machine currently carries placeholder identity values. It cannot be uploaded
until it is rebuilt with the real ones, and that happens on his machine, not in the browser.

---

## PHASE 2 — Only after Morten confirms the package is rebuilt

### 4. Upload the package

**Packages.** Upload the `.msix` Morten provides (~83 MB).

> **Do not sign the package, and do not accept any prompt offering to.** It is unsigned deliberately.
> The Store re-signs it during certification, and a signature whose subject does not match the
> Partner Center publisher causes the upload to fail validation.

If the upload is rejected, quote the exact error rather than summarising it.

### 5. Store listing

- **Name:** Loadstone
- **Short description:** Snap any window into place: halves, corners, and thirds from the keyboard or by dragging to an edge.
- **Description:** cover the features — halves, corners, thirds and two-thirds; maximize, center and a
  one-shot restore; drag a window to a screen edge to snap it; send a window to the next or previous
  display; fully editable keyboard shortcuts; optional launch at login. Mention that drag-to-edge
  snapping is off until the user turns it on, and that turning it on temporarily switches off
  Windows' own drag-to-edge snapping so the two do not fight.
- **Required limitation disclosure** — Store policy 10.1.1 requires important limitations to be
  stated. Include, in the description: **Loadstone cannot move windows belonging to elevated
  processes** (for example Task Manager or an administrator terminal). This is a Windows security
  restriction, not a defect; Microsoft's own PowerToys documents the same limitation.
- **Screenshots:** the two PNGs Morten provides (1920×1080).
- **Privacy policy URL:** Morten provides this. **If he has not, stop and ask** — it is mandatory
  and the submission cannot proceed without it.

### 6. Age rating

**Age ratings** → complete the IARC questionnaire. Loadstone has no content, no advertising, no data
collection, no user-to-user interaction, no purchases. Answer accurately; expect the lowest rating.
**Do not guess at any question you are unsure of** — ask.

### 7. Restricted capability justification

**Submission options → Restricted capabilities.** The package declares `runFullTrust`, which is
mandatory for any packaged Win32 desktop app and triggers review by a person at Microsoft.

Use this text exactly as written. **Morten should read it before it is submitted** — it is a
statement made to Microsoft under his publisher account:

> Packaged Win32 WPF desktop application running at medium integrity level. It uses documented user32
> APIs: RegisterHotKey for user-configured keyboard shortcuts, an out-of-context SetWinEventHook to
> observe window move and size events (no DLL injection), SetWindowPos to reposition the window the
> user selected, and SystemParametersInfo to toggle Windows' own drag-to-edge snapping with explicit
> user consent. No code injection and no input interception.

Do not reword it. It was written against the actual list of APIs the code calls, and a reviewer may
check it against the binary.

### 8. Pricing, availability, properties

Free. **Markets, release timing and category are Morten's decisions** — ask rather than choosing.
Suggested category: Utilities & tools.

### 9. Submit, then report

Report the submission ID and status. A first submission that declares a restricted capability takes
longer than a routine update, because the justification is read by a human.

---

## Stop and ask — do not improvise

- The name `Loadstone` is unavailable
- No privacy policy URL has been provided
- Partner Center rejects the package, for any reason
- Any IARC question whose honest answer is unclear
- Anything asking for payment details, bank information or tax forms
- Any request to change the account itself

## Never

- Sign the package
- Invent, guess or "correct" an identity value — they come from Partner Center and nowhere else
- Reserve a different name
- Reword the capability justification
- Choose pricing, markets or age-rating answers on Morten's behalf
- Submit while any of the above is unresolved

## Report back

1. The four identity values, verbatim, plus the Store ID
2. Every warning or rejection, quoted exactly rather than summarised
3. What you did not do, and why

---

*Repo-side reference, for whoever is rebuilding the package:
`docs/store-submission-runbook.md` — the paths, the rebuild command, and how to check which build is
on disk.*
