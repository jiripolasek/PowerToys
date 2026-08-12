// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ManagedCommon;
using Microsoft.UI.Dispatching;

namespace Microsoft.CmdPal.UI.Helpers;

/// <summary>
/// Samples normal-priority dispatcher responsiveness without allowing probes to queue up.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The owning diagnostic session calls Stop, which cancels the loop and disposes the source after it exits. Avoid implementing a WinRT interface on this internal NativeAOT type.")]
internal sealed class IconUiResponsivenessProbe
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(50);

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueHandler _probeCallback;
    private readonly IconLoadDiagnosticsSession _session;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _runTask;
    private int _active = 1;
    private int _callbackPending;
    private long _enqueuedAt;

    public IconUiResponsivenessProbe(
        DispatcherQueue dispatcherQueue,
        IconLoadDiagnosticsSession session)
    {
        _dispatcherQueue = dispatcherQueue;
        _probeCallback = ProbeCallback;
        _session = session;
        _runTask = RunAsync();
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _active, 0) == 0)
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to stop icon UI responsiveness probe", ex);
        }

        _ = _runTask.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            _cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(ProbeInterval);
            while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
            {
                if (Volatile.Read(ref _active) == 0)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _callbackPending, 1, 0) != 0)
                {
                    _session.RecordUiProbeSkipped();
                    continue;
                }

                Volatile.Write(ref _enqueuedAt, Stopwatch.GetTimestamp());
                _session.RecordUiProbeEnqueued();
                if (!_dispatcherQueue.TryEnqueue(
                        DispatcherQueuePriority.Normal,
                        _probeCallback))
                {
                    Interlocked.Exchange(ref _callbackPending, 0);
                    _session.RecordUiProbeRejected();
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError("Icon UI responsiveness probe failed", ex);
        }
    }

    private void ProbeCallback()
    {
        Interlocked.Exchange(ref _callbackPending, 0);
        if (Volatile.Read(ref _active) != 0)
        {
            _session.RecordUiProbeCompleted(Stopwatch.GetTimestamp() - Volatile.Read(ref _enqueuedAt));
        }
    }
}
