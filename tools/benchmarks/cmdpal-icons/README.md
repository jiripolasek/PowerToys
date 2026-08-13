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

- Scenario A opens each configured page, sends paced Down taps, and requires the list to reach the bottom and wrap to the top twice. The default reproduces the two complete traversals of `All apps` and `Segoe icons` used for the manual baseline. A timeout, a missing wrap, or a subsequent cycle outside 75–125% of the first cycle fails the run instead of silently recording partial or extra coverage.
- Scenario B applies a fixed number of fast wheel steps down and slower wheel steps up on each page.

The exact product-side input, cache, and result counts vary with the implementation and can be the intended outcome of a change. The comparison script gates Scenario A on the external automation contract and completed Down-tap traversal counts instead. It reports worker inputs, provider resolutions, and new-load result counts as effects rather than treating an effective cache or protocol change as a workload mismatch.

Each iteration launches a fresh CmdPal process. This resets process-local caches, but does not flush the OS file cache or control ambient temperature. Use the same Release build settings, close unrelated work, and leave the default cooldown enabled. Three runs per side are the minimum; five alternating baseline/candidate runs are preferable when thermals are unstable.

## Capture

Build CmdPal as Release NativeAOT for every commit under test. Uncomment the existing debugging block in `src/modules/cmdpal/Microsoft.CmdPal.UI/Microsoft.CmdPal.UI.csproj`:

```xml
<PropertyGroup>
  <EnableCmdPalAOT>true</EnableCmdPalAOT>
  <GeneratePackageLocally>true</GeneratePackageLocally>
</PropertyGroup>
```

Keep this local build-only edit identical across stages; it is not part of the product commit being measured. Then run:

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

The runner builds only the standalone benchmark harness, invokes its Microsoft.Testing.Platform executable directly, validates the resulting TRX, and stores the report plus reproducibility metadata. Diagnostics buttons use UI Automation's Invoke pattern, and report generation/clipboard collection have bounded retries so a missed coordinate click cannot masquerade as a product hang. The runner closes any running CmdPal instance before each iteration and closes the benchmark instance afterward; do not run it while using CmdPal for other work.

Use `-SkipBuild` to reuse an already-built Release harness. Use `-ProductPath` to select an exact Release `Microsoft.CmdPal.UI.exe`; the default is `x64\Release\WinUI3Apps\CmdPal\AppX\Microsoft.CmdPal.UI.exe`, with `native` as the fallback.

## Compare

```powershell
.\tools\benchmarks\cmdpal-icons\Compare-CmdPalIconReports.ps1 `
    -BaselinePath .\artifacts\cmdpal-icon-benchmarks\baseline\A\<run> `
    -CandidatePath .\artifacts\cmdpal-icon-benchmarks\candidate\A\<run> `
    -Scenario A
```

The command emits PR-ready Markdown and machine-readable JSON. It first verifies that both sides used the same automation settings and completed comparable traversals. It then reports median process cost, UI responsiveness, applied-row latency, cache behavior, glyph latency, queue/dispatcher work, and icon-element creation/reuse/update cost. Percentiles remain the histogram upper bounds recorded by CmdPal diagnostics.
