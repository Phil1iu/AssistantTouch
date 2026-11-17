using System.Windows;

namespace AssistantTouch;

/// <summary>悬浮球贴边的边；<see cref="None"/> 表示未贴边。</summary>
/// <remarks>
/// 公开而不是 internal：测试工程用 <c>[Theory]</c> 参数化断言四边，公开测试方法不能引用 internal 类型。
/// </remarks>
public enum DockEdge
{
    /// <summary>未贴边：窗口必须完整落在工作区内。</summary>
    None = 0,

    /// <summary>贴左边缘。</summary>
    Left,

    /// <summary>贴右边缘。</summary>
    Right,

    /// <summary>贴上边缘。</summary>
    Top,

    /// <summary>贴下边缘。</summary>
    Bottom
}

/// <summary>
/// 贴边收起/展开的纯函数（不碰窗口、不碰 Win32，便于单测）。
/// 所有坐标都是「窗口左上角」；单位与传入的工作区一致（WPF DIU）。
/// </summary>
/// <remarks>
/// 收起 = 沿贴边方向把窗口推出工作区，只留 <see cref="DockVisiblePx"/> 宽的一条露在工作区内；
/// 另一方向保持在工作区内（交叉方向继续钳制），因此收起态窗口仍可被鼠标命中，且不会跑到别的显示器上。
/// 展开 = 沿贴边方向把窗口拉回工作区，贴齐边缘（见 <see cref="ExpandMarginPx"/>）。
/// </remarks>
internal static class EdgeDocking
{
    /// <summary>松手时球边距最近工作区边缘 ≤ 该值（DIU）即自动贴边收起。</summary>
    public const double SnapThresholdPx = 8;

    /// <summary>收起后仍露在工作区内的宽度（DIU），即鼠标进入的「入口」。</summary>
    public const double DockVisiblePx = 16;

    /// <summary>
    /// 展开时球相对工作区边缘的间距。
    /// </summary>
    /// <remarks>
    /// 取 0（贴齐工作区边缘）而不是 8：收起态的入口是窗口贴边一侧的 16px，
    /// 展开若留 8px，窗口会从入口下方滑走、鼠标落进窗口外的空隙里，
    /// 触发 MouseLeave 后立刻又收起，表现为来回闪缩。
    /// 窗口自身有 2px 透明内边距，视觉上仍有细缝；圆角处的透明区域不参与球体绘制。
    /// </remarks>
    public const double ExpandMarginPx = 0;

    /// <summary>
    /// 判定球贴在哪条边：球边距工作区对应边缘 ≤ <paramref name="snapThreshold"/> 即算贴边。
    /// 两条边同时满足（贴角）时按 左 → 右 → 上 → 下 的顺序取第一条，保证结果确定。
    /// </summary>
    public static DockEdge DetectEdge(double left, double top, double width, double height, Rect workArea, double snapThreshold)
    {
        (DockEdge Edge, double Gap)[] gaps =
        {
            (DockEdge.Left, Math.Max(0, left - workArea.Left)),
            (DockEdge.Right, Math.Max(0, workArea.Right - (left + width))),
            (DockEdge.Top, Math.Max(0, top - workArea.Top)),
            (DockEdge.Bottom, Math.Max(0, workArea.Bottom - (top + height)))
        };

        DockEdge edge = DockEdge.None;
        double best = double.PositiveInfinity;

        foreach ((DockEdge candidate, double gap) in gaps)
        {
            if (gap <= snapThreshold && gap < best)
            {
                edge = candidate;
                best = gap;
            }
        }

        return edge;
    }

    /// <summary>
    /// 收起坐标：贴边方向推出工作区、只露 <paramref name="visiblePx"/>；另一方向钳制在工作区内。
    /// <paramref name="edge"/> 为 <see cref="DockEdge.None"/> 时原样返回左上角。
    /// </summary>
    public static Point ComputeDockedPosition(DockEdge edge, double left, double top, double width, double height, Rect workArea, double visiblePx)
    {
        double horizontalMax = workArea.Right - width;
        double verticalMax = workArea.Bottom - height;

        return edge switch
        {
            DockEdge.Left => new Point(workArea.Left - (width - visiblePx), ClampCross(top, workArea.Top, verticalMax)),
            DockEdge.Right => new Point(workArea.Right - visiblePx, ClampCross(top, workArea.Top, verticalMax)),
            DockEdge.Top => new Point(ClampCross(left, workArea.Left, horizontalMax), workArea.Top - (height - visiblePx)),
            DockEdge.Bottom => new Point(ClampCross(left, workArea.Left, horizontalMax), workArea.Bottom - visiblePx),
            _ => new Point(left, top)
        };
    }

    /// <summary>
    /// 展开坐标：贴边方向把整球拉回工作区（距边缘 <paramref name="margin"/>）；另一方向钳制在工作区内。
    /// <paramref name="edge"/> 为 <see cref="DockEdge.None"/> 时原样返回左上角。
    /// </summary>
    public static Point ComputeExpandedPosition(DockEdge edge, double left, double top, double width, double height, Rect workArea, double margin)
    {
        double horizontalMax = workArea.Right - width;
        double verticalMax = workArea.Bottom - height;

        return edge switch
        {
            DockEdge.Left => new Point(workArea.Left + margin, ClampCross(top, workArea.Top, verticalMax)),
            DockEdge.Right => new Point(workArea.Right - width - margin, ClampCross(top, workArea.Top, verticalMax)),
            DockEdge.Top => new Point(ClampCross(left, workArea.Left, horizontalMax), workArea.Top + margin),
            DockEdge.Bottom => new Point(ClampCross(left, workArea.Left, horizontalMax), workArea.Bottom - height - margin),
            _ => new Point(left, top)
        };
    }

    /// <summary>交叉方向钳制：工作区比球还小时落在工作区左上边缘（与 <see cref="WindowClamp"/> 同口径）。</summary>
    private static double ClampCross(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
