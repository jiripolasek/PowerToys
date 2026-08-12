// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;
using Microsoft.CmdPal.UI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public class SvgIconProtocolTests
{
    private const string Template = """
        <svg xmlns="http://www.w3.org/2000/svg">
          <path id="theme" fill="{{ThemeColor}}" />
          <path id="accent" fill="{{AccentColor}}" />
        </svg>
        """;

    private const string CurrentColorTemplate = """
        <svg xmlns="http://www.w3.org/2000/svg" color="{{ThemeColor}}">
          <path id="base" fill="currentColor" />
          <path id="overlay" fill="{{AccentColor}}" />
        </svg>
        """;

    [TestMethod]
    public void PlainInlineSvgIsNotTransformed()
    {
        var value = $"|Svg|{Template}";

        Assert.AreEqual(SvgIconProtocol.Kind.PlainInline, SvgIconProtocol.Classify(value));
        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var darkSvg));

        var expected = Encoding.UTF8.GetBytes(Template);
        CollectionAssert.AreEqual(expected, lightSvg);
        CollectionAssert.AreEqual(expected, darkSvg);
    }

    [TestMethod]
    public void PlainSvgFilePreservesOriginalBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"CmdPal-{Guid.NewGuid():N}.svg");
        try
        {
            var template = $"<?xml version=\"1.0\" encoding=\"utf-16\"?>{Template}";
            var content = Encoding.Unicode.GetBytes(template);
            var preamble = Encoding.Unicode.GetPreamble();
            var original = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, original, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, original, preamble.Length, content.Length);
            File.WriteAllBytes(path, original);

            var value = $"|Svg|{path}";
            Assert.AreEqual(SvgIconProtocol.Kind.PlainFile, SvgIconProtocol.Classify(value));
            Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var svg));

            CollectionAssert.AreEqual(original, svg);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ThemedInlineSvgReplacesThemeAndDefaultInfoAccent()
    {
        var value = $"|ThemedSvg|{Template}";

        Assert.AreEqual(SvgIconProtocol.Kind.ThemedInline, SvgIconProtocol.Classify(value));
        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var darkSvg));

        var light = Encoding.UTF8.GetString(lightSvg);
        var dark = Encoding.UTF8.GetString(darkSvg);
        StringAssert.Contains(light, "id=\"theme\" fill=\"#000000\"");
        StringAssert.Contains(dark, "id=\"theme\" fill=\"#FFFFFF\"");
        StringAssert.Contains(light, "id=\"accent\" fill=\"#0067C0\"");
        StringAssert.Contains(dark, "id=\"accent\" fill=\"#60CDFF\"");
        Assert.IsFalse(light.Contains("{{", StringComparison.Ordinal));
        Assert.IsFalse(dark.Contains("{{", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ThemedSvgCanSetInheritedCurrentColorWithoutRewritingKeyword()
    {
        var value = $"|ThemedSvg|success|{CurrentColorTemplate}";

        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var darkSvg));

        var light = Encoding.UTF8.GetString(lightSvg);
        var dark = Encoding.UTF8.GetString(darkSvg);
        StringAssert.Contains(light, "color=\"#000000\"");
        StringAssert.Contains(dark, "color=\"#FFFFFF\"");
        StringAssert.Contains(light, "id=\"base\" fill=\"currentColor\"");
        StringAssert.Contains(dark, "id=\"base\" fill=\"currentColor\"");
        StringAssert.Contains(light, "id=\"overlay\" fill=\"#0F7B0F\"");
        StringAssert.Contains(dark, "id=\"overlay\" fill=\"#6CCB5F\"");
    }

    [DataTestMethod]
    [DataRow("danger", "#C42B1C", "#FF99A4")]
    [DataRow("subtle", "#616161", "#C5C5C5")]
    [DataRow("info", "#0067C0", "#60CDFF")]
    [DataRow("warning", "#9D5D00", "#FCE100")]
    [DataRow("success", "#0F7B0F", "#6CCB5F")]
    [DataRow("neutral", "#8A8A8A", "#9D9D9D")]
    [DataRow("dark", "#1B1A19", "#1B1A19")]
    [DataRow("normal", "#000000", "#FFFFFF")]
    [DataRow("transparent", "#00000000", "#00000000")]
    public void SemanticAccentUsesLightAndDarkPalette(
        string semanticAccent,
        string expectedLight,
        string expectedDark)
    {
        var value = $"|ThemedSvg|{semanticAccent}|{Template}";

        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var darkSvg));

        StringAssert.Contains(Encoding.UTF8.GetString(lightSvg), $"id=\"accent\" fill=\"{expectedLight}\"");
        StringAssert.Contains(Encoding.UTF8.GetString(darkSvg), $"id=\"accent\" fill=\"{expectedDark}\"");
    }

    [DataTestMethod]
    [DataRow("#A4C")]
    [DataRow("#A4C8")]
    [DataRow("#7A3E9D")]
    [DataRow("#7A3E9DCC")]
    public void CustomSvgHexAccentIsUsedVerbatim(string customAccent)
    {
        var value = $"|ThemedSvg|{customAccent}|{Template}";

        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var lightSvg));
        Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var darkSvg));

        StringAssert.Contains(Encoding.UTF8.GetString(lightSvg), $"id=\"accent\" fill=\"{customAccent}\"");
        StringAssert.Contains(Encoding.UTF8.GetString(darkSvg), $"id=\"accent\" fill=\"{customAccent}\"");
    }

    [TestMethod]
    public void ThemedSvgFileIsReadAndResolvedAsUtf8()
    {
        var path = Path.Combine(Path.GetTempPath(), $"CmdPal-{Guid.NewGuid():N}.svg");
        try
        {
            var template = $"<?xml version=\"1.0\" encoding=\"utf-16\"?>{Template}";
            File.WriteAllText(path, template, Encoding.Unicode);

            var value = $"|ThemedSvg|success|{path}";
            Assert.AreEqual(SvgIconProtocol.Kind.ThemedFile, SvgIconProtocol.Classify(value));
            Assert.IsTrue(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Dark, out var svg));

            var resolved = Encoding.UTF8.GetString(svg);
            Assert.IsFalse(resolved.Contains("<?xml", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(resolved, "id=\"theme\" fill=\"#FFFFFF\"");
            StringAssert.Contains(resolved, "id=\"accent\" fill=\"#6CCB5F\"");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [DataTestMethod]
    [DataRow("|Svg|C:\\Icons\\plain.svg", "PlainFile")]
    [DataRow("|Svg|<svg />", "PlainInline")]
    [DataRow("|ThemedSvg|C:\\Icons\\themed.svg", "ThemedFile")]
    [DataRow("|ThemedSvg|<svg />", "ThemedInline")]
    [DataRow("|ThemedSvg|warning|C:\\Icons\\themed.svg", "ThemedFile")]
    [DataRow("|ThemedSvg|#7A3E9D|<svg />", "ThemedInline")]
    public void SvgProtocolClassifiesContractAndPayload(string value, string expected) =>
        Assert.AreEqual(expected, SvgIconProtocol.Classify(value).ToString());

    [TestMethod]
    public void OnlyThemedSvgUsesThemeInCacheIdentity()
    {
        var plain = $"|Svg|{Template}";
        var themed = $"|ThemedSvg|danger|{Template}";

        Assert.AreEqual(ElementTheme.Default, SvgIconProtocol.GetCacheTheme(plain, ElementTheme.Light));
        Assert.AreEqual(ElementTheme.Default, SvgIconProtocol.GetCacheTheme(plain, ElementTheme.Dark));
        Assert.AreEqual(ElementTheme.Light, SvgIconProtocol.GetCacheTheme(themed, ElementTheme.Default));
        Assert.AreEqual(ElementTheme.Light, SvgIconProtocol.GetCacheTheme(themed, ElementTheme.Light));
        Assert.AreEqual(ElementTheme.Dark, SvgIconProtocol.GetCacheTheme(themed, ElementTheme.Dark));
        Assert.AreEqual(ElementTheme.Default, SvgIconProtocol.GetCacheTheme("ordinary.svg", ElementTheme.Dark));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("|svg|<svg />")]
    [DataRow("|Svg|")]
    [DataRow("|Svg|not-an-svg-file.txt")]
    [DataRow("|Svg|Z:\\this-file-should-not-exist\\icon.svg")]
    [DataRow("|ThemedSvg|")]
    [DataRow("|ThemedSvg|unknown|<svg />")]
    [DataRow("|ThemedSvg|#12|<svg />")]
    [DataRow("|ThemedSvg|not-an-svg-file.txt")]
    public void InvalidSvgProtocolIsRejected(string? value)
    {
        Assert.IsFalse(SvgIconProtocol.TryCreateSvg(value, ElementTheme.Light, out var svg));
        Assert.AreEqual(0, svg.Length);
    }
}
