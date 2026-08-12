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

[Flags]
internal enum IconRequestReason
{
    None = 0,
    SourceChanged = 1 << 0,
    HandlerAttached = 1 << 1,
    Loaded = 1 << 2,
    ThemeChanged = 1 << 3,
    ScaleChanged = 1 << 4,
    Retry = 1 << 5,
}
