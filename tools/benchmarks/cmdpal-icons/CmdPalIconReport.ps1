# Copyright (c) Microsoft Corporation
# The Microsoft Corporation licenses this file to you under the MIT license.
# See the LICENSE file in the project root for more information.

Set-StrictMode -Version Latest

function ConvertFrom-CmdPalIconReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [string] $Path
    )

    process {
        $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
        $lines = Get-Content -LiteralPath $resolvedPath
        if ($lines.Count -eq 0 -or $lines[0] -ne 'CmdPal icon diagnostics') {
            throw "Not a CmdPal icon diagnostics report: $resolvedPath"
        }

        $values = @{}
        $headings = [System.Collections.Generic.List[object]]::new()

        foreach ($line in $lines) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $trimmed = $line.Trim()
            $indent = $line.Length - $line.TrimStart().Length
            if ($trimmed -match '^([^:]+):\s*(.*)$') {
                $parents = @($headings | Where-Object { $_.Indent -lt $indent } | ForEach-Object { $_.Text })
                $key = $Matches[1].Trim()
                $value = $Matches[2].Trim()
                $reportPath = (@($parents) + $key) -join ' > '
                $values[$reportPath] = $value
                continue
            }

            while ($headings.Count -gt 0 -and $headings[$headings.Count - 1].Indent -ge $indent) {
                $headings.RemoveAt($headings.Count - 1)
            }

            $headings.Add([pscustomobject]@{ Indent = $indent; Text = $trimmed })
        }

        function Get-RawValue([string] $key) {
            if ($values.ContainsKey($key)) {
                return $values[$key]
            }

            return $null
        }

        function Get-Number([string] $key) {
            $raw = Get-RawValue $key
            if ($null -eq $raw -or $raw -notmatch '^[+\-]?([0-9]+(?:\.[0-9]+)?)') {
                return $null
            }

            return [double]::Parse($Matches[0], [System.Globalization.CultureInfo]::InvariantCulture)
        }

        function Get-Statistics([string] $key) {
            $raw = Get-RawValue $key
            if ($null -eq $raw -or $raw -eq 'no samples') {
                return $null
            }

            if ($raw -notmatch '^count=([0-9]+), avg=([0-9.]+) ms, p50=<=([0-9.]+) ms, p95=<=([0-9.]+) ms, p99=<=([0-9.]+) ms, max=([0-9.]+) ms$') {
                throw "Unexpected statistics value for '$key' in '$resolvedPath': $raw"
            }

            return [pscustomobject]@{
                Count = [int64]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
                AverageMs = [double]::Parse($Matches[2], [System.Globalization.CultureInfo]::InvariantCulture)
                P50UpperBoundMs = [double]::Parse($Matches[3], [System.Globalization.CultureInfo]::InvariantCulture)
                P95UpperBoundMs = [double]::Parse($Matches[4], [System.Globalization.CultureInfo]::InvariantCulture)
                P99UpperBoundMs = [double]::Parse($Matches[5], [System.Globalization.CultureInfo]::InvariantCulture)
                MaximumMs = [double]::Parse($Matches[6], [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }

        $listItemPrefix = 'Request origins > ListItem / SingleRow'
        $demandPrefix = 'Load demand > Demand-aware queue view'

        [pscustomobject]@{
            Path = $resolvedPath
            Session = Get-Number 'Session'
            DurationMs = Get-Number 'Duration'

            ProcessCpuMs = Get-Number 'Process work during session > Process CPU time'
            LogicalCoreUtilizationPercent = Get-Number 'Process work during session > Equivalent logical-core utilization (100% = one fully busy logical core)'
            ManagedAllocationBytes = Get-Number 'Process work during session > Managed allocations'
            GcPauseMs = Get-Number 'Process work during session > GC pause time'
            WorkingSetStartMiB = Get-Number 'Process work during session > Working set at start'
            WorkingSetStopMiB = Get-Number 'Process work during session > Working set at stop'
            WorkingSetChangeMiB = Get-Number 'Process work during session > Working set change'

            UiCallbacksRejected = Get-Number 'UI responsiveness probe > Callbacks rejected by DispatcherQueue'
            UiQueueWait = Get-Statistics 'UI responsiveness probe > Normal-priority queue wait'

            RequestsStarted = Get-Number 'Requests > Started'
            RequestsApplied = Get-Number 'Requests > Applied'
            RequestsEmpty = Get-Number 'Requests > Empty'
            RequestsStale = Get-Number 'Requests > Stale'
            RequestsFailed = Get-Number 'Requests > Failed'
            RequestsOutstanding = Get-Number 'Requests > Outstanding at stop'
            RequestToCompletion = Get-Statistics 'Requests > Request to completion'

            ListItemIconBoxes = Get-Number "$listItemPrefix > Icon boxes"
            ListItemRequestsStarted = Get-Number "$listItemPrefix > Started"
            ListItemRequestsApplied = Get-Number "$listItemPrefix > Applied"
            ListItemRequestsStale = Get-Number "$listItemPrefix > Stale"
            ListItemAppliedLatency = Get-Statistics "$listItemPrefix > Request to completion by status > Applied"

            ProviderNewLoad = Get-Number 'Provider resolution > NewLoad'
            ProviderCacheHit = Get-Number 'Provider resolution > CacheHit'
            ProviderInFlight = Get-Number 'Provider resolution > InFlight'
            NewLoadBitmapLatency = Get-Statistics 'Provider resolution > Request to completion by resolution and result > NewLoad > Bitmap'
            NewLoadFluentGlyphLatency = Get-Statistics 'Provider resolution > Request to completion by resolution and result > NewLoad > FluentGlyph'
            NewLoadEmojiGlyphLatency = Get-Statistics 'Provider resolution > Request to completion by resolution and result > NewLoad > EmojiGlyph'

            LoadsCreated = Get-Number 'Loads > Created'
            LoadsRejected = Get-Number 'Loads > Rejected'
            DirectGlyphLoads = Get-Number 'Loads > Direct glyph loads'
            EnqueueToCompletion = Get-Statistics 'Loads > Enqueue to completion'
            DirectGlyphConstruction = Get-Statistics 'Loads > Direct glyph construction'
            QueueWait = Get-Statistics 'Loads > Queue wait'
            BackgroundPreparation = Get-Statistics 'Loads > Background preparation'
            DispatcherWait = Get-Statistics 'Loads > Dispatcher wait'
            DispatcherCallbackWallTime = Get-Statistics 'Loads > Dispatcher callback wall time'

            DemandedQueueWait = Get-Statistics "$demandPrefix > Demanded queue wait"
            SpeculativeQueueWait = Get-Statistics "$demandPrefix > Speculative queue wait"
            WorkersStartedWithoutRequester = Get-Number 'Load demand > Workers started with no live requester'
            LoadsCompletedWithoutRequester = Get-Number 'Load demand > Loads completed with no live requester'
            CompletedWithoutRequesterLaterCacheHit = Get-Number 'Load demand > Completed-without-requester loads later cache-hit'
            CapacityInterferingSpeculativeStarts = Get-Number "$demandPrefix > Speculative starts leaving demanded loads beyond remaining worker capacity"

            InputString = Get-Number 'Input kinds > String'
            InputShellBinary = Get-Number 'Input kinds > ShellBinary'
            InputStream = Get-Number 'Input kinds > Stream'
            InputSpecializedAppIcon = Get-Number 'Input kinds > SpecializedAppIcon'

            ResultBitmap = Get-Number 'New-load result kinds > Bitmap'
            ResultSvg = Get-Number 'New-load result kinds > Svg'
            ResultFluentGlyph = Get-Number 'New-load result kinds > FluentGlyph'
            ResultEmojiGlyph = Get-Number 'New-load result kinds > EmojiGlyph'
            ResultFailed = Get-Number 'New-load result kinds > Failed'

            IconElementsCreated = Get-Number 'Icon elements > Created'
            IconElementsReused = Get-Number 'Icon elements > Reused'
            IconElementUpdate = Get-Statistics 'Icon elements > Update wall time'

            RawValues = $values
        }
    }
}

function Get-CmdPalIconReports {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $resolvedPath = Resolve-Path -LiteralPath $Path
    $files = if ((Get-Item -LiteralPath $resolvedPath).PSIsContainer) {
        Get-ChildItem -LiteralPath $resolvedPath -Recurse -File -Filter '*.txt'
    }
    else {
        Get-Item -LiteralPath $resolvedPath
    }

    $reports = @()
    foreach ($file in $files) {
        if ((Get-Content -LiteralPath $file.FullName -TotalCount 1) -eq 'CmdPal icon diagnostics') {
            $reports += ConvertFrom-CmdPalIconReport -Path $file.FullName
        }
    }

    if ($reports.Count -eq 0) {
        throw "No CmdPal icon diagnostics reports found under '$Path'."
    }

    return $reports
}
