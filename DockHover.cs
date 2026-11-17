using System.Windows;

namespace AssistantTouch;

/// <summary>
/// 贴边悬停的纯函数判定：光标是否仍在「展开窗口 ∪ 收起条带」区域内（不碰窗口、不碰 Win32）。
/// </summary>
/// <remarks>
/// 收起窗口沿贴边方向伸出工作区 56px（72 − 16），展开窗口却整体缩回工作区内，两者互不包含：
/// 工作区与屏幕可视区之间的条带（底部任务栏、贴边侧的任务栏、相邻显示器、DPI 取整）上，
/// 存在「光标命中收起球、却落在展开球之外」的位置。若收起判定只看 <c>IsMouseOver</c>，
/// 光标停在这些位置就会形成「展开 → Leave → 300ms 收起 → 窗口滑回光标下 → Enter → 展开」的稳定振荡
/// （负责人 18:06 实测的「不停显示收起」闪烁，D110）。
/// 判定必须以两个窗口位置的并集为准；容差吸收 1px 取整/DPI 折误差，并顺带形成滞回：
/// 光标贴身停靠（贴边侧 8px）时保持展开，走远才收起。
/// </remarks>
internal static class DockHover
{
    /// <summary>
    /// 判定容差（DIU）：光标离「展开窗口 ∪ 收起条带」都不超过该值仍算悬停。
    /// 用来吸收窗口位置按设备像素取整、混合 DPI 折算产生的 1px 级误差，避免在边界上抖动。
    /// </summary>
    public const double TolerancePx = 8;

    /// <summary>
    /// 光标（屏幕 DIU 坐标）是否落在展开窗口矩形或收起条带矩形内（均按 <paramref name="tolerance"/> 外扩）。
    /// 两个矩形都是「窗口左上角 + 窗口尺寸」的完整矩形，包含露出屏幕外/工作区外的那部分。
    /// </summary>
    public static bool IsCursorInDockRegion(Point cursor, Rect expandedWindowRect, Rect stripWindowRect, double tolerance)
    {
        double inflate = Math.Max(0, tolerance);

        return Inflate(expandedWindowRect, inflate).Contains(cursor)
            || Inflate(stripWindowRect, inflate).Contains(cursor);
    }

    private static Rect Inflate(Rect rect, double tolerance)
    {
        // Rect.Inflate 对 Empty 矩形返回 Empty，Contains 恒为 false。
        return rect.IsEmpty ? rect : Rect.Inflate(rect, tolerance, tolerance);
    }
}
