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

        function Get-CacheAggregate([string] $namePattern) {
            $cachePaths = @(
                $values.Keys |
                    Where-Object { $_ -match "^Icon caches > ($namePattern) > Lookups$" } |
                    ForEach-Object { $_ -replace ' > Lookups$', '' }
            )
            if ($cachePaths.Count -eq 0) {
                return $null
            }

            $lookups = 0.0
            $hits = 0.0
            $misses = 0.0
            $capacity = 0.0
            $maximumObservedEntries = 0.0
            $entriesAdded = 0.0
            $entriesRemoved = 0.0
            $capacityRemovals = 0.0
            $lowScoreRemovals = 0.0
            foreach ($cachePath in $cachePaths) {
                $lookups += Get-Number "$cachePath > Lookups"
                $hits += Get-Number "$cachePath > Hits"
                $misses += Get-Number "$cachePath > Misses"
                $maximumObservedEntries += Get-Number "$cachePath > Maximum observed entries"
                $entriesAdded += Get-Number "$cachePath > Entries added during session"
                $entriesRemoved += Get-Number "$cachePath > Entries removed during session"
                $capacityRemovals += Get-Number "$cachePath > Removal reasons > Capacity"
                $lowScoreRemovals += Get-Number "$cachePath > Removal reasons > LowScore"

                $cacheName = $cachePath.Substring('Icon caches > '.Length)
                if ($cacheName -match 'capacity ([0-9]+)$') {
                    $capacity += [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
                }
            }

            [pscustomobject]@{
                Capacity = $capacity
                Lookups = $lookups
                Hits = $hits
                Misses = $misses
                HitRatePercent = if ($lookups -eq 0) { 0 } else { ($hits / $lookups) * 100 }
                MaximumObservedEntries = $maximumObservedEntries
                EntriesAdded = $entriesAdded
                EntriesRemoved = $entriesRemoved
                CapacityRemovals = $capacityRemovals
                LowScoreRemovals = $lowScoreRemovals
            }
        }

        $benchmarkMetadata = $null
        $benchmarkContract = $null
        $traversals = @{}
        $metadataPath = [IO.Path]::ChangeExtension($resolvedPath, '.json')
        if (Test-Path -LiteralPath $metadataPath) {
            $benchmarkMetadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
            $contract = [ordered]@{
                Scenario = $benchmarkMetadata.Scenario
                Pages = @($benchmarkMetadata.Pages)
                NavigationDelayMilliseconds = $benchmarkMetadata.NavigationDelayMilliseconds
                SettleMilliseconds = $benchmarkMetadata.SettleMilliseconds
                CooldownSeconds = $benchmarkMetadata.CooldownSeconds
            }

            if ($benchmarkMetadata.Scenario -eq 'A') {
                $contract.KeyboardTraversalTimeoutMilliseconds = @($benchmarkMetadata.KeyboardTraversalTimeoutMilliseconds)
                $contract.KeyboardWrapCounts = @($benchmarkMetadata.KeyboardWrapCounts)
                $contract.KeyboardTapDelayMilliseconds = $benchmarkMetadata.KeyboardTapDelayMilliseconds
                $contract.KeyboardCoarseProbeInterval = $benchmarkMetadata.KeyboardCoarseProbeInterval
                $contract.KeyboardFineProbeInterval = $benchmarkMetadata.KeyboardFineProbeInterval
            }
            elseif ($benchmarkMetadata.Scenario -eq 'B') {
                $contract.FastScrollCounts = @($benchmarkMetadata.FastScrollCounts)
                $contract.SlowScrollCounts = @($benchmarkMetadata.SlowScrollCounts)
                $contract.FastScrollDelayMilliseconds = $benchmarkMetadata.FastScrollDelayMilliseconds
                $contract.SlowScrollDelayMilliseconds = $benchmarkMetadata.SlowScrollDelayMilliseconds
            }

            $benchmarkContract = $contract | ConvertTo-Json -Compress -Depth 4

            $iteration = [int]$benchmarkMetadata.Iteration
            $trxPath = Join-Path (Split-Path -Parent $resolvedPath) ('test-results\run-{0:D2}\benchmark.trx' -f $iteration)
            if (Test-Path -LiteralPath $trxPath) {
                [xml] $trx = Get-Content -LiteralPath $trxPath -Raw
                $standardOutput = @(
                    $trx.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
                        ForEach-Object { $_.InnerText }
                ) -join [Environment]::NewLine

                foreach ($page in @($benchmarkMetadata.Pages)) {
                    $escapedPage = [regex]::Escape([string]$page)
                    $matches = [regex]::Matches(
                        $standardOutput,
                        "'$escapedPage' wrapped to the start \(([0-9]+)/([0-9]+)\) after ([0-9]+) Down taps")
                    if ($matches.Count -eq 0) {
                        continue
                    }

                    $cycleLengths = [System.Collections.Generic.List[int64]]::new()
                    $previousTotal = 0L
                    foreach ($match in $matches) {
                        $total = [int64]::Parse($match.Groups[3].Value, [System.Globalization.CultureInfo]::InvariantCulture)
                        $cycleLengths.Add($total - $previousTotal)
                        $previousTotal = $total
                    }

                    $lastMatch = $matches[$matches.Count - 1]
                    $firstCycleLength = $cycleLengths[0]
                    $cyclesEquivalent = $true
                    foreach ($cycleLength in $cycleLengths | Select-Object -Skip 1) {
                        if ($cycleLength -lt ($firstCycleLength * 0.75) -or $cycleLength -gt ($firstCycleLength * 1.25)) {
                            $cyclesEquivalent = $false
                        }
                    }

                    $traversals[[string]$page] = [pscustomobject]@{
                        CompletedWraps = [int]$lastMatch.Groups[1].Value
                        RequiredWraps = [int]$lastMatch.Groups[2].Value
                        TotalTaps = [int64]$lastMatch.Groups[3].Value
                        CycleLengths = @($cycleLengths)
                        CyclesEquivalent = $cyclesEquivalent
                    }
                }
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
            ListItemResultBitmap = Get-Number "$listItemPrefix > Result kinds > Bitmap"
            ListItemResultSvg = Get-Number "$listItemPrefix > Result kinds > Svg"
            ListItemResultFluentGlyph = Get-Number "$listItemPrefix > Result kinds > FluentGlyph"
            ListItemResultEmojiGlyph = Get-Number "$listItemPrefix > Result kinds > EmojiGlyph"

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

            Cache20 = Get-CacheAggregate '20x20[^>]*'
            GlyphCache20 = Get-CacheAggregate '20x20 Glyph cache,[^>]*'
            OtherCache20 = Get-CacheAggregate '20x20 Other cache,[^>]*'

            IconElementsCreated = Get-Number 'Icon elements > Created'
            IconElementsReused = Get-Number 'Icon elements > Reused'
            IconElementUpdate = Get-Statistics 'Icon elements > Update wall time'

            BenchmarkMetadata = $benchmarkMetadata
            BenchmarkContract = $benchmarkContract
            Traversals = $traversals

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
