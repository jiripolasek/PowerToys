// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.UI.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public class AppIconProtocolTests
{
    [DataTestMethod]
    [DataRow("|AppIcon|C:\\Windows\\System32\\shell32.dll,1", "C:\\Windows\\System32\\shell32.dll,1", false)]
    [DataRow("|JumboAppIcon|C:\\Program Files\\Example\\app.exe", "C:\\Program Files\\Example\\app.exe", true)]
    public void ValidProtocolPreservesPath(string value, string expectedPath, bool expectedJumbo)
    {
        Assert.IsTrue(AppIconProtocol.IsProtocol(value));
        var parsed = AppIconProtocol.TryParse(value, out var path, out var jumbo);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expectedPath, path);
        Assert.AreEqual(expectedJumbo, jumbo);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("|AppIcon|")]
    [DataRow("C:\\Windows\\System32\\shell32.dll,1")]
    [DataRow("|appicon|C:\\Windows\\System32\\shell32.dll,1")]
    public void InvalidProtocolIsRejected(string? value)
    {
        var parsed = AppIconProtocol.TryParse(value, out var path, out var jumbo);

        Assert.IsFalse(parsed);
        Assert.AreEqual(string.Empty, path);
        Assert.IsFalse(jumbo);
    }

    [DataTestMethod]
    [DataRow("|AppIcon|")]
    [DataRow("|JumboAppIcon|")]
    public void EmptyPayloadIsStillClaimedByProtocol(string value)
    {
        Assert.IsTrue(AppIconProtocol.IsProtocol(value));
        Assert.IsFalse(AppIconProtocol.TryParse(value, out _, out _));
    }
}
