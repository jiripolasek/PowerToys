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

internal enum IconLoadQueueDemandTransition
{
    EnqueuedDemanded,
    EnqueuedSpeculative,
    Demoted,
    Promoted,
}

/// <summary>
/// Opt-in, process-local measurements for the CmdPal icon pipeline.
/// No icon strings, paths, glyphs, application identifiers, or item data are recorded.
/// Diagnostic scopes are static developer-authored labels.
/// </summary>
