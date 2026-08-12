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

        if ($problems.Count -gt 0) {
            throw "$Label report '$($report.Path)' is incomplete: $($problems -join ', ')."
        }
    }
}

$baselineReports = @(Get-CmdPalIconReports -Path $BaselinePath)
$candidateReports = @(Get-CmdPalIconReports -Path $CandidatePath)
Assert-CompleteReports -Reports $baselineReports -Label 'Baseline'
Assert-CompleteReports -Reports $candidateReports -Label 'Candidate'

$workloadDefinitions = @(
    [pscustomobject]@{ Name = 'List-item requests'; Property = 'ListItemRequestsStarted'; Tolerance = 0.05 },
    [pscustomobject]@{ Name = 'Empty icon requests'; Property = 'RequestsEmpty'; Tolerance = 0.05 },
    [pscustomobject]@{ Name = 'String inputs'; Property = 'InputString'; Tolerance = 0.10 },
    [pscustomobject]@{ Name = 'Stream inputs'; Property = 'InputStream'; Tolerance = 0.05 },
    [pscustomobject]@{ Name = 'Bitmap results'; Property = 'ResultBitmap'; Tolerance = 0.05 },
    [pscustomobject]@{ Name = 'Fluent glyph results'; Property = 'ResultFluentGlyph'; Tolerance = 0.10 }
)

$workloadRows = foreach ($definition in $workloadDefinitions) {
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

    [pscustomobject]@{
        Name = $definition.Name
        Baseline = $baseline
        Candidate = $candidate
        Change = Format-Change -Baseline $baseline -Candidate $candidate
        TolerancePercent = $definition.Tolerance * 100
        Pass = $relativeDifference -le $definition.Tolerance
    }
}

$workloadPass = @($workloadRows | Where-Object { -not $_.Pass }).Count -eq 0

$metricDefinitions = @(
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
    WorkloadPass = $workloadPass
    Workload = $workloadRows
    Metrics = $metricRows
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath

Write-Host "Wrote $OutputPath"
Write-Host "Wrote $jsonPath"

if (-not $workloadPass -and -not $AllowWorkloadMismatch) {
    throw 'The reports do not represent equivalent workloads. Re-run the automation or pass -AllowWorkloadMismatch for exploratory analysis.'
}
