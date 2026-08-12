// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CmdPal.UI.Helpers;
using Microsoft.CmdPal.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public partial class CachedIconSourceProviderTests
{
    [TestMethod]
    [Timeout(5_000)]
    public async Task ConcurrentRequestsShareOneInFlightLoad()
    {
        var loader = new ControllableIconLoader();
        var provider = new CachedIconSourceProvider(loader, new Size(20, 20), cacheSize: 16);
        var icon = new IconDataViewModel { Icon = "test" };
        var requests = new ConcurrentBag<Task<IconSource?>>();

        Parallel.For(0, 32, _ => requests.Add(provider.GetIconSource(icon, 1.0)));

        var requestArray = requests.ToArray();
        Assert.HasCount(32, requestArray);
        Assert.AreEqual(1, loader.EnqueueCount);
        foreach (var request in requestArray)
        {
            Assert.AreSame(requestArray[0], request);
        }

        loader.CompleteNext(null);
        await Task.WhenAll(requestArray);
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task SuccessfulLoadIsCachedBeforeInFlightEntryIsRemoved()
    {
        var loader = new ControllableIconLoader();
        var provider = new CachedIconSourceProvider(loader, new Size(20, 20), cacheSize: 16);
        var icon = new IconDataViewModel { Icon = "test" };

        var first = provider.GetIconSource(icon, 1.0);
        loader.CompleteNext(null);
        await first;

        Assert.IsTrue(
            SpinWait.SpinUntil(() => GetInFlightCount(provider) == 0, TimeSpan.FromSeconds(2)),
            "The completed load was not retired from the in-flight dictionary.");

        var cached = provider.GetIconSource(icon, 1.0);

        Assert.AreSame(first, cached);
        Assert.AreEqual(1, loader.EnqueueCount);
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task SuccessfulDirectGlyphLoadIsCachedWithoutQueueing()
    {
        var loader = new ControllableIconLoader { LoadGlyphDirectly = true };
        var provider = new CachedIconSourceProvider(loader, new Size(20, 20), cacheSize: 16);
        var icon = new IconDataViewModel { Icon = "glyph" };

        var first = provider.GetIconSource(icon, 1.0);
        await first;

        Assert.IsTrue(
            SpinWait.SpinUntil(() => GetInFlightCount(provider) == 0, TimeSpan.FromSeconds(2)),
            "The direct glyph load was not retired from the in-flight dictionary.");

        var cached = provider.GetIconSource(icon, 1.0);

        Assert.AreSame(first, cached);
        Assert.AreEqual(1, loader.GlyphAttemptCount);
        Assert.AreEqual(0, loader.EnqueueCount);
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task DistinctStreamReferencesWithCollidingRuntimeHashesDoNotShareLoad()
    {
        var (firstStream, secondStream) = FindRuntimeHashCollision();
        Assert.AreNotSame(firstStream, secondStream);
        Assert.AreEqual(RuntimeHelpers.GetHashCode(firstStream), RuntimeHelpers.GetHashCode(secondStream));

        var loader = new ControllableIconLoader();
        var provider = new CachedIconSourceProvider(loader, new Size(20, 20), cacheSize: 16);
        var firstIcon = new IconDataViewModel
        {
            Data = new IconDataStreamReference { Unsafe = firstStream },
        };
        var secondIcon = new IconDataViewModel
        {
            Data = new IconDataStreamReference { Unsafe = secondStream },
        };

        var first = provider.GetIconSource(firstIcon, 1.0);
        var second = provider.GetIconSource(secondIcon, 1.0);

        Assert.AreNotSame(first, second);
        Assert.AreEqual(2, loader.EnqueueCount);

        loader.CompleteNext(null);
        loader.CompleteNext(null);
        await Task.WhenAll(first, second);
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task FailedLoadIsRemovedAndCanBeRetried()
    {
        var loader = new ControllableIconLoader();
        var provider = new CachedIconSourceProvider(loader, new Size(20, 20), cacheSize: 16);
        var icon = new IconDataViewModel { Icon = "test" };

        var failed = provider.GetIconSource(icon, 1.0);
        loader.FailNext(new InvalidOperationException("Icon load failed."));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await failed);
        Assert.IsTrue(
            SpinWait.SpinUntil(() => GetInFlightCount(provider) == 0, TimeSpan.FromSeconds(2)),
            "The failed load was not retired from the in-flight dictionary.");

        var retry = provider.GetIconSource(icon, 1.0);

        Assert.AreNotSame(failed, retry);
        Assert.AreEqual(2, loader.EnqueueCount);

        loader.CompleteNext(null);
        await retry;
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task RejectedLoadFaultsAndCanBeRetried()
    {
        var loader = new ControllableIconLoader { AcceptLoads = false };
        var provider = new CachedIconSourceProvider(loader, new Size(20, 20), cacheSize: 16);
        var icon = new IconDataViewModel { Icon = "test" };

        var rejected = provider.GetIconSource(icon, 1.0);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await rejected);
        Assert.IsTrue(
            SpinWait.SpinUntil(() => GetInFlightCount(provider) == 0, TimeSpan.FromSeconds(2)),
            "The rejected load was not retired from the in-flight dictionary.");

        loader.AcceptLoads = true;
        var retry = provider.GetIconSource(icon, 1.0);

        Assert.AreNotSame(rejected, retry);
        Assert.AreEqual(2, loader.EnqueueCount);

        loader.CompleteNext(null);
        await retry;
    }

    [TestMethod]
    public async Task UncachedProviderFaultsRejectedLoad()
    {
        var loader = new ControllableIconLoader { AcceptLoads = false };
        var provider = new IconSourceProvider(loader, new Size(16, 16));

        var rejected = provider.GetIconSource(new IconDataViewModel { Icon = "test" }, 1.0);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await rejected);
        Assert.AreEqual(1, loader.EnqueueCount);
    }

    [TestMethod]
    public async Task UncachedProviderLoadsGlyphDirectlyWithoutQueueing()
    {
        var loader = new ControllableIconLoader { LoadGlyphDirectly = true };
        var provider = new IconSourceProvider(loader, new Size(16, 16));

        var result = await provider.GetIconSource(new IconDataViewModel { Icon = "glyph" }, 1.0);

        Assert.IsNull(result);
        Assert.AreEqual(1, loader.GlyphAttemptCount);
        Assert.AreEqual(0, loader.EnqueueCount);
    }

    private static int GetInFlightCount(CachedIconSourceProvider provider)
    {
        var field = typeof(CachedIconSourceProvider).GetField("_inFlight", BindingFlags.Instance | BindingFlags.NonPublic);
        var inFlight = field!.GetValue(provider)!;
        var countProperty = inFlight.GetType().GetProperty("Count");
        return (int)countProperty!.GetValue(inFlight)!;
    }

    private static (TestStreamReference First, TestStreamReference Second) FindRuntimeHashCollision()
    {
        const int MaximumCandidates = 500_000;
        var referencesByHash = new Dictionary<int, TestStreamReference>();

        for (var i = 0; i < MaximumCandidates; i++)
        {
            var candidate = new TestStreamReference();
            var hash = RuntimeHelpers.GetHashCode(candidate);
            if (referencesByHash.TryGetValue(hash, out var existing))
            {
                return (existing, candidate);
            }

            referencesByHash.Add(hash, candidate);
        }

        throw new AssertFailedException($"No runtime identity-hash collision was found among {MaximumCandidates} live objects.");
    }

    private sealed partial class TestStreamReference : IRandomAccessStreamReference
    {
        public IAsyncOperation<IRandomAccessStreamWithContentType> OpenReadAsync() =>
            throw new NotSupportedException("The cache-key test does not open its stream references.");
    }

    private sealed class ControllableIconLoader : IIconLoaderService
    {
        private readonly ConcurrentQueue<TaskCompletionSource<IconSource?>> _pending = new();
        private int _enqueueCount;
        private int _glyphAttemptCount;

        public bool AcceptLoads { get; set; } = true;

        public bool LoadGlyphDirectly { get; set; }

        public int EnqueueCount => Volatile.Read(ref _enqueueCount);

        public int GlyphAttemptCount => Volatile.Read(ref _glyphAttemptCount);

        public bool TryLoadGlyph(
            string? iconString,
            string? fontFamily,
            Size iconSize,
            double scale,
            out IconSource? result)
        {
            Interlocked.Increment(ref _glyphAttemptCount);
            result = null;
            return LoadGlyphDirectly;
        }

        public bool TryEnqueueLoad(
            string? iconString,
            string? fontFamily,
            IRandomAccessStreamReference? streamRef,
            Size iconSize,
            double scale,
            TaskCompletionSource<IconSource?> tcs,
            IconLoadPriority priority,
            IconLoadMeasurement? diagnostics = null)
        {
            Interlocked.Increment(ref _enqueueCount);
            if (!AcceptLoads)
            {
                return false;
            }

            _pending.Enqueue(tcs);
            return true;
        }

        public void CompleteNext(IconSource? result)
        {
            Assert.IsTrue(_pending.TryDequeue(out var tcs), "No pending icon load was available.");
            tcs.SetResult(result);
        }

        public void FailNext(Exception exception)
        {
            Assert.IsTrue(_pending.TryDequeue(out var tcs), "No pending icon load was available.");
            tcs.SetException(exception);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
