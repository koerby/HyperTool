using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HyperTool.Helpers;

internal static class DwmWindowHelper
{
    private const int DwmaWindowCornerPreference = 33;

    internal static void ApplyRoundedCorners(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            var windowHandle = new WindowInteropHelper(window).Handle;
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            var preference = DwmWindowCornerPreference.Round;
            _ = DwmSetWindowAttribute(
                windowHandle,
                DwmaWindowCornerPreference,
                ref preference,
                Marshal.SizeOf<DwmWindowCornerPreference>());
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref DwmWindowCornerPreference pvAttribute,
        int cbAttribute);

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }
}
