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

internal enum IconLoadInputKind
{
    Empty,
    String,
    ShellBinary,
    Stream,
    SpecializedAppIcon,
    GeneratedSwatch,
    GeneratedInitials,
    SvgFile,
    SvgInline,
    ThemedSvgFile,
    ThemedSvgInline,
}
