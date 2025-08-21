// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using ManagedCommon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerToys.Interop;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Microsoft.CmdPal.UI.Settings;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class InternalPage : Page
{
    public InternalPage()
    {
        InitializeComponent();
    }

    private void ThrowPlainMainThreadException_Click(object sender, RoutedEventArgs e)
    {
        Logger.LogDebug("Throwing test exception from the UI thread");
        throw new NotImplementedException("Test exception; thrown from the UI thread");
    }

    private void ThrowExceptionInUnobservedTask_Click(object sender, RoutedEventArgs e)
    {
        Logger.LogDebug("Starting a task that will throw test exception");
        Task.Run(() =>
        {
            Logger.LogDebug("Throwing test exception from the a task thread");
            throw new InvalidOperationException("Test exception; throw from a task");
        });
    }

    private void OpenLogsCardClicked(object sender, RoutedEventArgs e)
    {
        // TODO: remove before merge
        try
        {
            var logPath = Path.Combine(Constants.AppDataPath(), "CmdPal", "Logs");
            if (Directory.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = logPath,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            throw;
        }
    }

    private void ThrowPlainMainThreadExceptionPiis_Click(object sender, RoutedEventArgs e)
    {
        Logger.LogDebug("Throwing test exception from the UI thread (PIIs)");
        throw new InvalidOperationException(SampleData.ExceptionMessageWithPiis);
    }
}
