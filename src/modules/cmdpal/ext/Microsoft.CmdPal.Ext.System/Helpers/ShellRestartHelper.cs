// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Microsoft.CmdPal.Ext.System.Helpers;

/// <summary>
/// Restarts running instances of system shell (Windows Explorer).
/// </summary>
internal static class ShellRestartHelper
{
    private const string ShellProcessExecutableName = "explorer.exe";

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PostRestartCheckDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Restarts all instances of the "explorer.exe" process in the current session.
    /// </summary>
    internal static async Task RestartAllInCurrentSession(bool tryToSaveStateBeforeExit)
    {
        // Restarting Windows Explorer:
        // - Explorer can have multiple processes running in the same session. Let's not speculate why the user
        //   wants to restart it and terminate them all.
        // - Explorer should always run un-elevated. If started elevated, it restarts itself (CreateExplorerShellUnelevatedTask).
        //   That means we don't have to worry about elevated processes.
        // - Restart Manager will restore opened folder windows after restart (only if enabled in Folder Options).
        // - Restarting by will make the new explorer.exe process a child process of CmdPal. This is not much of a
        //   problem unless something kills the entire CmdPal process tree.
        // - Windows can automatically restart the shell if it detects that it has crashed. This can be disabled
        //   in registry (key AutoRestartShell).
        var processes = GetProcessesInCurrentSessionByName(ShellProcessExecutableName);
        if (processes.Length > 0)
        {
            if (tryToSaveStateBeforeExit)
            {
                // This is the same trick Restart Manager uses to shutdown applications gracefully before restart.
                SendEndSessionToProcessWindows(processes);
            }

            await WaitForEndWithTimeoutAsync(processes, tryToSaveStateBeforeExit ? ShutdownTimeout : TimeSpan.Zero);
        }

        await EnsureProcessIsRunning(ShellProcessExecutableName, waitForAutoRestart: !tryToSaveStateBeforeExit);
    }

    private static Process[] GetProcessesInCurrentSessionByName(string processExecutableName)
    {
        var processName = Path.GetFileNameWithoutExtension(processExecutableName);
        return [.. Process.GetProcessesByName(processName).Where(Filter)];

        static bool Filter(Process process)
        {
            try
            {
                return !process.HasExited && process.SessionId == Process.GetCurrentProcess().SessionId;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private static void SendEndSessionToProcessWindows(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            var handles = GetWindowHandlesForProcess(process.Id);
            foreach (var hWnd in handles)
            {
                // WM_QUERYENDSESSION causes the process to prepare for shutdown (register for restart)
                NativeMethods.SendMessage(hWnd, (uint)WindowsMessages.WM_QUERYENDSESSION, 0, 0x01);

                // WM_ENDSESSION should save the state and close the window
                NativeMethods.SendMessage(hWnd, (uint)WindowsMessages.WM_ENDSESSION, 1, 0x01);
            }
        }
    }

    private static List<IntPtr> GetWindowHandlesForProcess(int processId)
    {
        var result = new List<IntPtr>();

        NativeMethods.EnumWindows(
            (hWnd, lParam) =>
            {
                _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var windowPid);
                if (windowPid == processId && NativeMethods.IsWindowVisible(hWnd))
                {
                    result.Add(hWnd);
                }

                return true; // continue enumeration
            },
            nint.Zero);

        return result;
    }

    /// <summary>
    /// Ensures that the specified process is running. If the process is not running, it attempts to start it.
    /// </summary>
    /// <param name="processExecutableName">The name of the process to restart.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private static async Task EnsureProcessIsRunning(string processExecutableName, bool waitForAutoRestart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processExecutableName);

        var processName = Path.GetFileNameWithoutExtension(processExecutableName);

        if (GetProcessesInCurrentSessionByName(processName).Length > 0)
        {
            return;
        }

        if (waitForAutoRestart)
        {
            await Task.Delay(PostRestartCheckDelay);

            if (GetProcessesInCurrentSessionByName(processName).Length > 0)
            {
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(processExecutableName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage($"Fail-safe failed to start {processExecutableName}: {ex.Message}");
        }
    }

    private static async Task WaitForEndWithTimeoutAsync(IEnumerable<Process> processes, TimeSpan timeout)
    {
        var activeProcesses = processes.Where(static p => p is { HasExited: false }).ToList();
        if (activeProcesses.Count == 0)
        {
            return;
        }

        var waitTasks = activeProcesses.Select(static p => p.WaitForExitAsync());
        await Task.WhenAny(Task.WhenAll(waitTasks), Task.Delay(timeout));

        // Kill any remaining processes
        var killTasks = activeProcesses
            .Where(static p => !p.HasExited)
            .Select(static async p =>
            {
                try
                {
                    p.Kill(entireProcessTree: false);
                    await p.WaitForExitAsync();
                }
                catch (ArgumentException)
                {
                    // Process might have exited already
                }
                catch (Exception ex)
                {
                    ExtensionHost.LogMessage($"Failed to kill process ID {p.Id}: {ex.Message}");
                }
            });

        await Task.WhenAll(killTasks);
    }
}
