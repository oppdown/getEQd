# getEQd for Windows

A native Windows desktop build of the getEQd web console, with a real-time DSP engine
behind it. The web console drew a curve from six numbers; this one filters audio. The
desktop console keeps those six quick bands and adds an Advanced EQ editor with ten
adjustable slots.

- **Language / UI** — C# on `net8.0-windows`, WPF, custom-drawn analyzer and meters
- **Audio** — NAudio 2.2.1, WASAPI shared mode, event-driven, 60 ms buffer
- **DSP** — RBJ Audio EQ Cookbook biquads, Linkwitz-Riley 4th-order crossover
- **Deliverables** — `dist-measured\getEQd.exe`, the current self-contained build (139 MB, no
  runtime to install), and `dist-advanced\getEQd.exe`, the earlier Advanced EQ build (63 MB)

## Status and version scheme

One scheme, applied here, in `PROJECT_CONTEXT.md`, and in the public changelog:

| Version | Meaning | State |
|---------|---------|-------|
| `v0.1.x` | Public preview line: headphone pass, Profile Lab, listening profiles. | Published on the public site. |
| `v0.2.0` | The measured model lab in this native app. | Built, self-tested, installed, and published as a GitHub release. Not yet on the public download page. |
| `v0.3.0` | The system-wide engine. | Not started. |

What exists today is the player and monitor described in this file, an MSI installer, and an
update check. What does **not** exist yet: the system-wide mode, and real measured headphone
data shipped in the app. Anything built but unpublished is described as local, never as
shipped.

The packaged `.exe` files are **local build output**. They are git-ignored and are not
repository content; when releases exist they become release assets.

## Running it

Double-click `dist-measured\getEQd.exe`. Nothing else is required — the .NET runtime and the
WPF framework are bundled inside the executable.

On first launch, choose an output endpoint on the right, then press **Pink noise**,
**1 kHz** or **Sweep** to audition the EQ with no file. **Open audio file…** loads any
format NAudio can read (WAV, MP3, FLAC, AIFF, WMA, M4A) and plays it through the same
chain.

### Command line

```
getEQd.exe                                 open the console
getEQd.exe --selftest [report.txt]         measure the audio engine, write a report
getEQd.exe --render-preview out.png [...]  render the interface offscreen to a PNG
getEQd.exe --help                          this list
```

`--render-preview` accepts `--preset <id>`, `--route <5.1|2.1|headphones>`, `--playing`,
`--width`, `--height`, `--scale`, and `--probe x,y` (prints the exact pixel colour, which
is how the styling was checked without a person looking at the screen).

Both headless modes write to the parent console when launched from a terminal and to the
report file otherwise.

## The signal chain

Audio runs left to right, once per block, with no allocation and no locks on the audio
thread. Parameters are published as an immutable snapshot and ramped over about 25 ms, so
dragging a fader never clicks.

```
file / probe signal
      │
      ▼
six-band EQ  ──►  routing  ──►  spatial  ──►  preamp  ──►  safety ceiling  ──►  meters
 (per channel)   (bass mgmt)   (width then      │
                                crossfeed)       └─ hold, then limit
```

### The quick bands

| # | Band | Role | Frequency | Filter | Q |
|---|------|------|-----------|--------|---|
| 01 | Sub foundation | Room / weight | 32 Hz | low shelf | 0.70 |
| 02 | Kick body | Punch / impact | 64 Hz | peaking | 1.00 |
| 03 | Low-mid | Warmth / mud | 250 Hz | peaking | 1.10 |
| 04 | Presence | Voice / attack | 2.5 kHz | peaking | 0.90 |
| 05 | Detail | Clarity / bite | 6.4 kHz | peaking | 1.00 |
| 06 | Air | Space / shimmer | 12 kHz | high shelf | 0.70 |

Range is −12 to +11 dB in 0.5 dB steps, in both the faders and the graph handles.

### Advanced EQ

Open **Advanced EQ** in the right rail to edit all ten filter slots. Each slot can be
enabled or bypassed, moved from 20 Hz to 20 kHz, set to −12 to +11 dB, and given a Q from
0.20 to 8.00. The available shapes are peaking, low shelf, high shelf, low-pass,
high-pass, and notch. In Advanced mode, dragging a graph handle horizontally changes its
frequency; vertical dragging still changes gain and the mouse wheel changes Q. The four
extra slots start disabled, so opening the editor never changes a quick-band sound by
itself.

Listening profiles now preserve the advanced slot frequency, type, enable state, and Q.
Profiles written before Advanced EQ remain valid and keep their original quick-band
settings.

The curve you see is the **analytic magnitude response of the filters that are actually
running**, not a decorative spline. `CurveDbAt` evaluates the same coefficients the audio
thread uses, and the self-test confirms the drawn curve matches measured audio output.

### Presets

These are the web console's exact values, unchanged.

| Preset | Sub | Kick | Low-mid | Presence | Detail | Air |
|--------|-----|------|---------|----------|--------|-----|
| Reference | 0 | 0 | 0 | 0 | 0 | 0 |
| Impact | +4.0 | +3.0 | −1.5 | +1.0 | +1.5 | +0.5 |
| Dialogue | −2.0 | −1.0 | −2.0 | +2.5 | +1.5 | 0 |
| Night | −3.0 | −2.0 | +1.0 | +1.0 | −1.0 | −2.0 |

Editing any band switches the preset indicator to a custom state.

### Routing

- **5.1 Surround** — hands everything below 80 Hz to the LFE channel, derives the centre
  from the front pair, and feeds the surrounds the difference signal. **Centre lift**
  trims the derived centre.
- **2.1 Stereo** — one sub lane, same 80 Hz split.
- **Headphones** — the headphone section becomes active.

The crossover is a **Linkwitz-Riley 4th-order** (two cascaded Butterworth sections per
path), so low plus high sums back to flat magnitude rather than a lumpy bump. The
self-test measures −118 dB of leak above the crossover and −1.0 dB opposite-side level for
hard-panned sub content.

### Headphones

Crossfeed blends a delayed, low-passed copy of each channel into the other ear — 700 Hz
one-pole, 250 µs delay, 0–35%. Stage width scales the side signal from 70% to 130%.

Both are **disabled when the software handoff is not Direct stereo**, because Windows
Spatial Sound or a game engine already owns spatialisation and stacking a second
crossfeed on top of it smears the image. The console says so rather than silently
double-processing.

The four headphone targets are **category starting curves**, labelled as such in the UI.
They are not measured targets for any specific model — per the project guardrail, model
targets have to be measured data.

| Target | Sub | Kick | Low-mid | Presence | Detail | Air |
|--------|-----|------|---------|----------|--------|-----|
| Neutral reference | 0 | 0 | 0 | 0 | 0 | 0 |
| Closed-back punch | −1.5 | −1.0 | +0.5 | +0.5 | +1.0 | 0 |
| Open-back air | +0.5 | +0.5 | 0 | 0 | +0.5 | +1.5 |
| Gaming headset clarity | −3.0 | −2.0 | −1.5 | +2.5 | +2.0 | +0.5 |

The trim rides on top of your faders. The faders, the graph handles and the curve all
show the effective gain, so a handle always sits on the curve it controls.

### Gain staging

The preamp is −12 to +11 dB; the safety ceiling is −6 to 0 dBFS. The **Trim to fit**
readout shows the preamp that would put material already peaking at full scale exactly on
the ceiling, and the **Apply trim** button sets it. That is the difference between a
limiter that never engages and one that is quietly working on every transient.

The ceiling has a 0.4 ms attack and a 150 ms release and reports its gain reduction in the
meter lane labelled CEILING.

## Profiles

**Save this sound** writes a listening profile. Import accepts the web console's
`getEQd-profile/v1` format. Older files that omit `responseType` remain valid and are
treated as correction data; new measurement files should use `raw`:

```json
{
  "schema": "getEQd-profile/v1",
  "model": "Example over-ear",
  "source": "Measurement source",
  "measuredAt": "2026-09-12",
  "rig": "5128 fixture",
  "target": "Flat reference",
  "responseType": "raw",
  "notes": "Seal checked; left and right averaged.",
  "frequenciesHz": [20, 100, 1000, 10000],
  "gainDb": [4.5, 1.0, -2.0, 1.5],
  "targetDb": [0.0, 0.0, 0.0, 0.0]
}
```

For a raw measurement, the correction is `targetDb - gainDb`. Correction files use
`gainDb` directly, which preserves the earlier import behavior. The Measured Model Lab
draws raw response, target, and correction separately; **Apply correction** maps the
correction onto the six quick bands and lowers preamp when the current curve needs it.
**Export audit** writes the source arrays, metadata, correction curve, and quick-band
landing to `getEQd-calibration-audit/v1` JSON. Nothing overwrites the imported source.

Frequencies are log-interpolated onto the six band centres and snapped to the 0.5 dB grid.
The importer rejects unknown response types, mismatched target arrays, wrong schema,
missing model, mismatched array lengths, fewer than two points, non-numeric values, and
non-positive frequencies.

Stored under `%LOCALAPPDATA%\getEQd\`:

- `measurements.json` — imported measurement profiles
- `listening-profiles.json` — saved listening profiles

## Verifying a build

The self-test runs a **real audio rig**, not mocks: it generates sine tones, pushes them
through the actual processor, measures the output, and compares it against the analytic
prediction.

```
dist-measured\getEQd.exe --selftest report.txt
```

60 checks, all passing. What it establishes:

| Area | Evidence |
|------|----------|
| Flat chain | bit-exact passthrough, deviation 0 |
| Every band | measured response matches the drawn curve exactly |
| Advanced filters | notch rejection and high-pass separation measured on real output |
| Peaking bands | full +6.00 dB at centre |
| Full curve | matches real audio within 0.044 dB worst case |
| Preamp | exactly −6.00 dB |
| Ceiling | holds full scale at 0.5093 peak against a 0.5012 target, −5.87 dB reduction |
| Ceiling idle | no reduction on a quiet signal |
| Bypass | true bypass: +11 dB on every band yields 0.00 dB |
| Bass management | hard-panned 40 Hz arrives opposite-side at −1.02 dB, correlation 1.0000 |
| Crossover isolation | 2 kHz leaks only −118.05 dB |
| Crossfeed | 35% gives −6.90 dB opposite ear; 0% and Spatial Sound handoff give exactly 0 |
| Stage width | 70% and 130% exact |
| Headphone trim | applies, disables, and is isolated to the headphone route |
| Presets | all four match the console |
| Profile import | all six rejection cases produce the right message |
| Measured model lab | raw / target / correction math, provenance, audit JSON, and template round-trip |
| Listening profiles | round-trip cleanly |

Two checks in an earlier revision failed, and both were the *test* being wrong rather than
the DSP: a low shelf was asserted to reach its full +6 dB only 0.6 octaves below its
corner (it reaches +4.93 dB, which is correct shelf behaviour), and a channel-correlation
assertion was being used to prove hard-panning, which cannot work because correlation is
scale invariant and a −118 dB coherent leak still correlates at 1.0000. The tests now
assert the honest claims: shelf lift plus measured-equals-curve, and a level ratio for
channel isolation.

## Unit tests

The self-test above proves the audio path on a real endpoint. The unit suite covers the
parts that are easier to reason about in isolation, and it runs in CI.

```powershell
& ".\.tools\dotnet\dotnet.exe" test windows\tests\GetEQd.Tests\GetEQd.Tests.csproj
```

34 tests across four files:

| File | What it pins down |
|------|-------------------|
| `AudioMathTests.cs` | Every filter shape at frequencies where the Cookbook formula has a known answer, plus a sine pushed through the section compared against the analytic response |
| `CurveInvariantTests.cs` | The drawn curve against **measured audio** through the built filters, the preamp's place in the chain, a bypassed band contributing nothing, and the headroom suggestion |
| `MeasurementProfileTests.cs` | The importer accepting good data, refusing eight kinds of bad data with the right message, legacy correction files, and the audit export carrying its own provenance |
| `SettingsRoundTripTests.cs` | Settings surviving the stored shape and JSON, and hostile stored values being clamped rather than trusted |

`.github\workflows\ci.yml` builds the console and runs this suite on `windows-latest`. The
self-test stays a local gate because a hosted runner has no audio endpoint.

## Updates

The console has no menu bar, so the check lives in the right rail as an **UPDATES** card
rather than under a `Help` menu: it shows the build version, a **Check for updates** button,
and a line that always says what happened. Nothing is sent until you press the button.

It reads the latest release from
`https://api.github.com/repos/oppdown/getEQd/releases/latest`, compares the tag against the
running build, and offers the installer when a newer one exists. The request has an
**8 second timeout**, so a dead network ends in the offline state instead of a spinner.

The same check runs headless, which is how each branch was verified:

```
getEQd.exe --check-updates
getEQd.exe --check-updates --as-version 0.1.3
getEQd.exe --check-updates --as-endpoint http://127.0.0.1:8144/403
```

`--as-version` only changes what the answer is compared against, so the "an update is
available" branch can be exercised on a machine that is already current. `--as-endpoint`
points the check at another release feed, so the failure branches can be exercised without
waiting for GitHub to misbehave. Neither can change what the default endpoint reports.

Every state has its own copy, and every one of them was exercised:

| State | How it was produced | Result |
|-------|--------------------|--------|
| Up to date | live, against the published `v0.2.0` release | `This build (0.2.0) is the latest release.` |
| Update available | live, `--as-version 0.1.3` | `getEQd 0.2.0 is available.` plus the `.msi` download |
| No releases | live, HTTP 404 | `No published release was found for this build yet.` |
| Rate limited | live, HTTP 403 and 429 from a stub feed | its own state, not a generic failure |
| Malformed feed | live, HTTP 200 with a non-release body | `GitHub returned a release this build could not read.` |
| Failed | live, HTTP 500 | `The update check failed (HTTP 500).` |
| Offline | live, connection refused | `No connection to GitHub. The console still works offline.` |
| Timeout | live, unroutable address | `The update check timed out after 8 seconds.` |

The stub feed and the offline cases are not shipped: they were a local HTTP server and a
dead address used for this verification pass.

## Building from source

Requires the .NET 8 SDK. On this machine it is installed locally at `.tools\dotnet`
because only the .NET 6 runtime was present and installing a machine-wide SDK needs admin.

```powershell
$env:DOTNET_ROOT = 'C:\Users\phill\Documents\GetEQd Workspace\.tools\dotnet'
$root = 'C:\Users\phill\Documents\GetEQd Workspace\windows'

# build and test
& "$env:DOTNET_ROOT\dotnet.exe" build "$root\src\GetEQd.App\GetEQd.App.csproj" -c Debug
& "$root\src\GetEQd.App\bin\Debug\net8.0-windows\getEQd.exe" --selftest "$root\geteqd-selftest.txt"

# publish the single file
& "$env:DOTNET_ROOT\dotnet.exe" publish "$root\src\GetEQd.App\GetEQd.App.csproj" `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o "$root\dist-advanced"
```

### The installer

`installer\getEQd.wxs` builds an MSI around the packaged executable. WiX is pinned in
`windows\.config\dotnet-tools.json`, so there is nothing to install by hand:

```powershell
pwsh -File windows\installer\build-installer.ps1
```

It installs **per user**, to `%LOCALAPPDATA%\Programs\getEQd`, with a Start Menu shortcut.
Per-user is deliberate: the app is unsigned, so a per-user install needs no elevation and
the whole install / launch / uninstall cycle can be checked without an admin prompt.
Uninstalling removes the program and the shortcut and leaves your saved profiles in
`%LOCALAPPDATA%\getEQd` alone.

**The app is not code-signed, so SmartScreen will warn on first run.** That is a fact about
the current state, not something the installer hides. Fixing it needs a code-signing
certificate.

The verified cycle, run against the v0.2.0 build:

| Step | Result |
|------|--------|
| Install silently, per user | exit 0, `getEQd.exe` in `%LOCALAPPDATA%\Programs\getEQd` |
| Start Menu shortcut | created |
| Installed binary `--selftest` | all 60 checks passed |
| Launch the console | window opens, process responding, closes cleanly |
| Uninstall | exit 0, program and shortcut gone, `%LOCALAPPDATA%\getEQd` preserved |

WiX 7 is not usable here: it gates behind the Open Source Maintenance Fee EULA. The tool
manifest pins WiX 5, which is free.

### Layout

```
windows\
  dist-measured\getEQd.exe        current measured-model-lab single file (ignored)
  dist-advanced\getEQd.exe        prior Advanced EQ single file (ignored)
  dist\getEQd.exe                 earliest single file, left in place while it was running (ignored)
  geteqd-*-selftest.txt           recorded self-test reports (ignored)
  preview\                        rendered UI previews (ignored)
  docs\                           integration notes: the updater contract, the APO spec
  profiles\                       sourced measurement data with provenance
  tests\GetEQd.Tests\             the unit suite
  src\GetEQd.App\
    Audio\
      Biquad.cs                   RBJ filter designs + magnitude response
      EqModel.cs                  bands, presets, targets, settings, snapshot, headroom
      EqProcessor.cs              the real-time processor
      AudioEngine.cs              device enumeration, playback, probe signals
      SpectrumAnalyzer.cs         2048-point Hann FFT
      Profiles.cs                 profile import/export and storage
    Ui\
      Theme.xaml                  dark studio palette and control styles
      CurveView.cs                interactive response analyzer
      MeterBar.cs                 peak meter with hold and gain reduction
      MainView.xaml(.cs)          the console
      MainWindow.xaml(.cs)        window, DPI awareness, dark title bar
    Diagnostics\
      SelfTest.cs                 60-check measurement suite
      PreviewRenderer.cs          offscreen renderer for design review
```

## What this is not

- **Not system-wide yet.** Today it is a player and monitor: it filters the audio it plays
  plus its own probe signals. Other applications' streams are untouched.

  The decided route for `v0.3.0` is to make getEQd the **front end** for Equalizer APO
  rather than to write a driver. getEQd would generate APO configuration and APO would stay
  the filter engine, with the user installing APO themselves. That gives real system-wide EQ
  at no signing cost, and the intent is that switching between "player and monitor" and "APO
  configuration generator" is a mode rather than a rewrite. Writing a kernel-mode APO or a
  virtual audio driver is still rejected: it needs an Extended Validation certificate and a
  Microsoft Partner Center hardware account, roughly $200-500 a year plus identity
  verification.

  None of that is built. `docs\equalizer-apo-integration.md` records what the config format
  requires and what the approach cannot do.
- **Not a headphone measurement database.** The targets are category curves and are
  labelled that way in the interface.
- The app is unsigned, so SmartScreen will warn on first run.
