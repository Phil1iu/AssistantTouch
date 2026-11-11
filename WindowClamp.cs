using System.Windows;

namespace AssistantTouch;

/// <summary>
/// 把窗口左上角钳进指定显示器工作区的纯函数（无 Win32/无 WPF 窗口依赖，便于单测）。
/// </summary>
internal static class WindowClamp
{
    /// <summary>
    /// 返回钳制后的左上角。工作区比窗口还小时，上界小于下界，结果落在工作区左上角。
    /// </summary>
    public static Point Clamp(double desiredLeft, double desiredTop, double width, double height, Rect workArea)
    {
        double left = Math.Max(workArea.Left, Math.Min(workArea.Right - width, desiredLeft));
        double top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - height, desiredTop));
        return new Point(left, top);
    }
}
