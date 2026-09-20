# getEQd v0.3.0: System-Wide Mode via Equalizer APO

Status: complete — implementation-ready spec. Decisions: getEQd owns one file (`<ConfigPath>\geteqd.txt`) and adds one idempotent `Include: geteqd.txt` line to `config.txt`; shelves map to `LSC`/`HSC` with a dB-per-octave slope so Equalizer APO's RBJ `S` matches getEQd's own `Biquad`; the preamp comes from the existing `Analysis.Evaluate` so the file matches the drawn curve; no elevation, no signing, and no money are required for this route. Verified against Equalizer APO's configuration reference, installer script, and filter sources; nine items remain explicitly unverified and are listed at the end.

Scope note: this document specifies getEQd's **system-wide mode** only. The decision is
already made and is not re-opened here: getEQd does **not** ship a kernel-mode APO or a
virtual audio driver. getEQd generates Equalizer APO configuration text; Equalizer APO
remains the filter engine and the user installs APO themselves.

Every syntax claim below is grounded in a primary source and cited inline. Where I could
not confirm something, it is labelled **unverified**. The equalizer engine is the open
source project at `sourceforge.net/p/equalizerapo/`; the `mirror/equalizerapo` GitHub
repository is a SourceForge mirror and is cited by file path plus URL.

## 1. Where Equalizer APO reads configuration on Windows 10/11

### 1.1 Install location and the two registry values

Equalizer APO installs with an NSIS installer that requests elevation
(`RequestExecutionLevel admin`) and defaults to `$PROGRAMFILES64\EqualizerAPO`, i.e.
`C:\Program Files\EqualizerAPO`
([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)).
It records two values under `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO`:

- `InstallPath` — the install directory.
- `ConfigPath` — the configuration directory, written as `$INSTDIR\config`, so
  `C:\Program Files\EqualizerAPO\config` on a default install.

The registry root name comes from the engine's own constant, `APP_REGPATH`, defined as
`L"HKEY_LOCAL_MACHINE\\SOFTWARE\\EqualizerAPO"`
([helpers/RegistryHelper.h](https://github.com/mirror/equalizerapo/blob/master/helpers/RegistryHelper.h)).
The installer writes `ConfigPath` only when it is absent or when `InstallPath` changed, so a
user who relocated the config directory keeps that choice across upgrades
([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)).

### 1.2 Which file is actually loaded

The audio engine reads `ConfigPath` at initialisation and then loads exactly one entry file,
`<ConfigPath>\config.txt`:

```cpp
configPath = RegistryHelper::readValue(APP_REGPATH, L"ConfigPath");
...
if (customPath.empty())
    loadConfigFile(configPath + L"\\config.txt");
```

([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
`initialize` and `loadConfig`). Everything else is reached from `config.txt` through
`Include:` lines. The shipped `config.txt` is three lines — a preamp, an include, and a flat
15-band graphic EQ:

```
Preamp: -6 dB
Include: example.txt
GraphicEQ: 25 0; 40 0; 63 0; 100 0; 160 0; 250 0; 400 0; 630 0; 1000 0; 1600 0; 2500 0; 4000 0; 6300 0; 10000 0; 16000 0
```

([Setup/config/config.txt](https://github.com/mirror/equalizerapo/blob/master/Setup/config/config.txt)).
The user documentation states the same thing in prose: `config.txt` "is the main
configuration file that will automatically be loaded by Equalizer APO"
([Documentation wiki](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)).

### 1.3 Does writing there need elevation?

Normally, **no**. The installer explicitly grants the built-in Users group full access to
the config directory:

```
;Grant write access to the config directory for all users
AccessControl::GrantOnFile "$INSTDIR\config" "(S-1-5-32-545)" "FullAccess"
```

([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)).
`S-1-5-32-545` is `BUILTIN\Users`. So on a default install an unelevated process can rewrite
`config.txt` and any included file. Two caveats getEQd must handle rather than assume away:

- A user who moved `ConfigPath` elsewhere, or tightened the ACL, may produce an
  `UnauthorizedAccessException`. getEQd should surface that plainly and offer an elevated
  retry only as a fallback.
- Reading `HKLM\SOFTWARE\EqualizerAPO` needs no elevation; **writing** it would. getEQd must
  only ever read these values.

### 1.4 How changes take effect

The engine spawns a directory-change notification thread watching `ConfigPath` **including
its subtree**, for file-name and last-write changes:

```cpp
HANDLE notificationHandle = FindFirstChangeNotificationW(engine->configPath.c_str(), true,
    FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_LAST_WRITE);
```

([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
`notificationThread`). On a change it reloads the configuration and cross-fades from the old
filter set to the new one over `sampleRate / 100` samples — about 10 ms at 48 kHz. The wiki
puts the user-visible behaviour plainly: "You should notice the volume changes immediately
each time after you save the file"
([Documentation wiki](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)). Registry
values read through the expression function `readRegString` are also watched with
`RegNotifyChangeKeyValue` ([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).

One thing a file save does **not** fix: a freshly installed APO is not used until the audio
service restarts. The user documentation tells the user to allow the reboot for exactly this
reason ([Documentation wiki](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)).

### 1.5 Global vs device-specific configuration

There is one configuration stream, not one file per device. Commands apply to every channel
of every device the APO is installed into unless a `Device:` line scopes them:

```
Device: <Device pattern 1>; <Device pattern 2>; ...
```

If the pattern does not match the current output device, all following commands **except
`Device` commands** are ignored
([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).
The match target is the string `"Device_name Connection_name GUID"`, every word of a pattern
must be found in it, `;` separates alternative patterns, and the literal pattern `all` always
matches. The implementation confirms the details
([filters/DeviceFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/DeviceFilterFactory.cpp)):

- Matching is case-insensitive substring matching.
- Words containing `{` are matched against the full device string, so GUID fragments work;
  other words are matched against the device string with the GUID removed.
- `Device:` state is *not* inherited into included files as a restriction: on entering an
  include the outer file's match is assumed true, and at the end of an include the state is
  reset to matched (`endOfFile` sets `deviceMatches = true`). The practical rule is that a
  `Device:` line must be repeated inside any file that needs it.

### 1.6 The Configurator ("Device Selector") and the Administrator question

Two different things are easy to conflate:

- **Which devices the APO is installed into.** This is a driver/registry-level operation done
  by the Configurator, shipped as `DeviceSelector.exe` (Start Menu entry "Equalizer APO
  Device Selector"). The installer runs it as `ExecWait '"$INSTDIR\DeviceSelector.exe" /i'`
  ([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)) and
  the documentation tells the user to "select the correct audio device to install the APO to"
  and to re-run `C:\Program Files\EqualizerAPO\Configurator.exe` later for other devices
  ([Documentation wiki](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)). This
  needs elevation and is entirely the user's job, not getEQd's.
- **What the filter does.** That is the config text, which is what getEQd writes.

The Configuration Editor (`Editor.exe`) edits `config.txt`; its device dropdown drives the
preview/analysis and channel layout in the UI
([Editor/MainWindow.h](https://github.com/mirror/equalizerapo/blob/master/Editor/MainWindow.h)
— members `deviceComboBox`, `outputDevices`, `defaultOutputDevice`, and
`getDeviceAndChannelMask`). Whether any current Editor build writes additional per-device
config files beyond `config.txt` plus includes is **unverified**; the documented and
version-stable mechanism is the `Device:` command described above, and that is what this spec
relies on.

### 1.7 How getEQd should locate the config directory

1. Read `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO\ConfigPath` (64-bit view).
2. If absent, fall back to the 32-bit view (`WOW6432Node`) and to
   `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO\InstallPath` + `\config`. The installer uses
   `SetRegView 64` for all but the 32-bit package, and the engine reads without an explicit
   WOW64 flag, so the 32-bit-view fallback is a defensive measure and is **unverified** as a
   real-world requirement.
3. If neither exists, report "Equalizer APO is not installed" rather than creating anything.
4. Never write to `HKLM`. Only read.

## 2. The literal config grammar getEQd would emit

### 2.1 Line format

Every meaningful line is `Command: Parameters`. The parser takes the text up to the **first**
`:` as the command key, trims it, and hands the remainder to each filter factory in turn
([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
`loadConfigFile`). Consequences that matter for a generator:

- A line with no `:` is ignored, so `# comments` and REW's header lines (`Filter Settings
  file`, `Room EQ V5,01`, `Equaliser: Generic`) are harmless
  ([Setup/config/example.txt](https://github.com/mirror/equalizerapo/blob/master/Setup/config/example.txt)).
- An unrecognised command is **silently** ignored — there is no error, so a typo in a command
  name fails quietly. getEQd must not rely on APO reporting mistakes back.
- Indentation is allowed (the key is trimmed), so `  Filter 1: ...` is fine.
- Before parsing, `,` in the parameter text is rewritten to `.`
  ([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp),
  `filters/PreampFilterFactory.cpp`). getEQd should still emit `.` as the decimal separator.
- Files are read as UTF-8, falling back to the ANSI code page if invalid
  ([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp)).
  getEQd should emit UTF-8 without a BOM.

### 2.2 Preamp

```
Preamp: -6.5 dB
```

Syntax is `Preamp: <number> dB`, parsed with `swscanf_s(value, L" %lf dB", &preamp_dB)`
([filters/PreampFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/PreampFilterFactory.cpp)).
The wiki adds the important rule: "Since version 0.8, when multiple preamps apply to the same
channel, the resulting preamp is the **sum** in dB"
([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).
So a preamp getEQd writes is *added* to anything the user already has on the same channel, not
substituted for it.

### 2.3 Filter

```
Filter <n>: ON <Type> Fc <Frequency> Hz Gain <Gain value> dB Q <Q value>
Filter <n>: ON <Type> Fc <Frequency> Hz Gain <Gain value> dB BW Oct <Bandwidth value>
```

`<n>` "is not interpreted and can be omitted", so `Filter: ON ...` is valid
([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).

**ON vs OFF.** The factory only builds a filter when the parameter text matches
`^\s*ON\s+([A-Za-z]+)`; anything else — including `OFF None` — yields no filter and no error
([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)).
That is exactly why REW exports full of `Filter 3: OFF None` load cleanly
([Setup/config/example.txt](https://github.com/mirror/equalizerapo/blob/master/Setup/config/example.txt)).
So `OFF` is a no-op line, and a disabled getEQd slot may either be omitted or written as
`OFF None`.

**Type names.** The type is looked up in an exact, case-sensitive map
([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)) —
`pk` in lower case would be rejected as an invalid type. The accepted names, with the
parameters each requires (`X`) or accepts (`O`), per the
[Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/):

| Type | Meaning | Fc | Gain | Q/BW | Example |
|------|---------|----|------|------|---------|
| `PK`, `PEQ`, `Modal` | Peaking / parametric | X | X | X | `Filter 1: ON PK Fc 50 Hz Gain -3.0 dB Q 10.00` |
| `LP`, `LPQ` | Low-pass | X | — | O | `Filter 2: ON LPQ Fc 10000 Hz Q 0.400` |
| `HP`, `HPQ` | High-pass | X | — | O | `Filter 3: ON HP Fc 30 Hz` |
| `BP` | Band-pass (real band-pass, no gain) | X | — | O | `Filter 4: ON BP Fc 1000 Hz Q 0.100` |
| `LS`, `LSC` | Low shelf (corner freq. / center freq.) | X | X | O | `Filter 5: ON LS Fc 300 Hz Gain 5.0 dB` |
| `HS`, `HSC` | High shelf (corner freq. / center freq.) | X | X | O | `Filter 6: ON HS Fc 1000 Hz Gain -3.0 dB` |
| `NO` | Notch | X | — | O | `Filter 7: ON NO Fc 800 Hz` |
| `AP` | All-pass | X | — | X | `Filter 8: ON AP Fc 900 Hz Q 0.707` |

There are **no higher-order type names** such as `LP2` or `HP4`. Higher orders are expressed
either by stacking several biquad lines or by the custom-coefficient form
`Filter: ON IIR Order <m> Coefficients <b0> ... <bm> <a0> ... <am>`, which takes `2*(m+1)`
coefficients ([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).
The only "order-ish" tokens in the filter grammar are the shelf slope forms described next.

**Parameter tokens.** Each token is located by an independent regular expression over the text
after the type, so token order is flexible
([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)):

| Token | Regex (as implemented) | Notes |
|-------|------------------------|-------|
| Frequency | `\s+Fc\s*([-+0-9.eE\u00A0]+)\s*H\s*z` | `Hz` effectively optional; required for every type or the filter is dropped with a logged error |
| Gain | `\s+Gain\s*([-+0-9.eE]+)\s*dB` | Required for `PK`/`PEQ`/`LS`/`HS`; silently ignored for `LP`/`HP`/`BP`/`NO`/`AP` |
| Q | `\s+Q\s*([-+0-9.eE]+)` | Must be preceded by whitespace |
| Bandwidth | `\s+BW\s+Oct\s*([-+0-9.eE]+)` | Ignored for `LS`/`HS` |
| Slope | `^\s*([-+0-9.eE]+)\s*dB` | Anchored at the start of the text after the type; this is what makes `LSC 10.8 dB ...` and `LS 6dB ...` work |

**Defaults when Q/BW is omitted** ([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)):

- `PK`, `PEQ`, `AP` → no filter, error logged (Q is mandatory).
- `LP`, `HP`, `BP` → `Q = 1/√2 ≈ 0.7071`.
- `LS`, `HS` → slope `S = 0.9`.
- `NO` → `Q = 30`.

**Shelf corner vs center frequency.** This is the subtlest part of the grammar and getEQd's
mapping depends on it. If the type name does **not** end in `C` (`LS`, `HS`), the frequency is
treated as a corner frequency and a DCX2496 center-frequency correction is applied; if it does
end in `C` (`LSC`, `HSC`), the frequency is used directly as the center frequency
([filters/BiQuadFilter.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilter.cpp),
`initialize`; and the `isCornerFreq` assignment in
[filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)).
The `x dB` token in `LSC`/`HSC` is dB per octave and is divided by 12 to become the RBJ `S`
parameter.

### 2.4 Ordering and precedence

Filters run in the order their lines appear, in a single chain per channel. The commands that
change *which* lines apply are
([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)):

- `Device: <patterns>` — gates every following command except further `Device` lines.
- `Channel: <positions>` — restricts following `Filter`/`Preamp` lines to those channels
  (`L`, `R`, `C`, `LFE`, `RL`, `RR`, numbers counted from 1, or `all`).
- `Stage: <pre-mix|post-mix|capture>` — output devices default to `post-mix`; a line selects
  the stage for what follows.
- `If:` / `ElseIf:` / `Else:` / `EndIf:` — conditional execution with a small expression
  language (`sampleRate`, `inputChannelCount`, `deviceName`, arithmetic, string functions).
  `If` may not conditionally execute a `Device` statement.
- `Include: <file>` — splices another file's commands at that point; a relative path resolves
  against the *including file's* directory, and recursion is capped at 100
  ([filters/IncludeFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/IncludeFilterFactory.cpp)).

The last `Channel`, `Stage` or `Device` line before a filter is the one in force, and an
include restores the outer file's channel selection when it ends
([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
`loadConfigFile` — "restore channels selected in outer configuration file").

### 2.5 Short real examples

Peaking, from the shipped REW export:

```
Filter  1: ON  PK       Fc    20,0 Hz  Gain   4,0 dB  Q  1,00
Filter  2: ON  PK       Fc    45,0 Hz  Gain   2,0 dB  Q  1,00
Filter  3: OFF None
```

([Setup/config/example.txt](https://github.com/mirror/equalizerapo/blob/master/Setup/config/example.txt)).

Shelf with slope, center frequency, and channel scoping, from the wiki reference:

```
Filter 1: ON LSC 10.8 dB Fc 300 Hz Gain 5.0 dB
Channel: L RL
Filter: ON LS Fc 300 Hz Gain 5.0 dB
```

([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).

Device and channel scoping in one file, from the shipped multichannel sample:

```
Preamp: -6 dB
Channel: L
Preamp: -5 dB
Include: demo.txt
Channel: 2 C
Include: example.txt
```

([Setup/config/multichannel.txt](https://github.com/mirror/equalizerapo/blob/master/Setup/config/multichannel.txt)).

## 3. Mapping the getEQd band model onto that grammar

### 3.1 What getEQd actually holds

From `windows/src/GetEQd.App/Audio/EqModel.cs` and `Audio/Biquad.cs`:

- Ten bands. Indices 0–5 are the quick bands and are enabled by default; indices 6–9 are the
  advanced slots and start disabled (`Bands.All`, `EqSettings` constructor).
- Default layout: 32 Hz `LowShelf` Q 0.70, 64 Hz `Peaking` Q 1.00, 250 Hz `Peaking` Q 1.10,
  2500 Hz `Peaking` Q 0.90, 6400 Hz `Peaking` Q 1.00, 12000 Hz `HighShelf` Q 0.70, then four
  `Peaking` slots at 125 Hz, 1 kHz, 4 kHz and 16 kHz, each Q 1.00.
- Per band: frequency in Hz, Q ("width"), `BandKind` (`LowShelf`, `Peaking`, `HighShelf`,
  `LowPass`, `HighPass`, `Notch`), gain in dB clamped to −12…+11, and an enabled flag.
- `EqSettings.EffectiveGain(i)` folds in the headphone target trim and clamps to −12…+11;
  `EqSnapshot.Gains` is exactly `EffectiveGains()`. The drawn curve, the player's filters and
  therefore the APO config must all use those same numbers.
- `Biquad.ForBand` clamps frequency to `[20, 0.45 × sampleRate]` and Q to `[0.2, 8.0]` before
  designing the section, and shelves are designed with an RBJ slope
  `S = clamp(0.5 + 0.5 × Q, 0.1, 1.0)` (`Biquad.ShelfSlopeFromQ`).

### 3.2 Band kind to APO type

| getEQd `BandKind` | Emit | Why |
|---|---|---|
| `Peaking` | `PK` | Both are the RBJ peaking form with `alpha = sin(w0)/(2Q)` |
| `LowShelf` | `LSC <12·S> dB` | `LSC` uses the center frequency directly and takes dB/octave, which becomes the same RBJ `S` getEQd uses |
| `HighShelf` | `HSC <12·S> dB` | as above |
| `LowPass` | `LPQ` | RBJ low-pass with Q; no gain token |
| `HighPass` | `HPQ` | RBJ high-pass with Q; no gain token |
| `Notch` | `NO` | RBJ notch with Q |

The shelf mapping is the one that needs care, and it is why `LSC`/`HSC` are used rather than
plain `LS`/`HS`. getEQd designs its shelves with the RBJ `S` (slope) parameter
(`Biquad.LowShelf`/`HighShelf` in `Audio/Biquad.cs`). Equalizer APO's shelf is the same RBJ
formula — `alpha = sin(w0)/2 · sqrt((A + 1/A)(1/S − 1) + 2)` — when it is given a slope
([filters/BiQuad.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuad.cpp)).
`LSC`/`HSC` supply that slope as dB per octave, divided by 12
([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)),
and because the type name ends in `C` the frequency is used as the center frequency with no
DCX2496 correction ([filters/BiQuadFilter.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilter.cpp)).
Plain `LS`/`HS` with a `Q` value would instead apply the DCX center-frequency shift and would
not reproduce getEQd's curve.

Worked slope values: the default shelf Q of 0.70 gives `S = 0.5 + 0.35 = 0.85`, so the token
is `10.2 dB` per octave. Q 0.20 gives `7.2 dB`; Q 1.00 and above clamp to `12.0 dB`.

### 3.3 One band, end to end

Peaking band. Quick band index 2 (Low-mid), default 250 Hz, Q 1.10, and the **Dialogue**
preset's gain of −2.0 dB:

| getEQd | Value |
|---|---|
| frequency | 250 Hz |
| gain | −2.0 dB |
| Q | 1.10 |
| kind | `Peaking` |

emits

```
Filter 3: ON PK Fc 250 Hz Gain -2.0 dB Q 1.10
```

Shelf band. Quick band index 0 (Sub foundation), 32 Hz, `LowShelf`, Q 0.70, **Impact**
preset gain +4.0 dB:

```
Filter 1: ON LSC 10.2 dB Fc 32 Hz Gain +4.0 dB
```

High shelf. Quick band index 5 (Air), 12000 Hz, `HighShelf`, Q 0.70, Impact gain +0.5 dB:

```
Filter 6: ON HSC 10.2 dB Fc 12000 Hz Gain +0.5 dB
```

Advanced slot. Index 6 (Advanced 07), 125 Hz, `Peaking`, Q 1.00, enabled by the user with
+3.0 dB:

```
Filter 7: ON PK Fc 125 Hz Gain +3.0 dB Q 1.00
```

Disabled slot. Index 7 (Advanced 08) is disabled by default. Either omit the line entirely,
or emit the REW-style no-op so the file stays readable and the band numbering stays obvious:

```
Filter 8: OFF None
```

Both are safe: the factory creates no filter from a line that does not begin with `ON <Type>`
([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)).
Omitting is preferred because it keeps the file honest about what is running.

### 3.4 A complete emitted file

For the **Impact** preset, six quick bands, advanced slots disabled, headphone trim off
(`Preamp` derived in section 4):

```
# Generated by getEQd. Edits here are overwritten.
Preamp: -4.6 dB
Filter 1: ON LSC 10.2 dB Fc 32 Hz Gain +4.0 dB
Filter 2: ON PK Fc 64 Hz Gain +3.0 dB Q 1.00
Filter 3: ON PK Fc 250 Hz Gain -1.5 dB Q 1.10
Filter 4: ON PK Fc 2500 Hz Gain +1.0 dB Q 0.90
Filter 5: ON PK Fc 6400 Hz Gain +1.5 dB Q 1.00
Filter 6: ON HSC 10.2 dB Fc 12000 Hz Gain +0.5 dB
```

Two properties worth stating explicitly, because they are what makes this a *mode* rather than
a second product:

1. **Same curve.** getEQd draws `EqSnapshot.CurveDbAt(sampleRate, f)`, which sums the analytic
   magnitude of its own biquads. Equalizer APO designs the same RBJ sections from the same
   Fc/Gain/Q/S values at the same device sample rate, so the emitted config reproduces the
   drawn curve. The one structural difference to watch is frequency clamping: getEQd clamps a
   band to `0.45 × sampleRate` while APO does not. On a 48 kHz device that bound is 21.6 kHz
   and the highest getEQd band is 16 kHz, so it does not bite in practice; on a 32 kHz device
   the 16 kHz slot would be clamped by getEQd to 14.4 kHz and the emitted line must carry the
   clamped value, not the nominal one.
2. **Same numbers the user sees.** Emitting `EffectiveGain` (trim included) means the file
   matches the fader positions and the headphone target trims the UI is showing, rather than a
   pre-trim value that would disagree with the display.

### 3.5 Minimum Equalizer APO version

The reference table is explicitly scoped: "The following table lists the filter types supported
by Equalizer APO since version 0.8.1", and the shelf rows carry a `(1.2.1)` annotation on the
optional Q/bandwidth column
([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).
Because this spec depends on the `LSC`/`HSC` slope form and on shelves without an explicit Q,
getEQd should require a current 1.x Equalizer APO and say so at the point of setup rather than
emitting a config that an old install parses differently. The exact first release that accepted
`LSC x dB` is **unverified**; the version annotation is read from the wiki table, not from a
release-by-release changelog.

## 4. Headroom and clipping

### 4.1 getEQd already computes the answer

`Analysis.Evaluate` in `windows/src/GetEQd.App/Audio/EqModel.cs` already does the work this
mode needs. It scans 240 logarithmically spaced points from 20 Hz to 20 kHz plus every exact
band centre, takes the peak of `EqSnapshot.CurveDbAt`, and returns
`SuggestedPreampDb = CeilingDb − peak`. `EqSettings.CeilingDb` defaults to −1.0 dB, so the
suggestion already carries 1 dB of margin, and `LimiterWillEngage` is true when the peak
exceeds the ceiling by more than 0.05 dB.

Reproducing those formulas at 48 kHz with the advanced slots disabled and headphone trim off
gives:

| Preset | Peak of the curve | Suggested preamp (ceiling −1.0 dB) |
|---|---|---|
| Reference | 0.000 dB | −1.000 dB |
| Impact | +3.618 dB | −4.618 dB |
| Dialogue | +2.728 dB | −3.728 dB |
| Night | +0.866 dB | −1.866 dB |

Those figures come from an independent re-implementation of `Biquad` and `Analysis.Evaluate`;
the app's own `Analysis.Evaluate` is the authority and should be the thing that fills the
field, not a second copy of the maths.

### 4.2 The rule for the emitted file

Emit exactly one preamp line, from the same value the app already shows:

```
Preamp: <HeadroomAnalysis.SuggestedPreampDb> dB      # rounded to 0.1 dB
```

That is `CeilingDb − peak`, which is the preamp that "puts material already peaking at full
scale exactly on the ceiling" — the wording `HeadroomAnalysis.SuggestedPreampDb` already
carries. For the Impact preset above, the line is `Preamp: -4.6 dB`.

### 4.3 What Equalizer APO does and does not protect against

**It does not protect anything by itself.** There is no limiter, no auto-gain and no clipping
stage in the filter chain: the factories registered by the engine are Device, If, Expression,
Include, Stage, Channel, IIR, BiQuad, Preamp, Delay, Copy, Convolution, GraphicEQ, VSTPlugin
and LoudnessCorrection ([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
`FilterEngine::FilterEngine`). A limiter can only appear if the user adds a VST plugin of their
own. The engine's process entry points take `float` buffers
([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
`FilterEngine::process`), and nothing in the chain clamps them, so a signal driven above
full scale will clip wherever it is finally converted, not inside APO.

The only headroom control APO offers is `Preamp:`, and its own documentation frames it exactly
that way: it "is useful when you are using filters with positive gain, to make sure that no
clipping occurs"
([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).

Three consequences getEQd must be honest about:

1. **Preamps sum.** "When multiple preamps apply to the same channel, the resulting preamp is
   the sum in dB" ([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).
   getEQd's line adds to whatever the user already has, so it must write the computed value
   once and never re-apply it on each save, and the UI should say the trim is additive with
   anything else in the chain.
2. **No ceiling enforcement.** In the player path getEQd can honour `CeilingDb` with its own
   limiter. In system-wide mode the ceiling is only a target used to *derive* the preamp; there
   is nothing downstream to catch a transient. That is a real capability difference between the
   two modes and belongs in the UI, not in a footnote.
3. **It is a worst-case static trim.** It assumes material that already peaks at full scale.
   It does not address source material that is already clipped, inter-sample peaks, or the
   sample-rate dependence of the curve's shape near Nyquist. The heavy-boost case is also
   audibly quieter overall, which is the honest trade for not clipping.

## 5. File layout, detection, and elevation

### 5.1 Yes, use an Include-based layout

getEQd should own one file and leave `config.txt` almost untouched:

```
<ConfigPath>\config.txt      # user-owned entry file; getEQd adds one Include line
<ConfigPath>\geteqd.txt      # getEQd-owned; preamp + filter lines, rewritten on every change
```

Reasons this is the right shape, all grounded:

- It is the pattern APO itself documents and ships. The tutorial says `config.txt` "first
  defines a preamplification value and then includes example.txt", and the reference adds that
  "instead of directly replacing config.txt, it can be better to load the actual filter
  definition from a separate file"
  ([Documentation wiki](https://sourceforge.net/p/equalizerapo/wiki/Documentation/),
  [Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)).
- A relative `Include:` resolves against the including file's directory, so `Include:
  geteqd.txt` next to `config.txt` resolves correctly with no absolute paths
  ([filters/IncludeFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/IncludeFilterFactory.cpp)).
- The change watcher covers the whole `ConfigPath` subtree, so editing only `geteqd.txt`
  reloads the configuration ([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
  `notificationThread`).
- The installer writes the shipped config files with `SetOverwrite off`, so an APO upgrade does
  not clobber an existing `config.txt`
  ([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)).
  That makes `config.txt` a durable place for the include line.
- Keeping getEQd's output in its own file means a user can delete one file to undo getEQd
  entirely, and getEQd never has to round-trip text it does not understand.

### 5.2 The one edit to config.txt

On first use, getEQd appends a single line if it is not already there:

```
Include: geteqd.txt
```

Rules for that edit:

- Idempotent. Search existing lines for `Include` whose argument is `geteqd.txt`, comparing
  case-insensitively and ignoring surrounding whitespace. If present, change nothing.
- Append at the end rather than rewriting the file, so the user's own preamp and filters stay
  exactly where they were.
- Never rewrite, reorder or reformat any other line, and preserve the file's existing line
  endings and encoding.
- Order note: a `Preamp:` line takes effect at its position in the chain, and preamps sum
  (section 4.3). Appending the include means getEQd's trim lands after the user's own, which is
  the least surprising placement and does not move any existing line.

### 5.3 Detecting whether APO is installed

Primary signal, and enough on its own:

1. Read `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO\ConfigPath`.
2. Confirm the directory exists.
3. Read `<ConfigPath>\config.txt` and check whether the include line is present.

Secondary signals, useful for a clearer message but not required:

- `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO\InstallPath` and
  `HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO\EnableTrace` exist alongside `ConfigPath`
  ([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)).
- The APO's own per-device registration lives under
  `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{...}`,
  which is where APO looks when it decides whether it is installed on a given endpoint — see
  the key paths in
  [DeviceAPOInfo.cpp](https://github.com/mirror/equalizerapo/blob/master/DeviceAPOInfo.cpp)
  (`commonKeyPath`, `renderKeyPath`, and the `Child APOs` key under `APP_REGPATH`). Reusing
  that shape lets getEQd answer "is the APO actually attached to my default output device",
  which is the question that matters when the user says "nothing changed". The exact value
  names under those keys are **unverified** here and would need a closer read of
  `DeviceAPOInfo.cpp` before getEQd depends on them.

A registry key existing is not the same as the APO being active. A freshly installed APO needs
the audio service to restart before it is used
([Documentation wiki](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)), so the UI
should be able to say "installed, but this device has not been confirmed working" rather than
claiming the EQ is live.

### 5.4 Do writes need elevation?

No, not on a default install, and getEQd should not ask for it.

- The installer grants `BUILTIN\Users` full access to the config directory
  ([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)), so
  writing `geteqd.txt` and appending to `config.txt` works from a normal user process.
- getEQd's own manifest is `asInvoker` (`windows/src/GetEQd.App/app.manifest`), so the app
  currently runs unelevated. That is the correct posture: an EQ front end that needs
  Administrator to move a fader is a worse product than one that does not.

Handling for the cases where it is not a default install:

- Read `HKLM` freely; never write it. Writing those values is the installer's and the
  Configurator's job.
- If a write throws `UnauthorizedAccessException`, report the exact path, the exact operation
  and the reason, and offer an elevated retry as an explicit user action. Do not relaunch
  elevated silently and do not retry in a loop.
- If `ConfigPath` is unreadable or the directory is missing, treat it as "APO not installed"
  rather than creating anything.

### 5.5 Writing safely

Two small disciplines that avoid visible glitches:

- **Write whole files atomically.** Write the new text to a temporary file in the same
  directory and then replace `geteqd.txt` with a rename, so APO never reads a half-written
  file. APO does retry on `ERROR_SHARING_VIOLATION` while a file is being written
  ([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
  `loadConfigFile`), so a torn read is unlikely, but a rename removes the window entirely.
  This is an engineering recommendation, not a documented APO requirement.
- **Skip identical writes.** APO reloads and cross-fades on every file change, so hold the last
  written text in memory and do nothing when the new render is byte-identical. Moving a fader
  continuously would otherwise queue a reload per step.

Encoding: UTF-8 without a BOM, `\r\n` line endings to match the shipped samples. APO reads
UTF-8 and falls back to the ANSI code page if the bytes are not valid UTF-8
([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp)).

## 6. Honest limits

### 6.1 What this cannot do

- **It cannot equalise audio APO never sees.** The APO author states the constraint directly:
  "E-APO can only process uncompressed audio data", so a Dolby Digital bitstream sent for
  passthrough is not processed
  ([Documentation wiki discussion](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)).
  That is a discussion comment rather than reference documentation, so treat it as a strong
  signal rather than a spec line.
- **Exclusive-mode and ASIO streams bypass the shared-mode APO chain.** This is standard
  Windows audio-engine behaviour, and APO's own documentation documents the sibling case for
  hardware-accelerated OpenAL, where "there is no way to enable APO support" and the only fixes
  are software fallback or replacing the vendor library
  ([Documentation wiki](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)). The
  exclusive-mode claim itself is **unverified against an APO document** in this pass.
- **It cannot be signed, and that has a cost.** Equalizer APO is an unsigned APO, and its
  installer sets `DisableProtectedAudioDG = 1` under
  `HKLM\Software\Microsoft\Windows\CurrentVersion\Audio`
  ([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi)).
  The author's own explanation is that the registry entry "is required by E-APO because it is
  an unsigned APO" and that this breaks the protected audio path, so software that checks for
  it "is practically incompatible with E-APO"
  ([Documentation wiki discussion](https://sourceforge.net/p/equalizerapo/wiki/Documentation/)).
  In practice some DRM'd playback can refuse to output sound. That is a machine-wide side effect
  of installing APO, and getEQd's setup copy has to say so before the user installs it.
- **getEQd cannot make itself trusted by association.** Shipping a signed getEQd does not sign
  APO. The signing spend in the project's decision record — an EV certificate plus a Microsoft
  Partner Center hardware account, roughly $200–500/year with identity verification — is what
  the kernel-APO route would have cost; the APO route avoids that spend but inherits APO's
  unsigned status. That figure is from the project's own decision record and was not
  independently re-verified in this pass.

### 6.2 What breaks when the user changes their default output device

- If APO was only installed to the old endpoint, the new device has no APO, Windows plays
  normally, and the EQ silently stops applying. Nothing errors, which is the worst kind of
  failure. getEQd should surface which endpoints APO is attached to and warn when the current
  default is not one of them.
- A `Device:` line makes this worse, not better, because it pins the config to a device string
  and possibly a GUID. Endpoint GUIDs are per-endpoint-instance: unplugging and replugging a
  USB headset can create a new endpoint with a new GUID, at which point a GUID-bearing pattern
  stops matching and the commands after it are ignored
  ([Configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/),
  [filters/DeviceFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/DeviceFilterFactory.cpp)).
  Recommendation: write no `Device:` line by default. A global config that the user scopes by
  installing APO only where they want it is more robust than a GUID match that quietly expires.
- Sample-rate changes re-initialise the APO and recompute every biquad at the new rate
  ([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
  `initialize`). The filter shapes are preserved by the RBJ design, but the response near
  Nyquist shifts, and getEQd's drawn curve is only exact for the rate it read.

### 6.3 What happens on uninstall

- **Uninstalling Equalizer APO** removes the APO, and the uninstaller has an optional component
  that deletes the config directory and the registry backups: it removes `$INSTDIR\*.reg`, does
  `RMDir /REBOOTOK /r "$INSTDIR\config"`, and deletes `HKCU\SOFTWARE\EqualizerAPO`
  ([Setup/Setup.nsi](https://github.com/mirror/equalizerapo/blob/master/Setup/Setup.nsi),
  uninstaller section). That component is opt-in — the section is marked `/o` — so by default
  the config files stay behind.
- If `geteqd.txt` disappears while the include line remains, APO logs an error and continues
  with the rest of `config.txt`
  ([FilterEngine.cpp](https://github.com/mirror/equalizerapo/blob/master/FilterEngine.cpp),
  `loadConfigFile` logs "Error while reading configuration file"). No EQ is applied and no
  harm is done, but getEQd should report the missing file rather than showing a green state.
- **Uninstalling getEQd is the real gap.** Nothing removes `geteqd.txt` or the include line, so
  the user's machine keeps applying the last EQ with no UI left to change it, and no obvious
  clue where it is coming from. The WiX installer already exists
  (`windows/installer/getEQd.wxs`, building `getEQd-0.2.0.msi`), so the uninstall path must be
  extended to offer removing `geteqd.txt` and stripping the include line it added — and only
  the line it added.

### 6.4 Things getEQd must not pretend to know

- The config directory is machine-wide and shared, so another user or the Configuration Editor
  may change it at any time. getEQd owns one file, not the directory.
- "Configuration written" is not "EQ is audible". getEQd's own probe signals play through its
  player path, not through APO, so they prove nothing about the APO chain. Verifying system-wide
  EQ means playing audio from another application, or measuring a loopback — neither of which
  getEQd can do on its own today.
- The config applies to every application on that device, including system sounds. That is the
  point of system-wide mode, and it is also why a bad config is immediately audible everywhere.

## 7. Staged outline: a mode, not a rewrite

Grounding read for this section: `windows/src/GetEQd.App/Audio/EqModel.cs`,
`windows/src/GetEQd.App/Audio/Profiles.cs`, `windows/src/GetEQd.App/Ui/ResponseLabView.cs`.

### 7.1 The one design idea

`EqSnapshot` is already the right seam. It is immutable, it carries the effective (trim-included)
gains, and it is the single source the curve, the analyser and the audio thread all read
(`EqSnapshot.From`, `BuildFilters`, `CurveDbAt` in `Audio/EqModel.cs`). System-wide mode adds a
**second consumer** of that same snapshot — a text renderer — and nothing else:

```
EqSettings ──► EqSnapshot ──┬──► EqProcessor / AudioEngine   (existing player path)
                            ├──► CurveView, Analysis.Evaluate (existing display path)
                            └──► ApoConfigText ──► geteqd.txt (new system-wide path)
```

That is why this is a mode switch rather than a fork. The DSP, the ten bands, the presets, the
headphone trims, the measured-model lab and the drawn curve are all untouched. If the renderer
ever disagrees with the drawn curve, that is a bug in the renderer, and the fix is in the
renderer — the curve stays authoritative.

### 7.2 Stage 7.1 — The renderer (pure and testable)

New file `windows/src/GetEQd.App/Apo/ApoConfigText.cs`. One public entry point that takes an
`EqSnapshot` and a sample rate and returns the file text. No file system, no UI, no state.

It owns the section 3 mapping and the section 4 preamp derivation, reusing
`Analysis.Evaluate(snapshot, sampleRate)` for the preamp so there is exactly one implementation
of that calculation.

Two details that will otherwise cause real bugs:

- **Culture.** Format every number with `CultureInfo.InvariantCulture`. A German-locale user
  would otherwise get `Fc 250,0 Hz`, and while APO does rewrite `,` to `.` before parsing
  ([filters/BiQuadFilterFactory.cpp](https://github.com/mirror/equalizerapo/blob/master/filters/BiQuadFilterFactory.cpp)),
  relying on that is unnecessary and would make golden-file tests locale-dependent.
- **Clamped values only.** Emit the frequency and Q that `Biquad.ForBand` actually uses, not the
  raw editor values, so the file and the curve cannot diverge (section 3.4).

Tests go in the existing xUnit project `windows/tests/GetEQd.Tests`, alongside
`AudioMathTests.cs`, `CurveInvariantTests.cs` and `SettingsRoundTripTests.cs`, as a new
`ApoConfigTextTests.cs`: each `BandKind` maps to the expected type token; the default 32 Hz
shelf emits `LSC 10.2 dB`; a disabled slot emits nothing; a 16 kHz band on a 32 kHz rate emits
the clamped 14.4 kHz; Q below 0.2 emits 0.2; the Impact preset renders a byte-exact golden
file; a comma-locale produces the same bytes as an invariant locale.

### 7.3 Stage 7.2 — Detect the installation

New file `windows/src/GetEQd.App/Apo/ApoInstallation.cs`. A small read-only probe returning one
of: not installed, installed (with `ConfigPath`, whether the include line is present, and
whether the directory is writable), or an error carrying a human-readable reason.

Registry access goes through `Microsoft.Win32.Registry`, which is already available to a
`net8.0-windows` app. Keep the registry read in a thin, untested shell and push the parts that
can be tested — include-line detection, path composition, writability probing — into functions
that take a directory path so they can run against a temporary directory in tests.

### 7.4 Stage 7.3 — Write the file

New file `windows/src/GetEQd.App/Apo/ApoConfigWriter.cs` with two operations:

- `EnsureInclude(configPath)` — append `Include: geteqd.txt` only when absent (section 5.2).
- `Write(configPath, text)` — atomic replace of `geteqd.txt`, skipped when the text is
  unchanged (section 5.5).

Return a small result describing what changed so the UI can say "wrote 6 filters" versus
"nothing to do". Tests run against a temp directory: the include is added exactly once across
repeated calls; unrelated lines, ordering, CRLF endings and encoding survive; identical text is
not rewritten; a read-only directory produces a clean error rather than an exception escaping
to the UI.

### 7.5 Stage 7.4 — Carry the mode in settings

Add an `OutputMode` value to `EqSettings` (`Audio/EqModel.cs`) and to `ListeningSettings`
(`Audio/Profiles.cs`), serialised as a string the way `Route`, `HeadphoneTarget` and `Handoff`
already are. Do **not** overload `Route` for this: `Route` means the speaker layout
(`Surround51`, `Stereo21`, `Headphones`) and is orthogonal to where the EQ is applied. Adding a
field is safe for existing profiles because `ProfileMapping.Apply` only touches fields it finds
and leaves defaults otherwise, which is the same mechanism that let `AdvancedMode` be added
after the six-band release. Cover it with a round-trip test.

### 7.6 Stage 7.5 — The UI switch and status

`windows/src/GetEQd.App/Ui/MainView.xaml.cs` is the ~1,311-line shared file called out in the
release plan; edits there must be serialized with any other agent touching it. The additions are
deliberately small:

- A two-state mode control next to the existing route selector, plus a status line showing
  whether APO was found, the config path, the last write time, and any error.
- A read-only panel showing the exact emitted text, following the `ResponseLabView` idiom — a
  view that displays and never edits. It is the fastest way for a user (or a support thread) to
  see why the sound changed.
- Debounce writes while a fader is being dragged. Combined with the skip-identical rule this
  keeps APO from reloading dozens of times per gesture.

### 7.7 Stage 7.6 — Decide what happens to the player

This is the design question that will otherwise become the top support complaint. getEQd's
player runs in WASAPI **shared** mode (see `windows/README.md`), so its own output passes through
the APO. If the player keeps applying its biquads while the APO applies the same curve, every
sample is equalised twice.

The honest default: when system-wide mode is on, getEQd's player bypasses its own filter chain
and lets APO do the work, so the two paths never stack. The mode control should say which
behaviour is in force rather than leaving the user to infer it. Treat "both at once" as an
explicit, warned choice at most — never the silent default.

### 7.8 Stage 7.7 — Bypass and empty states

`EqSettings.Bypassed` currently means "audio passes through untouched" (`Analysis.Evaluate`).
In system-wide mode the equivalent is a file that applies nothing: write a comment-only
`geteqd.txt` (or delete it and leave the include line, accepting the logged error) rather than
leaving a stale EQ in place. Whichever is chosen, make it explicit and test it, because "I
bypassed and the sound did not change" is otherwise indistinguishable from "the APO is not
working".

### 7.9 Stage 7.8 — Verification

What can be proven in CI, and what cannot:

| Claim | Evidence |
|---|---|
| The renderer emits the documented grammar | Unit tests plus a byte-exact golden file |
| The emitted values reproduce the drawn curve | Test that compares `ApoConfigText` output parsed back through a test-side RBJ evaluator against `EqSnapshot.CurveDbAt` at the same sample rate |
| The include line is added once and nothing else changes | Temp-directory writer tests |
| APO actually processes audio | Manual only: install APO, enable the mode, play audio from another app, hear the change. Cannot be automated without APO installed, and must never be claimed from a green CI run |

That last row is the rule from the release plan applied here: a written file is not evidence
that the EQ is audible.

### 7.10 What explicitly does not change

`Audio/AudioEngine.cs`, `Audio/EqProcessor.cs`, `Audio/Biquad.cs`, `Audio/SpectrumAnalyzer.cs`,
`Ui/CurveView.cs`, `Ui/MeterBar.cs`, the ten-band model, the presets, the headphone targets, the
measured-model lab, and the profile file formats. The only existing files that gain code are
`Audio/EqModel.cs` (one settings field), `Audio/Profiles.cs` (one serialised field),
`Ui/MainView.xaml.cs` and `Ui/MainView.xaml` (the mode control and status), and the project file
for the new `Apo` folder.

## Sources

Equalizer APO documentation (canonical, on SourceForge):

- Configuration reference — <https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/>
- User documentation, installation and configuration tutorial, troubleshooting —
  <https://sourceforge.net/p/equalizerapo/wiki/Documentation/>
- Project code — <https://sourceforge.net/p/equalizerapo/code/> (read here through the
  SourceForge mirror at <https://github.com/mirror/equalizerapo>, `master`)

Equalizer APO source files read for this spec:

- `Setup/Setup.nsi` — install path, registry values, `RequestExecutionLevel admin`, the
  Users-group ACL grant on the config directory, `SetOverwrite off` for config files, the
  uninstaller's optional config removal, `DisableProtectedAudioDG`
- `Setup/config/config.txt`, `Setup/config/example.txt`, `Setup/config/multichannel.txt` —
  shipped samples
- `FilterEngine.cpp` — `ConfigPath` read, `config.txt` load, include handling, directory-change
  notification, reload cross-fade, channel restore after include
- `helpers/RegistryHelper.h` — `APP_REGPATH` / `USER_REGPATH`
- `filters/BiQuadFilterFactory.cpp` — type map, parameter regexes, defaults, `ON` requirement
- `filters/BiQuadFilter.cpp`, `filters/BiQuad.cpp` — shelf center-vs-corner handling and the RBJ
  formulas
- `filters/PreampFilterFactory.cpp` — preamp parsing
- `filters/IncludeFilterFactory.cpp` — relative include resolution and recursion limit
- `filters/DeviceFilterFactory.cpp` — `Device:` matching rules
- `DeviceAPOInfo.cpp` — the registry keys APO itself uses for per-device registration
- `Editor/main.cpp`, `Editor/MainWindow.h` — how the Configuration Editor resolves the config
  directory

getEQd source read for this spec (read-only, not modified):

- `windows/src/GetEQd.App/Audio/EqModel.cs`, `Audio/Biquad.cs`, `Audio/Profiles.cs`
- `windows/src/GetEQd.App/Ui/ResponseLabView.cs`
- `windows/src/GetEQd.App/app.manifest`, `windows/src/GetEQd.App/GetEQd.App.csproj`,
  `windows/README.md`

## Unverified and open questions

These are labelled rather than guessed, and each one is cheap to close before implementation
starts:

1. **Per-device config files.** Whether any current Configuration Editor build writes
   additional per-device config files beyond `config.txt` plus includes. This spec relies only on
   the documented `Device:` command, so the answer does not change the design.
2. **32-bit registry view.** Whether a real-world install ever needs the `WOW6432Node` fallback
   when reading `ConfigPath`. Defensive only.
3. **Per-device APO detection.** The exact value names under
   `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{...}` that prove APO
   is attached to one endpoint. Needs a closer read of `DeviceAPOInfo.cpp` before getEQd shows
   per-device status.
4. **Exclusive-mode and ASIO bypass.** Widely true of the Windows audio engine, but not verified
   against an Equalizer APO document in this pass.
5. **Minimum version for `LSC x dB`.** The wiki annotates the shelf rows with `(1.2.1)`; the
   exact first release that accepted the slope form is unconfirmed.
6. **Processing latency.** APO adds latency; no figure is documented on the pages read, and none
   is claimed here.
7. **Signing cost.** The EV-certificate plus Partner Center hardware account figure (roughly
   $200–500/year) is from the project's own decision record, not independently re-verified.
8. **Internal numeric precision.** Only the `float` signatures of `FilterEngine::process` were
   verified; the full internal precision of the chain was not audited.
9. **Mirror freshness.** Syntax was verified against the mirror's `master`, not against a
   specific released installer. The shipped release is the ground truth for what users have
   installed.

No money is required for the approach specified here: Equalizer APO is free and open source, the
config directory is writable without elevation, and no signing is needed for getEQd to generate
config text. The only signing-related cost is inherited: Equalizer APO itself is unsigned, which
is why installing it sets `DisableProtectedAudioDG` and can affect DRM'd playback (section 6.1).
