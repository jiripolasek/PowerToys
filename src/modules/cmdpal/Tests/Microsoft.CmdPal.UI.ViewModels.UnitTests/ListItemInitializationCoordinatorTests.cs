// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public sealed partial class ListItemInitializationCoordinatorTests
{
    private static readonly int[] RealizedPriorityOrder = [0, 3, 1, 2];
    private static readonly int[] SequentialOrder = [0, 1, 2, 3];

    private sealed class TestPageContext : IPageContext
    {
        public TaskScheduler Scheduler => TaskScheduler.Default;

        public ICommandProviderContext ProviderContext => CommandProviderContext.Empty;

        public void ShowException(Exception ex, string? extensionHint = null) =>
            throw new AssertFailedException($"Unexpected exception from view model: {ex}");
    }

    private sealed partial class TrackingListItem : ListItem
    {
        private readonly int _index;
        private readonly ConcurrentQueue<int> _initializationOrder;
        private readonly ManualResetEventSlim? _initializationStarted;
        private readonly ManualResetEventSlim? _continueInitialization;
        private int _initializationCount;

        public TrackingListItem(
            int index,
            ConcurrentQueue<int> initializationOrder,
            ManualResetEventSlim? initializationStarted = null,
            ManualResetEventSlim? continueInitialization = null)
            : base(new NoOpCommand { Name = $"Item {index}" })
        {
            _index = index;
            _initializationOrder = initializationOrder;
            _initializationStarted = initializationStarted;
            _continueInitialization = continueInitialization;
        }

        public int InitializationCount => Volatile.Read(ref _initializationCount);

        public override ITag[] Tags
        {
            get
            {
                Interlocked.Increment(ref _initializationCount);
                _initializationOrder.Enqueue(_index);
                _initializationStarted?.Set();

                if (_continueInitialization is not null && !_continueInitialization.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Test initialization was not released.");
                }

                return [];
            }

            set
            {
            }
        }
    }

    [TestMethod]
    public async Task RealizedItemJumpsAheadOfSpeculativeInitialization()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var continueFirst = new ManualResetEventSlim();
        var order = new ConcurrentQueue<int>();
        var (models, viewModels) = CreateItems(4, order, firstStarted, continueFirst);
        var coordinator = new ListItemInitializationCoordinator(viewModels);
        var worker = Task.Run(() => coordinator.Run(CancellationToken.None));

        try
        {
            Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(2)));
            var registration = viewModels[3].BeginRealization();
            Assert.IsTrue(registration.IsValid);

            continueFirst.Set();
            await worker.WaitAsync(TimeSpan.FromSeconds(2));
            registration.Release();

            CollectionAssert.AreEqual(RealizedPriorityOrder, order.ToArray());
            Assert.AreEqual(1, models[3].InitializationCount);
        }
        finally
        {
            continueFirst.Set();
            coordinator.Stop();
        }
    }

    [TestMethod]
    public async Task UnrealizedItemLosesPriorityBeforeWorkerDequeuesIt()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var continueFirst = new ManualResetEventSlim();
        var order = new ConcurrentQueue<int>();
        var (_, viewModels) = CreateItems(4, order, firstStarted, continueFirst);
        var coordinator = new ListItemInitializationCoordinator(viewModels);
        var worker = Task.Run(() => coordinator.Run(CancellationToken.None));

        try
        {
            Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(2)));
            var registration = viewModels[3].BeginRealization();
            Assert.IsTrue(registration.IsValid);
            registration.Release();

            continueFirst.Set();
            await worker.WaitAsync(TimeSpan.FromSeconds(2));

            CollectionAssert.AreEqual(SequentialOrder, order.ToArray());
        }
        finally
        {
            continueFirst.Set();
            coordinator.Stop();
        }
    }

    [TestMethod]
    public async Task SelectionRequestUsesSameWorkerAndJumpsAhead()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var continueFirst = new ManualResetEventSlim();
        var order = new ConcurrentQueue<int>();
        var (_, viewModels) = CreateItems(4, order, firstStarted, continueFirst);
        var coordinator = new ListItemInitializationCoordinator(viewModels);
        var worker = Task.Run(() => coordinator.Run(CancellationToken.None));

        try
        {
            Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(2)));
            var selectedInitialization = coordinator.RequestInitializationAsync(viewModels[3], CancellationToken.None);

            continueFirst.Set();
            Assert.IsTrue(await selectedInitialization.WaitAsync(TimeSpan.FromSeconds(2)));
            await worker.WaitAsync(TimeSpan.FromSeconds(2));

            CollectionAssert.AreEqual(RealizedPriorityOrder, order.ToArray());
        }
        finally
        {
            continueFirst.Set();
            coordinator.Stop();
        }
    }

    [TestMethod]
    public async Task ConcurrentInitializationClaimsRunExtensionGetterOnce()
    {
        using var initializationStarted = new ManualResetEventSlim();
        using var continueInitialization = new ManualResetEventSlim();
        var order = new ConcurrentQueue<int>();
        var (models, viewModels) = CreateItems(1, order, initializationStarted, continueInitialization);
        var viewModel = viewModels[0];

        var first = Task.Run(viewModel.InitializePropertiesOnce);
        Assert.IsTrue(initializationStarted.Wait(TimeSpan.FromSeconds(2)));

        var waiter = viewModel.WaitForInitializationAsync(CancellationToken.None);
        var second = Task.Run(viewModel.InitializePropertiesOnce);
        await second.WaitAsync(TimeSpan.FromSeconds(2));

        continueInitialization.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(await waiter.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(1, models[0].InitializationCount);
    }

    [TestMethod]
    public async Task SelectionRequestAfterCoordinatorStopsDoesNotHang()
    {
        var order = new ConcurrentQueue<int>();
        var (_, viewModels) = CreateItems(1, order);
        var coordinator = new ListItemInitializationCoordinator(viewModels);
        coordinator.Stop();

        var initialized = await coordinator.RequestInitializationAsync(viewModels[0], CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(initialized);
        Assert.AreEqual(0, order.Count);
    }

    [TestMethod]
    public async Task ReplacedCoordinatorCannotInitializeItemFromStaleSelection()
    {
        var order = new ConcurrentQueue<int>();
        var (_, viewModels) = CreateItems(1, order);
        var oldCoordinator = new ListItemInitializationCoordinator(viewModels);
        var currentCoordinator = new ListItemInitializationCoordinator(viewModels);

        var initializedByOldCoordinator = await oldCoordinator.RequestInitializationAsync(viewModels[0], CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(initializedByOldCoordinator);

        currentCoordinator.Run(CancellationToken.None);
        Assert.AreEqual(1, order.Count);
    }

    private static (TrackingListItem[] Models, ListItemViewModel[] ViewModels) CreateItems(
        int count,
        ConcurrentQueue<int> initializationOrder,
        ManualResetEventSlim? firstInitializationStarted = null,
        ManualResetEventSlim? continueFirstInitialization = null)
    {
        var pageContext = new TestPageContext();
        var models = new TrackingListItem[count];
        var viewModels = new ListItemViewModel[count];
        for (var i = 0; i < count; i++)
        {
            models[i] = new(
                i,
                initializationOrder,
                i == 0 ? firstInitializationStarted : null,
                i == 0 ? continueFirstInitialization : null);
            viewModels[i] = new(models[i], new(pageContext), DefaultContextMenuFactory.Instance);
            Assert.IsTrue(viewModels[i].SafeFastInit());
        }

        return (models, viewModels);
    }
}
