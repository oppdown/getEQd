# getEQd project context

## Ownership

- **Project owner:** Phil (the person holding this repository). There was no recorded
  owner before this was written down; the five commits in the history are all authored by
  the placeholder identity `getEQd <geteqd@localhost>`.
- The owner decides what ships, what gets parked, and what the next version number means.
  This document is the single plan of record.
- `windows\` is run by the owner directly. There is no separate maintainer for the native
  app, the public site, or the browser console.

## Product

getEQd is a Windows-first EQ for surround speakers and headphones. The design language is focused, dark, studio-like, and slightly theatrical: audiophile controls with sensible labels and a visible path to 11.

## Current site

- Public site: https://geteqd.speedy-star-8288.chatgpt.site/
- Console: live six-band EQ curve with Reference, Impact, Dialogue, and Night presets.
- Speaker outputs: 5.1 Surround and 2.1 Stereo.
- Headphone output: dedicated path for 3.5 mm, USB DACs, USB headsets, and Bluetooth.
- Headphone targets: Neutral reference, Closed-back punch, Open-back air, and Gaming headset clarity.
- Headphone controls: Crossfeed, Stage width, Direct stereo, Windows Spatial Sound, and Game spatial mix.
- Supporting routes: `/changelog/` and `/download/`.
- The download page currently provides starter profiles; the Windows installer is not packaged yet.
- The console now includes a local Profile Lab: validated `getEQd-profile/v1` JSON imports, device-local storage, six-band auditioning, and a downloadable template. It is a calibration-data foundation, not the system-wide audio engine.
- The downloadable template is the measurement profile (`getEQd-profile/v1`). Saved listening profiles are a separate, named local format (`geteqd-listening-profiles-v1`) and are not offered as a public starter file.
- The Profile Lab now distinguishes raw measurement data from correction data, accepts optional target curves plus source/date/rig/target metadata, draws raw/target/correction comparisons, applies a six-band correction with headroom-safe preamp handling, and exports `getEQd-calibration-audit/v1` JSON. Existing profiles without `responseType` remain correction data.
- The console also saves named listening profiles locally, including band gains, output path, headphone controls, preamp, safety ceiling, and bypass. Saved profiles can be followed/reapplied or deleted; live changes remain unsaved until explicitly saved.
- The native console retains six quick bands and adds an Advanced EQ editor with ten slots. Each slot supports enable/disable, 20 Hz–20 kHz frequency, −12 to +11 dB gain, Q 0.20–8.00, and peaking, shelf, pass, or notch shapes. Advanced settings are included in new listening profiles; older profiles remain compatible.

## Native Windows build

`windows\` holds a native WPF application (`getEQd for Windows`) that carries the console's
six bands, presets, routing, headphone controls, preamp, safety ceiling, profile import and
saved listening profiles onto a real-time DSP engine. It is a **player and monitor**, not a
system-wide filter: it processes the audio it plays, plus built-in probe signals so the EQ
can be auditioned with no file. Intercepting other applications' streams still needs a
kernel-mode APO or virtual driver and remains out of scope.

- Deliverables: `windows\dist-advanced\getEQd.exe` remains the prior Advanced EQ build; `windows\dist-measured\getEQd.exe` is the new self-contained measured-model-lab build, no runtime to install. The prior `windows\dist\getEQd.exe` was left untouched because it was still running during packaging.
- The drawn curve is the analytic magnitude response of the filters that are actually
  running. The self-test measures real audio output against that curve within 0.044 dB;
  keep that invariant and do not replace it with a cosmetic curve.
- `windows\dist-measured\getEQd.exe --selftest report.txt` runs 60 measurement checks. Two earlier
  failures were wrong test expectations, not DSP defects — see `windows\README.md`.
- Documented in `windows\README.md`.

## Current product pass

Single plan of record, in order. `Done` means the work exists and was verified; `Open` means
it has not been started.

1. **Import real measured profiles for popular current headphone models.** `Done` — the
   `getEQd-profile/v1` importer and the measured model lab exist in both the browser console
   and the native app. Publicly shipping model-specific curves is still open.
2. **Review the raw/target/correction audit workflow against those real measurements.**
   `Done (local)` — `getEQd-calibration-audit/v1` exports the metadata, raw, target, and
   correction arrays plus the six-band landing. It has not been exercised against a real
   third-party measurement in this repo.
3. **Take the native engine system-wide, preserving output-path and channel intent.**
   `Open — active, no driver assigned`. The real-time DSP and channel routing already exist
   in `windows\`. What is missing is intercepting other applications, which needs a
   kernel-mode APO or virtual audio driver plus code signing. This is the largest item on
   the list and the one that needs a decision before work starts. Not parked.
4. **Add microphone room calibration, per-channel delay, phase/polarity, and a safe
   limiter.** `Open` — send a "limiter" already exists in the console; this item means the
   measurement-driven version.

### Version scheme

One scheme, applied to the public changelog, the download page, and this document:

- `v0.1.x` — public preview revisions: headphone pass, Profile Lab, listening profiles.
  This is what the live site currently shows.
- `v0.2.0` — the measured model lab. Built and self-tested locally; not yet published.
- `v0.3.0` — the system-wide engine in item 3. This is the next major milestone and it is
  not a roadmap placeholder that can drift.
- Anything built but not published is described as local, never as shipped.

## Guardrails

- Keep the public site marketing-led; do not add repository, source, or release links to public navigation or calls to action.
- Treat model-specific headphone targets as measured data, not marketing assumptions.
- Keep the current public site version separate from unpublished local revisions until explicitly approved for publication.

## Open decisions

- Publish status: the live site is still the v0.1.3 listening-profile revision. The Advanced EQ,
  the measured model lab, and the changelog entries for them are local only. Publishing them is
  the last step of this pass.
- The desktop app now has a home: `https://github.com/oppdown/getEQd`, **public**, with `v0.2.0`
  published as a release carrying `getEQd.exe` and `getEQd-0.2.0.msi`. It was created private and
  had to be made public, because GitHub's release API answers 404 for a private repository and the
  update check cannot authenticate. If the source must go private again, the update channel has to
  move to a version manifest on the public site, or `Help > Check for updates` stops working for
  everyone including the owner.
- Resolved: the Windows app source is tracked. `windows\src`, `windows\tests`, `windows\README.md`
  and `AGENTS.md` are committed on the `codex/ship-ready-v0.2` branch; the packaged executables,
  previews, build output, and self-test reports are ignored and stay local. Binaries become
  release assets, not repository content.
- Resolved: the `.git` bloat is gone, and the cause was not the loose objects alone. Codex's own
  turn-checkpoint refs (`refs/codex/turn-diffs/...`) snapshot the whole working tree, which kept
  the packaged 145 MB and 66 MB executables reachable, so the earlier prune reclaimed almost
  nothing. With those refs dropped and the objects expired, `.git` went from 117 MB to 0.13 MB.
  History was not rewritten; the six commits are intact.
- `windows\dist\getEQd.exe` was left untouched because it was running during packaging. Replace
  it with the current measured build once nothing holds the file open.
- Still open: the installer, `Help > Check for Updates`, a GitHub repository for the app source
  and releases, and real measured headphone data. See the version scheme above for what each
  one gates.
