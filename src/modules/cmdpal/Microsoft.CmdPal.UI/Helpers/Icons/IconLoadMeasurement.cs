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
    private const int DispatcherWaitingState = 1;
    private const int DispatcherCallbackState = 2;
    private const int DispatcherCompletedState = 3;

    private readonly long _createdAt = Stopwatch.GetTimestamp();
    private long _enqueuedAt;
    private int _queuePriority;
    private int _enqueued;
    private int _started;
    private int _completed;
    private int _resultKind;
    private int _dispatcherState;
    private int _dispatcherMaterializationKind;
    private int _dispatcherDemandAtEnqueue;

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

    public long BeginDispatcherWait(
        IconDispatcherMaterializationKind materializationKind = IconDispatcherMaterializationKind.Unknown,
        bool isDemanded = true)
    {
        var now = Stopwatch.GetTimestamp();
        Volatile.Write(ref _dispatcherMaterializationKind, (int)materializationKind);
        Volatile.Write(ref _dispatcherDemandAtEnqueue, isDemanded ? 1 : 0);
        if (Interlocked.CompareExchange(ref _dispatcherState, DispatcherWaitingState, 0) == 0)
        {
            Session.RecordDispatcherEnqueued(Id, InputKind, materializationKind, isDemanded);
        }

        return now;
    }

    public long DispatcherStarted(long enqueuedAt, bool isDemanded = true)
    {
        var now = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(
                ref _dispatcherState,
                DispatcherCallbackState,
                DispatcherWaitingState) == DispatcherWaitingState)
        {
            Session.RecordDispatcherWait(
                Id,
                InputKind,
                (IconDispatcherMaterializationKind)Volatile.Read(ref _dispatcherMaterializationKind),
                isDemanded,
                enqueuedAt,
                now - enqueuedAt);
        }

        // Start callback-wall and UI-slice timing after recording the queue-wait
        // sample so diagnostics bookkeeping is not attributed to materialization.
        return Stopwatch.GetTimestamp();
    }

    public long DispatcherUiSliceCompleted(
        long startedAt,
        IconDispatcherUiSliceKind sliceKind,
        bool isDemanded)
    {
        var now = Stopwatch.GetTimestamp();
        Session.RecordDispatcherUiSlice(
            Id,
            InputKind,
            (IconDispatcherMaterializationKind)Volatile.Read(ref _dispatcherMaterializationKind),
            sliceKind,
            isDemanded,
            startedAt,
            now - startedAt);
        return Stopwatch.GetTimestamp();
    }

    public long DispatcherAsyncSuspensionCompleted(long startedAt, bool isDemanded)
    {
        var now = Stopwatch.GetTimestamp();
        Session.RecordDispatcherAsyncSuspension(
            Id,
            InputKind,
            (IconDispatcherMaterializationKind)Volatile.Read(ref _dispatcherMaterializationKind),
            isDemanded,
            startedAt,
            now - startedAt);
        return Stopwatch.GetTimestamp();
    }

    public void DispatcherCompleted(long startedAt, bool isDemanded = true)
    {
        var now = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(
                ref _dispatcherState,
                DispatcherCompletedState,
                DispatcherCallbackState) == DispatcherCallbackState)
        {
            Session.RecordDispatcherWork(
                Id,
                InputKind,
                (IconDispatcherMaterializationKind)Volatile.Read(ref _dispatcherMaterializationKind),
                isDemanded,
                startedAt,
                now - startedAt);
        }
    }

    public void DispatcherWaitFailed(long enqueuedAt)
    {
        var now = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(
                ref _dispatcherState,
                DispatcherCompletedState,
                DispatcherWaitingState) == DispatcherWaitingState)
        {
            Session.RecordDispatcherWaitFailed(
                Id,
                InputKind,
                (IconDispatcherMaterializationKind)Volatile.Read(ref _dispatcherMaterializationKind),
                Volatile.Read(ref _dispatcherDemandAtEnqueue) != 0,
                enqueuedAt,
                now - enqueuedAt);
        }
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
