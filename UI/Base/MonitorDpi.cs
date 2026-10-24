using System;
using System.Runtime.InteropServices;

namespace UI.Base
{
    /// <summary>
    /// 多显示器 DPI 辅助：按目标窗口/屏幕点所在显示器取缩放比例。
    /// 混合缩放（如 100%+150%）下用主窗口 DPI 换算其他屏幕坐标会错位。
    /// </summary>
    public static class MonitorDpi
    {
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>取目标窗口所在显示器的 DPI 缩放。</summary>
        public static bool TryGetScaleForWindow(IntPtr hwnd, out double scaleX, out double scaleY)
        {
            if (hwnd != IntPtr.Zero)
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                return TryGetScaleForMonitor(monitor, out scaleX, out scaleY);
            }
            scaleX = 1;
            scaleY = 1;
            return false;
        }

        /// <summary>取包含指定屏幕物理坐标点的显示器的 DPI 缩放。</summary>
        public static bool TryGetScaleForPoint(int screenX, int screenY, out double scaleX, out double scaleY)
        {
            var pt = new POINT { X = screenX, Y = screenY };
            IntPtr monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            return TryGetScaleForMonitor(monitor, out scaleX, out scaleY);
        }

        private static bool TryGetScaleForMonitor(IntPtr monitor, out double scaleX, out double scaleY)
        {
            scaleX = 1;
            scaleY = 1;
            if (monitor == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                if (GetDpiForMonitor(monitor, 0, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
                {
                    scaleX = dpiX / 96.0;
                    scaleY = dpiY / 96.0;
                    return true;
                }
            }
            catch
            {
                // shcore 缺失（Win7）：调用方回退窗口自身 DPI
            }
            return false;
        }
    }
}
