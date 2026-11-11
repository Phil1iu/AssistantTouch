using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace AssistantTouch;

/// <summary>
/// 显示器工作区查询：Win32 <c>MonitorFromPoint</c> + <c>GetMonitorInfo</c>。
/// </summary>
/// <remarks>
/// 单位：Win32 返回物理像素，统一按传入的 <see cref="DpiScale"/> 折算成 WPF DIU。
/// 进程是 System DPI Aware（非 PerMonitorV2），100% 缩放下 px == DIU；混合 DPI 的多显示器未适配。
/// 点落在任何显示器之外（双屏错位死区、显示器已拔）时，
/// <c>MONITOR_DEFAULTTONEAREST</c> 会返回最近显示器。
/// </remarks>
internal static class MonitorLayout
{
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>
    /// 返回包含指定 DIU 点（不在任何显示器内时取最近显示器）的工作区，单位为 WPF DIU。
    /// </summary>
    public static Rect GetWorkAreaAt(double x, double y, DpiScale dpi)
    {
        var nativePoint = new NativePoint
        {
            X = (int)Math.Round(x * dpi.DpiScaleX),
            Y = (int)Math.Round(y * dpi.DpiScaleY)
        };

        IntPtr monitor = MonitorFromPoint(nativePoint, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            // 极端情况下的兜底：主屏工作区（已是 DIU）。
            return SystemParameters.WorkArea;
        }

        return new Rect(
            info.Work.Left / dpi.DpiScaleX,
            info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }
}
