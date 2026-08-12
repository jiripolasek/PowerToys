# CmdPal icon benchmark

This opt-in benchmark drives a Release CmdPal build through `winappcli`, captures the existing icon diagnostics report, and compares matched runs outside the measured process. It adds no benchmark callbacks, locks, or per-item measurement work to CmdPal's STA path.

The harness is deliberately independent from the product UI-test suites. It is not registered in `PowerToys.slnx` or CI, and it neither edits nor depends on the legacy WinAppDriver CmdPal tests.

## Prerequisite

Install `winappcli` once:

```powershell
winget install Microsoft.winappcli
```

The runner checks `%LOCALAPPDATA%\Microsoft\WindowsApps\winapp.exe` and `PATH`. Use `-WinAppCliPath` when the executable lives elsewhere.

## Workloads

- Scenario A opens each configured page, sends paced Down taps, and requires the list to reach the bottom and wrap to the top twice. The default reproduces the two complete traversals of `All apps` and `Segoe icons` used for the manual baseline; a timeout fails the run instead of silently recording partial coverage.
- Scenario B applies a fixed number of fast wheel steps down and slower wheel steps up on each page.

The exact input/result counts still vary with machine contents and timing. The comparison script therefore rejects pairs whose String, Stream, Bitmap, or FluentGlyph counts differ beyond their configured tolerances.

Each iteration launches a fresh CmdPal process. This resets process-local caches, but does not flush the OS file cache or control ambient temperature. Use the same Release build settings, close unrelated work, and leave the default cooldown enabled. Three runs per side are the minimum; five alternating baseline/candidate runs are preferable when thermals are unstable.

## Capture

Build the Release CmdPal binary for the commit under test, then run:

```powershell
.\tools\benchmarks\cmdpal-icons\Invoke-CmdPalIconBenchmark.ps1 `
    -Scenario A `
    -Stage baseline `
    -Iterations 3
```

For a short harness smoke test:

```powershell
.\tools\benchmarks\cmdpal-icons\Invoke-CmdPalIconBenchmark.ps1 `
    -Scenario A `
    -Stage smoke `
    -Iterations 1 `
    -Pages 'All apps' `
    -KeyboardTraversalTimeoutMilliseconds 30000 `
    -KeyboardWrapCounts 1 `
    -FastScrollCounts 1 `
    -SlowScrollCounts 1 `
    -CooldownSeconds 0
```

The runner builds only the standalone benchmark harness, invokes its Microsoft.Testing.Platform executable directly, validates the resulting TRX, and stores the report plus reproducibility metadata. It closes any running CmdPal instance before each iteration and closes the benchmark instance afterward; do not run it while using CmdPal for other work.

Use `-SkipBuild` to reuse an already-built Release harness. Use `-ProductPath` to select an exact Release `Microsoft.CmdPal.UI.exe`; the default is `x64\Release\WinUI3Apps\CmdPal\AppX\Microsoft.CmdPal.UI.exe`, with `native` as the fallback.

## Compare

```powershell
.\tools\benchmarks\cmdpal-icons\Compare-CmdPalIconReports.ps1 `
    -BaselinePath .\artifacts\cmdpal-icon-benchmarks\baseline\A\<run> `
    -CandidatePath .\artifacts\cmdpal-icon-benchmarks\candidate\A\<run> `
    -Scenario A
```

The command emits PR-ready Markdown and machine-readable JSON. It reports workload equivalence first, then median process cost, UI responsiveness, applied-row latency, glyph latency, queue/dispatcher work, and icon-element update cost. Percentiles remain the histogram upper bounds recorded by CmdPal diagnostics.
