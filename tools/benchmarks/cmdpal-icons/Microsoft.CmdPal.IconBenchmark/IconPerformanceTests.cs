// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.PowerToys.UITest.Next;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.IconBenchmark;

[TestClass]
public class IconPerformanceTests
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;
    private const string ProcessName = "Microsoft.CmdPal.UI";
    private const string ScenarioEnvironmentVariable = "CMDPAL_ICON_BENCHMARK_SCENARIO";
    private const string StartDiagnosticsAutomationId = "CmdPal_InternalPage_StartIconDiagnostics";
    private const string StopDiagnosticsAutomationId = "CmdPal_InternalPage_StopIconDiagnostics";
    private const string CopyDiagnosticsAutomationId = "CmdPal_InternalPage_CopyIconDiagnostics_1";
    private const int DiagnosticsReportGenerationTimeoutMilliseconds = 120_000;
    private const int DiagnosticsClipboardTimeoutMilliseconds = 120_000;
    private const int DiagnosticsClipboardRetryMilliseconds = 10_000;

    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestCategory("ManualPerformance")]
    [TestCategory("winappcli")]
    [Timeout(15 * 60 * 1000)]
    public void CaptureIconEvidence()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScenarioEnvironmentVariable)))
        {
            Assert.Inconclusive($"Set {ScenarioEnvironmentVariable} to A or B to run this opt-in benchmark.");
        }

        Assert.IsTrue(WinappCli.IsAvailable(), WinappCli.InstallHint);
        var options = IconBenchmarkOptions.FromEnvironment();

        try
        {
            RunBenchmark(options);
        }
        finally
        {
            Step("Closing the benchmark CmdPal process");
            WindowControl.TryCloseByApp(ProcessName);
            if (!WaitForProcessState(expectedRunning: false, timeoutMilliseconds: 5_000))
            {
                WindowControl.TryKillProcessByName(ProcessName);
                WaitForProcessState(expectedRunning: false, timeoutMilliseconds: 5_000);
            }
        }
    }

    private void RunBenchmark(IconBenchmarkOptions options)
    {
        Step("Removing any previously running CmdPal instance");
        WindowControl.TryCloseByApp(ProcessName);
        if (!WaitForProcessState(expectedRunning: false, timeoutMilliseconds: 5_000))
        {
            WindowControl.TryKillProcessByName(ProcessName);
        }

        Assert.IsTrue(
            WaitForProcessState(expectedRunning: false, timeoutMilliseconds: 5_000),
            "A previously running CmdPal process did not exit before the benchmark.");

        Step($"Launching Release CmdPal from '{options.ProductPath}'");
        LaunchOrActivate(options.ProductPath);

        Step("Waiting for the CmdPal main window");
        var mainWindow = WaitForMainWindow(timeoutMilliseconds: 60_000);

        WindowHelper.MaximizeWindow(new IntPtr(mainWindow.WindowHandle));
        Assert.IsTrue(
            WaitForForegroundWithAltFallback(mainWindow),
            "The CmdPal main window could not be brought to the foreground.");

        Step("Waiting for the main command surface to be ready");
        mainWindow.Find<Button>(By.AccessibilityId("SettingsIconButton"), 30_000);

        mainWindow = StartDiagnostics(mainWindow, options.ProductPath);

        for (var pageIndex = 0; pageIndex < options.Pages.Count; pageIndex++)
        {
            OpenPage(mainWindow, options.Pages[pageIndex], options.NavigationDelayMilliseconds);

            if (options.UsesKeyboard)
            {
                RunKeyboardWorkload(
                    mainWindow,
                    options.Pages[pageIndex],
                    options.KeyboardWrapCounts[pageIndex],
                    options.KeyboardTraversalTimeoutMilliseconds[pageIndex],
                    options.KeyboardTapDelayMilliseconds,
                    options.KeyboardCoarseProbeInterval,
                    options.KeyboardFineProbeInterval);
            }
            else
            {
                RunMouseWorkload(
                    mainWindow,
                    options.FastScrollCounts[pageIndex],
                    options.SlowScrollCounts[pageIndex],
                    options.FastScrollDelayMilliseconds,
                    options.SlowScrollDelayMilliseconds);
            }

            Step($"Allowing '{options.Pages[pageIndex]}' to settle for {options.SettleMilliseconds} ms");
            Thread.Sleep(options.SettleMilliseconds);
            if (pageIndex + 1 < options.Pages.Count)
            {
                Step("Returning to the CmdPal home page");
                KeyboardHelper.SendKey(Key.Esc);
                Thread.Sleep(options.NavigationDelayMilliseconds);
            }
        }

        var report = StopDiagnosticsAndCopyReport(mainWindow);
        var outputDirectory = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        Step($"Writing the diagnostics report to '{options.OutputPath}'");
        File.WriteAllText(options.OutputPath, report);
        TestContext.AddResultFile(options.OutputPath);
    }

    private static bool WaitForForegroundWithAltFallback(Session mainWindow)
    {
        var windowHandle = new IntPtr(mainWindow.WindowHandle);
        if (WindowControl.WaitForForeground(windowHandle, timeoutMS: 5_000, requiredConsecutiveMatches: 2))
        {
            return true;
        }

        // An injected ALT press grants the calling input queue the same foreground-activation
        // opportunity as an interactive ALT gesture. This is only a setup fallback; diagnostics
        // have not started and no benchmark input has been sent yet.
        KeyboardHelper.PressKey(Key.Alt);
        try
        {
            WindowControl.TryBringToForeground(windowHandle);
        }
        finally
        {
            KeyboardHelper.ReleaseKey(Key.Alt);
        }

        if (WindowControl.WaitForForeground(windowHandle, timeoutMS: 10_000, requiredConsecutiveMatches: 2))
        {
            return true;
        }

        // As a final fallback, expose the target above the current foreground window and perform
        // a genuine title-bar click. This avoids clicking any command content and is still entirely
        // before diagnostics or benchmark input begin.
        var bounds = WindowHelper.GetWindowBounds(windowHandle);
        SetWindowPos(windowHandle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        try
        {
            MouseHelper.LeftClickAt((bounds.Left + bounds.Right) / 2, bounds.Top + 16);
        }
        finally
        {
            SetWindowPos(windowHandle, HwndNotTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        }

        return WindowControl.WaitForForeground(windowHandle, timeoutMS: 10_000, requiredConsecutiveMatches: 2);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private Session StartDiagnostics(Session mainWindow, string productPath)
    {
        var settingsWindow = OpenInternalTools(mainWindow);
        Step("Starting icon diagnostics");
        settingsWindow.Find<Button>(By.AccessibilityId(StartDiagnosticsAutomationId), 30_000).Invoke();
        CloseSettingsWindow(settingsWindow);

        // Opening Settings unloads the main window's XAML automation surface. Closing Settings leaves
        // that HWND alive but empty; activating the single-instance executable restores the surface.
        Step("Reactivating the CmdPal main surface after closing Settings");
        LaunchOrActivate(productPath);
        var restoredMainWindow = WaitForMainWindow(timeoutMilliseconds: 30_000);
        restoredMainWindow.Find<TextBox>(By.AccessibilityId("MainSearchBox"), 30_000);
        return restoredMainWindow;
    }

    private string StopDiagnosticsAndCopyReport(Session mainWindow)
    {
        var settingsWindow = OpenInternalTools(mainWindow);
        Step("Stopping icon diagnostics");
        // These actions delimit or follow the measured interval. InvokePattern avoids turning a
        // missed coordinate click into an apparent diagnostics-generation hang.
        settingsWindow.Find<Button>(By.AccessibilityId(StopDiagnosticsAutomationId), 30_000).Invoke();

        Step("Waiting for icon diagnostics session 1 report");
        var copyButton = settingsWindow.Find<Button>(
            By.AccessibilityId(CopyDiagnosticsAutomationId),
            DiagnosticsReportGenerationTimeoutMilliseconds);

        ClipboardHelper.Clear();
        Step("Copying icon diagnostics session 1");
        copyButton.Invoke();

        var deadline = Environment.TickCount64 + DiagnosticsClipboardTimeoutMilliseconds;
        var nextCopyAttempt = Environment.TickCount64 + DiagnosticsClipboardRetryMilliseconds;
        do
        {
            var report = ClipboardHelper.GetText();
            if (report.StartsWith("CmdPal icon diagnostics", StringComparison.Ordinal))
            {
                return report;
            }

            if (Environment.TickCount64 >= nextCopyAttempt)
            {
                Step("Retrying icon diagnostics clipboard copy");
                copyButton.Invoke();
                nextCopyAttempt = Environment.TickCount64 + DiagnosticsClipboardRetryMilliseconds;
            }

            Thread.Sleep(100);
        }
        while (Environment.TickCount64 < deadline);

        Assert.Fail("The icon diagnostics report was not available on the clipboard within 120 seconds.");
        return string.Empty;
    }

    private Session OpenInternalTools(Session mainWindow)
    {
        Step("Opening CmdPal settings with Ctrl+,");
        mainWindow.EnsureForeground();
        Assert.IsTrue(
            WindowControl.WaitForForeground(new IntPtr(mainWindow.WindowHandle), timeoutMS: 5_000, requiredConsecutiveMatches: 2),
            "The CmdPal main window was not foreground before opening Settings.");

        KeyboardHelper.PressKey(Key.Ctrl);
        try
        {
            KeyboardHelper.SendKey((Key)0xBC); // VK_OEM_COMMA
        }
        finally
        {
            KeyboardHelper.ReleaseKey(Key.Ctrl);
        }

        Step("Waiting for the CmdPal settings window");
        var settingsWindow = WindowsFinder.WaitForWindowByApp(
            ProcessName,
            window => window.Hwnd != mainWindow.WindowHandle && window.Width >= 480 && window.Height >= 480,
            timeoutMS: 30_000);
        Assert.IsNotNull(settingsWindow, "The CmdPal settings window did not appear within 30 seconds.");

        WindowHelper.MaximizeWindow(new IntPtr(settingsWindow!.WindowHandle));
        Step("Navigating to Internal Tools");
        settingsWindow.Find<NavigationViewItem>("Internal Tools", 30_000).Click(msPostAction: 500);
        return settingsWindow;
    }

    private void CloseSettingsWindow(Session settingsWindow)
    {
        Step("Closing CmdPal settings");
        Assert.IsTrue(
            WindowControl.TryCloseByApp(ProcessName, window => window.Hwnd == settingsWindow.WindowHandle),
            "The CmdPal settings window did not close.");
    }

    private void OpenPage(Session mainWindow, string pageName, int navigationDelayMilliseconds)
    {
        var resultName = pageName switch
        {
            "All apps" => "Search apps",
            "Segoe icons" => "Segoe Icons",
            _ => pageName,
        };
        var readySearchBoxName = pageName switch
        {
            "All apps" => "Search apps...",
            "Segoe icons" => "Type here to search...",
            _ => null,
        };

        Step($"Finding '{pageName}' through the CmdPal search box");
        mainWindow.EnsureForeground();
        Assert.IsTrue(
            WindowControl.WaitForForeground(new IntPtr(mainWindow.WindowHandle), timeoutMS: 5_000, requiredConsecutiveMatches: 2),
            $"The CmdPal main window was not foreground before opening '{pageName}'.");

        mainWindow.Find<TextBox>(By.AccessibilityId("MainSearchBox"), 30_000).SetText(pageName);

        Element? result = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        do
        {
            var matches = mainWindow.FindAll<Element>(By.Name(resultName), timeoutMS: 0)
                .Where(element =>
                    element.ControlType.Equals("ListItem", StringComparison.OrdinalIgnoreCase) &&
                    element.Name.Equals(resultName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            result = matches.FirstOrDefault(element => element.Width > 0 && element.Height > 0);
            if (result is not null)
            {
                break;
            }

            var offscreenResult = matches.FirstOrDefault();
            if (offscreenResult is not null)
            {
                offscreenResult.ScrollIntoView();
            }

            Thread.Sleep(200);
        }
        while (DateTime.UtcNow < deadline);

        Assert.IsNotNull(result, $"A visible '{resultName}' result did not appear within 30 seconds for query '{pageName}'.");

        Step($"Opening '{resultName}'");
        result!.DoubleClick(msPostAction: 0);
        if (readySearchBoxName is not null)
        {
            Assert.IsTrue(
                mainWindow.WaitFor(
                    () => mainWindow.Find<TextBox>(By.AccessibilityId("MainSearchBox"), 1_000).Name.Equals(readySearchBoxName, StringComparison.Ordinal),
                    timeoutMS: 10_000,
                    pollIntervalMS: 200),
                $"'{resultName}' did not reach its expected page state ('{readySearchBoxName}').");
        }

        Thread.Sleep(navigationDelayMilliseconds);
    }

    private void RunKeyboardWorkload(
        Session mainWindow,
        string pageName,
        int requiredWraps,
        int timeoutMilliseconds,
        int tapDelayMilliseconds,
        int coarseProbeInterval,
        int fineProbeInterval)
    {
        FocusMainWindowForInput(mainWindow);
        mainWindow.Find<TextBox>(By.AccessibilityId("MainSearchBox"), 30_000).Focus();
        var list = mainWindow.Find<Element>(By.AccessibilityId("ItemsList"), 30_000);
        var stopwatch = Stopwatch.StartNew();
        var wraps = 0;
        var taps = 0;
        var tapsAtLastWrap = 0;
        int? firstCycleLength = null;
        var lastScrollPercent = 0.0;
        int? nextKnownCycleProbeInterval = null;
        var waitingForTop = false;

        Step($"Traversing '{pageName}' until {requiredWraps} list wrap(s) are observed");
        while (wraps < requiredWraps && stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            var probeInterval = nextKnownCycleProbeInterval ??
                (waitingForTop || lastScrollPercent >= 90
                    ? fineProbeInterval
                    : lastScrollPercent >= 60
                        ? Math.Min(50, coarseProbeInterval)
                        : coarseProbeInterval);
            nextKnownCycleProbeInterval = null;
            for (var index = 0; index < probeInterval; index++)
            {
                KeyboardHelper.SendKey(Key.Down);
                taps++;
                if (tapDelayMilliseconds > 0)
                {
                    Thread.Sleep(tapDelayMilliseconds);
                }
            }

            var scrollPercent = ReadVerticalScrollPercent(list, pageName);
            Step($"'{pageName}' scroll is {scrollPercent:0.0}% after {taps} Down taps");
            if (!waitingForTop && scrollPercent >= 95)
            {
                waitingForTop = true;
                Step($"'{pageName}' reached the end after {taps} Down taps");
            }
            else if (waitingForTop && scrollPercent <= 20)
            {
                wraps++;
                waitingForTop = false;
                var cycleLength = taps - tapsAtLastWrap;
                tapsAtLastWrap = taps;
                if (firstCycleLength is null)
                {
                    firstCycleLength = cycleLength;
                }
                else
                {
                    var minimumExpectedCycleLength = firstCycleLength.Value * 3 / 4;
                    var maximumExpectedCycleLength = firstCycleLength.Value * 5 / 4;
                    Assert.IsTrue(
                        cycleLength >= minimumExpectedCycleLength && cycleLength <= maximumExpectedCycleLength,
                        $"'{pageName}' traversal {wraps} took {cycleLength} Down taps after the first traversal took " +
                        $"{firstCycleLength.Value}. The benchmark cannot claim equivalent full-page coverage.");
                }

                if (wraps < requiredWraps)
                {
                    // The first cycle includes startup/selection effects, so its tap count is only
                    // an estimate of the next cycle. Resume probes around 90% of that estimate;
                    // a fixed 50-tap margin can land just after a shorter subsequent cycle and miss
                    // the end-to-start transition entirely.
                    var safetyMargin = Math.Max(10 * fineProbeInterval, cycleLength / 10);
                    nextKnownCycleProbeInterval = Math.Max(
                        fineProbeInterval,
                        cycleLength - safetyMargin);
                }

                Step($"'{pageName}' wrapped to the start ({wraps}/{requiredWraps}) after {taps} Down taps");
            }

            lastScrollPercent = scrollPercent;
        }

        Assert.AreEqual(
            requiredWraps,
            wraps,
            $"'{pageName}' did not complete {requiredWraps} traversal(s) within {timeoutMilliseconds} ms " +
            $"({taps} Down taps sent). The benchmark cannot claim complete page coverage.");
    }

    private static double ReadVerticalScrollPercent(Element list, string pageName)
    {
        var value = list.GetProperty("ScrollVerticalPercent");
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result >= 0)
        {
            return result;
        }

        throw new InvalidOperationException(
            $"The '{pageName}' ItemsList did not expose a valid ScrollVerticalPercent value (actual: '{value}').");
    }

    private void RunMouseWorkload(
        Session mainWindow,
        int fastScrollCount,
        int slowScrollCount,
        int fastScrollDelayMilliseconds,
        int slowScrollDelayMilliseconds)
    {
        FocusMainWindowForInput(mainWindow);
        var bounds = WindowHelper.GetWindowBounds(new IntPtr(mainWindow.WindowHandle));
        MouseHelper.MoveTo((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);

        Step($"Scrolling down {fastScrollCount} ticks with {fastScrollDelayMilliseconds} ms spacing");
        for (var index = 0; index < fastScrollCount; index++)
        {
            MouseHelper.ScrollDown();
            Thread.Sleep(fastScrollDelayMilliseconds);
        }

        Step($"Scrolling up {slowScrollCount} ticks with {slowScrollDelayMilliseconds} ms spacing");
        for (var index = 0; index < slowScrollCount; index++)
        {
            MouseHelper.ScrollUp();
            Thread.Sleep(slowScrollDelayMilliseconds);
        }
    }

    private static void FocusMainWindowForInput(Session mainWindow)
    {
        Assert.IsTrue(WaitForForegroundWithAltFallback(mainWindow), "The CmdPal main window could not be focused for benchmark input.");
    }

    private static bool WaitForProcessState(bool expectedRunning, int timeoutMilliseconds)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMilliseconds);
        do
        {
            var processes = Process.GetProcessesByName(ProcessName);
            var running = processes.Length > 0;
            foreach (var process in processes)
            {
                process.Dispose();
            }

            if (running == expectedRunning)
            {
                return true;
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    private static Session WaitForMainWindow(int timeoutMilliseconds)
    {
        var mainWindow = WindowsFinder.WaitForWindowByApp(
            ProcessName,
            window => window.Title.Equals("Command Palette", StringComparison.OrdinalIgnoreCase),
            timeoutMS: timeoutMilliseconds);
        Assert.IsNotNull(mainWindow, $"The CmdPal main window did not appear within {timeoutMilliseconds / 1_000} seconds.");
        return mainWindow!;
    }

    private static void LaunchOrActivate(string productPath)
    {
        using (Process.Start(new ProcessStartInfo
        {
            FileName = productPath,
            WorkingDirectory = Path.GetDirectoryName(productPath)!,
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException($"Process.Start returned null for '{productPath}'."))
        {
        }
    }

    private void Step(string message)
    {
        TestContext.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
    }
}
