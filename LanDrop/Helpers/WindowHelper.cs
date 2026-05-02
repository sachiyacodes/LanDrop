// Helpers/WindowHelper.cs
// Uses DWM API to color the Windows title bar to match the app background,
// eliminating the white bar at the top entirely.

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LanDrop.Helpers
{
    public static class WindowHelper
    {
        // DWM attribute for title bar color (Windows 11 22H1+)
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_BORDER_COLOR   = 34;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Set the OS title bar background to match the app so there's no
        /// visible seam between the Windows chrome and the custom header.
        /// </summary>
        public static void ApplyTitleBarColor(Window window, bool darkMode)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                // Tell Windows to use dark mode title bar decorations
                int dark = darkMode ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref dark, sizeof(int));

                // Set caption (title bar) background color
                // Color is COLORREF = 0x00BBGGRR
                Color bg = darkMode
                    ? Color.FromRgb(0x04, 0x06, 0x06)   // #040606
                    : Color.FromRgb(0xFF, 0xFF, 0xFF);   // #ffffff

                int colorRef = bg.R | (bg.G << 8) | (bg.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR,
                    ref colorRef, sizeof(int));

                // Also match the border color
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR,
                    ref colorRef, sizeof(int));
            }
            catch
            {
                // DWM not available on older Windows — silently ignore
            }
        }
    }
}
