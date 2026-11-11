using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace AssistantTouch;

/// <summary>
/// 显示器工作区查询：Win32 <c>MonitorFromPoint</c> + <c>GetMonitorInfo</c>。
/// </summary>
/// <remarks>
/// 单位：Win32 返回物理像素，统一按传入的 <see cref="DpiScale"/> 折算成 WPF DIU。
/// 进程已声明 PerMonitorV2（app.manifest）：显示器工作区按目标显示器 DPI 折算；100% 缩放下 px == DIU，
/// 混合 DPI 已按目标显示器 DPI 处理但未实机验证。
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    // MDT_EFFECTIVE_DPI = 0
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>目标显示器：工作区（已按该显示器 DPI 折算成 DIU）与该显示器的有效 DPI。</summary>
    internal readonly struct MonitorTarget
    {
        public Rect WorkArea { get; init; }

        public DpiScale Dpi { get; init; }
    }

    /// <summary>
    /// 用<strong>物理像素</strong>点（不在任何显示器内时取最近显示器）找到目标显示器，
    /// 返回该显示器工作区（按目标显示器 DPI 折算成 DIU）及其 DPI。
    /// </summary>
    public static MonitorTarget GetNearestTarget(double physicalX, double physicalY, DpiScale fallbackDpi)
    {
        var nativePoint = new NativePoint
        {
            X = (int)Math.Round(physicalX),
            Y = (int)Math.Round(physicalY)
        };

        IntPtr monitor = MonitorFromPoint(nativePoint, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            // 极端情况下的兜底：主屏工作区（已是 DIU）+ 当前 DPI。
            return new MonitorTarget { WorkArea = SystemParameters.WorkArea, Dpi = fallbackDpi };
        }

        DpiScale targetDpi = GetMonitorDpi(monitor, fallbackDpi);
        var workArea = new Rect(
            info.Work.Left / targetDpi.DpiScaleX,
            info.Work.Top / targetDpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / targetDpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / targetDpi.DpiScaleY);

        return new MonitorTarget { WorkArea = workArea, Dpi = targetDpi };
    }

    private static DpiScale GetMonitorDpi(IntPtr monitor, DpiScale fallback)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
            {
                return new DpiScale(dpiX / 96.0, dpiY / 96.0);
            }
        }
        catch (DllNotFoundException)
        {
            // 老系统没有 shcore.dll：回退到当前视觉 DPI。
        }
        catch (EntryPointNotFoundException)
        {
        }

        return fallback;
    }
}
