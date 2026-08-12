// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.UI.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public class IconLoadQueueTests
{
    [TestMethod]
    [Timeout(5_000)]
    public async Task DemandedWorkRunsBeforeSpeculativeHighPriorityWork()
    {
        var queue = new IconLoadQueue(workerCount: 1);
        var speculativeDemand = IconLoadDemand.CreateDemanded();
        speculativeDemand.RemoveRequester();
        var demanded = IconLoadDemand.CreateDemanded();
        Func<Task> speculativeWork = () => Task.CompletedTask;
        Func<Task> demandedWork = () => Task.CompletedTask;

        Assert.IsTrue(queue.TryEnqueue(
            speculativeWork,
            IconLoadPriority.High,
            speculativeDemand,
            out var speculativePriority));
        Assert.AreEqual(IconLoadPriority.High, speculativePriority);
        Assert.IsTrue(queue.TryEnqueue(
            demandedWork,
            IconLoadPriority.Low,
            demanded,
            out var demandedPriority));
        Assert.AreEqual(IconLoadPriority.Low, demandedPriority);

        Assert.AreSame(demandedWork, await queue.DequeueAsync());
        Assert.AreSame(speculativeWork, await queue.DequeueAsync());
        queue.Complete();
        await queue.Completion;
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task DemandLossMovesQueuedWorkBehindLiveRequests()
    {
        var queue = new IconLoadQueue(workerCount: 1);
        var firstRequest = new IconRequestDemand();
        var firstDemand = new IconLoadDemand();
        firstDemand.Attach(firstRequest);
        var secondDemand = IconLoadDemand.CreateDemanded();
        Func<Task> firstWork = () => Task.CompletedTask;
        Func<Task> secondWork = () => Task.CompletedTask;

        Assert.IsTrue(queue.TryEnqueue(firstWork, IconLoadPriority.Low, firstDemand, out _));
        Assert.IsTrue(queue.TryEnqueue(secondWork, IconLoadPriority.Low, secondDemand, out _));

        firstRequest.Release();

        Assert.AreSame(secondWork, await queue.DequeueAsync());
        Assert.AreSame(firstWork, await queue.DequeueAsync());
        queue.Complete();
        await queue.Completion;
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task ReturnedDemandPromotesQueuedWork()
    {
        var queue = new IconLoadQueue(workerCount: 1);
        var promotedDemand = new IconLoadDemand();
        var speculativeDemand = new IconLoadDemand();
        Func<Task> promotedWork = () => Task.CompletedTask;
        Func<Task> speculativeWork = () => Task.CompletedTask;

        Assert.IsTrue(queue.TryEnqueue(promotedWork, IconLoadPriority.Low, promotedDemand, out _));
        Assert.IsTrue(queue.TryEnqueue(speculativeWork, IconLoadPriority.Low, speculativeDemand, out _));

        var returnedRequest = new IconRequestDemand();
        promotedDemand.Attach(returnedRequest);

        Assert.AreSame(promotedWork, await queue.DequeueAsync());
        Assert.AreSame(speculativeWork, await queue.DequeueAsync());
        queue.Complete();
        await queue.Completion;
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task HighPriorityRemainsFirstWithinDemandClass()
    {
        var queue = new IconLoadQueue(workerCount: 1);
        var lowDemand = IconLoadDemand.CreateDemanded();
        var highDemand = IconLoadDemand.CreateDemanded();
        Func<Task> lowWork = () => Task.CompletedTask;
        Func<Task> highWork = () => Task.CompletedTask;

        Assert.IsTrue(queue.TryEnqueue(lowWork, IconLoadPriority.Low, lowDemand, out _));
        Assert.IsTrue(queue.TryEnqueue(highWork, IconLoadPriority.High, highDemand, out _));

        Assert.AreSame(highWork, await queue.DequeueAsync());
        Assert.AreSame(lowWork, await queue.DequeueAsync());
        queue.Complete();
        await queue.Completion;
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task CompletionDrainsQueuedWorkBeforeStoppingWorkers()
    {
        var queue = new IconLoadQueue(workerCount: 2);
        var firstWork = () => Task.CompletedTask;
        var secondWork = () => Task.CompletedTask;

        Assert.IsTrue(queue.TryEnqueue(
            firstWork,
            IconLoadPriority.Low,
            IconLoadDemand.CreateDemanded(),
            out _));
        Assert.IsTrue(queue.TryEnqueue(
            secondWork,
            IconLoadPriority.Low,
            IconLoadDemand.CreateDemanded(),
            out _));

        queue.Complete();

        Assert.AreSame(firstWork, await queue.DequeueAsync());
        Assert.AreSame(secondWork, await queue.DequeueAsync());
        Assert.IsNull(await queue.DequeueAsync());
        await queue.Completion;
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task DequeueRejectsMoreConcurrentConsumersThanConfigured()
    {
        var queue = new IconLoadQueue(workerCount: 1);
        var firstDequeue = queue.DequeueAsync().AsTask();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await queue.DequeueAsync());

        queue.Complete();
        Assert.IsNull(await firstDequeue);
        await queue.Completion;
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task DemandChurnDuringDequeueRunsEveryWorkExactlyOnce()
    {
        const int WorkerCount = 4;
        const int ItemCount = 256;
        var queue = new IconLoadQueue(WorkerCount);
        var demands = new IconLoadDemand[ItemCount];
        var executionCounts = new int[ItemCount];
        var startedCount = 0;
        var firstBatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstBatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var i = 0; i < ItemCount; i++)
        {
            var index = i;
            demands[index] = IconLoadDemand.CreateDemanded();
            Assert.IsTrue(queue.TryEnqueue(
                async () =>
                {
                    var started = Interlocked.Increment(ref startedCount);
                    if (started <= WorkerCount)
                    {
                        if (started == WorkerCount)
                        {
                            firstBatchStarted.TrySetResult(true);
                        }

                        await releaseFirstBatch.Task;
                    }

                    Interlocked.Increment(ref executionCounts[index]);
                },
                IconLoadPriority.Low,
                demands[index],
                out _));
        }

        var workers = new Task[WorkerCount];
        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = RunWorkerAsync(queue);
        }

        await firstBatchStarted.Task;
        await Task.Run(() =>
        {
            Parallel.For(0, ItemCount, i =>
            {
                for (var cycle = 0; cycle < 20; cycle++)
                {
                    demands[i].RemoveRequester();
                    demands[i].AddRequester();
                }
            });
        });

        queue.Complete();
        releaseFirstBatch.TrySetResult(true);
        await Task.WhenAll(workers);
        await queue.Completion;

        for (var i = 0; i < executionCounts.Length; i++)
        {
            Assert.AreEqual(1, executionCounts[i], $"Work item {i} ran an unexpected number of times.");
        }
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task CompletionRacingEnqueueDoesNotLoseAcceptedWork()
    {
        const int RoundCount = 10;
        const int WorkerCount = 4;
        const int ProducerCount = 8;
        const int AttemptsPerProducer = 128;

        for (var round = 0; round < RoundCount; round++)
        {
            var queue = new IconLoadQueue(WorkerCount);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = 0;
            var accepted = 0;
            var executed = 0;

            var workers = new Task[WorkerCount];
            for (var i = 0; i < workers.Length; i++)
            {
                workers[i] = RunWorkerAsync(queue);
            }

            var producers = new Task[ProducerCount];
            for (var i = 0; i < producers.Length; i++)
            {
                producers[i] = Task.Run(async () =>
                {
                    await start.Task;
                    for (var attempt = 0; attempt < AttemptsPerProducer; attempt++)
                    {
                        if (queue.TryEnqueue(
                            () =>
                            {
                                Interlocked.Increment(ref executed);
                                return Task.CompletedTask;
                            },
                            IconLoadPriority.Low,
                            IconLoadDemand.CreateDemanded(),
                            out _))
                        {
                            Interlocked.Increment(ref accepted);
                        }

                        Interlocked.Increment(ref attempts);
                        Thread.Yield();
                    }
                });
            }

            var completion = Task.Run(async () =>
            {
                await start.Task;
                SpinWait.SpinUntil(() => Volatile.Read(ref attempts) >= 32, TimeSpan.FromSeconds(1));
                queue.Complete();
            });

            start.TrySetResult(true);
            await Task.WhenAll(producers);
            await completion;
            await Task.WhenAll(workers);
            await queue.Completion;

            Assert.AreEqual(accepted, executed, $"Accepted work was lost in round {round}.");
        }
    }

    private static async Task RunWorkerAsync(IconLoadQueue queue)
    {
        while (await queue.DequeueAsync() is { } work)
        {
            await work();
        }
    }
}
