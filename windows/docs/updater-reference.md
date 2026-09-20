# getEQd update reference: how TaxMan does it

Purpose: record, from source, exactly how the user's TaxMan desktop app checks for and
applies updates, so getEQd's "Stage 4: Help > Check for Updates" can mirror the existing
pattern instead of inventing a new one. Everything below was read on 2026-09-20 from the
TaxMan source tree. Nothing in TaxMan was modified, built, or run.

## What was inspected, and why

- **Inspected (the real app source): `C:\Users\phill\Documents\Tax Workspace`.**
  This is an Electron app: `package.json` has `"main": "src/main.cjs"`, plus `src/`,
  `scripts/`, `test/`, `.github/workflows/`, and `dist/`. The updater lives here.
- **Not the app: `C:\Users\phill\Documents\Tax`.** This is Electron user-data (Chromium
  profile): `Local State`, `Preferences`, `Cache`, `Network`, `GPUCache`. No source.
- **Not the app: `C:\Users\phill\Documents\Tax Site Publish 20260916`.** Site publish
  artifacts: `*-site.tar.gz` bundles plus a `source` folder for the public site.
- **Not the app: `C:\Users\phill\Documents\PDH Tax Records`.** Ledger data
  (`data.json`, backups). No code.

Files read in Tax Workspace:

| File | Role |
| --- | --- |
| `src/main.cjs` | Updater wiring, GitHub feed config, dialog copy |
| `src/menu.cjs` | The Help menu item that triggers a check |
| `src/preload.cjs` | `contextBridge` IPC surface exposed to the renderer |
| `src/renderer.js` | Menu action handler and status toasts |
| `src/update-errors.cjs` | Error taxonomy, codes, and user-facing messages |
| `src/web-bridge.js` | Mobile/web stub for the same action |
| `package.json` | Version, `electron-updater` dependency, `build.publish` GitHub config |
| `.github/workflows/release.yml` | How release assets and `latest.yml` get published |
| `dist/latest.yml` | Generated update channel file (sample of the real feed) |
| `dist/win-unpacked/resources/app-update.yml` | Generated feed config shipped in the app |
| `test/update-errors.test.cjs` | Asserted error copy |
| `README.md` | User-facing promise of the behavior |

## 1. Where the update check lives and what triggers it

Single trigger: a Help menu item. No timer, no startup check.

`src/menu.cjs` (Help submenu):

```js
{
  label: 'Help',
  submenu: [
    { label: 'Check for Updates...', click: send('check-for-updates') },
    ...
  ]
}
```

`send` is `(action) => () => dispatch(action)`, and `dispatch` is `sendMenuAction`
(`src/main.cjs:308`), which posts `menu:action` to the renderer. The renderer handles it in
`src/renderer.js:175`:

```js
if (action === 'check-for-updates') {
  state.openMenu = null;
  const result = await window.taxLedger.checkForUpdates();
  if (result?.status === 'unavailable' || result?.status === 'error')
    toast(result.message || 'TaxMan could not check for updates.', true);
  else if (result?.status === 'checking')
    toast(result.message || 'Checking for a TaxMan update...');
}
```

`window.taxLedger.checkForUpdates` is the preload bridge (`src/preload.cjs:18`):

```js
checkForUpdates: () => ipcRenderer.invoke('app:check-for-updates'),
```

Which reaches `src/main.cjs:535` (`ipcMain.handle('app:check-for-updates', ...)`) and calls
`checkForUpdates()` at `src/main.cjs:369`.

Updater event handlers are registered once at startup, not per check: `app.whenReady()`
calls `configureAutoUpdater()` (`src/main.cjs:568`). `configureAutoUpdater()`
(`src/main.cjs:323`) returns immediately on non-Windows.

## 2. Mechanism

It uses the `electron-updater` package (`package.json` dependency `electron-updater:
^6.8.9`), imported at `src/main.cjs:4`:

```js
const { autoUpdater } = require('electron-updater');
```

- **Current version** comes from Electron's `app.getVersion()`, exposed over IPC as
  `app:version` (`src/main.cjs:536`) and read by the renderer at startup
  (`src/renderer.js:18`). It resolves to `package.json` `"version": "0.4.18"`.
- **Does it call GitHub itself?** No. The app contains no HTTP call, URL string, or shell
  invocation for updates. It delegates entirely to `electron-updater`, which reads the
  feed descriptor shipped inside the installed app:
  `dist/win-unpacked/resources/app-update.yml`:

  ```yaml
  owner: oppdown
  repo: TaxMan
  provider: github
  releaseType: release
  updaterCacheDirName: taxman-updater
  ```

  That file is generated from `package.json` `build.publish`:

  ```json
  "publish": [{ "provider": "github", "owner": "oppdown", "repo": "TaxMan", "releaseType": "release" }]
  ```

  The git remote confirms the target: `origin  https://github.com/oppdown/TaxMan.git`.
- **Transport:** Electron preload + IPC (`contextBridge.exposeInMainWorld('taxLedger', ...)`
  in `src/preload.cjs`). Main process owns the network work; the renderer only receives
  status events via `onUpdateStatus` (`app:update-status`).
- **Configuration set at startup** (`src/main.cjs:325-326`):
  `autoUpdater.autoDownload = false` and `autoUpdater.autoInstallOnAppQuit = true`.

The check function itself (`src/main.cjs:369`):

```js
async function checkForUpdates() {
  if (!app.isPackaged || process.platform !== 'win32')
    return { status: 'unavailable', message: 'Automatic updates are available in the installed Windows version of TaxMan.' };
  if (updateCheckInProgress) return { status: 'checking', message: 'TaxMan is already checking for updates.' };
  updateCheckInProgress = true;
  try {
    await autoUpdater.checkForUpdates();
    return { status: 'checking', message: 'Checking for a TaxMan update...' };
  } catch (error) {
    const failure = sendUpdateError(error);
    return { status: 'error', message: failure.message, code: failure.code };
  } finally { updateCheckInProgress = false; }
}
```

**Which endpoint is hit (inference).** The app code does not name a URL, so the concrete
endpoint is inferred from `electron-updater`'s documented GitHub provider behavior plus the
config above: the library resolves the channel file `latest.yml` from the repository's
latest GitHub release (`https://github.com/oppdown/TaxMan/releases/latest/download/latest.yml`
or the equivalent `api.github.com` release endpoint), then reads the version and asset
names from that YAML. This inference is corroborated by `dist/latest.yml` being the exact
file the release workflow uploads:

```yaml
version: 0.4.16
files:
  - url: TaxMan-0.4.16-Setup.exe
    sha512: QicOnP0bnXQFxtjl9TYnYbCVJVUaZ7II9shtlbKgf7O/jZoR2v7murMBNGtlqBo73fy2mG9I4hAKnt92iPqc3A==
    size: 115102720
path: TaxMan-0.4.16-Setup.exe
sha512: QicOnP0bnXQFxtjl9TYnYbCVJVUaZ7II9shtlbKgf7O/jZoR2v7murMBNGtlqBo73fy2mG9I4hAKnt92iPqc3A==
releaseDate: '2026-09-18T17:04:13.658Z'
```

## 3. User-visible states and copy

Update lifecycle copy is emitted from `configureAutoUpdater()` (`src/main.cjs:327-366`).
Errors go through `describeUpdateError` (`src/update-errors.cjs`), which appends a stable
code to every message.

| State | Trigger | Exact copy |
| --- | --- | --- |
| Checking | `checking-for-update` | `Checking for a TaxMan update...` |
| Already current | `update-not-available` | `TaxMan is up to date.` |
| Update available | `update-available` | Dialog: title `TaxMan update available`, message `TaxMan <version> is ready to download.`, detail `TaxMan will download the update and ask before restarting. Your local records will remain in place.`, buttons `Download update` / `Later`. Status event: `TaxMan <version> is available.` If deferred: `TaxMan <version> is available whenever you are ready.` |
| Downloading | `download-progress` | `Downloading the TaxMan update... <n>%` |
| Downloaded / ready | `update-downloaded` | Dialog: title `TaxMan update ready`, message `TaxMan <version> has been downloaded.`, detail `Restart TaxMan now to finish installing the update, or choose Later and install it the next time you close the app.`, buttons `Restart and install` / `Later`. Status event: `TaxMan <version> is ready to install.` |
| Offline / network failure | error with `ERR_NETWORK`, `ECONNRESET`, `ETIMEDOUT`, `ENOTFOUND`, `ECONNREFUSED`, or a message matching `network\|internet\|connect\|socket\|timed out` | `TaxMan could not reach the update service. Check your internet connection and try again. (TAXMAN-UPDATE-003)` |
| Malformed / invalid feed | `ERR_UPDATER_INVALID_RELEASE_FEED` or message matching `invalid feed` / `release feed` | `TaxMan received invalid update information. Please try again later or install the latest version manually. (TAXMAN-UPDATE-004)` |
| Missing config | `ENOENT` on `app-update.yml` | `TaxMan could not find its update configuration. Reinstall the latest Windows version, then try Help > Check for Updates again. (TAXMAN-UPDATE-001)` |
| Config unreadable | `EACCES` on `app-update.yml` | `TaxMan could not read its update configuration because access was denied. Close TaxMan and run the latest installer again. (TAXMAN-UPDATE-002)` |
| Any other error | fallback | `TaxMan could not check for updates. (TAXMAN-UPDATE-099)` |
| Not installed / non-Windows | `!app.isPackaged \|\| platform !== win32` | `Automatic updates are available in the installed Windows version of TaxMan.` |

Transcription note: TaxMan's source uses the single-character ellipsis (U+2026) in its
user-facing strings, for example the menu label `Check for Updates\u2026` and
`Checking for a TaxMan update\u2026`. This document normalizes those to three dots for a
plain-ASCII file; the words are otherwise verbatim.

Two more visible paths:

- The renderer only toasts a subset of statuses (`src/renderer.js:1071`,
  `handleUpdateStatus`): `error` (as a failure toast), `not-available`, and `downloaded`.
  `available`, `checking`, and `downloading` are surfaced by the main-process dialog and by
  the return value of `checkForUpdates()`, not by the status toast.
- In the mobile/web build, the same menu action does not check anything; it opens the
  download page in a new tab (`src/web-bridge.js:126`):
  `window.open('https://taxman.speedy-star-8288.chatgpt.site/download.html', '_blank')`.

**Rate-limited (HTTP 403/429) is not handled as its own state.** `src/update-errors.cjs`
matches only `ENOENT`/`EACCES` on `app-update.yml`, `ERR_UPDATER_INVALID_RELEASE_FEED`,
and network-shaped errors. A GitHub API 403 rate-limit response matches none of those
patterns, so it falls through to the generic `TAXMAN-UPDATE-099` message. This is observed
from the regexes in the source, and is the single most important gap for getEQd to fix.

## 4. How the update is applied

Observed behavior:

1. `update-available` shows a `Download update` / `Later` dialog. Download starts only on
   explicit confirmation (`await autoUpdater.downloadUpdate()`, `src/main.cjs:345`).
2. The download target is the installer named in the feed (`TaxMan-<version>-Setup.exe`),
   matching `package.json` `nsis.artifactName: "TaxMan-${version}-Setup.${ext}"`. The
   release workflow uploads `dist/TaxMan-$version-Setup.exe` plus its `.blockmap` and
   `dist/latest.yml`.
3. Integrity: the app performs no custom hash or signature check. `electron-updater`
   verifies the downloaded file against the `sha512` + `size` recorded in `latest.yml`
   (present in `dist/latest.yml` above). The app itself is not code-signed.
4. On `update-downloaded`, a `Restart and install` / `Later` dialog calls
   `autoUpdater.quitAndInstall()` on confirmation. Because
   `autoInstallOnAppQuit = true`, choosing `Later` installs on the next normal exit.

It downloads and installs; it does not open a browser page (that is only the mobile/web
stub). It does not verify a signature.

## 5. Failure modes not worth copying

1. **No dedicated rate-limit state.** 403/429 (GitHub unauthenticated limit is 60
   requests/hour/IP) becomes the generic `TAXMAN-UPDATE-099`. See section 3.
2. **Silent status drops when the window is gone.** `sendUpdateStatus` and `sendUpdateError`
   no-op if `mainWindow` is missing or destroyed (`src/main.cjs:312-314`), so a check
   started with no window produces no user feedback at all.
3. **The in-progress guard releases too early.** `updateCheckInProgress` is reset in
   `finally` right after `await autoUpdater.checkForUpdates()` resolves
   (`src/main.cjs:372-379`), but the real outcome arrives later via events. A quick second
   click can therefore start a parallel check.
4. **Success is reported as "checking".** `checkForUpdates()` returns `{status:'checking'}`
   on a successful call. The actual result is only delivered through the
   `app:update-status` event. If that event path is broken, the user sees "checking"
   indefinitely with no error (inference from the return shape plus the
   `onUpdateStatus` subscription).
5. **No explicit timeouts.** The code sets none; offline detection depends entirely on
   `electron-updater` emitting an `error` event. There is no getEQd-style bounded wait.
6. **Hard-coded version literals that can drift.** `package.json` is `0.4.18`, but
   `src/renderer.js:6` also defaults `appVersion: '0.4.18'` and `src/web-bridge.js:127`
   returns the literal `'0.4.18'`. The desktop path overwrites the renderer default from
   `app.getVersion()`, but the mobile literal must be bumped by hand each release.
7. **Raw error text is logged with local paths** (`console.warn` in
   `sendUpdateError`, `src/main.cjs:318`). User-facing copy is redacted, so this is a
   logging concern only.
8. **Manual-only checks.** There is no startup check and no timer; users only learn of a
   release if they open Help. `README.md:23` documents this as intentional, so it is a
   choice to make deliberately, not a bug to inherit.

## 6. Adaptation for getEQd (WPF / C#)

Reuse the shape, not the library. getEQd has no Electron, so `electron-updater` and the
preload/IPC layer do not transfer; the *contract* does.

**Reuse as-is (concepts and contracts):**

- A `Help > Check for Updates...` entry as the single manual trigger, with a status event
  model that has a stable error-code taxonomy (`TAXMAN-UPDATE-00x` maps cleanly to
  `GETEQD-UPDATE-00x`).
- The GitHub provider config: `owner` / `repo` / `releaseType: release`, mirrored in
  getEQd's own GitHub repository (Stage 2). Asset naming `getEQd-<version>-Setup.exe`.
- The `latest.yml` channel-file idea. A C# client can fetch it and read `version`,
  `path`, `sha512`, and `size` the same way, which also gives size and hash verification
  for free without a signing certificate.
- The four-state copy structure and the "install on next exit" fallback.

**Must change for WPF / C#:**

- Replace `electron-updater` with a `HttpClient` call. Either the GitHub REST endpoint
  (`https://api.github.com/repos/<owner>/<repo>/releases/latest`) or the channel asset
  (`.../releases/latest/download/latest.yml`). Set `HttpClient.Timeout` (for example 10 s),
  check `response.StatusCode` explicitly, and parse with a real YAML/JSON parser.
- Replace `app.getVersion()` with the assembly version (`AssemblyInformationalVersion` or
  `FileVersionInfo` on `getEQd.exe`), read once and cached.
- Replace `dialog.showMessageBox` and the preload/IPC channel with WPF dialogs plus an
  `async` `Task`-based call on the UI thread; never block the dispatcher.
- Because getEQd is currently unsigned and has no installer yet, "update available" cannot
  mean silent in-place upgrade the way TaxMan's NSIS path does. Until Stage 3 ships an
  installer, the honest action is to open the GitHub release/download page. Once an
  installer exists, offer download-and-launch of `getEQd-<version>-Setup.exe`.

**The four required states, for a C# implementation:**

- **Already current:** compare parsed release version to the local version; show
  `getEQd is up to date.`
- **Update available:** show the new version and offer the action; keep the "your local
  settings and profiles stay in place" reassurance only if it is actually true.
- **Offline / network failure:** catch `HttpRequestException`, `SocketException`, and
  `TaskCanceledException` (timeout) and show `getEQd could not reach the update service.
  Check your internet connection and try again. (GETEQD-UPDATE-003)`.
- **Rate-limited:** treat `403` with `X-RateLimit-Remaining: 0` and `429` as their own
  state, distinct from offline, with copy like `The update service is busy right now.
  Try again later. (GETEQD-UPDATE-005)`. This is the state TaxMan lacks.
- Add a fifth, cheap state TaxMan does have: **malformed response** (non-2xx handled
  above, plus YAML/JSON parse failure or a missing asset) as
  `GETEQD-UPDATE-004`.

## What could not be determined from the source

- The exact live HTTP request `electron-updater` issues (API vs. `releases/latest` asset
  redirect) is library-internal; only the provider config and channel file are visible in
  the repo. The endpoint in section 2 is labeled as inference.
- Whether the GitHub repo `oppdown/TaxMan` is currently public, and its live release list,
  were not checked. That would need a network call, which this task did not require.
- TaxMan's own `HUMAN-TEST-PLAN.md` does not cover the update flow, so there is no
  in-repo record of manual verification for these branches.
