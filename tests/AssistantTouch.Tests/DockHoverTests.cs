using System.Windows;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>
/// 贴边悬停几何判定（<see cref="DockHover"/>）的测试：D110「悬停闪烁」回归。
/// 核心不变量：光标落在「展开窗口 ∪ 收起条带」任一处都算仍在贴边区域。
/// 收起条带沿贴边方向伸出工作区 56px，展开窗口则整球缩回工作区内；只看展开窗口的话，
/// 伸出段上的点会形成 展开 → Leave → 收起 → 窗口滑回光标下 → Enter → 展开 的稳定振荡。
/// </summary>
public class DockHoverTests
{
    private const double BallWidth = 72;
    private const double BallHeight = 72;

    /// <summary>D109 实测的 VM 几何：显示 1918×918、工作区 1918×878（底部 40px 任务栏）。</summary>
    private static readonly Rect WorkArea = new(0, 0, 1918, 878);

    private static readonly Size BallSize = new(BallWidth, BallHeight);

    // 参考点（贴边前的球位置）取工作区中部，贴边后四边展开/收起矩形见各测试注释。
    private const double ReferenceLeft = 923;
    private const double ReferenceTop = 400;

    private static Rect ExpandedRect(DockEdge edge) => new(
        EdgeDocking.ComputeExpandedPosition(
            edge, ReferenceLeft, ReferenceTop, BallWidth, BallHeight, WorkArea, EdgeDocking.ExpandMarginPx),
        BallSize);

    private static Rect StripRect(DockEdge edge) => new(
        EdgeDocking.ComputeDockedPosition(
            edge, ReferenceLeft, ReferenceTop, BallWidth, BallHeight, WorkArea, EdgeDocking.DockVisiblePx),
        BallSize);

    private static bool InRegion(DockEdge edge, Point cursor) =>
        DockHover.IsCursorInDockRegion(cursor, ExpandedRect(edge), StripRect(edge), DockHover.TolerancePx);

    [Theory]
    // 左：展开 [0,72]×[400,472]，收起 [-56,16]×[400,472]，入口是工作区左侧 0–16px。
    [InlineData(DockEdge.Left, 0.5, 436)]
    [InlineData(DockEdge.Left, 8, 436)]
    [InlineData(DockEdge.Left, 15.5, 436)]
    // 右：展开 [1846,1918]×[400,472]，收起 [1902,1974]×[400,472]。
    [InlineData(DockEdge.Right, 1902.5, 436)]
    [InlineData(DockEdge.Right, 1910, 436)]
    [InlineData(DockEdge.Right, 1917.5, 436)]
    // 上：展开 [923,995]×[0,72]，收起 [923,995]×[-56,16]。
    [InlineData(DockEdge.Top, 959, 0.5)]
    [InlineData(DockEdge.Top, 959, 8)]
    [InlineData(DockEdge.Top, 959, 15.5)]
    // 下：展开 [923,995]×[806,878]，收起 [923,995]×[862,934]。
    [InlineData(DockEdge.Bottom, 959, 862.5)]
    [InlineData(DockEdge.Bottom, 959, 870)]
    [InlineData(DockEdge.Bottom, 959, 877.5)]
    public void CursorOnVisibleStrip_IsInDockRegion(DockEdge edge, double x, double y)
    {
        Assert.True(InRegion(edge, new Point(x, y)));
    }

    [Theory]
    // 贴边方向伸出工作区、但仍在收起球上（屏幕外侧/任务栏/相邻显示器可到的地方）：
    // 这些点落在展开窗口之外——只按展开窗口判定就是闪烁点。
    [InlineData(DockEdge.Left, -0.5, 436)]
    [InlineData(DockEdge.Left, -20, 436)]
    [InlineData(DockEdge.Right, 1920, 436)]
    [InlineData(DockEdge.Right, 1960, 436)]
    [InlineData(DockEdge.Top, 959, -0.5)]
    [InlineData(DockEdge.Top, 959, -20)]
    [InlineData(DockEdge.Bottom, 959, 880)]   // 底部任务栏带（工作区下沿 878，屏幕 918）
    [InlineData(DockEdge.Bottom, 959, 900)]
    [InlineData(DockEdge.Bottom, 959, 932)]
    public void CursorBeyondWorkAreaButOnStrip_IsInDockRegion(DockEdge edge, double x, double y)
    {
        var cursor = new Point(x, y);

        Assert.False(ExpandedRect(edge).Contains(cursor)); // 只看展开窗口会判「已离开」→ 闪烁
        Assert.True(InRegion(edge, cursor));
    }

    [Theory]
    [InlineData(DockEdge.Left, -56, 400)]   // 条带四角（含边界）
    [InlineData(DockEdge.Left, 16, 472)]
    [InlineData(DockEdge.Right, 1902, 400)]
    [InlineData(DockEdge.Right, 1974, 472)]
    [InlineData(DockEdge.Top, 923, -56)]
    [InlineData(DockEdge.Top, 995, 16)]
    [InlineData(DockEdge.Bottom, 923, 862)]
    [InlineData(DockEdge.Bottom, 995, 934)]
    public void CursorOnStripCorner_IsInDockRegion(DockEdge edge, double x, double y)
    {
        Assert.True(InRegion(edge, new Point(x, y)));
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    public void CursorFarOutside_IsNotInDockRegion(DockEdge edge)
    {
        Assert.False(InRegion(edge, new Point(600, 600)));
    }

    [Theory]
    // 右：展开矩形 [1846,1918]×[400,472]，外扩 8px 后 y 方向为 [392,480]。x=1910 在两个矩形的 x 范围内。
    [InlineData(392.5, true)]   // 容差内
    [InlineData(392, true)]     // 恰好在容差边界
    [InlineData(391, false)]    // 超出 1px
    [InlineData(479.5, true)]
    [InlineData(481, false)]
    public void Tolerance_ExtendsRegionByEightPx(double y, bool expected)
    {
        Assert.Equal(expected, InRegion(DockEdge.Right, new Point(1910, y)));
    }

    [Theory]
    // 右：收起矩形 x 方向 [1902,1974]，外扩 8px 后到 1982。
    [InlineData(1981, true)]
    [InlineData(1983, false)]
    public void CursorPastStripAlongDockAxis_WithinTolerance(double x, bool expected)
    {
        Assert.Equal(expected, InRegion(DockEdge.Right, new Point(x, 436)));
    }

    [Fact]
    public void EmptyRects_NeverContainCursor()
    {
        Assert.False(DockHover.IsCursorInDockRegion(new Point(0, 0), Rect.Empty, Rect.Empty, DockHover.TolerancePx));
    }
}
