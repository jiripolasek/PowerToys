// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using ManagedCommon;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Microsoft.CmdPal.UI.Helpers;

internal static class WindowExtensions
{
    public static void SetIcon(this Window window)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(@"Assets\icon.ico");
    }

    private static HWND GetWindowHwnd(this Window window)
    {
        return window is null
            ? throw new ArgumentNullException(nameof(window))
            : new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }

    /// <summary>
    /// Toggles the specified extended window style on or off for the supplied <see cref="Window"/>.
    /// </summary>
    /// <param name="window">The <see cref="Window"/> whose extended window styles will be modified. Cannot be null.</param>
    /// <param name="style">The <see cref="WINDOW_EX_STYLE"/> flag(s) to set or clear.</param>
    /// <param name="isStyleSet">When true, the specified <paramref name="style"/> bit(s) will be set (added). When false, the bit(s) will be cleared (removed).</param>
    /// <returns>True if the call to SetWindowLong succeeded and the style was applied; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="window"/> is null.</exception>
    /// <exception cref="Win32Exception">Thrown if the Windows API call fails with an error code.</exception>
    private static bool ToggleExtendedWindowStyle(this Window window, WINDOW_EX_STYLE style, bool isStyleSet)
    {
        var hWnd = GetWindowHwnd(window);
        var currentStyle = PInvoke.GetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        var newStyle = isStyleSet
            ? currentStyle | (int)style
            : currentStyle & ~(int)style;

        // No change needed
        if (newStyle == currentStyle)
        {
            return false;
        }

        // cleanup last error before the call, to reliably detect if an error occurred
        Marshal.SetLastSystemError(0);

        // if the return value is zero, we need to check GetLastError to determine if an error occurred
        var previousStyle = PInvoke.SetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, newStyle);
        if (previousStyle == 0)
        {
            var lastError = Marshal.GetLastWin32Error();
            if (lastError != 0)
            {
                throw new Win32Exception(lastError, $"Failed to set window extended style 0x{style:X}");
            }
        }

        // Force the window to refresh its cached style information
        const SET_WINDOW_POS_FLAGS refreshFlags =
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED |
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;

        PInvoke.SetWindowPos(hWnd, HWND.Null, 0, 0, 0, 0, refreshFlags);
        return previousStyle != currentStyle;
    }

    /// <summary>
    /// Sets or removes the tool window style for the specified window using all available methods.
    /// </summary>
    /// <param name="window">The window to modify.</param>
    /// <param name="isToolWindow">True to turn the window into a tool window (hidden from Alt+Tab); false to make it a normal window.</param>
    internal static void SetToolWindowStyleSafe(this Window window, bool isToolWindow)
    {
        ArgumentNullException.ThrowIfNull(window);

        var success = false;

        // Method 1: Try to set WS_EX_TOOLWINDOW via Win32 API
        try
        {
            if (window.ToggleExtendedWindowStyle(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, isToolWindow))
            {
                success = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to set WS_EX_TOOLWINDOW to {isToolWindow}", ex);
        }

        // Method 2: Try to set IsShownInSwitchers as well
        try
        {
            window.AppWindow.IsShownInSwitchers = !isToolWindow;
            success = true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to set IsShownInSwitchers to {!isToolWindow}", ex);
        }

        if (!success)
        {
            Logger.LogError($"Failed to {(isToolWindow ? "hide" : "show")} window in Alt+Tab using all available methods");
        }
    }

    /// <summary>
    /// Sets the window corner preference
    /// </summary>
    /// <param name="window">The window</param>
    /// <param name="cornerPreference">The desired corner preference</param>
    /// <returns>True if the operation succeeded</returns>
    public static bool SetCornerPreference(this Window window, DWM_WINDOW_CORNER_PREFERENCE cornerPreference)
    {
        return window.GetWindowHwnd().SetDwmWindowAttribute(DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, cornerPreference);
    }

    /// <summary>
    /// Unified wrapper for DwmSetWindowAttribute calls with enum values
    /// </summary>
    private static bool SetDwmWindowAttribute<T>(this HWND hwnd, DWMWINDOWATTRIBUTE attribute, T value)
        where T : unmanaged, Enum
    {
        unsafe
        {
            var result = PInvoke.DwmSetWindowAttribute(hwnd, attribute, &value, (uint)sizeof(T));
            return result.Succeeded;
        }
    }
}
