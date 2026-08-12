// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.UI.Xaml;

namespace Microsoft.CmdPal.UI.Helpers;

internal sealed class AppIconProtocolProcessor : IIconProtocolProcessor
{
    public static AppIconProtocolProcessor Instance { get; } = new();

    private AppIconProtocolProcessor()
    {
    }

    public IconCachePartition CachePartition => IconCachePartition.Other;

    public bool Matches(string value) => AppIconProtocol.IsProtocol(value);

    public ElementTheme GetCacheTheme(string value, ElementTheme theme) => ElementTheme.Default;

    public IconLoadInputKind ClassifyInput(string value) => IconLoadInputKind.SpecializedAppIcon;

    public bool TryPrepareSynchronously(
        string value,
        int targetSize,
        ElementTheme theme,
        out IconPathConverter.PreparedIcon preparedIcon)
    {
        preparedIcon = null!;
        return false;
    }

    public async ValueTask<IconProtocolProcessingResult> PrepareAsync(
        string value,
        int targetSize,
        ElementTheme theme)
    {
        _ = targetSize;
        _ = theme;

        if (!AppIconProtocol.TryParse(value, out var path, out var jumbo))
        {
            return IconProtocolProcessingResult.Empty();
        }

        try
        {
            if (await ThumbnailHelper.GetThumbnail(path, jumbo).ConfigureAwait(false) is { } stream)
            {
                return IconProtocolProcessingResult.FromBitmapStream(stream);
            }
        }
        catch
        {
            // Fall back to the ordinary path/index converter below.
        }

        return IconProtocolProcessingResult.FromFallbackIconString(path);
    }
}
