// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.UI.Xaml;

namespace Microsoft.CmdPal.UI.Helpers;

/// <summary>
/// Parses <c>|Swatch|color[|dark]</c> and
/// <c>|Initials|text|color[|dark][|circle|rounded]</c> icon strings.
/// Colors use the XAML #RGB, #ARGB, #RRGGBB, or #AARRGGBB forms.
/// </summary>
internal static class GeneratedIconProtocol
{
    private const string SwatchPrefix = "|Swatch|";
    private const string InitialsPrefix = "|Initials|";

    public static Kind Classify(string? value)
    {
        if (value?.StartsWith(SwatchPrefix, StringComparison.Ordinal) == true)
        {
            return Kind.Swatch;
        }

        if (value?.StartsWith(InitialsPrefix, StringComparison.Ordinal) == true)
        {
            return Kind.Initials;
        }

        return Kind.None;
    }

    public static ElementTheme GetCacheTheme(string? value, ElementTheme theme)
    {
        if (!IsThemeDependent(value))
        {
            return ElementTheme.Default;
        }

        return theme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    public static bool TryCreateSvg(string? value, ElementTheme theme, out byte[] svg)
    {
        svg = [];

        try
        {
            switch (Classify(value))
            {
                case Kind.Swatch:
                {
                    if (!TryParseSwatch(value!.AsSpan(SwatchPrefix.Length), out var light, out var dark, out _))
                    {
                        return false;
                    }

                    svg = CreateSwatchSvg(SelectColor(light, dark, theme));
                    return true;
                }

                case Kind.Initials:
                {
                    if (!TryParseInitials(
                            value!.AsSpan(InitialsPrefix.Length),
                            out var initials,
                            out var light,
                            out var dark,
                            out _,
                            out var shape))
                    {
                        return false;
                    }

                    svg = CreateInitialsSvg(
                        initials,
                        SelectColor(light, dark, theme),
                        theme,
                        shape);
                    return true;
                }

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

    private static bool IsThemeDependent(string? value)
    {
        switch (Classify(value))
        {
            case Kind.Swatch:
                return TryParseSwatch(value!.AsSpan(SwatchPrefix.Length), out _, out _, out var hasDark) && hasDark;

            case Kind.Initials:
                // Foreground contrast can depend on the surface theme when the
                // background is translucent. Keep every initials entry isolated
                // by theme so this cheap discriminator never has to parse it.
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseSwatch(
        ReadOnlySpan<char> payload,
        out RgbaColor light,
        out RgbaColor dark,
        out bool hasDark)
    {
        light = default;
        dark = default;
        hasDark = false;
        payload = TrimOptionalTrailingSeparator(payload);
        if (!TryReadToken(ref payload, out var lightToken) || !TryParseColor(lightToken, out light))
        {
            return false;
        }

        dark = light;
        if (!payload.IsEmpty)
        {
            if (!TryReadToken(ref payload, out var darkToken) || !TryParseColor(darkToken, out dark))
            {
                return false;
            }

            hasDark = true;
        }

        return payload.IsEmpty;
    }

    private static bool TryParseInitials(
        ReadOnlySpan<char> payload,
        out string initials,
        out RgbaColor light,
        out RgbaColor dark,
        out bool hasDark,
        out InitialsShape shape)
    {
        initials = string.Empty;
        light = default;
        dark = default;
        hasDark = false;
        shape = InitialsShape.Circle;

        payload = TrimOptionalTrailingSeparator(payload);
        if (!TryReadToken(ref payload, out var initialsToken)
            || !InitialsVectorFont.TryNormalize(initialsToken, out initials)
            || !TryReadToken(ref payload, out var lightToken)
            || !TryParseColor(lightToken, out light))
        {
            return false;
        }

        dark = light;
        if (!payload.IsEmpty)
        {
            if (!TryReadToken(ref payload, out var nextToken))
            {
                return false;
            }

            if (TryParseColor(nextToken, out var darkColor))
            {
                dark = darkColor;
                hasDark = true;
                if (!payload.IsEmpty
                    && (!TryReadToken(ref payload, out var shapeToken) || !TryParseShape(shapeToken, out shape)))
                {
                    return false;
                }
            }
            else if (!TryParseShape(nextToken, out shape))
            {
                return false;
            }
        }

        if (!payload.IsEmpty)
        {
            return false;
        }

        return true;
    }

    private static bool TryParseShape(ReadOnlySpan<char> value, out InitialsShape shape)
    {
        if (value.Equals("circle", StringComparison.OrdinalIgnoreCase))
        {
            shape = InitialsShape.Circle;
            return true;
        }

        if (value.Equals("rounded", StringComparison.OrdinalIgnoreCase))
        {
            shape = InitialsShape.RoundedSquare;
            return true;
        }

        shape = default;
        return false;
    }

    private static bool TryParseColor(ReadOnlySpan<char> value, out RgbaColor color)
    {
        color = default;
        if (value.IsEmpty || value[0] != '#')
        {
            return false;
        }

        value = value[1..];
        switch (value.Length)
        {
            case 3:
                if (!TryParseHexDigit(value[0], out var shortRed)
                    || !TryParseHexDigit(value[1], out var shortGreen)
                    || !TryParseHexDigit(value[2], out var shortBlue))
                {
                    return false;
                }

                color = new RgbaColor(255, ExpandHexDigit(shortRed), ExpandHexDigit(shortGreen), ExpandHexDigit(shortBlue));
                return true;

            case 4:
                if (!TryParseHexDigit(value[0], out var shortAlpha)
                    || !TryParseHexDigit(value[1], out shortRed)
                    || !TryParseHexDigit(value[2], out shortGreen)
                    || !TryParseHexDigit(value[3], out shortBlue))
                {
                    return false;
                }

                color = new RgbaColor(
                    ExpandHexDigit(shortAlpha),
                    ExpandHexDigit(shortRed),
                    ExpandHexDigit(shortGreen),
                    ExpandHexDigit(shortBlue));
                return true;

            case 6:
                if (!TryParseHexByte(value[..2], out var red)
                    || !TryParseHexByte(value.Slice(2, 2), out var green)
                    || !TryParseHexByte(value.Slice(4, 2), out var blue))
                {
                    return false;
                }

                color = new RgbaColor(255, red, green, blue);
                return true;

            case 8:
                if (!TryParseHexByte(value[..2], out var alpha)
                    || !TryParseHexByte(value.Slice(2, 2), out red)
                    || !TryParseHexByte(value.Slice(4, 2), out green)
                    || !TryParseHexByte(value.Slice(6, 2), out blue))
                {
                    return false;
                }

                color = new RgbaColor(alpha, red, green, blue);
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> value, out byte result)
    {
        if (!TryParseHexDigit(value[0], out var high) || !TryParseHexDigit(value[1], out var low))
        {
            result = 0;
            return false;
        }

        result = (byte)((high << 4) | low);
        return true;
    }

    private static bool TryParseHexDigit(char value, out byte result)
    {
        if (value is >= '0' and <= '9')
        {
            result = (byte)(value - '0');
            return true;
        }

        if (value is >= 'A' and <= 'F')
        {
            result = (byte)(value - 'A' + 10);
            return true;
        }

        if (value is >= 'a' and <= 'f')
        {
            result = (byte)(value - 'a' + 10);
            return true;
        }

        result = 0;
        return false;
    }

    private static byte ExpandHexDigit(byte value) => (byte)((value << 4) | value);

    private static ReadOnlySpan<char> TrimOptionalTrailingSeparator(ReadOnlySpan<char> value) =>
        !value.IsEmpty && value[^1] == '|' ? value[..^1] : value;

    private static bool TryReadToken(ref ReadOnlySpan<char> remaining, out ReadOnlySpan<char> token)
    {
        if (remaining.IsEmpty)
        {
            token = default;
            return false;
        }

        var separator = remaining.IndexOf('|');
        if (separator < 0)
        {
            token = remaining;
            remaining = [];
        }
        else
        {
            token = remaining[..separator];
            remaining = remaining[(separator + 1)..];
        }

        return !token.IsEmpty;
    }

    private static RgbaColor SelectColor(RgbaColor light, RgbaColor dark, ElementTheme theme) =>
        theme == ElementTheme.Dark ? dark : light;

    private static byte[] CreateSwatchSvg(RgbaColor color)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateSvgWriter(stream))
        {
            WriteSvgStart(writer);
            writer.WriteStartElement("circle");
            writer.WriteAttributeString("cx", "16");
            writer.WriteAttributeString("cy", "16");
            writer.WriteAttributeString("r", "12");
            WriteFill(writer, color);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return stream.ToArray();
    }

    private static byte[] CreateInitialsSvg(
        string initials,
        RgbaColor background,
        ElementTheme theme,
        InitialsShape shape)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateSvgWriter(stream))
        {
            WriteSvgStart(writer);
            if (shape == InitialsShape.Circle)
            {
                writer.WriteStartElement("circle");
                writer.WriteAttributeString("cx", "16");
                writer.WriteAttributeString("cy", "16");
                writer.WriteAttributeString("r", "15.5");
            }
            else
            {
                writer.WriteStartElement("rect");
                writer.WriteAttributeString("x", "0.5");
                writer.WriteAttributeString("y", "0.5");
                writer.WriteAttributeString("width", "31");
                writer.WriteAttributeString("height", "31");
                writer.WriteAttributeString("rx", "7");
            }

            WriteFill(writer, background);
            writer.WriteEndElement();

            writer.WriteStartElement("path");
            writer.WriteAttributeString("d", InitialsVectorFont.CreatePathData(initials));
            WriteFill(writer, GetContrastingForeground(background, theme));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return stream.ToArray();
    }

    private static XmlWriter CreateSvgWriter(Stream stream) =>
        XmlWriter.Create(
            stream,
            new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                OmitXmlDeclaration = true,
                Indent = false,
                CloseOutput = false,
            });

    private static void WriteSvgStart(XmlWriter writer)
    {
        writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        writer.WriteAttributeString("viewBox", "0 0 32 32");
    }

    private static void WriteFill(XmlWriter writer, RgbaColor color)
    {
        writer.WriteAttributeString("fill", FormattableString.Invariant($"#{color.R:X2}{color.G:X2}{color.B:X2}"));
        if (color.A != byte.MaxValue)
        {
            writer.WriteAttributeString(
                "fill-opacity",
                (color.A / 255d).ToString("0.###", CultureInfo.InvariantCulture));
        }
    }

    private static RgbaColor GetContrastingForeground(RgbaColor background, ElementTheme theme)
    {
        var surface = theme == ElementTheme.Dark ? (byte)32 : byte.MaxValue;
        var red = Composite(background.R, background.A, surface);
        var green = Composite(background.G, background.A, surface);
        var blue = Composite(background.B, background.A, surface);
        var luminance = (0.2126 * ToLinear(red)) + (0.7152 * ToLinear(green)) + (0.0722 * ToLinear(blue));
        return luminance > 0.179
            ? new RgbaColor(255, 0, 0, 0)
            : new RgbaColor(255, 255, 255, 255);
    }

    private static byte Composite(byte foreground, byte alpha, byte background) =>
        (byte)(((foreground * alpha) + (background * (byte.MaxValue - alpha)) + 127) / byte.MaxValue);

    private static double ToLinear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    internal enum Kind
    {
        None,
        Swatch,
        Initials,
    }

    private enum InitialsShape
    {
        Circle,
        RoundedSquare,
    }

    private readonly record struct RgbaColor(byte A, byte R, byte G, byte B);
}
