// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;
using ManagedCommon;
using Microsoft.CmdPal.UI.Controls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Microsoft.CmdPal.UI.Helpers;

internal sealed class IconLoadMeasurement
{
    private readonly long _createdAt = Stopwatch.GetTimestamp();
    private long _enqueuedAt;
    private int _queuePriority;
    private int _enqueued;
    private int _started;
    private int _completed;
    private int _resultKind;

    internal IconLoadDiagnosticsSession Session { get; }

    internal long Id { get; }

    internal IconLoadInputKind InputKind { get; }

    internal IconLoadMeasurement(IconLoadDiagnosticsSession session, long id, IconLoadInputKind inputKind)
    {
        Session = session;
        Id = id;
        InputKind = inputKind;
    }

    public void Enqueued(IconLoadPriority priority, int workerCount = 1)
    {
        _queuePriority = (int)priority;
        _enqueuedAt = Stopwatch.GetTimestamp();
        Session.RecordLoadEnqueued(Id, priority, Math.Max(1, workerCount));
        Volatile.Write(ref _enqueued, 1);
    }

    public void RegisterTask(Task<IconSource?> task)
    {
        Session.RegisterLoad(task, this);
    }

    public void Rejected()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            Session.RecordLoadRejected(Id);
        }
    }

    public void WorkerStarted(int workerCount = 1)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        SpinWait spinner = default;
        while (Volatile.Read(ref _enqueued) == 0)
        {
            spinner.SpinOnce();
        }

        var now = Stopwatch.GetTimestamp();
        Session.RecordWorkerStarted(Id, InputKind, (IconLoadPriority)_queuePriority, now - _enqueuedAt, workerCount);
    }

    public long BeginBackgroundPreparation() => Stopwatch.GetTimestamp();

    public void CompleteBackgroundPreparation(long startedAt)
    {
        Session.RecordBackgroundPreparation(Id, InputKind, Stopwatch.GetTimestamp() - startedAt);
    }

    public long BeginDispatcherWait() => Stopwatch.GetTimestamp();

    public long DispatcherStarted(long enqueuedAt)
    {
        var now = Stopwatch.GetTimestamp();
        Session.RecordDispatcherWait(Id, InputKind, now - enqueuedAt);
        return now;
    }

    public void DispatcherCompleted(long startedAt)
    {
        Session.RecordDispatcherWork(Id, InputKind, Stopwatch.GetTimestamp() - startedAt);
    }

    public void SetResult(IconSource? result)
    {
        _resultKind = (int)IconLoadDiagnostics.ClassifyResult(result);
    }

    public void CompleteDirectGlyph(IconSource? result)
    {
        SetResult(result);
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            Session.RecordDirectGlyphCompleted(
                Id,
                InputKind,
                (IconLoadResultKind)_resultKind,
                Stopwatch.GetTimestamp() - _createdAt);
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            Session.RecordLoadCompleted(Id, InputKind, (IconLoadResultKind)_resultKind, Stopwatch.GetTimestamp() - _enqueuedAt);
        }
    }

    public void Fail()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            Session.RecordLoadCompleted(Id, InputKind, IconLoadResultKind.Failed, Stopwatch.GetTimestamp() - _enqueuedAt);
        }
    }
}
