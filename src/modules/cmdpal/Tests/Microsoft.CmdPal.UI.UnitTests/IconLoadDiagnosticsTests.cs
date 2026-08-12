// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.Tracing;
using Microsoft.CmdPal.UI.Controls;
using Microsoft.CmdPal.UI.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
[DoNotParallelize]
public class IconLoadDiagnosticsTests
{
    [TestCleanup]
    public void Cleanup()
    {
        IconLoadDiagnostics.Reset();
    }

    [TestMethod]
    public void RecordingProducesAnonymousAggregateReport()
    {
        var sessionId = IconLoadDiagnostics.Start();
        var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.5);
        var load = IconLoadDiagnostics.CreateLoad(
            request,
            @"C:\private\secret.exe,0",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.5);

        Assert.IsNotNull(load);
        request.RecordProviderResolution(IconProviderResolution.NewLoad, load);
        load.Enqueued(IconLoadPriority.Low);
        load.WorkerStarted();
        var preparationStartedAt = load.BeginBackgroundPreparation();
        load.CompleteBackgroundPreparation(preparationStartedAt);
        var dispatcherEnqueuedAt = load.BeginDispatcherWait();
        var dispatcherStartedAt = load.DispatcherStarted(dispatcherEnqueuedAt);
        load.DispatcherCompleted(dispatcherStartedAt);
        load.SetResult(null);
        load.Complete();
        request.Complete(IconRequestStatus.Stale);
        var elementStartedAt = IconLoadDiagnostics.BeginElementUpdate();
        IconLoadDiagnostics.RecordElementUpdate(reused: false, source: null, elementStartedAt);
        elementStartedAt = IconLoadDiagnostics.BeginElementUpdate();
        IconLoadDiagnostics.RecordElementUpdate(reused: true, source: null, elementStartedAt);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        Assert.AreEqual(sessionId, report.SessionId);
        Assert.IsTrue(report.EndedUtc >= report.StartedUtc);
        Assert.IsTrue(report.Duration >= TimeSpan.Zero);
        StringAssert.Contains(report.Text, $"Session: {sessionId}");
        StringAssert.Contains(report.Text, "Ended UTC:");
        StringAssert.Contains(report.Text, "Process work during session");
        StringAssert.Contains(report.Text, "Managed allocations:");
        StringAssert.Contains(report.Text, "UI responsiveness probe");
        StringAssert.Contains(report.Text, "  Enabled: no");
        StringAssert.Contains(report.Text, "Started: 1");
        StringAssert.Contains(report.Text, "Stale: 1");
        StringAssert.Contains(report.Text, "NewLoad: 1");
        StringAssert.Contains(report.Text, "Request to completion by resolution and result");
        StringAssert.Contains(report.Text, "      Empty: count=1");
        StringAssert.Contains(report.Text, "ShellBinary: 1");
        StringAssert.Contains(report.Text, "    Enqueue to completion: count=1");
        StringAssert.Contains(report.Text, "    Dispatcher wait: count=1");
        StringAssert.Contains(report.Text, "Empty: 1");
        StringAssert.Contains(report.Text, "Maximum low queue depth: 1");
        StringAssert.Contains(report.Text, "Dispatcher wait: count=1");
        StringAssert.Contains(report.Text, "Load demand");
        StringAssert.Contains(report.Text, "Requests linked to session loads: 1");
        StringAssert.Contains(report.Text, "    Completed: 1");
        StringAssert.Contains(report.Text, "Loads completed with no live requester: 0");
        StringAssert.Contains(report.Text, "Installed Apps icon extraction enters this pipeline as SpecializedAppIcon work");
        StringAssert.Contains(report.Text, "Created: 1");
        StringAssert.Contains(report.Text, "Reused: 1");
        StringAssert.Contains(report.Text, "Update wall time: count=2");
        StringAssert.Contains(report.Text, "Empty: created=1, reused=1");
        Assert.IsFalse(report.Text.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(report.Text.Contains(@"C:\private", StringComparison.OrdinalIgnoreCase));

        var reports = IconLoadDiagnostics.GetReports();
        Assert.HasCount(1, reports);
        Assert.AreSame(report, reports[0]);
    }

    [TestMethod]
    public void DirectGlyphLoadDoesNotCountAsActiveWorker()
    {
        IconLoadDiagnostics.Start();
        var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var load = IconLoadDiagnostics.CreateLoad(
            request,
            "\uE700",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);

        Assert.IsNotNull(load);
        request.RecordProviderResolution(IconProviderResolution.NewLoad, load);
        load.CompleteDirectGlyph(result: null);
        request.Complete(IconRequestStatus.Empty);

        var report = IconLoadDiagnostics.StopAndCreateReport();
        var directGlyphResults =
            $"  Direct glyph construction by result kind{Environment.NewLine}" +
            $"    Empty: count=1";

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "Direct glyph loads: 1");
        StringAssert.Contains(report.Text, "Direct glyph construction: count=1");
        StringAssert.Contains(report.Text, directGlyphResults);
        StringAssert.Contains(report.Text, "Active at stop: 0");
        StringAssert.Contains(report.Text, "Maximum active workers: 0");
        StringAssert.Contains(report.Text, "Enqueue to completion: no samples");
        StringAssert.Contains(report.Text, "New-load result kinds");
        StringAssert.Contains(report.Text, "Empty: 1");
    }

    [TestMethod]
    public void AppIconProtocolUsesSpecializedInputKind()
    {
        IconLoadDiagnostics.Start();
        var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var load = IconLoadDiagnostics.CreateLoad(
            request,
            "|AppIcon|C:\\Windows\\System32\\shell32.dll,1",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);

        Assert.IsNotNull(load);
        request.RecordProviderResolution(IconProviderResolution.NewLoad, load);
        load.SetResult(null);
        load.Complete();
        request.Complete(IconRequestStatus.Empty);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "  SpecializedAppIcon: 1");
        Assert.IsFalse(report.Text.Contains("shell32", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task SchedulerReportCapturesCoordinatorAndWorkerHandoff()
    {
        using var coordinatorThreadListener = new CoordinatorThreadListener();
        IconLoadDiagnostics.Start();
        var queue = new IconLoadQueue(workerCount: 1);
        Func<Task> work = () => Task.CompletedTask;

        var dequeue = queue.DequeueAsync().AsTask();
        Assert.IsTrue(queue.TryEnqueue(
            work,
            IconLoadPriority.Low,
            IconLoadDemand.CreateDemanded(),
            out _));

        Assert.AreSame(work, await dequeue);
        Assert.IsFalse(await coordinatorThreadListener.IsThreadPoolThread);
        queue.Complete();
        await queue.Completion;

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        var publishedCommands =
            $"  Commands published by kind{Environment.NewLine}" +
            $"    Enqueue: 1{Environment.NewLine}" +
            $"    DemandChanged: 0{Environment.NewLine}" +
            $"    WorkerReady: 1{Environment.NewLine}" +
            $"    Complete: 1";
        var processedCommands =
            $"  Commands processed by kind{Environment.NewLine}" +
            $"    Enqueue: 1{Environment.NewLine}" +
            $"    DemandChanged: 0{Environment.NewLine}" +
            $"    WorkerReady: 1{Environment.NewLine}" +
            $"    Complete: 1";

        StringAssert.Contains(report.Text, "Scheduler coordination");
        StringAssert.Contains(report.Text, publishedCommands);
        StringAssert.Contains(report.Text, processedCommands);
        StringAssert.Contains(report.Text, "Commands outstanding at stop: 0");
        StringAssert.Contains(report.Text, "    Enqueue: count=1");
        StringAssert.Contains(report.Text, "    WorkerReady: count=1");
        StringAssert.Contains(report.Text, "  Coordinator wake and batch processing");
        StringAssert.Contains(report.Text, "    Signal to coordinator pass start for non-empty batches: count=");
        StringAssert.Contains(report.Text, "      Complete: count=1");
        StringAssert.Contains(report.Text, "    Commands drained: 3");
        StringAssert.Contains(report.Text, "    Work items dispatched: 1");
        StringAssert.Contains(report.Text, "    Non-empty batch command drain wall time: count=");
        StringAssert.Contains(report.Text, "    Non-empty batch pass-start-to-dispatch-complete wall time: count=");
        StringAssert.Contains(report.Text, "    Ready to work dispatch: count=1");
        StringAssert.Contains(report.Text, "    Ready to demanded work dispatch: count=1");
        StringAssert.Contains(report.Text, "    Ready to speculative work dispatch: no samples");
        StringAssert.Contains(report.Text, "    Intervals started: 1");
        StringAssert.Contains(report.Text, "    Intervals active at stop: 0");
        StringAssert.Contains(report.Text, "    Maximum demanded queue depth during an interval: 1");
        StringAssert.Contains(report.Text, "    Maximum available worker slots during an interval: 1");
        StringAssert.Contains(report.Text, "    Interval duration: count=1");
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task SchedulerReportCapturesSpeculativeDemandReserve()
    {
        IconLoadDiagnostics.Start();
        var queue = new IconLoadQueue(workerCount: 4);
        Func<Task> speculativeWork = () => Task.CompletedTask;
        Func<Task> demandedWork = () => Task.CompletedTask;
        var speculativeDemand = IconLoadDemand.CreateDemanded();
        speculativeDemand.RemoveRequester();

        Assert.IsTrue(queue.TryEnqueue(
            speculativeWork,
            IconLoadPriority.Low,
            speculativeDemand,
            out _));

        var reservedDequeue = queue.DequeueAsync().AsTask();
        Assert.IsTrue(queue.TryEnqueue(
            demandedWork,
            IconLoadPriority.Low,
            IconLoadDemand.CreateDemanded(),
            out _));
        Assert.AreSame(demandedWork, await reservedDequeue);

        var firstReadyWorker = queue.DequeueAsync().AsTask();
        var secondReadyWorker = queue.DequeueAsync().AsTask();
        var speculativeDequeue = await Task.WhenAny(firstReadyWorker, secondReadyWorker);
        Assert.AreSame(speculativeWork, await speculativeDequeue);

        queue.Complete();
        var remainingDequeue = ReferenceEquals(speculativeDequeue, firstReadyWorker)
            ? secondReadyWorker
            : firstReadyWorker;
        Assert.IsNull(await remainingDequeue);
        await queue.Completion;

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        var reserveBlock =
            $"  Speculative dispatch deferred by the demand reserve{Environment.NewLine}" +
            $"    Definition: a coordinator-state interval with speculative work queued, no demanded work queued, and a worker-ready slot deliberately retained for a future live request.{Environment.NewLine}" +
            $"    Intervals started: 2{Environment.NewLine}" +
            $"    Intervals active at stop: 0{Environment.NewLine}" +
            $"    Maximum speculative queue depth during an interval: 1{Environment.NewLine}" +
            $"    Maximum worker-ready slots retained during an interval: 1{Environment.NewLine}" +
            $"    Interval duration: count=2";
        StringAssert.Contains(report.Text, reserveBlock);
    }

    [TestMethod]
    public void SchedulerReportSeparatesEmptyCoalescedBatchWakeLatency()
    {
        IconLoadDiagnostics.Start();
        var command = IconLoadDiagnostics.BeginSchedulerCommand(IconLoadQueue.QueueCommandKind.Enqueue);

        Assert.IsNotNull(command);
        var wake = command.CreateWakeMeasurement();
        command.Processed();
        wake.Woke(System.Diagnostics.Stopwatch.GetTimestamp());
        wake.BatchCompleted(
            commandCount: 0,
            dispatchedWorkItemCount: 0,
            drainTicks: 0,
            passTicks: 0);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "    Signal to coordinator pass start for non-empty batches: no samples");
        StringAssert.Contains(report.Text, "    Signal to coordinator pass start for empty coalesced batches: count=1");
        StringAssert.Contains(report.Text, "    Batches completed: 1");
        StringAssert.Contains(report.Text, "    Empty batches: 1");
        StringAssert.Contains(report.Text, "    Commands drained: 0");
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task SchedulerMeasurementsRemainPairedWithConcurrentPublishersAndWorkers()
    {
        const int WorkerCount = 4;
        const int WorkItemCount = 128;

        IconLoadDiagnostics.Start();
        var queue = new IconLoadQueue(WorkerCount);
        var work = new Func<Task>[WorkItemCount];
        var accepted = 0;

        Parallel.For(0, WorkItemCount, i =>
        {
            work[i] = () => Task.CompletedTask;
            if (queue.TryEnqueue(
                work[i],
                IconLoadPriority.Low,
                IconLoadDemand.CreateDemanded(),
                out _))
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.AreEqual(WorkItemCount, accepted);
        for (var i = 0; i < WorkItemCount; i += WorkerCount)
        {
            var dequeued = await Task.WhenAll(
                queue.DequeueAsync().AsTask(),
                queue.DequeueAsync().AsTask(),
                queue.DequeueAsync().AsTask(),
                queue.DequeueAsync().AsTask());
            Assert.IsTrue(dequeued.All(item => item is not null));
        }

        queue.Complete();
        await queue.Completion;

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, $"    Enqueue: {WorkItemCount}");
        StringAssert.Contains(report.Text, $"    WorkerReady: {WorkItemCount}");
        StringAssert.Contains(report.Text, "    Complete: 1");
        StringAssert.Contains(report.Text, "Commands outstanding at stop: 0");
        StringAssert.Contains(report.Text, $"    Commands drained: {(WorkItemCount * 2) + 1}");
        StringAssert.Contains(report.Text, $"    Work items dispatched: {WorkItemCount}");
        StringAssert.Contains(report.Text, $"    Ready to work dispatch: count={WorkItemCount}");
        StringAssert.Contains(report.Text, $"    Ready to demanded work dispatch: count={WorkItemCount}");
    }

    [TestMethod]
    public void CacheReportTracksLookupsOccupancyAndRemovalReasons()
    {
        IconLoadDiagnostics.Start();
        var size = new global::Windows.Foundation.Size(20, 20);
        IconLoadDiagnostics.RecordCacheLookup(size, IconCachePartition.Glyph, capacity: 16, hit: false);
        IconLoadDiagnostics.RecordCacheEntryAdded(size, IconCachePartition.Glyph, capacity: 16, entryCount: 1);
        IconLoadDiagnostics.RecordCacheLookup(size, IconCachePartition.Glyph, capacity: 16, hit: true);
        IconLoadDiagnostics.RecordCacheEntryRemoved(
            size,
            IconCachePartition.Glyph,
            capacity: 16,
            entryCount: 0,
            AdaptiveCacheRemovalReason.Explicit);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        var expectedHeader =
            $"Icon caches{Environment.NewLine}" +
            $"  Definition: each entry is a cached IconSource task; counts are approximate concurrent observations. Eviction only drops the cache reference.{Environment.NewLine}" +
            "  20x20 Glyph cache, capacity 16";
        StringAssert.Contains(report.Text, expectedHeader);
        StringAssert.Contains(report.Text, "    Lookups: 2");
        StringAssert.Contains(report.Text, "    Hits: 1");
        StringAssert.Contains(report.Text, "    Misses: 1");
        StringAssert.Contains(report.Text, "    Hit rate: 50 %");
        StringAssert.Contains(report.Text, "    Maximum observed entries: 1");
        var expectedRemovalReason =
            $"    Removal reasons{Environment.NewLine}" +
            "      Explicit: 1";
        StringAssert.Contains(report.Text, expectedRemovalReason);
    }

    [TestMethod]
    public void StaleQueuedRequestTracksRetainedCacheUse()
    {
        IconLoadDiagnostics.Start();
        var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var load = IconLoadDiagnostics.CreateLoad(
            request,
            "bitmap.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);

        Assert.IsNotNull(load);
        var task = Task.FromResult<Microsoft.UI.Xaml.Controls.IconSource?>(null);
        load.RegisterTask(task);
        request.RecordProviderResolution(IconProviderResolution.NewLoad, load);
        load.Enqueued(IconLoadPriority.Low);
        request.Invalidate();
        request.Complete(IconRequestStatus.Stale);
        load.WorkerStarted();
        load.SetResult(null);
        load.Complete();

        var cacheRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.Loaded, 1.0);
        cacheRequest.RecordProviderResolution(IconProviderResolution.CacheHit, task);
        cacheRequest.Complete(IconRequestStatus.Empty);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "Requests linked to session loads: 2");
        StringAssert.Contains(report.Text, "    Queued: 1");
        StringAssert.Contains(report.Text, "  Invalidated requests by load stage");
        StringAssert.Contains(report.Text, "  Demand-loss events after the last requester was invalidated");
        StringAssert.Contains(report.Text, "    Queued: 1");
        StringAssert.Contains(report.Text, "Workers started with no live requester: 1");
        StringAssert.Contains(report.Text, "Loads completed with no live requester: 1");
        StringAssert.Contains(report.Text, "Loads completed with no live requester by input kind");
        StringAssert.Contains(report.Text, "Loads completed with no live requester by result kind");
        StringAssert.Contains(report.Text, "Completed-without-requester loads later cache-hit: 1");
        StringAssert.Contains(report.Text, "Later cache-hit requests: 1");
        StringAssert.Contains(report.Text, "No-requester time before worker start: count=1");
        StringAssert.Contains(report.Text, "No-requester time before load completion: count=1");
    }

    [TestMethod]
    public void ReturnedInFlightDemandPreventsFalseAbandonment()
    {
        IconLoadDiagnostics.Start();
        var firstRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var load = IconLoadDiagnostics.CreateLoad(
            firstRequest,
            "bitmap.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);

        Assert.IsNotNull(load);
        firstRequest.RecordProviderResolution(IconProviderResolution.NewLoad, load);
        var secondRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        secondRequest.RecordProviderResolution(IconProviderResolution.InFlight, load);
        load.Enqueued(IconLoadPriority.Low);

        firstRequest.Invalidate();
        firstRequest.Complete(IconRequestStatus.Stale);
        secondRequest.Invalidate();
        secondRequest.Complete(IconRequestStatus.Stale);

        var returnedRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.Loaded, 1.0);
        returnedRequest.RecordProviderResolution(IconProviderResolution.InFlight, load);
        load.WorkerStarted();
        load.SetResult(null);
        load.Complete();
        returnedRequest.Complete(IconRequestStatus.Empty);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "Requests linked to session loads: 3");
        StringAssert.Contains(report.Text, "Loads with multiple simultaneous requesters: 1");
        StringAssert.Contains(report.Text, "Maximum simultaneous requesters per load: 2");
        StringAssert.Contains(report.Text, "    Queued: 2");
        StringAssert.Contains(report.Text, "    Queued: 1");
        StringAssert.Contains(report.Text, "Loads where demand returned before completion: 1");
        StringAssert.Contains(report.Text, "Queued demotions after demand loss: 1");
        StringAssert.Contains(report.Text, "Queued promotions after demand returned: 1");
        StringAssert.Contains(report.Text, "Workers started demanded: 1");
        StringAssert.Contains(report.Text, "Workers started speculative: 0");
        StringAssert.Contains(report.Text, "Workers started with no live requester: 0");
        StringAssert.Contains(report.Text, "Loads completed with no live requester: 0");
    }

    [TestMethod]
    public void DemandQueueReportSeparatesQueuedDemandFromCapacityInterference()
    {
        IconLoadDiagnostics.Start();

        var firstSpeculativeRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var firstSpeculativeLoad = IconLoadDiagnostics.CreateLoad(
            firstSpeculativeRequest,
            "bitmap.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);
        Assert.IsNotNull(firstSpeculativeLoad);
        firstSpeculativeRequest.RecordProviderResolution(IconProviderResolution.NewLoad, firstSpeculativeLoad);
        firstSpeculativeLoad.Enqueued(IconLoadPriority.Low);
        firstSpeculativeRequest.Invalidate();
        firstSpeculativeRequest.Complete(IconRequestStatus.Stale);

        var firstDemandedRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var firstDemandedLoad = IconLoadDiagnostics.CreateLoad(
            firstDemandedRequest,
            "bitmap.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);
        Assert.IsNotNull(firstDemandedLoad);
        firstDemandedRequest.RecordProviderResolution(IconProviderResolution.NewLoad, firstDemandedLoad);
        firstDemandedLoad.Enqueued(IconLoadPriority.Low);

        firstSpeculativeLoad.WorkerStarted(workerCount: 1);
        firstSpeculativeLoad.SetResult(null);
        firstSpeculativeLoad.Complete();
        firstDemandedLoad.WorkerStarted(workerCount: 1);
        firstDemandedLoad.SetResult(null);
        firstDemandedLoad.Complete();
        firstDemandedRequest.Complete(IconRequestStatus.Empty);

        var secondSpeculativeRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var secondSpeculativeLoad = IconLoadDiagnostics.CreateLoad(
            secondSpeculativeRequest,
            "bitmap.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);
        Assert.IsNotNull(secondSpeculativeLoad);
        secondSpeculativeRequest.RecordProviderResolution(IconProviderResolution.NewLoad, secondSpeculativeLoad);
        secondSpeculativeLoad.Enqueued(IconLoadPriority.Low);
        secondSpeculativeRequest.Invalidate();
        secondSpeculativeRequest.Complete(IconRequestStatus.Stale);

        var secondDemandedRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var secondDemandedLoad = IconLoadDiagnostics.CreateLoad(
            secondDemandedRequest,
            "bitmap.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);
        Assert.IsNotNull(secondDemandedLoad);
        secondDemandedRequest.RecordProviderResolution(IconProviderResolution.NewLoad, secondDemandedLoad);
        secondDemandedLoad.Enqueued(IconLoadPriority.Low);

        secondSpeculativeLoad.WorkerStarted(workerCount: 4);
        secondSpeculativeLoad.SetResult(null);
        secondSpeculativeLoad.Complete();
        secondDemandedLoad.WorkerStarted(workerCount: 4);
        secondDemandedLoad.SetResult(null);
        secondDemandedLoad.Complete();
        secondDemandedRequest.Complete(IconRequestStatus.Empty);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "Maximum demanded queue depth: 1");
        StringAssert.Contains(report.Text, "Maximum speculative queue depth: 1");
        StringAssert.Contains(report.Text, "Queued demotions after demand loss: 2");
        StringAssert.Contains(report.Text, "Queued promotions after demand returned: 0");
        StringAssert.Contains(report.Text, "Workers started demanded: 2");
        StringAssert.Contains(report.Text, "Workers started speculative: 2");
        StringAssert.Contains(report.Text, "Speculative starts with demanded loads queued: 2");
        StringAssert.Contains(report.Text, "Speculative starts leaving demanded loads beyond remaining worker capacity: 1");
        StringAssert.Contains(report.Text, "Demanded loads beyond remaining capacity across those starts: 1");
        StringAssert.Contains(report.Text, "Maximum demanded loads beyond remaining capacity at one start: 1");
        StringAssert.Contains(report.Text, "Capacity-interfering speculative starts by input kind");
        StringAssert.Contains(report.Text, "      String: 1");
        StringAssert.Contains(report.Text, "Demanded queue wait: count=2");
        StringAssert.Contains(report.Text, "Speculative queue wait: count=2");

        var stringInputMeasurements = GetTextBetween(
            report.Text,
            "  String: 4",
            "  ShellBinary: 0");
        StringAssert.Contains(stringInputMeasurements, "    Demanded queue wait: count=2");
        StringAssert.Contains(stringInputMeasurements, "    Speculative queue wait: count=2");
    }

    [TestMethod]
    public void DemandArrivalReportCapturesActiveSpeculativeCapacity()
    {
        IconLoadDiagnostics.Start();

        var activeRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var activeLoad = IconLoadDiagnostics.CreateLoad(
            activeRequest,
            "active.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);
        Assert.IsNotNull(activeLoad);
        activeRequest.RecordProviderResolution(IconProviderResolution.NewLoad, activeLoad);
        activeLoad.Enqueued(IconLoadPriority.Low, workerCount: 1);
        activeLoad.WorkerStarted(workerCount: 1);

        activeRequest.Invalidate();
        activeRequest.Complete(IconRequestStatus.Stale);

        var demandedRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        var demandedLoad = IconLoadDiagnostics.CreateLoad(
            demandedRequest,
            "demanded.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);
        Assert.IsNotNull(demandedLoad);
        demandedRequest.RecordProviderResolution(IconProviderResolution.NewLoad, demandedLoad);
        demandedLoad.Enqueued(IconLoadPriority.Low, workerCount: 1);

        activeLoad.SetResult(null);
        activeLoad.Complete();
        demandedLoad.WorkerStarted(workerCount: 1);
        demandedLoad.SetResult(null);
        demandedLoad.Complete();
        demandedRequest.Complete(IconRequestStatus.Empty);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        var speculativeOccupancyBlock =
            $"      Speculative worker occupancy observed at demanded arrivals by speculative input kind{Environment.NewLine}" +
            $"        Empty: 0{Environment.NewLine}" +
            $"        String: 1";
        var directlyBlockedBlock =
            $"      Directly blocked demanded arrivals by demanded input kind{Environment.NewLine}" +
            $"        Empty: 0{Environment.NewLine}" +
            $"        String: 1";

        StringAssert.Contains(report.Text, "Active demanded workers at stop: 0");
        StringAssert.Contains(report.Text, "Active speculative workers at stop: 0");
        StringAssert.Contains(report.Text, "Maximum active speculative workers: 1");
        StringAssert.Contains(report.Text, "Demanded queue arrivals: 2");
        StringAssert.Contains(report.Text, "Arrivals with active speculative workers: 1");
        StringAssert.Contains(report.Text, "Sum of active speculative workers observed at those arrivals: 1");
        StringAssert.Contains(report.Text, "Maximum speculative workers active at one demanded arrival: 1");
        StringAssert.Contains(report.Text, "Arrivals directly blocked by speculative worker capacity: 1");
        StringAssert.Contains(report.Text, speculativeOccupancyBlock);
        StringAssert.Contains(report.Text, directlyBlockedBlock);
        StringAssert.Contains(report.Text, "Demand arrival to worker start with speculative workers active: count=1");
        StringAssert.Contains(report.Text, "Directly blocked demand arrival to worker start: count=1");
    }

    [TestMethod]
    [Timeout(5_000)]
    public void ConcurrentActiveInvalidationAndCompletionDoNotLeakWorkerDemand()
    {
        IconLoadDiagnostics.Start();

        for (var i = 0; i < 500; i++)
        {
            var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
            var load = IconLoadDiagnostics.CreateLoad(
                request,
                "bitmap.png",
                hasStream: false,
                width: 20,
                height: 20,
                scale: 1.0);
            Assert.IsNotNull(load);
            request.RecordProviderResolution(IconProviderResolution.NewLoad, load);
            load.Enqueued(IconLoadPriority.Low, workerCount: 1);
            load.WorkerStarted(workerCount: 1);
            load.SetResult(null);

            Parallel.Invoke(request.Invalidate, load.Complete);
            request.Complete(IconRequestStatus.Stale);
        }

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "Active demanded workers at stop: 0");
        StringAssert.Contains(report.Text, "Active speculative workers at stop: 0");
    }

    [TestMethod]
    public void InvalidationBeforeResolutionStillTracksLoadWithoutDemand()
    {
        IconLoadDiagnostics.Start();
        var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
        request.Invalidate();

        var load = IconLoadDiagnostics.CreateLoad(
            request,
            "bitmap.png",
            hasStream: false,
            width: 20,
            height: 20,
            scale: 1.0);

        Assert.IsNotNull(load);
        request.RecordProviderResolution(IconProviderResolution.NewLoad, load);
        load.Enqueued(IconLoadPriority.Low);
        load.WorkerStarted();
        load.SetResult(null);
        load.Complete();
        request.Complete(IconRequestStatus.Stale);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "    BeforeEnqueue: 1");
        StringAssert.Contains(report.Text, "Workers started with no live requester: 1");
        StringAssert.Contains(report.Text, "Loads completed with no live requester: 1");
    }

    [TestMethod]
    public void RequestLatencyIsAttributedToEveryProviderResolution()
    {
        IconLoadDiagnostics.Start();

        foreach (var resolution in Enum.GetValues<IconProviderResolution>())
        {
            var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0);
            request.RecordProviderResolution(resolution, load: null);
            request.Complete(IconRequestStatus.Empty);
        }

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        foreach (var resolution in Enum.GetValues<IconProviderResolution>())
        {
            StringAssert.Contains(report.Text, $"    {resolution}");
        }

        Assert.AreEqual(
            Enum.GetValues<IconProviderResolution>().Length,
            CountOccurrences(report.Text, "      Empty: count=1"));
        Assert.IsFalse(report.Text.Contains("Unattributed completed requests", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RequestOriginsAggregateBySiteAndStaticScope()
    {
        IconLoadDiagnostics.Start();
        var firstOrigin = new IconRequestOrigin(101, IconRequestSite.ListItem, "SingleRow");
        var secondOrigin = new IconRequestOrigin(102, IconRequestSite.ListItem, "SingleRow");

        var firstRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0, firstOrigin);
        firstRequest.RecordProviderResolution(IconProviderResolution.CacheHit, load: null);
        firstRequest.Complete(IconRequestStatus.Applied);

        var staleRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0, firstOrigin);
        staleRequest.RecordProviderResolution(IconProviderResolution.NewLoad, load: null);
        staleRequest.Complete(IconRequestStatus.Stale);

        var secondRequest = IconLoadDiagnostics.BeginRequest(IconRequestReason.Loaded, 1.0, secondOrigin);
        secondRequest.RecordProviderResolution(IconProviderResolution.CacheHit, load: null);
        secondRequest.Complete(IconRequestStatus.Applied);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "Request origins");
        StringAssert.Contains(report.Text, "  ListItem / SingleRow");
        StringAssert.Contains(report.Text, "    Icon boxes: 2");
        StringAssert.Contains(report.Text, "    Started: 3");
        StringAssert.Contains(report.Text, "    Applied: 2");
        StringAssert.Contains(report.Text, "    Stale: 1");
        StringAssert.Contains(report.Text, "      NewLoad: 1");
        StringAssert.Contains(report.Text, "      CacheHit: 2");
        StringAssert.Contains(report.Text, "    Result kinds");
        StringAssert.Contains(report.Text, "      Empty: 3");
        StringAssert.Contains(report.Text, "      Applied: count=2");
        StringAssert.Contains(report.Text, "      Stale: count=1");
        var globalAppliedResolutionBlock =
            $"  Applied request to completion by provider resolution{Environment.NewLine}" +
            $"    NewLoad: no samples{Environment.NewLine}" +
            $"    CacheHit: count=2";
        var originAppliedResolutionBlock =
            $"    Applied request to completion by provider resolution{Environment.NewLine}" +
            $"      NewLoad: no samples{Environment.NewLine}" +
            $"      CacheHit: count=2";
        StringAssert.Contains(report.Text, globalAppliedResolutionBlock);
        StringAssert.Contains(report.Text, originAppliedResolutionBlock);
        StringAssert.Contains(report.Text, "Individual process-local IconBox IDs are available in RequestOrigin ETW events.");
    }

    [TestMethod]
    public void DiagnosticScopeRejectsPathsAndReportInjection()
    {
        IconLoadDiagnostics.Start();
        var origin = new IconRequestOrigin(101, IconRequestSite.Settings, "C:\\private\\secret.exe\r\nInjected: 1");
        var request = IconLoadDiagnostics.BeginRequest(IconRequestReason.SourceChanged, 1.0, origin);
        request.Complete(IconRequestStatus.Empty);

        var report = IconLoadDiagnostics.StopAndCreateReport();

        Assert.IsNotNull(report);
        StringAssert.Contains(report.Text, "  Settings");
        Assert.IsFalse(report.Text.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(report.Text.Contains("Injected", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(report.Text.Contains("C:\\private", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ReportsRemainAvailableUntilReset()
    {
        var firstSessionId = IconLoadDiagnostics.Start();
        var firstReport = IconLoadDiagnostics.StopAndCreateReport();
        var secondSessionId = IconLoadDiagnostics.Start();
        var secondReport = IconLoadDiagnostics.StopAndCreateReport();

        var reports = IconLoadDiagnostics.GetReports();

        Assert.HasCount(2, reports);
        Assert.AreEqual(firstSessionId, firstReport?.SessionId);
        Assert.AreEqual(secondSessionId, secondReport?.SessionId);
        Assert.AreSame(firstReport, reports[0]);
        Assert.AreSame(secondReport, reports[1]);

        IconLoadDiagnostics.Start();

        IconLoadDiagnostics.Reset();

        Assert.IsFalse(IconLoadDiagnostics.IsRecording);
        Assert.IsNull(IconLoadDiagnostics.StopAndCreateReport());
        Assert.IsEmpty(IconLoadDiagnostics.GetReports());
    }

    private static int CountOccurrences(string value, string text)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(text, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += text.Length;
        }

        return count;
    }

    private static string GetTextBetween(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        Assert.IsTrue(startIndex >= 0, $"Missing report section start: {start}");
        var endIndex = value.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.IsTrue(endIndex > startIndex, $"Missing report section end: {end}");
        return value[startIndex..endIndex];
    }

    private sealed class CoordinatorThreadListener : EventListener
    {
        private readonly TaskCompletionSource<bool> _isThreadPoolThread = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> IsThreadPoolThread => _isThreadPoolThread.Task;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft.PowerToys.CmdPal.IconLoading")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName == nameof(IconLoadEventSource.SchedulerCoordinatorWoke))
            {
                _isThreadPoolThread.TrySetResult(Thread.CurrentThread.IsThreadPoolThread);
            }
        }
    }
}
