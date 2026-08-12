// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.UI.Helpers;

internal static class IconProtocolRegistry
{
    // This is deliberately immutable after type initialization. Protocol lookup is
    // used from the WinUI STA and loader workers, so it must not acquire a registry lock.
    // Explicit construction also keeps the registry visible to Native AOT without reflection.
    private static readonly IIconProtocolProcessor[] Processors =
    [
        AppIconProtocolProcessor.Instance,
        GeneratedIconProtocolProcessor.Instance,
        SvgIconProtocolProcessor.Instance,
    ];

    public static IIconProtocolProcessor? Find(string? value) => Find(value, Processors);

    internal static IIconProtocolProcessor? Find(
        string? value,
        ReadOnlySpan<IIconProtocolProcessor> processors)
    {
        // Every registered protocol starts with '|'. This leaves ordinary glyphs and
        // paths—the overwhelmingly common inputs—at one predictable character check.
        if (string.IsNullOrEmpty(value) || value[0] != '|')
        {
            return null;
        }

        foreach (var processor in processors)
        {
            if (processor.Matches(value))
            {
                return processor;
            }
        }

        return null;
    }
}
