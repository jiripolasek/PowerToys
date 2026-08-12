// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.UI.ViewModels;

public readonly struct ListItemRealizationRegistration
{
    private readonly ListItemInitializationCoordinator? _coordinator;
    private readonly ListItemViewModel? _item;
    private readonly long _token;

    internal ListItemRealizationRegistration(ListItemInitializationCoordinator coordinator, ListItemViewModel item, long token)
    {
        _coordinator = coordinator;
        _item = item;
        _token = token;
    }

    public bool IsValid => _coordinator is not null;

    public bool IsFor(ListItemViewModel item) => ReferenceEquals(_item, item);

    public void Release()
    {
        if (_coordinator is not null && _item is not null)
        {
            _coordinator.EndRealization(_item, _token);
        }
    }
}
