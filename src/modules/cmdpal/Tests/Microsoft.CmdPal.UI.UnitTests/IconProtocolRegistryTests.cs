// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.UI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public class IconProtocolRegistryTests
{
    [DataTestMethod]
    [DataRow("|AppIcon|C:\\Windows\\notepad.exe")]
    [DataRow("|JumboAppIcon|C:\\Windows\\notepad.exe")]
    public void BuiltInRegistryFindsAppIconProcessor(string value)
    {
        var processor = IconProtocolRegistry.Find(value);

        Assert.IsNotNull(processor);
        Assert.AreSame(AppIconProtocolProcessor.Instance, processor);
        Assert.AreEqual(IconCachePartition.Other, processor.CachePartition);
        Assert.AreEqual(IconLoadInputKind.SpecializedAppIcon, processor.ClassifyInput(value));
        Assert.AreEqual(ElementTheme.Default, processor.GetCacheTheme(value, ElementTheme.Dark));
        Assert.IsFalse(processor.TryPrepareSynchronously(
            value,
            20,
            ElementTheme.Dark,
            out var preparedIcon));
        Assert.IsNull(preparedIcon);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("\uE700")]
    [DataRow("C:\\Icons\\sample.svg")]
    [DataRow("|Unknown|value")]
    public void UnknownInputsDoNotEnterTheBuiltInRegistry(string? value)
    {
        Assert.IsNull(IconProtocolRegistry.Find(value));
    }

    [TestMethod]
    public void OrdinaryInputsSkipProcessorMatching()
    {
        var processor = new TestProcessor("|Test|");

        var result = IconProtocolRegistry.Find("ordinary.png", [processor]);

        Assert.IsNull(result);
        Assert.AreEqual(0, processor.MatchAttempts);
    }

    [TestMethod]
    public void RegistryReturnsFirstMatchingProcessor()
    {
        var first = new TestProcessor("|Other|");
        var matching = new TestProcessor("|Test|");
        var later = new TestProcessor("|Test|");

        var result = IconProtocolRegistry.Find("|Test|value", [first, matching, later]);

        Assert.AreSame(matching, result);
        Assert.AreEqual(1, first.MatchAttempts);
        Assert.AreEqual(1, matching.MatchAttempts);
        Assert.AreEqual(0, later.MatchAttempts);
    }

    [TestMethod]
    public void ProcessingResultCanTransferPreparedIconOwnershipOnce()
    {
        var prepared = IconPathConverter.PreparedIcon.FromGlyph("\uE700", "Segoe Fluent Icons", 20);
        using var result = IconProtocolProcessingResult.FromPreparedIcon(prepared);

        var transferred = result.TakePreparedIcon();

        Assert.IsNotNull(transferred);
        Assert.AreSame(prepared, transferred);
        Assert.IsNull(result.TakePreparedIcon());
        transferred.Dispose();
    }

    private sealed class TestProcessor(string prefix) : IIconProtocolProcessor
    {
        public int MatchAttempts { get; private set; }

        public IconCachePartition CachePartition => IconCachePartition.Other;

        public bool Matches(string value)
        {
            MatchAttempts++;
            return value.StartsWith(prefix, StringComparison.Ordinal);
        }

        public ElementTheme GetCacheTheme(string value, ElementTheme theme) => ElementTheme.Default;

        public IconLoadInputKind ClassifyInput(string value) => IconLoadInputKind.String;

        public bool TryPrepareSynchronously(
            string value,
            int targetSize,
            ElementTheme theme,
            out IconPathConverter.PreparedIcon preparedIcon)
        {
            preparedIcon = IconPathConverter.PreparedIcon.Empty();
            return true;
        }

        public ValueTask<IconProtocolProcessingResult> PrepareAsync(
            string value,
            int targetSize,
            ElementTheme theme) =>
            ValueTask.FromResult(IconProtocolProcessingResult.Empty());
    }
}
