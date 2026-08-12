// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public sealed partial class ListItemTagPublicationTests
{
    private sealed class TestPageContext(TaskScheduler scheduler) : IPageContext
    {
        public TaskScheduler Scheduler { get; } = scheduler;

        public ICommandProviderContext ProviderContext => CommandProviderContext.Empty;

        public void ShowException(Exception ex, string? extensionHint = null) =>
            throw new AssertFailedException($"Unexpected exception from view model: {ex}");
    }

    private sealed class PausedTaskScheduler : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> _tasks = new();

        protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();

        protected override void QueueTask(Task task) => _tasks.Enqueue(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        public int DirectTaskCount => _tasks.Count(task => task.AsyncState is null);

        public void RunQueuedTasks()
        {
            while (_tasks.TryDequeue(out var task))
            {
                _ = TryExecuteTask(task);
            }
        }
    }

    [TestMethod]
    public async Task InitializePropertiesCoalescesTagPublicationIntoPropertyBatch()
    {
        var scheduler = new PausedTaskScheduler();
        var context = new TestPageContext(scheduler);
        var item = new ListItem(new NoOpCommand { Name = "Primary" })
        {
            Tags =
            [
                new Tag("sample"),
            ],
        };
        var viewModel = new ListItemViewModel(item, new(context), DefaultContextMenuFactory.Instance);

        try
        {
            viewModel.InitializeProperties();

            // The snapshot remains UI-owned until the shared property batch runs.
            Assert.IsNull(viewModel.Tags);

            await Task.Delay(100);

            // A dedicated DoOnUiThread task has no AsyncState. Batched view-model
            // tasks carry UiBatch state, including nested command/tag updates.
            Assert.AreEqual(0, scheduler.DirectTaskCount);
            scheduler.RunQueuedTasks();

            Assert.IsNotNull(viewModel.Tags);
            Assert.AreEqual(1, viewModel.Tags.Count);
            Assert.IsNotNull(viewModel.VisibleTags);
            Assert.AreEqual(1, viewModel.VisibleTags.Count);
            Assert.AreEqual("sample", viewModel.VisibleTags[0].Text);
        }
        finally
        {
            // BatchUpdateManager uses a 40 ms collection window. Drain the
            // deliberately paused UI notifications so the test leaves no work.
            await Task.Delay(100);
            scheduler.RunQueuedTasks();
        }
    }
}
