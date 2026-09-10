# getEQd project context

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
- The console also saves named listening profiles locally, including band gains, output path, headphone controls, preamp, safety ceiling, and bypass. Saved profiles can be followed/reapplied or deleted; live changes remain unsaved until explicitly saved.

## Next product pass

1. Add measured profiles for popular current headphone models instead of shipping guessed curves.
2. Support calibration-data import and an auditable target/measurement workflow.
3. Build the actual Windows system-wide real-time audio engine, preserving output-path and channel intent.
4. Add microphone room calibration, per-channel delay, phase/polarity, and a safe limiter.

## Guardrails

- Keep the public site marketing-led; do not add repository, source, or release links to public navigation or calls to action.
- Treat model-specific headphone targets as measured data, not marketing assumptions.
- Keep the current public site version separate from unpublished local revisions until explicitly approved for publication.
