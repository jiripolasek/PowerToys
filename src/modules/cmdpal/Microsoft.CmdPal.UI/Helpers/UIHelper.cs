// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using ManagedCommon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Microsoft.CmdPal.UI.Helpers;

internal static partial class UIHelper
{
    internal static void AnnounceActionForAccessibility(this UIElement ue, string announcement, string activityId)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(ue);
        peer?.AnnounceActionForAccessibility(announcement, activityId);
    }

    internal static void AnnounceActionForAccessibility(this AutomationPeer peer, string announcement, string activityId)
    {
        peer.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            announcement,
            activityId);
        Logger.LogInfo($"AnnounceActionForAccessibility Announcement: {announcement}, ActivityId: {activityId}, Peer: {peer.GetName()} CTRL {peer.IsControlElement()} CNT {peer.IsContentElement()}");
    }
}
