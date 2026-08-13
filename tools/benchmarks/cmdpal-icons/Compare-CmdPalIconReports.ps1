# Copyright (c) Microsoft Corporation
# The Microsoft Corporation licenses this file to you under the MIT license.
# See the LICENSE file in the project root for more information.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BaselinePath,

    [Parameter(Mandatory)]
    [string] $CandidatePath,

    [Parameter(Mandatory)]
    [ValidateSet('A', 'B')]
    [string] $Scenario,

    [string] $OutputPath,

    [switch] $AllowWorkloadMismatch
)

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'CmdPalIconReport.ps1')

function Get-PropertyValue {
    param(
        [Parameter(Mandatory)]
        [object] $InputObject,

        [Parameter(Mandatory)]
        [string] $PropertyPath
    )

    $value = $InputObject
    foreach ($propertyName in $PropertyPath.Split('.')) {
        if ($null -eq $value) {
            return $null
        }

        $property = $value.PSObject.Properties[$propertyName]
        if ($null -eq $property) {
            return $null
        }

        $value = $property.Value
    }

    return $value
}

function Get-Median {
    param(
        [Parameter(Mandatory)]
        [object[]] $Reports,

        [Parameter(Mandatory)]
        [string] $PropertyPath
    )

    $values = @(
        $Reports |
            ForEach-Object { Get-PropertyValue -InputObject $_ -PropertyPath $PropertyPath } |
            Where-Object { $null -ne $_ } |
            ForEach-Object { [double]$_ } |
            Sort-Object
    )

    if ($values.Count -eq 0) {
        return $null
    }

    $middle = [int][Math]::Floor($values.Count / 2)
    if (($values.Count % 2) -eq 1) {
        return $values[$middle]
    }

    return ($values[$middle - 1] + $values[$middle]) / 2
}

function Get-TraversalMedian {
    param(
        [Parameter(Mandatory)]
        [object[]] $Reports,

        [Parameter(Mandatory)]
        [string] $Page
    )

    $values = @(
        $Reports |
            ForEach-Object {
                if ($_.Traversals.ContainsKey($Page)) {
                    $_.Traversals[$Page].TotalTaps
                }
            } |
            Where-Object { $null -ne $_ } |
            ForEach-Object { [double]$_ } |
            Sort-Object
    )
    if ($values.Count -eq 0) {
        return $null
    }

    $middle = [int][Math]::Floor($values.Count / 2)
    if (($values.Count % 2) -eq 1) {
        return $values[$middle]
    }

    return ($values[$middle - 1] + $values[$middle]) / 2
}

function Format-Number {
    param(
        [AllowNull()]
        [object] $Value,

        [string] $Unit = ''
    )

    if ($null -eq $Value) {
        return 'n/a'
    }

    $number = [double]$Value
    $formatted = if ([Math]::Abs($number - [Math]::Round($number)) -lt 0.0005) {
        $number.ToString('0', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    else {
        $number.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $formatted + $Unit
}

function Format-Change {
    param(
        [AllowNull()]
        [object] $Baseline,

        [AllowNull()]
        [object] $Candidate
    )

    if ($null -eq $Baseline -or $null -eq $Candidate -or [double]$Baseline -eq 0) {
        return 'n/a'
    }

    $change = (([double]$Candidate - [double]$Baseline) / [double]$Baseline) * 100
    return $change.ToString('+0.0;-0.0;0.0', [System.Globalization.CultureInfo]::InvariantCulture) + '%'
}

function Assert-CompleteReports {
    param(
        [Parameter(Mandatory)]
        [object[]] $Reports,

        [Parameter(Mandatory)]
        [string] $Label
    )

    foreach ($report in $Reports) {
        $problems = [System.Collections.Generic.List[string]]::new()
        if ($report.RequestsFailed -ne 0) { $problems.Add("request failures=$($report.RequestsFailed)") }
        if ($report.RequestsOutstanding -ne 0) { $problems.Add("outstanding requests=$($report.RequestsOutstanding)") }
        if ($report.LoadsRejected -ne 0) { $problems.Add("rejected loads=$($report.LoadsRejected)") }
        if ($null -ne $report.ResultFailed -and $report.ResultFailed -ne 0) { $problems.Add("failed results=$($report.ResultFailed)") }
        if ($null -ne $report.UiCallbacksRejected -and $report.UiCallbacksRejected -ne 0) { $problems.Add("rejected UI probes=$($report.UiCallbacksRejected)") }

        if ($null -ne $report.BenchmarkMetadata -and $report.BenchmarkMetadata.Scenario -eq 'A') {
            foreach ($page in @($report.BenchmarkMetadata.Pages)) {
                if (-not $report.Traversals.ContainsKey([string]$page)) {
                    $problems.Add("missing completed traversal for '$page'")
                    continue
                }

                $traversal = $report.Traversals[[string]$page]
                if ($traversal.CompletedWraps -ne $traversal.RequiredWraps) {
                    $problems.Add("incomplete traversal for '$page' ($($traversal.CompletedWraps)/$($traversal.RequiredWraps))")
                }

                if (-not $traversal.CyclesEquivalent) {
                    $problems.Add("non-equivalent traversal cycles for '$page' ($($traversal.CycleLengths -join ', ') taps)")
                }
            }
        }

        if ($problems.Count -gt 0) {
            throw "$Label report '$($report.Path)' is incomplete: $($problems -join ', ')."
        }
    }
}

$baselineReports = @(Get-CmdPalIconReports -Path $BaselinePath)
$candidateReports = @(Get-CmdPalIconReports -Path $CandidatePath)
Assert-CompleteReports -Reports $baselineReports -Label 'Baseline'
Assert-CompleteReports -Reports $candidateReports -Label 'Candidate'

$allReports = @($baselineReports) + @($candidateReports)
$reportsWithMetadata = @($allReports | Where-Object { $null -ne $_.BenchmarkMetadata })
$benchmarkMetadataAvailable = $reportsWithMetadata.Count -eq $allReports.Count
$benchmarkContractPass = $true
$benchmarkContractStatus = 'not available; using observed row/result counts'
if ($reportsWithMetadata.Count -gt 0) {
    $contracts = @($reportsWithMetadata | ForEach-Object { $_.BenchmarkContract } | Sort-Object -Unique)
    $scenarios = @($reportsWithMetadata | ForEach-Object { $_.BenchmarkMetadata.Scenario } | Sort-Object -Unique)
    $benchmarkContractPass = $benchmarkMetadataAvailable -and $contracts.Count -eq 1 -and $scenarios.Count -eq 1 -and $scenarios[0] -eq $Scenario
    $benchmarkContractStatus = if ($benchmarkContractPass) { 'PASS' } else { 'FAIL' }
}

$workloadRows = [System.Collections.Generic.List[object]]::new()
if ($benchmarkMetadataAvailable -and $Scenario -eq 'A') {
    foreach ($page in @($baselineReports[0].BenchmarkMetadata.Pages)) {
        $baseline = Get-TraversalMedian -Reports $baselineReports -Page ([string]$page)
        $candidate = Get-TraversalMedian -Reports $candidateReports -Page ([string]$page)
        $tolerance = 0.05
        $relativeDifference = if ($null -ne $baseline -and $null -ne $candidate -and $baseline -ne 0) {
            [Math]::Abs(($candidate - $baseline) / $baseline)
        }
        elseif ($baseline -eq $candidate) {
            0
        }
        else {
            [double]::PositiveInfinity
        }

        $workloadRows.Add([pscustomobject]@{
            Name = "Down taps for complete '$page' traversals"
            Baseline = $baseline
            Candidate = $candidate
            Change = Format-Change -Baseline $baseline -Candidate $candidate
            TolerancePercent = $tolerance * 100
            Pass = $relativeDifference -le $tolerance
        })
    }
}
elseif ($benchmarkMetadataAvailable -and $Scenario -eq 'B') {
    for ($index = 0; $index -lt $baselineReports[0].BenchmarkMetadata.Pages.Count; $index++) {
        $page = $baselineReports[0].BenchmarkMetadata.Pages[$index]
        $baseline = [double]$baselineReports[0].BenchmarkMetadata.FastScrollCounts[$index] + [double]$baselineReports[0].BenchmarkMetadata.SlowScrollCounts[$index]
        $candidate = [double]$candidateReports[0].BenchmarkMetadata.FastScrollCounts[$index] + [double]$candidateReports[0].BenchmarkMetadata.SlowScrollCounts[$index]
        $workloadRows.Add([pscustomobject]@{
            Name = "Configured wheel steps for '$page'"
            Baseline = $baseline
            Candidate = $candidate
            Change = Format-Change -Baseline $baseline -Candidate $candidate
            TolerancePercent = 0
            Pass = $baseline -eq $candidate
        })
    }
}
else {
    $fallbackWorkloadDefinitions = @(
        [pscustomobject]@{ Name = 'List-item requests'; Property = 'ListItemRequestsStarted'; Tolerance = 0.10 },
        [pscustomobject]@{ Name = 'List-item bitmap results'; Property = 'ListItemResultBitmap'; Tolerance = 0.05 },
        [pscustomobject]@{ Name = 'List-item Fluent glyph results'; Property = 'ListItemResultFluentGlyph'; Tolerance = 0.05 }
    )

    foreach ($definition in $fallbackWorkloadDefinitions) {
        $baseline = Get-Median -Reports $baselineReports -PropertyPath $definition.Property
        $candidate = Get-Median -Reports $candidateReports -PropertyPath $definition.Property
        $relativeDifference = if ($null -ne $baseline -and $null -ne $candidate -and $baseline -ne 0) {
            [Math]::Abs(($candidate - $baseline) / $baseline)
        }
        elseif ($baseline -eq $candidate) {
            0
        }
        else {
            [double]::PositiveInfinity
        }

        $workloadRows.Add([pscustomobject]@{
            Name = $definition.Name
            Baseline = $baseline
            Candidate = $candidate
            Change = Format-Change -Baseline $baseline -Candidate $candidate
            TolerancePercent = $definition.Tolerance * 100
            Pass = $relativeDifference -le $definition.Tolerance
        })
    }
}

$workloadPass = $benchmarkContractPass -and @($workloadRows | Where-Object { -not $_.Pass }).Count -eq 0

$metricDefinitions = @(
    [pscustomobject]@{ Name = 'Observed list-item requests'; Property = 'ListItemRequestsStarted'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Observed empty icon requests'; Property = 'RequestsEmpty'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Observed list-item bitmap results'; Property = 'ListItemResultBitmap'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Observed list-item Fluent glyph results'; Property = 'ListItemResultFluentGlyph'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Provider new loads'; Property = 'ProviderNewLoad'; Unit = ''; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Provider cache hits'; Property = 'ProviderCacheHit'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Worker String inputs'; Property = 'InputString'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Worker Stream inputs'; Property = 'InputStream'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'New-load bitmap results'; Property = 'ResultBitmap'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'New-load Fluent glyph results'; Property = 'ResultFluentGlyph'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Session duration'; Property = 'DurationMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Process CPU time'; Property = 'ProcessCpuMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Logical-core utilization'; Property = 'LogicalCoreUtilizationPercent'; Unit = '%'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Managed allocations'; Property = 'ManagedAllocationBytes'; Unit = ' bytes'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'GC pause time'; Property = 'GcPauseMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Working-set change'; Property = 'WorkingSetChangeMiB'; Unit = ' MiB'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'UI queue wait average'; Property = 'UiQueueWait.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'UI queue wait p95 bound'; Property = 'UiQueueWait.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'UI queue wait p99 bound'; Property = 'UiQueueWait.P99UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Applied list-row latency average'; Property = 'ListItemAppliedLatency.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Applied list-row latency p95 bound'; Property = 'ListItemAppliedLatency.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'New-load glyph latency average'; Property = 'NewLoadFluentGlyphLatency.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'New-load glyph latency p95 bound'; Property = 'NewLoadFluentGlyphLatency.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Loads created'; Property = 'LoadsCreated'; Unit = ''; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Direct glyph loads'; Property = 'DirectGlyphLoads'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Loads sent through worker queue'; Property = 'EnqueueToCompletion.Count'; Unit = ''; LowerIsBetter = $true },
    [pscustomobject]@{ Name = '20x20 cache capacity'; Property = 'Cache20.Capacity'; Unit = ' entries'; LowerIsBetter = $false },
    [pscustomobject]@{ Name = '20x20 cache hit rate'; Property = 'Cache20.HitRatePercent'; Unit = '%'; LowerIsBetter = $false },
    [pscustomobject]@{ Name = '20x20 cache removals'; Property = 'Cache20.EntriesRemoved'; Unit = ''; LowerIsBetter = $true },
    [pscustomobject]@{ Name = '20x20 cache low-score removals'; Property = 'Cache20.LowScoreRemovals'; Unit = ''; LowerIsBetter = $true },
    [pscustomobject]@{ Name = '20x20 glyph-cache hit rate'; Property = 'GlyphCache20.HitRatePercent'; Unit = '%'; LowerIsBetter = $false },
    [pscustomobject]@{ Name = '20x20 glyph-cache maximum entries'; Property = 'GlyphCache20.MaximumObservedEntries'; Unit = ' entries'; LowerIsBetter = $false },
    [pscustomobject]@{ Name = '20x20 other-cache hit rate'; Property = 'OtherCache20.HitRatePercent'; Unit = '%'; LowerIsBetter = $false },
    [pscustomobject]@{ Name = '20x20 other-cache maximum entries'; Property = 'OtherCache20.MaximumObservedEntries'; Unit = ' entries'; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Queue wait average'; Property = 'QueueWait.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Queue wait p95 bound'; Property = 'QueueWait.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Demanded queue wait average'; Property = 'DemandedQueueWait.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Demanded queue wait p95 bound'; Property = 'DemandedQueueWait.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Dispatcher callbacks for queued loads'; Property = 'DispatcherWait.Count'; Unit = ''; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Dispatcher wait average'; Property = 'DispatcherWait.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Dispatcher wait p95 bound'; Property = 'DispatcherWait.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Dispatcher callback wall-time average'; Property = 'DispatcherCallbackWallTime.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Dispatcher callback wall-time p95 bound'; Property = 'DispatcherCallbackWallTime.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Icon-element update average'; Property = 'IconElementUpdate.AverageMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Icon-element update p95 bound'; Property = 'IconElementUpdate.P95UpperBoundMs'; Unit = ' ms'; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Icon elements created'; Property = 'IconElementsCreated'; Unit = ''; LowerIsBetter = $true },
    [pscustomobject]@{ Name = 'Icon elements reused'; Property = 'IconElementsReused'; Unit = ''; LowerIsBetter = $false },
    [pscustomobject]@{ Name = 'Loads completed without requester'; Property = 'LoadsCompletedWithoutRequester'; Unit = ''; LowerIsBetter = $true }
)

$metricRows = foreach ($definition in $metricDefinitions) {
    $baseline = Get-Median -Reports $baselineReports -PropertyPath $definition.Property
    $candidate = Get-Median -Reports $candidateReports -PropertyPath $definition.Property
    [pscustomobject]@{
        Name = $definition.Name
        Property = $definition.Property
        Unit = $definition.Unit
        LowerIsBetter = $definition.LowerIsBetter
        Baseline = $baseline
        Candidate = $candidate
        Change = Format-Change -Baseline $baseline -Candidate $candidate
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $candidateItem = Get-Item -LiteralPath (Resolve-Path -LiteralPath $CandidatePath)
    $outputDirectory = if ($candidateItem.PSIsContainer) { $candidateItem.FullName } else { $candidateItem.DirectoryName }
    $OutputPath = Join-Path $outputDirectory "comparison-$Scenario.md"
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$markdown = [System.Text.StringBuilder]::new()
$null = $markdown.AppendLine('# CmdPal icon benchmark comparison')
$null = $markdown.AppendLine()
$null = $markdown.AppendLine("- Scenario: $Scenario")
$null = $markdown.AppendLine("- Baseline runs: $($baselineReports.Count)")
$null = $markdown.AppendLine("- Candidate runs: $($candidateReports.Count)")
$null = $markdown.AppendLine("- Automation contract: $benchmarkContractStatus")
$null = $markdown.AppendLine("- Workload gate: $(if ($workloadPass) { 'PASS' } else { 'FAIL' })")
if ($baselineReports.Count -lt 3 -or $candidateReports.Count -lt 3) {
    $null = $markdown.AppendLine('- Confidence: exploratory; collect at least three runs per side for PR evidence.')
}
else {
    $null = $markdown.AppendLine('- Confidence: medians of at least three runs per side.')
}

$null = $markdown.AppendLine()
$null = $markdown.AppendLine('## Workload equivalence')
$null = $markdown.AppendLine()
$null = $markdown.AppendLine('The gate uses the external automation contract and completed traversal input. Worker input kinds, provider resolutions, cache activity, and new-load result counts are outcomes of the implementation and are reported below instead of being used to reject intended optimizations.')
$null = $markdown.AppendLine()
$null = $markdown.AppendLine('| Metric | Baseline median | Candidate median | Change | Allowed | Result |')
$null = $markdown.AppendLine('|---|---:|---:|---:|---:|:---:|')
foreach ($row in $workloadRows) {
    $null = $markdown.AppendLine("| $($row.Name) | $(Format-Number $row.Baseline) | $(Format-Number $row.Candidate) | $($row.Change) | +/-$(Format-Number $row.TolerancePercent '%') | $(if ($row.Pass) { 'PASS' } else { 'FAIL' }) |")
}

$null = $markdown.AppendLine()
$null = $markdown.AppendLine('## Performance and cost')
$null = $markdown.AppendLine()
$null = $markdown.AppendLine('| Metric | Baseline median | Candidate median | Change | Direction |')
$null = $markdown.AppendLine('|---|---:|---:|---:|---|')
foreach ($row in $metricRows) {
    $direction = if ($row.LowerIsBetter) { 'lower is better' } else { 'descriptive' }
    $null = $markdown.AppendLine("| $($row.Name) | $(Format-Number $row.Baseline $row.Unit) | $(Format-Number $row.Candidate $row.Unit) | $($row.Change) | $direction |")
}

$null = $markdown.AppendLine()
$null = $markdown.AppendLine('Percentile values are histogram upper bounds from the diagnostics report. Process measurements include all CmdPal work during the session, not only icon loading.')

[IO.File]::WriteAllText($OutputPath, $markdown.ToString())

$jsonPath = [IO.Path]::ChangeExtension($OutputPath, '.json')
[pscustomobject]@{
    Scenario = $Scenario
    BaselineRuns = $baselineReports.Count
    CandidateRuns = $candidateReports.Count
    BenchmarkMetadataAvailable = $benchmarkMetadataAvailable
    BenchmarkContractPass = $benchmarkContractPass
    WorkloadPass = $workloadPass
    Workload = $workloadRows
    Metrics = $metricRows
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath

Write-Host "Wrote $OutputPath"
Write-Host "Wrote $jsonPath"

if (-not $workloadPass -and -not $AllowWorkloadMismatch) {
    throw 'The reports do not represent equivalent workloads. Re-run the automation or pass -AllowWorkloadMismatch for exploratory analysis.'
}
