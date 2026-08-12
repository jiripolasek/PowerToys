// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml;

namespace Microsoft.CmdPal.UI.Helpers;

internal sealed class GeneratedIconProtocolProcessor : IIconProtocolProcessor
{
    public static GeneratedIconProtocolProcessor Instance { get; } = new();

    private GeneratedIconProtocolProcessor()
    {
    }

    public IconCachePartition CachePartition => IconCachePartition.Other;

    public bool Matches(string value) => GeneratedIconProtocol.Classify(value) != GeneratedIconProtocol.Kind.None;

    public ElementTheme GetCacheTheme(string value, ElementTheme theme) =>
        GeneratedIconProtocol.GetCacheTheme(value, theme);

    public IconLoadInputKind ClassifyInput(string value) =>
        GeneratedIconProtocol.Classify(value) switch
        {
            GeneratedIconProtocol.Kind.Swatch => IconLoadInputKind.GeneratedSwatch,
            GeneratedIconProtocol.Kind.Initials => IconLoadInputKind.GeneratedInitials,
            _ => IconLoadInputKind.String,
        };

    public bool TryPrepareSynchronously(
        string value,
        int targetSize,
        ElementTheme theme,
        out IconPathConverter.PreparedIcon preparedIcon)
    {
        preparedIcon = GeneratedIconProtocol.TryCreateSvg(value, theme, out var svg)
            ? IconPathConverter.PreparedIcon.FromSvgData(svg, targetSize)
            : IconPathConverter.PreparedIcon.Empty();
        return true;
    }

    public ValueTask<IconProtocolProcessingResult> PrepareAsync(
        string value,
        int targetSize,
        ElementTheme theme)
    {
        _ = TryPrepareSynchronously(value, targetSize, theme, out var preparedIcon);
        return ValueTask.FromResult(IconProtocolProcessingResult.FromPreparedIcon(preparedIcon));
    }
}
