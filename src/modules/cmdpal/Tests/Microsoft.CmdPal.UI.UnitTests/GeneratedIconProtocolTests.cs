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
        Assert.AreEqual("12", shape.Attribute("r")?.Value);
    }

    [TestMethod]
    public void ThemeAwareSwatchSelectsThemeColorAndUsesThemeInCacheIdentity()
    {
        const string Value = "|Swatch|#FF0067C0|#FF60CDFF|";

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
    public void InitialsSupportsCircleRoundedSquareAndVectorGlyphs()
    {
        Assert.IsTrue(
            GeneratedIconProtocol.TryCreateSvg(
                "|Initials|a|#FFFFFFFF|circle|",
                ElementTheme.Light,
                out var circleSvg));
        Assert.IsTrue(
            GeneratedIconProtocol.TryCreateSvg(
                "|Initials|CP|#FF005FB8|#FF60CDFF|rounded|",
                ElementTheme.Dark,
                out var roundedSvg));

        var circle = ParseSvg(circleSvg);
        Assert.IsNotNull(circle.Element(SvgName("circle")));
        Assert.IsFalse(string.IsNullOrEmpty(circle.Element(SvgName("path"))?.Attribute("d")?.Value));
        Assert.AreEqual("#000000", circle.Element(SvgName("path"))?.Attribute("fill")?.Value);

        var rounded = ParseSvg(roundedSvg);
        Assert.IsNotNull(rounded.Element(SvgName("rect")));
        Assert.AreEqual("#60CDFF", rounded.Element(SvgName("rect"))?.Attribute("fill")?.Value);
        Assert.IsFalse(string.IsNullOrEmpty(rounded.Element(SvgName("path"))?.Attribute("d")?.Value));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("|Swatch|")]
    [DataRow("|Swatch|red|")]
    [DataRow("|Swatch|#12345|")]
    [DataRow("|Swatch|#123456|#654321|#ABCDEF|")]
    [DataRow("|swatch|#123456|")]
    [DataRow("|Initials||#123456|")]
    [DataRow("|Initials|TOOLONG|#123456|")]
    [DataRow("|Initials|Æ|#123456|")]
    [DataRow("|Initials|A B|#123456|")]
    [DataRow("|Initials|AB|#123456|triangle|")]
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

    private static string? GetForegroundFill(byte[] svg) =>
        ParseSvg(svg).Element(SvgName("path"))?.Attribute("fill")?.Value;

    private static XName SvgName(string localName) => XName.Get(localName, "http://www.w3.org/2000/svg");
}
