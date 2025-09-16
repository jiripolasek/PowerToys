// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace SamplePagesExtension.Pages;

public sealed partial class SamplePageFactoryCommand(SamplePageFactoryPage items) : PageFactoryCommand
{
    public override IPage CreatePage()
    {
        var newPage = new SampleTimeCapturePage(Guid.CreateVersion7(DateTimeOffset.UtcNow).ToString());
        items.AddPage(newPage);
        return newPage;
    }
}
