// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.UI.Helpers;

internal static class AppIconProtocol
{
    private const string AppIconPrefix = "|AppIcon|";
    private const string JumboAppIconPrefix = "|JumboAppIcon|";

    public static bool IsProtocol(string? value) =>
        value?.StartsWith(AppIconPrefix, StringComparison.Ordinal) == true
        || value?.StartsWith(JumboAppIconPrefix, StringComparison.Ordinal) == true;

    public static bool TryParse(string? value, out string path, out bool jumbo)
    {
        if (value?.StartsWith(AppIconPrefix, StringComparison.Ordinal) == true)
        {
            path = value[AppIconPrefix.Length..];
            jumbo = false;
            return path.Length > 0;
        }

        if (value?.StartsWith(JumboAppIconPrefix, StringComparison.Ordinal) == true)
        {
            path = value[JumboAppIconPrefix.Length..];
            jumbo = true;
            return path.Length > 0;
        }

        path = string.Empty;
        jumbo = false;
        return false;
    }
}
