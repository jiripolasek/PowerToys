// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CmdPal.Ext.Apps.Programs;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.Apps.UnitTests;

[TestClass]
public class AllAppsPageTests : AppsTestBase
{
    [TestMethod]
    public void AllAppsPage_Constructor_ThrowsOnNullAppCache()
    {
        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => new AllAppsPage(null!));
    }

    [TestMethod]
    public void AllAppsPage_WithMockCache_InitializesSuccessfully()
    {
        // Arrange
        var mockCache = new MockAppCache();

        // Act
        var page = new AllAppsPage(mockCache);

        // Assert
        Assert.IsNotNull(page);
        Assert.IsNotNull(page.Name);
        Assert.IsNotNull(page.Icon);
    }

    [TestMethod]
    public async Task AllAppsPage_GetItems_ReturnsEmptyWithEmptyCache()
    {
        // Act - Wait for initialization to complete
        await WaitForPageInitializationAsync();
        var items = Page.GetItems();

        // Assert
        Assert.IsNotNull(items);
        Assert.AreEqual(0, items.Length);
    }

    [TestMethod]
    public async Task AllAppsPage_GetItems_ReturnsAppsFromCacheAsync()
    {
        // Arrange
        var mockCache = new MockAppCache();
        var win32App = TestDataHelper.CreateTestWin32Program("Notepad", "C:\\Windows\\System32\\notepad.exe");
        var uwpApp = TestDataHelper.CreateTestUWPApplication("Calculator");

        mockCache.AddWin32Program(win32App);
        mockCache.AddUWPApplication(uwpApp);

        var page = new AllAppsPage(mockCache);

        // Wait a bit for initialization to complete
        await Task.Delay(100);

        // Act
        var items = page.GetItems();

        // Assert
        Assert.IsNotNull(items);
        Assert.AreEqual(2, items.Length);

        // we need to loop the items to ensure we got the correct ones
        Assert.IsTrue(items.Any(i => i.Title == "Notepad"));
        Assert.IsTrue(items.Any(i => i.Title == "Calculator"));
    }

    [TestMethod]
    public async Task AllAppsPage_GetItems_HidesSubtitlesWhenSettingEnabled()
    {
        // Arrange
        var mockCache = new MockAppCache();
        var win32App = TestDataHelper.CreateTestWin32Program("Notepad", "C:\\Windows\\System32\\notepad.exe");
        mockCache.AddWin32Program(win32App);

        try
        {
            AllAppsSettings.Instance.Settings.Update("{\"apps.HideAppDescriptions\": \"true\"}");

            var page = new AllAppsPage(mockCache);
            await Task.Delay(100);

            // Act
            var items = page.GetItems();

            // Assert
            Assert.AreEqual(1, items.Length);
            var appItem = items.OfType<AppListItem>().Single();
            Assert.AreEqual(string.Empty, appItem.Subtitle);
        }
        finally
        {
            AllAppsSettings.Instance.Settings.Update("{\"apps.HideAppDescriptions\": \"false\"}");
        }
    }

    [TestMethod]
    public async Task AllAppsPage_GetItems_ShowsSubtitlesWhenSettingDisabled()
    {
        // Arrange
        var mockCache = new MockAppCache();
        var win32App = TestDataHelper.CreateTestWin32Program("Notepad", "C:\\Windows\\System32\\notepad.exe");
        mockCache.AddWin32Program(win32App);

        try
        {
            AllAppsSettings.Instance.Settings.Update("{\"apps.HideAppDescriptions\": \"false\"}");

            var page = new AllAppsPage(mockCache);
            await Task.Delay(100);

            // Act
            var items = page.GetItems();

            // Assert
            Assert.AreEqual(1, items.Length);
            var appItem = items.OfType<AppListItem>().Single();
            Assert.IsFalse(string.IsNullOrEmpty(appItem.Subtitle));
        }
        finally
        {
            AllAppsSettings.Instance.Settings.Update("{\"apps.HideAppDescriptions\": \"false\"}");
        }
    }

    [TestMethod]
    public void AppListItem_DefersWin32RowAndHeroIconsToUiLoader()
    {
        var app = new AppItem
        {
            Name = "Test App",
            IcoPath = "C:\\Windows\\System32\\shell32.dll,1",
            ExePath = "C:\\Program Files\\Example\\app.exe",
        };

        var item = new AppListItem(app, useThumbnails: true);

        var rowIcon = (IconInfo)item.Icon!;
        Assert.AreEqual("|AppIcon|C:\\Windows\\System32\\shell32.dll,1", rowIcon.Light.Icon);
        Assert.AreSame(rowIcon, item.Command.Icon);

        var details = (Details)item.Details!;
        var heroIcon = (IconInfo)details.HeroImage;
        Assert.AreEqual("|JumboAppIcon|C:\\Windows\\System32\\shell32.dll,1", heroIcon.Light.Icon);
    }

    [TestMethod]
    public void AppListItem_KeepsPackagedIconAssetsAsDirectPaths()
    {
        var app = new AppItem
        {
            Name = "Test Packaged App",
            IcoPath = "C:\\Program Files\\WindowsApps\\Example\\small.png",
            JumboIconPath = "C:\\Program Files\\WindowsApps\\Example\\large.png",
            IsPackaged = true,
        };

        var item = new AppListItem(app, useThumbnails: true);

        var rowIcon = (IconInfo)item.Icon!;
        Assert.AreEqual(app.IcoPath, rowIcon.Light.Icon);

        var details = (Details)item.Details!;
        var heroIcon = (IconInfo)details.HeroImage;
        Assert.AreEqual(app.JumboIconPath, heroIcon.Light.Icon);
    }
}
