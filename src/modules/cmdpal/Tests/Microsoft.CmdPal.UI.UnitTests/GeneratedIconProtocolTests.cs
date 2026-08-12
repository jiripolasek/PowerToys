// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Xml.Linq;
using Microsoft.CmdPal.UI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public class GeneratedIconProtocolTests
{
    [DataTestMethod]
    [DataRow("|Swatch|#07A|", "#0077AA", null)]
    [DataRow("|Swatch|#807A|", "#0077AA", "0.533")]
    [DataRow("|Swatch|#102030", "#102030", null)]
    [DataRow("|Swatch|#80102030|", "#102030", "0.502")]
    public void SwatchSupportsXamlHexColorForms(string value, string expectedFill, string? expectedOpacity)
    {
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var svg));

        var shape = ParseSvg(svg).Element(SvgName("circle"));
        Assert.IsNotNull(shape);
        Assert.AreEqual(expectedFill, shape.Attribute("fill")?.Value);
        Assert.AreEqual(expectedOpacity, shape.Attribute("fill-opacity")?.Value);
        Assert.AreEqual("15.5", shape.Attribute("r")?.Value);
    }

    [TestMethod]
    public void ThemeAwareSwatchSelectsThemeColorAndUsesThemeInCacheIdentity()
    {
        const string Value = "|Swatch|#FF0067C0|#FF60CDFF|square|";

        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Value, ElementTheme.Dark, out var darkSvg));

        Assert.AreEqual("#0067C0", GetBackgroundFill(lightSvg));
        Assert.AreEqual("#60CDFF", GetBackgroundFill(darkSvg));
        Assert.AreEqual(ElementTheme.Light, GeneratedIconProtocol.GetCacheTheme(Value, ElementTheme.Light));
        Assert.AreEqual(ElementTheme.Dark, GeneratedIconProtocol.GetCacheTheme(Value, ElementTheme.Dark));
        Assert.AreEqual(ElementTheme.Light, GeneratedIconProtocol.GetCacheTheme(Value, ElementTheme.Default));
    }

    [TestMethod]
    public void SingleColorSwatchSharesCacheIdentityAcrossThemes()
    {
        const string Value = "|Swatch|#0067C0|";

        Assert.AreEqual(ElementTheme.Default, GeneratedIconProtocol.GetCacheTheme(Value, ElementTheme.Light));
        Assert.AreEqual(ElementTheme.Default, GeneratedIconProtocol.GetCacheTheme(Value, ElementTheme.Dark));
    }

    [DataTestMethod]
    [DataRow("danger", "#C42B1C", "#FF99A4", true, null)]
    [DataRow("subtle", "#616161", "#C5C5C5", true, null)]
    [DataRow("info", "#0067C0", "#60CDFF", true, null)]
    [DataRow("warning", "#9D5D00", "#FCE100", true, null)]
    [DataRow("success", "#0F7B0F", "#6CCB5F", true, null)]
    [DataRow("neutral", "#8A8A8A", "#9D9D9D", true, null)]
    [DataRow("dark", "#1B1A19", "#1B1A19", false, null)]
    [DataRow("normal", "#000000", "#FFFFFF", true, null)]
    [DataRow("transparent", "#000000", "#000000", false, "0")]
    public void SwatchSupportsSemanticColors(
        string semanticColor,
        string expectedLight,
        string expectedDark,
        bool isThemeDependent,
        string? expectedOpacity)
    {
        var value = $"|Swatch|{semanticColor}|square|";

        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var darkSvg));

        Assert.AreEqual(expectedLight, GetBackgroundFill(lightSvg));
        Assert.AreEqual(expectedDark, GetBackgroundFill(darkSvg));
        Assert.AreEqual(expectedOpacity, GetBackgroundOpacity(lightSvg));
        Assert.AreEqual(expectedOpacity, GetBackgroundOpacity(darkSvg));
        Assert.IsNotNull(ParseSvg(lightSvg).Element(SvgName("rect")));
        Assert.AreEqual(
            isThemeDependent ? ElementTheme.Light : ElementTheme.Default,
            GeneratedIconProtocol.GetCacheTheme(value, ElementTheme.Light));
        Assert.AreEqual(
            isThemeDependent ? ElementTheme.Dark : ElementTheme.Default,
            GeneratedIconProtocol.GetCacheTheme(value, ElementTheme.Dark));
    }

    [TestMethod]
    public void InitialsSupportsNormalAndTransparentSemanticBackgrounds()
    {
        const string Normal = "|Initials|N|normal|circle|";
        const string Transparent = "|Initials|T|transparent|square|";

        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Normal, ElementTheme.Light, out var normalLight));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Normal, ElementTheme.Dark, out var normalDark));
        Assert.AreEqual("#000000", GetBackgroundFill(normalLight));
        Assert.AreEqual("#FFFFFF", GetBackgroundFill(normalDark));
        Assert.AreEqual("#FFFFFF", GetForegroundFill(normalLight));
        Assert.AreEqual("#000000", GetForegroundFill(normalDark));

        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Transparent, ElementTheme.Light, out var transparentLight));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Transparent, ElementTheme.Dark, out var transparentDark));
        Assert.AreEqual("0", GetBackgroundOpacity(transparentLight));
        Assert.AreEqual("0", GetBackgroundOpacity(transparentDark));
        Assert.AreEqual("#000000", GetForegroundFill(transparentLight));
        Assert.AreEqual("#FFFFFF", GetForegroundFill(transparentDark));
    }

    [TestMethod]
    public void TranslucentInitialsUsesThemeForContrastAndCacheIdentity()
    {
        const string Value = "|Initials|AB|#80000000|rounded|";

        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg(Value, ElementTheme.Dark, out var darkSvg));

        Assert.AreEqual("#000000", GetForegroundFill(lightSvg));
        Assert.AreEqual("#FFFFFF", GetForegroundFill(darkSvg));
        Assert.AreEqual(ElementTheme.Light, GeneratedIconProtocol.GetCacheTheme(Value, ElementTheme.Light));
        Assert.AreEqual(ElementTheme.Dark, GeneratedIconProtocol.GetCacheTheme(Value, ElementTheme.Dark));
    }

    [TestMethod]
    public void InitialsSupportsCircleSquareAndVectorGlyphs()
    {
        Assert.IsTrue(
            GeneratedIconProtocol.TryCreateSvg(
                "|Initials|a|#FFFFFFFF|circle|",
                ElementTheme.Light,
                out var circleSvg));
        Assert.IsTrue(
            GeneratedIconProtocol.TryCreateSvg(
                "|Initials|CP|#FF005FB8|#FF60CDFF|square|",
                ElementTheme.Dark,
                out var squareSvg));

        var circle = ParseSvg(circleSvg);
        Assert.IsNotNull(circle.Element(SvgName("circle")));
        Assert.IsFalse(string.IsNullOrEmpty(circle.Element(SvgName("path"))?.Attribute("d")?.Value));
        Assert.AreEqual("#000000", circle.Element(SvgName("path"))?.Attribute("fill")?.Value);

        var square = ParseSvg(squareSvg);
        Assert.IsNotNull(square.Element(SvgName("rect")));
        Assert.AreEqual("#60CDFF", square.Element(SvgName("rect"))?.Attribute("fill")?.Value);
        Assert.IsFalse(string.IsNullOrEmpty(square.Element(SvgName("path"))?.Attribute("d")?.Value));
    }

    [TestMethod]
    public void SwatchAndInitialsShareCircleAndSquareBackgroundGeometry()
    {
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg("|Swatch|#0067C0|", ElementTheme.Light, out var circleSwatch));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg("|Initials|A|#0067C0|", ElementTheme.Light, out var circleInitials));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg("|Swatch|#0067C0|square|", ElementTheme.Light, out var squareSwatch));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg("|Initials|A|#0067C0|square|", ElementTheme.Light, out var squareInitials));

        Assert.AreEqual(GetBackgroundGeometry(circleSwatch), GetBackgroundGeometry(circleInitials));
        Assert.AreEqual(GetBackgroundGeometry(squareSwatch), GetBackgroundGeometry(squareInitials));
        Assert.AreNotEqual(GetBackgroundGeometry(circleSwatch), GetBackgroundGeometry(squareSwatch));
    }

    [TestMethod]
    public void RoundedInitialsShapeRemainsAnAliasForSquare()
    {
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg("|Initials|A|#0067C0|rounded|", ElementTheme.Light, out var rounded));
        Assert.IsTrue(GeneratedIconProtocol.TryCreateSvg("|Initials|A|#0067C0|square|", ElementTheme.Light, out var square));

        Assert.AreEqual(GetBackgroundGeometry(square), GetBackgroundGeometry(rounded));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("|Swatch|")]
    [DataRow("|Swatch|red|")]
    [DataRow("|Swatch|#12345|")]
    [DataRow("|Swatch|#123456|triangle|")]
    [DataRow("|Swatch|#123456|#654321|#ABCDEF|")]
    [DataRow("|swatch|#123456|")]
    [DataRow("|Initials||#123456|")]
    [DataRow("|Initials|TOOLONG|#123456|")]
    [DataRow("|Initials|Æ|#123456|")]
    [DataRow("|Initials|A B|#123456|")]
    [DataRow("|Initials|AB|#123456|triangle|")]
    [DataRow("|Initials|AB|unknown|circle|")]
    [DataRow("|Initials|AB|#123456|#654321|rounded|extra|")]
    public void InvalidProtocolIsRejected(string? value)
    {
        Assert.IsFalse(GeneratedIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var svg));
        Assert.AreEqual(0, svg.Length);
    }

    private static XElement ParseSvg(byte[] svg) => XDocument.Parse(Encoding.UTF8.GetString(svg)).Root!;

    private static string? GetBackgroundFill(byte[] svg)
    {
        var root = ParseSvg(svg);
        return (root.Element(SvgName("circle")) ?? root.Element(SvgName("rect")))?.Attribute("fill")?.Value;
    }

    private static string? GetBackgroundOpacity(byte[] svg)
    {
        var root = ParseSvg(svg);
        return (root.Element(SvgName("circle")) ?? root.Element(SvgName("rect")))?.Attribute("fill-opacity")?.Value;
    }

    private static string? GetForegroundFill(byte[] svg) =>
        ParseSvg(svg).Element(SvgName("path"))?.Attribute("fill")?.Value;

    private static string GetBackgroundGeometry(byte[] svg)
    {
        var root = ParseSvg(svg);
        var background = root.Element(SvgName("circle")) ?? root.Element(SvgName("rect"));
        Assert.IsNotNull(background);

        var geometry = background.Name.LocalName;
        foreach (var attribute in background.Attributes())
        {
            if (attribute.Name.LocalName is not "fill" and not "fill-opacity")
            {
                geometry += $"|{attribute.Name.LocalName}={attribute.Value}";
            }
        }

        return geometry;
    }

    private static XName SvgName(string localName) => XName.Get(localName, "http://www.w3.org/2000/svg");
}
