# Copyright (c) Microsoft Corporation
# The Microsoft Corporation licenses this file to you under the MIT license.
# See the LICENSE file in the project root for more information.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('A', 'B')]
    [string] $Scenario,

    [ValidateRange(1, 20)]
    [int] $Iterations = 3,

    [string] $Stage,

    [string] $ProductPath,

    [string] $OutputRoot,

    [string] $WinAppCliPath,

    [string[]] $Pages = @('All apps', 'Segoe icons'),

    [int[]] $KeyboardTraversalTimeoutMilliseconds = @(120000, 240000),

    [int[]] $KeyboardWrapCounts = @(2, 2),

    [ValidateRange(0, 1000)]
    [int] $KeyboardTapDelayMilliseconds = 0,

    [ValidateRange(1, 1000)]
    [int] $KeyboardCoarseProbeInterval = 250,

    [ValidateRange(1, 1000)]
    [int] $KeyboardFineProbeInterval = 10,

    [int[]] $FastScrollCounts = @(80, 240),

    [int[]] $SlowScrollCounts = @(80, 240),

    [ValidateRange(0, 600000)]
    [int] $NavigationDelayMilliseconds = 1500,

    [ValidateRange(0, 600000)]
    [int] $SettleMilliseconds = 1500,

    [ValidateRange(0, 60000)]
    [int] $FastScrollDelayMilliseconds = 5,

    [ValidateRange(0, 60000)]
    [int] $SlowScrollDelayMilliseconds = 75,

    [ValidateRange(0, 3600)]
    [int] $CooldownSeconds = 30,

    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-WinAppCliPath {
    if (-not [string]::IsNullOrWhiteSpace($WinAppCliPath)) {
        return (Resolve-Path -LiteralPath $WinAppCliPath).Path
    }

    $configured = [Environment]::GetEnvironmentVariable('WINAPP_CLI_PATH', 'Process')
    if (-not [string]::IsNullOrWhiteSpace($configured) -and [IO.File]::Exists($configured)) {
        return [IO.Path]::GetFullPath($configured)
    }

    $standardPath = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winapp.exe'
    if ([IO.File]::Exists($standardPath)) {
        return [IO.Path]::GetFullPath($standardPath)
    }

    $command = Get-Command 'winapp.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    throw 'winapp.exe was not found. Install it with `winget install Microsoft.winappcli` or pass -WinAppCliPath.'
}

function Set-BenchmarkEnvironment {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Values
    )

    $previous = @{}
    foreach ($entry in $Values.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, 'Process')
    }

    return $previous
}

function Restore-BenchmarkEnvironment {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Values
    )

    foreach ($entry in $Values.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}

function Assert-PassingTrx {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    [xml] $trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters) {
        throw "TRX '$Path' has no test counters."
    }

    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    if ($total -lt 1 -or $executed -ne $total -or $passed -ne $total) {
        throw "TRX '$Path' is not a complete passing run (total=$total, executed=$executed, passed=$passed)."
    }
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$benchmarkProjectDirectory = Join-Path $PSScriptRoot 'Microsoft.CmdPal.IconBenchmark'
$benchmarkProject = Join-Path $benchmarkProjectDirectory 'Microsoft.CmdPal.IconBenchmark.csproj'
$buildScript = Join-Path $repoRoot 'tools\build\build.ps1'

if ($Pages.Count -eq 0) {
    throw 'At least one page is required.'
}

if ($KeyboardTraversalTimeoutMilliseconds.Count -ne $Pages.Count -or
    $KeyboardWrapCounts.Count -ne $Pages.Count -or
    $FastScrollCounts.Count -ne $Pages.Count -or
    $SlowScrollCounts.Count -ne $Pages.Count) {
    throw 'KeyboardTraversalTimeoutMilliseconds, KeyboardWrapCounts, FastScrollCounts, and SlowScrollCounts must each contain one value per page.'
}

if (@($KeyboardWrapCounts | Where-Object { $_ -le 0 }).Count -ne 0) {
    throw 'KeyboardWrapCounts values must be positive integers.'
}

if (@($KeyboardTraversalTimeoutMilliseconds | Where-Object { $_ -le 0 }).Count -ne 0) {
    throw 'KeyboardTraversalTimeoutMilliseconds values must be positive integers.'
}

if ([string]::IsNullOrWhiteSpace($ProductPath)) {
    $ProductPath = Join-Path $repoRoot 'x64\Release\WinUI3Apps\CmdPal\AppX\Microsoft.CmdPal.UI.exe'
    if (-not (Test-Path -LiteralPath $ProductPath)) {
        $ProductPath = Join-Path $repoRoot 'x64\Release\WinUI3Apps\CmdPal\native\Microsoft.CmdPal.UI.exe'
    }
}

$ProductPath = (Resolve-Path -LiteralPath $ProductPath).Path
if ($ProductPath -notmatch '\\Release\\') {
    throw "The benchmark requires a Release CmdPal binary; got '$ProductPath'."
}

$resolvedWinAppCliPath = Resolve-WinAppCliPath

if ([string]::IsNullOrWhiteSpace($Stage)) {
    $Stage = (& git -C $repoRoot rev-parse --short=10 HEAD).Trim()
}

$safeStage = $Stage -replace '[^A-Za-z0-9._-]', '_'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\cmdpal-icon-benchmarks'
}

$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) (Join-Path $safeStage (Join-Path $Scenario (Get-Date -Format 'yyyyMMdd-HHmmss')))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

if (-not $SkipBuild) {
    & dotnet restore $benchmarkProject '-p:Platform=x64'
    if ($LASTEXITCODE -ne 0) {
        throw 'The benchmark harness restore failed.'
    }

    & $buildScript -Path $benchmarkProjectDirectory -Platform x64 -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw 'The benchmark harness build failed.'
    }
}

$testExecutable = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'x64\Release\tools\Microsoft.CmdPal.IconBenchmark') -Recurse -File -Filter 'Microsoft.CmdPal.IconBenchmark.exe' |
    Where-Object { $_.FullName -match '\\net10\.' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $testExecutable) {
    throw 'The Release Microsoft.CmdPal.IconBenchmark test executable was not found.'
}

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$binaryHash = (Get-FileHash -LiteralPath $ProductPath -Algorithm SHA256).Hash

for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    if ($CooldownSeconds -gt 0) {
        Write-Host "Cooling down for $CooldownSeconds seconds before run $iteration/$Iterations..."
        Start-Sleep -Seconds $CooldownSeconds
    }

    $reportPath = Join-Path $runDirectory ('run-{0:D2}.txt' -f $iteration)
    $metadataPath = Join-Path $runDirectory ('run-{0:D2}.json' -f $iteration)
    $testResultsDirectory = Join-Path $runDirectory ('test-results\run-{0:D2}' -f $iteration)
    New-Item -ItemType Directory -Path $testResultsDirectory -Force | Out-Null
    $trxFileName = 'benchmark.trx'
    $trxPath = Join-Path $testResultsDirectory $trxFileName

    $environment = @{
        TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
        WINAPP_CLI_PATH = $resolvedWinAppCliPath
        CMDPAL_ICON_BENCHMARK_PRODUCT = $ProductPath
        CMDPAL_ICON_BENCHMARK_SCENARIO = $Scenario
        CMDPAL_ICON_BENCHMARK_OUTPUT = $reportPath
        CMDPAL_ICON_BENCHMARK_PAGES = $Pages -join ';'
        CMDPAL_ICON_BENCHMARK_KEYBOARD_TIMEOUT_MS = $KeyboardTraversalTimeoutMilliseconds -join ';'
        CMDPAL_ICON_BENCHMARK_KEYBOARD_WRAP_COUNTS = $KeyboardWrapCounts -join ';'
        CMDPAL_ICON_BENCHMARK_KEYBOARD_TAP_DELAY_MS = $KeyboardTapDelayMilliseconds
        CMDPAL_ICON_BENCHMARK_KEYBOARD_COARSE_PROBE_INTERVAL = $KeyboardCoarseProbeInterval
        CMDPAL_ICON_BENCHMARK_KEYBOARD_FINE_PROBE_INTERVAL = $KeyboardFineProbeInterval
        CMDPAL_ICON_BENCHMARK_FAST_SCROLL_COUNTS = $FastScrollCounts -join ';'
        CMDPAL_ICON_BENCHMARK_SLOW_SCROLL_COUNTS = $SlowScrollCounts -join ';'
        CMDPAL_ICON_BENCHMARK_NAVIGATION_DELAY_MS = $NavigationDelayMilliseconds
        CMDPAL_ICON_BENCHMARK_SETTLE_MS = $SettleMilliseconds
        CMDPAL_ICON_BENCHMARK_FAST_SCROLL_DELAY_MS = $FastScrollDelayMilliseconds
        CMDPAL_ICON_BENCHMARK_SLOW_SCROLL_DELAY_MS = $SlowScrollDelayMilliseconds
    }

    $previousEnvironment = Set-BenchmarkEnvironment -Values $environment
    try {
        Write-Host "Running scenario $Scenario, iteration $iteration/$Iterations..."
        & $testExecutable.FullName `
            '--filter' 'FullyQualifiedName~Microsoft.CmdPal.IconBenchmark.IconPerformanceTests.CaptureIconEvidence' `
            '--report-trx' `
            '--report-trx-filename' $trxFileName `
            '--results-directory' $testResultsDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "The benchmark test failed for iteration $iteration with exit code $LASTEXITCODE. See '$testResultsDirectory'."
        }
    }
    finally {
        Restore-BenchmarkEnvironment -Values $previousEnvironment
    }

    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "The benchmark completed without writing '$reportPath'."
    }

    if (-not (Test-Path -LiteralPath $trxPath)) {
        throw "The benchmark completed without writing '$trxPath'."
    }

    Assert-PassingTrx -Path $trxPath

    [pscustomobject]@{
        Stage = $Stage
        Commit = $commit
        Scenario = $Scenario
        Iteration = $iteration
        CapturedUtc = [DateTimeOffset]::UtcNow
        ProductPath = $ProductPath
        ProductSha256 = $binaryHash
        WinAppCliPath = $resolvedWinAppCliPath
        TestExecutable = $testExecutable.FullName
        TrxPath = $trxPath
        Pages = $Pages
        KeyboardTraversalTimeoutMilliseconds = $KeyboardTraversalTimeoutMilliseconds
        KeyboardWrapCounts = $KeyboardWrapCounts
        KeyboardTapDelayMilliseconds = $KeyboardTapDelayMilliseconds
        KeyboardCoarseProbeInterval = $KeyboardCoarseProbeInterval
        KeyboardFineProbeInterval = $KeyboardFineProbeInterval
        FastScrollCounts = $FastScrollCounts
        SlowScrollCounts = $SlowScrollCounts
        NavigationDelayMilliseconds = $NavigationDelayMilliseconds
        SettleMilliseconds = $SettleMilliseconds
        FastScrollDelayMilliseconds = $FastScrollDelayMilliseconds
        SlowScrollDelayMilliseconds = $SlowScrollDelayMilliseconds
        CooldownSeconds = $CooldownSeconds
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath
}

Write-Host "Reports written to $runDirectory"
Write-Host "Compare with: .\tools\benchmarks\cmdpal-icons\Compare-CmdPalIconReports.ps1 -BaselinePath '<baseline>' -CandidatePath '$runDirectory' -Scenario $Scenario"
