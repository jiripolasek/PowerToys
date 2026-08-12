// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;
using Microsoft.UI.Xaml;

namespace Microsoft.CmdPal.UI.Helpers;

/// <summary>
/// Resolves plain <c>|Svg|payload</c> and theme-aware
/// <c>|ThemedSvg|[accent|]payload</c> icon strings. A payload is either inline SVG
/// or the path to an SVG file.
/// </summary>
/// <remarks>
/// Plain SVGs are passed through without placeholder expansion and share cache entries
/// across themes. Themed SVGs replace <c>{{ThemeColor}}</c> and <c>{{AccentColor}}</c>
/// and use distinct light- and dark-theme cache entries. Accent values may be CSS SVG
/// hex colors or one of: danger, subtle, info, warning, success, neutral, dark,
/// normal, or transparent.
/// SVG files are treated as immutable while cached.
/// </remarks>
internal static class SvgIconProtocol
{
    private const string PlainPrefix = "|Svg|";
    private const string ThemedPrefix = "|ThemedSvg|";
    private const string ThemeColorPlaceholder = "{{ThemeColor}}";
    private const string AccentColorPlaceholder = "{{AccentColor}}";
    private const string LightThemeColor = "#000000";
    private const string DarkThemeColor = "#FFFFFF";

    public static bool IsProtocol(string? value) =>
        value?.StartsWith(PlainPrefix, StringComparison.Ordinal) == true
        || value?.StartsWith(ThemedPrefix, StringComparison.Ordinal) == true;

    public static Kind Classify(string? value)
    {
        if (value?.StartsWith(PlainPrefix, StringComparison.Ordinal) == true)
        {
            return IsInline(value.AsSpan(PlainPrefix.Length)) ? Kind.PlainInline : Kind.PlainFile;
        }

        if (value?.StartsWith(ThemedPrefix, StringComparison.Ordinal) != true)
        {
            return Kind.None;
        }

        var payload = value.AsSpan(ThemedPrefix.Length).TrimStart();
        if (!payload.IsEmpty && payload[0] != '<')
        {
            var separator = payload.IndexOf('|');
            if (separator >= 0 && IsAccent(payload[..separator]))
            {
                payload = payload[(separator + 1)..];
            }
        }

        return IsInline(payload) ? Kind.ThemedInline : Kind.ThemedFile;
    }

    public static ElementTheme GetCacheTheme(string? value, ElementTheme theme) =>
        value?.StartsWith(ThemedPrefix, StringComparison.Ordinal) == true
            ? theme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light
            : ElementTheme.Default;

    public static bool TryCreateSvg(string? value, ElementTheme theme, out byte[] svg)
    {
        svg = [];

        try
        {
            switch (Classify(value))
            {
                case Kind.PlainFile:
                case Kind.PlainInline:
                    return TryCreatePlainSvg(value!, out svg);

                case Kind.ThemedFile:
                case Kind.ThemedInline:
                    return TryCreateThemedSvg(value!, theme, out svg);

                default:
                    return false;
            }
        }
        catch
        {
            svg = [];
            return false;
        }
    }

    private static bool TryCreatePlainSvg(string value, out byte[] svg)
    {
        svg = [];
        var payload = value[PlainPrefix.Length..];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        if (IsInline(payload))
        {
            // Inline strings have no source encoding; UTF-8 is the protocol encoding.
            // Apart from that encoding step, plain SVG content is not rewritten.
            svg = Encoding.UTF8.GetBytes(payload);
            return true;
        }

        if (!IsSvgPath(payload))
        {
            return false;
        }

        // IconPathConverter.Prepare invokes this on an icon-loader worker, so
        // filesystem access never blocks the WinUI STA thread. Reading bytes also
        // preserves the file's original encoding and XML declaration exactly.
        svg = File.ReadAllBytes(payload);
        return svg.Length > 0;
    }

    private static bool TryCreateThemedSvg(string value, ElementTheme theme, out byte[] svg)
    {
        svg = [];
        if (!TryParseThemedPayload(value, theme, out var payload, out var accentColor))
        {
            return false;
        }

        string template;
        if (IsInline(payload))
        {
            template = payload;
        }
        else
        {
            if (!IsSvgPath(payload))
            {
                return false;
            }

            // This path is reached only from an icon-loader worker; see the plain
            // SVG path above. File.ReadAllText detects the source BOM before the
            // expanded result is re-encoded as UTF-8.
            template = File.ReadAllText(payload);
        }

        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        // A source file may declare a different encoding. Drop that now-stale
        // declaration before emitting the expanded SVG as UTF-8.
        template = RemoveXmlDeclaration(template);
        var themeColor = theme == ElementTheme.Dark ? DarkThemeColor : LightThemeColor;
        var resolved = template
            .Replace(ThemeColorPlaceholder, themeColor, StringComparison.Ordinal)
            .Replace(AccentColorPlaceholder, accentColor, StringComparison.Ordinal);

        svg = Encoding.UTF8.GetBytes(resolved);
        return true;
    }

    private static bool TryParseThemedPayload(
        string value,
        ElementTheme theme,
        out string payload,
        out string accentColor)
    {
        payload = string.Empty;
        accentColor = SemanticIconColor.GetDefault(theme);

        var remaining = value.AsSpan(ThemedPrefix.Length).TrimStart();
        if (remaining.IsEmpty)
        {
            return false;
        }

        if (remaining[0] != '<')
        {
            var separator = remaining.IndexOf('|');
            if (separator >= 0)
            {
                if (!TryResolveAccent(remaining[..separator], theme, out accentColor))
                {
                    return false;
                }

                remaining = remaining[(separator + 1)..].TrimStart();
                if (remaining.IsEmpty)
                {
                    return false;
                }
            }
        }

        payload = remaining.ToString();
        return true;
    }

    private static bool TryResolveAccent(
        ReadOnlySpan<char> value,
        ElementTheme theme,
        out string accentColor)
    {
        if (IsCssHexColor(value))
        {
            accentColor = value.ToString();
            return true;
        }

        return SemanticIconColor.TryResolve(value, theme, out accentColor);
    }

    private static bool IsAccent(ReadOnlySpan<char> value) =>
        IsCssHexColor(value) || SemanticIconColor.IsSemantic(value);

    private static bool IsCssHexColor(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value[0] != '#' || value.Length is not (4 or 5 or 7 or 9))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSvgPath(string value) =>
        Path.GetExtension(value).Equals(".svg", StringComparison.OrdinalIgnoreCase);

    private static bool IsInline(string value) => IsInline(value.AsSpan());

    private static bool IsInline(ReadOnlySpan<char> value)
    {
        value = value.TrimStart();
        return !value.IsEmpty && value[0] == '<';
    }

    private static string RemoveXmlDeclaration(string template)
    {
        var firstNonWhitespace = 0;
        while (firstNonWhitespace < template.Length && char.IsWhiteSpace(template[firstNonWhitespace]))
        {
            firstNonWhitespace++;
        }

        if (!template.AsSpan(firstNonWhitespace).StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return template;
        }

        var declarationEnd = template.IndexOf("?>", firstNonWhitespace + 5, StringComparison.Ordinal);
        return declarationEnd >= 0
            ? template.Remove(firstNonWhitespace, (declarationEnd + 2) - firstNonWhitespace)
            : template;
    }

    internal enum Kind
    {
        None,
        PlainFile,
        PlainInline,
        ThemedFile,
        ThemedInline,
    }
}
