// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;

namespace Microsoft.CmdPal.UI.Helpers;

internal static class InitialsVectorFont
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;

    public static bool TryNormalize(ReadOnlySpan<char> value, out string initials)
    {
        value = value.Trim();
        if (value.Length is < 1 or > 3)
        {
            initials = string.Empty;
            return false;
        }

        Span<char> normalized = stackalloc char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = char.ToUpperInvariant(value[index]);
            if (GetPattern(character) is null)
            {
                initials = string.Empty;
                return false;
            }

            normalized[index] = character;
        }

        initials = normalized.ToString();
        return true;
    }

    public static string CreatePathData(string initials)
    {
        var (cellWidth, cellHeight, gap) = initials.Length switch
        {
            1 => (2.4, 2.4, 0.75),
            2 => (1.8, 2.0, 0.7),
            _ => (1.32, 1.85, 0.65),
        };
        var width = ((initials.Length * GlyphWidth) + ((initials.Length - 1) * gap)) * cellWidth;
        var height = GlyphHeight * cellHeight;
        var originX = (32 - width) / 2;
        var originY = (32 - height) / 2;
        var path = new StringBuilder(initials.Length * 180);

        for (var glyphIndex = 0; glyphIndex < initials.Length; glyphIndex++)
        {
            var pattern = GetPattern(initials[glyphIndex])!;
            var glyphX = originX + (glyphIndex * (GlyphWidth + gap) * cellWidth);
            for (var row = 0; row < GlyphHeight; row++)
            {
                var column = 0;
                while (column < GlyphWidth)
                {
                    if (pattern[(row * (GlyphWidth + 1)) + column] == '0')
                    {
                        column++;
                        continue;
                    }

                    var runStart = column;
                    while (column < GlyphWidth && pattern[(row * (GlyphWidth + 1)) + column] == '1')
                    {
                        column++;
                    }

                    AppendRectangle(
                        path,
                        glyphX + (runStart * cellWidth),
                        originY + (row * cellHeight),
                        (column - runStart) * cellWidth,
                        cellHeight);
                }
            }
        }

        return path.ToString();
    }

    private static void AppendRectangle(StringBuilder path, double x, double y, double width, double height)
    {
        path.Append('M').Append(Format(x)).Append(' ').Append(Format(y));
        path.Append('h').Append(Format(width));
        path.Append('v').Append(Format(height));
        path.Append('h').Append(Format(-width)).Append('z');
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // SvgImageSource's Direct2D SVG subset does not render text elements. This
    // small built-in alphabet keeps generated avatars deterministic and avoids
    // invoking a font renderer or the UI thread merely to prepare their glyphs.
    private static string? GetPattern(char value) =>
        value switch
        {
            'A' => "01110/10001/10001/11111/10001/10001/10001",
            'B' => "11110/10001/10001/11110/10001/10001/11110",
            'C' => "01111/10000/10000/10000/10000/10000/01111",
            'D' => "11110/10001/10001/10001/10001/10001/11110",
            'E' => "11111/10000/10000/11110/10000/10000/11111",
            'F' => "11111/10000/10000/11110/10000/10000/10000",
            'G' => "01111/10000/10000/10111/10001/10001/01110",
            'H' => "10001/10001/10001/11111/10001/10001/10001",
            'I' => "11111/00100/00100/00100/00100/00100/11111",
            'J' => "00111/00010/00010/00010/10010/10010/01100",
            'K' => "10001/10010/10100/11000/10100/10010/10001",
            'L' => "10000/10000/10000/10000/10000/10000/11111",
            'M' => "10001/11011/10101/10101/10001/10001/10001",
            'N' => "10001/11001/10101/10011/10001/10001/10001",
            'O' => "01110/10001/10001/10001/10001/10001/01110",
            'P' => "11110/10001/10001/11110/10000/10000/10000",
            'Q' => "01110/10001/10001/10001/10101/10010/01101",
            'R' => "11110/10001/10001/11110/10100/10010/10001",
            'S' => "01111/10000/10000/01110/00001/00001/11110",
            'T' => "11111/00100/00100/00100/00100/00100/00100",
            'U' => "10001/10001/10001/10001/10001/10001/01110",
            'V' => "10001/10001/10001/10001/10001/01010/00100",
            'W' => "10001/10001/10001/10101/10101/11011/10001",
            'X' => "10001/01010/00100/00100/00100/01010/10001",
            'Y' => "10001/01010/00100/00100/00100/00100/00100",
            'Z' => "11111/00001/00010/00100/01000/10000/11111",
            '0' => "01110/10001/10011/10101/11001/10001/01110",
            '1' => "00100/01100/00100/00100/00100/00100/01110",
            '2' => "01110/10001/00001/00010/00100/01000/11111",
            '3' => "11110/00001/00001/01110/00001/00001/11110",
            '4' => "00010/00110/01010/10010/11111/00010/00010",
            '5' => "11111/10000/10000/11110/00001/00001/11110",
            '6' => "01110/10000/10000/11110/10001/10001/01110",
            '7' => "11111/00001/00010/00100/01000/01000/01000",
            '8' => "01110/10001/10001/01110/10001/10001/01110",
            '9' => "01110/10001/10001/01111/00001/00001/01110",
            _ => null,
        };
}
