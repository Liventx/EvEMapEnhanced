using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Opens http(s) URLs in the user's default browser. Uses several Windows fallbacks because
/// <see cref="Process.Start(string)"/> with <c>UseShellExecute</c> fails on some machines
/// (no default browser, locked-down shells, single-file publish quirks).
/// </summary>
public static class BrowserLauncher
{
    public static bool TryOpen(string url, out string? error)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL пустой.";
            return false;
        }

        if (TryProcessStart(url))
        {
            error = null;
            return true;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (TryWindowsExplorer(url) || TryWindowsCmdStart(url))
            {
                error = null;
                return true;
            }
        }

        error = "не удалось запустить браузер по умолчанию";
        return false;
    }

    public static void OpenOrThrow(string url)
    {
        if (!TryOpen(url, out string? error))
        {
            throw new InvalidOperationException(
                $"Не удалось открыть браузер ({error}). Установите браузер по умолчанию в Windows " +
                $"или откройте ссылку вручную:\n{url}");
        }
    }

    private static bool TryProcessStart(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return process is not null || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWindowsExplorer(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWindowsCmdStart(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{url}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
