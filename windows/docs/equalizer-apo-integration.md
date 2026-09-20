# getEQd v0.3.0: System-Wide Mode via Equalizer APO

Status: in progress

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
