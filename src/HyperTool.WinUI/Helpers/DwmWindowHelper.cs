using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace HyperTool.WinUI.Helpers;

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
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == nint.Zero)
            {
                return;
            }

            var preference = DwmWindowCornerPreference.Round;
            _ = DwmSetWindowAttribute(
                hwnd,
                DwmaWindowCornerPreference,
                ref preference,
                Marshal.SizeOf<DwmWindowCornerPreference>());
        }
        catch
        {
        }
    }

    internal static void ApplyRoundedRegion(Window window, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0 || radius <= 0)
        {
            return;
        }

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == nint.Zero)
            {
                return;
            }

            var diameter = radius * 2;
            var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region == nint.Zero)
            {
                return;
            }

            var result = SetWindowRgn(hwnd, region, true);
            if (result == 0)
            {
                _ = DeleteObject(region);
            }
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int dwAttribute,
        ref DwmWindowCornerPreference pvAttribute,
        int cbAttribute);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(nint hObject);

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }
}
