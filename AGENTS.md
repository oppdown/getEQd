# Working in this repository

`PROJECT_CONTEXT.md` is the plan of record: product intent, ownership, version scheme, and
the open items. Read it first. This file covers how to change the code without breaking
what already works.

## Layout

| Path | What it is |
|------|------------|
| `dist/` | The published public site. Static HTML/CSS/JS; a Sites host serves this directory. |
| `windows/src/GetEQd.App/` | The native Windows console. C# on `net8.0-windows`, WPF. |
| `windows/tests/` | xUnit tests for the audio math, profile handling, and settings round-tripping. |
| `windows/README.md` | The native app's own documentation. |
| `.tools/dotnet/` | A machine-local .NET SDK. Ignored; never commit it. |

## The one invariant that must not break

The curve drawn in the UI is the analytic magnitude response of the filters that are
actually running. The self-test measures real audio output against that drawn curve and
holds it within 0.044 dB. Never replace that with a curve computed separately from the
audio path, and never let the display and the DSP drift apart. If a change makes the two
disagree, the change is wrong regardless of how good it looks.

## Building and verifying

Build with the bundled SDK; a machine-wide `dotnet` may not exist.

    .\.tools\dotnet\dotnet.exe build windows\src\GetEQd.App\GetEQd.App.csproj -c Release
    .\.tools\dotnet\dotnet.exe test windows\tests\GetEQd.Tests\GetEQd.Tests.csproj
    windows\dist-measured\getEQd.exe --selftest report.txt

The last one is the acceptance check: the app measures its own audio path. Build
existence is not completion evidence; the self-test and a real launch are.

## Shared files: one writer at a time

`windows/src/GetEQd.App/Ui/MainView.xaml.cs` is roughly 1,300 lines and holds most of the
console's behavior. Two agents editing it concurrently will conflict. Serialize changes to
it, or split the work so one writer owns it for the duration.

The same applies to `windows/src/GetEQd.App/Ui/MainView.xaml` and `Ui/Theme.xaml`: they
define the layout and styling for every panel, so a concurrent edit repaints someone
else's work.

Safe for parallel writers by convention: new files under
`windows/src/GetEQd.App/Diagnostics/`, `windows/tests/`, and `windows/profiles/`.

## Guardrails

- The public site stays marketing-led. No repository, source, or release links in public
  navigation or calls to action.
- Never claim something ships when it is only local. Say "local" plainly.
- Do not commit executables. Binaries are release assets attached to a release, not
  repository content. `.gitignore` blocks `*.exe` and `*.pdb` for this reason.
- Never launch an interactive Git credential prompt. Fail visibly rather than triggering a
  login window. The publishing remote requires a credential that is not stored here.
- Treat model-specific headphone curves as measured data with a named source, rig, and
  date. Category or average curves are not model measurements.
- Keep the published site revision separate from unpublished local work until publication
  is explicitly approved.
