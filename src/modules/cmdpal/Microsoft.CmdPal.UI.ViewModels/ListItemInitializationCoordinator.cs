// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using Microsoft.CmdPal.Common;

namespace Microsoft.CmdPal.UI.ViewModels;

internal sealed class ListItemInitializationCoordinator
{
    private readonly ListItemViewModel[] _items;
    private readonly ConcurrentQueue<(ListItemViewModel Item, long RealizationToken)> _priorityRequests = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextRealizationToken;
    private int _accepting = 1;

    internal ListItemInitializationCoordinator(ListItemViewModel[] items)
    {
        _items = items;
        foreach (var item in items)
        {
            item.AttachInitializationCoordinator(this);
        }
    }

    internal ListItemRealizationRegistration BeginRealization(ListItemViewModel item)
    {
        if (Volatile.Read(ref _accepting) == 0 || item.IsInitializationComplete)
        {
            return default;
        }

        var token = Interlocked.Increment(ref _nextRealizationToken);
        if (!item.TrySetRealization(this, token))
        {
            return default;
        }

        if (Volatile.Read(ref _accepting) == 0 || !item.IsAttachedTo(this))
        {
            item.EndRealization(this, token);
            return default;
        }

        _priorityRequests.Enqueue((item, token));
        return new(this, item, token);
    }

    internal void EndRealization(ListItemViewModel item, long token) => item.EndRealization(this, token);

    internal async Task<bool> RequestInitializationAsync(ListItemViewModel item, CancellationToken cancellationToken)
    {
        if (item.IsInitializationComplete)
        {
            return item.InitializationWasSuccessful;
        }

        if (Volatile.Read(ref _accepting) == 0 || !item.IsAttachedTo(this))
        {
            return false;
        }

        // A zero token is an unconditional request used by selection. Realized
        // requests carry a token so recycling can invalidate them without
        // removing anything from ConcurrentQueue on the UI thread.
        _priorityRequests.Enqueue((item, 0));

        var initialization = item.WaitForInitializationAsync(cancellationToken);
        var completed = await Task.WhenAny(initialization, _stopped.Task).ConfigureAwait(false);
        if (completed == initialization)
        {
            return await initialization.ConfigureAwait(false);
        }

        return item.IsInitializationComplete && item.InitializationWasSuccessful;
    }

    internal void Run(CancellationToken cancellationToken)
    {
        try
        {
            var speculativeIndex = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (TryTakePriorityRequest(out var item))
                {
                    item.InitializePropertiesOnce();
                    continue;
                }

                while (speculativeIndex < _items.Length && _items[speculativeIndex].IsInitializationComplete)
                {
                    speculativeIndex++;
                }

                if (speculativeIndex >= _items.Length)
                {
                    return;
                }

                item = _items[speculativeIndex++];
                item.InitializePropertiesOnce();
            }
        }
        catch (Exception ex)
        {
            CoreLogger.LogError("Failed to coordinate list item initialization", ex);
        }
        finally
        {
            Stop();
        }
    }

    internal void Stop()
    {
        Interlocked.Exchange(ref _accepting, 0);
        _stopped.TrySetResult();
    }

    private bool TryTakePriorityRequest(out ListItemViewModel item)
    {
        while (_priorityRequests.TryDequeue(out var request))
        {
            if (!request.Item.IsAttachedTo(this))
            {
                continue;
            }

            if (request.RealizationToken != 0 && !request.Item.IsCurrentRealization(this, request.RealizationToken))
            {
                continue;
            }

            if (request.Item.IsInitializationComplete)
            {
                continue;
            }

            item = request.Item;
            return true;
        }

        item = null!;
        return false;
    }
}
