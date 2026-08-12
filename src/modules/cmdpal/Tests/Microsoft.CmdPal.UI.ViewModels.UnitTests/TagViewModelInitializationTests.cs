// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public sealed partial class TagViewModelInitializationTests
{
    private sealed class TestPageContext(TaskScheduler scheduler) : IPageContext
    {
        public TaskScheduler Scheduler { get; } = scheduler;

        public ICommandProviderContext ProviderContext => CommandProviderContext.Empty;

        public void ShowException(Exception ex, string? extensionHint = null) =>
            throw new AssertFailedException($"Unexpected exception from view model: {ex}");
    }

    private sealed class RecordingTaskScheduler : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> _tasks = new();

        public int ScheduledTaskCount => _tasks.Count;

        protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();

        protected override void QueueTask(Task task) => _tasks.Enqueue(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    }

    [TestMethod]
    public async Task InitializationBeforePublicationDoesNotScheduleUiWork()
    {
        var scheduler = new RecordingTaskScheduler();
        var context = new TestPageContext(scheduler);
        var viewModel = new TagViewModel(
            new Tag("sample")
            {
                ToolTip = "sample tooltip",
            },
            new(context));

        viewModel.InitializePropertiesBeforePublication();

        Assert.AreEqual("sample", viewModel.Text);
        Assert.AreEqual("sample tooltip", viewModel.ToolTip);

        // BatchUpdateManager collects notifications for 40 ms before it posts
        // them, so wait past that window before proving none were scheduled.
        await Task.Delay(100);
        Assert.AreEqual(0, scheduler.ScheduledTaskCount);
    }
}
