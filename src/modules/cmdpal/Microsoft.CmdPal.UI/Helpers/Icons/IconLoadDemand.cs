// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Microsoft.CmdPal.UI.Helpers;

/// <summary>
/// Aggregates all live UI requests sharing one icon load.
/// </summary>
internal sealed class IconLoadDemand
{
    private int _liveRequesters;
    private IObserver? _observer;

    internal interface IObserver
    {
        // This is an invalidation, not a state snapshot. Notifications may overtake
        // one another, so observers must read current state.
        void OnDemandChanged();
    }

    public bool IsDemanded => Volatile.Read(ref _liveRequesters) > 0;

    public static IconLoadDemand CreateDemanded()
    {
        var demand = new IconLoadDemand();
        demand.AddRequester();
        return demand;
    }

    public void Attach(IIconRequestDemand? requestDemand)
    {
        if (requestDemand is null)
        {
            AddRequester();
        }
        else
        {
            requestDemand.Attach(this);
        }
    }

    public void Subscribe(IObserver observer)
    {
        Debug.Assert(Volatile.Read(ref _observer) is null, "An icon load can be queued only once.");
        if (Interlocked.CompareExchange(ref _observer, observer, null) is not null)
        {
            throw new InvalidOperationException("The icon load already has a queue observer.");
        }
    }

    public void Unsubscribe(IObserver observer)
    {
        Interlocked.CompareExchange(ref _observer, null, observer);
    }

    internal void AddRequester()
    {
        if (Interlocked.Increment(ref _liveRequesters) == 1)
        {
            Volatile.Read(ref _observer)?.OnDemandChanged();
        }
    }

    internal void RemoveRequester()
    {
        var liveRequesters = Interlocked.Decrement(ref _liveRequesters);
        Debug.Assert(liveRequesters >= 0, "A released icon request must be attached to the load.");
        if (liveRequesters < 0)
        {
            Interlocked.Increment(ref _liveRequesters);
            return;
        }

        if (liveRequesters == 0)
        {
            Volatile.Read(ref _observer)?.OnDemandChanged();
        }
    }
}
