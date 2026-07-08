using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace EvEMapEnhanced.Desktop;

internal static class WindowActivation
{
    private const int SwRestore = 9;

    public static void BringToFront(Window window)
    {
        window.Show();
        window.Activate();

        if (OperatingSystem.IsWindows() &&
            window.TryGetPlatformHandle()?.Handle is { } handle &&
            handle != IntPtr.Zero)
        {
            // SW_RESTORE un-maximizes a visible maximized window; only use it when minimized.
            if (IsIconic(handle))
                ShowWindow(handle, SwRestore);

            SetForegroundWindow(handle);
            return;
        }

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
