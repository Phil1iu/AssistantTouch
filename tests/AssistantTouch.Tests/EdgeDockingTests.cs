using System.Windows;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>
/// 贴边收起的纯函数测试（<see cref="EdgeDocking"/>）：四边判定、8px 阈值边界、收起/展开坐标、多显示器工作区。
/// </summary>
public class EdgeDockingTests
{
    private const double BallWidth = 72;
    private const double BallHeight = 72;
    private const double VisiblePx = EdgeDocking.DockVisiblePx;
    private const double Threshold = EdgeDocking.SnapThresholdPx;

    private static readonly Rect PrimaryWorkArea = new(0, 0, 1920, 1032);
    private static readonly Rect SecondaryWorkArea = new(1920, -51, 1920, 1032);

    [Theory]
    [InlineData(0, 480, DockEdge.Left)]      // 球左边贴工作区左边
    [InlineData(1848, 480, DockEdge.Right)]  // 球右边贴工作区右边
    [InlineData(924, 0, DockEdge.Top)]       // 球上边贴工作区上边
    [InlineData(924, 960, DockEdge.Bottom)]  // 球下边贴工作区下边
    public void DetectEdge_EachSideTouching_ReturnsThatEdge(double left, double top, DockEdge expected)
    {
        DockEdge edge = EdgeDocking.DetectEdge(left, top, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(expected, edge);
    }

    [Theory]
    [InlineData(7, true)]   // 阈值内
    [InlineData(8, true)]   // 恰好等于 8px 也算贴边
    [InlineData(9, false)]  // 超出 1px 不贴
    public void DetectEdge_LeftGapBoundary_MatchesThreshold(double gap, bool expectedDock)
    {
        DockEdge edge = EdgeDocking.DetectEdge(gap, 480, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(expectedDock ? DockEdge.Left : DockEdge.None, edge);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void DetectEdge_RightGapBoundary_MatchesThreshold(double gap, bool expectedDock)
    {
        double left = PrimaryWorkArea.Right - BallWidth - gap;

        DockEdge edge = EdgeDocking.DetectEdge(left, 480, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(expectedDock ? DockEdge.Right : DockEdge.None, edge);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void DetectEdge_VerticalGapBoundary_MatchesThreshold(double gap, bool expectedDock)
    {
        DockEdge topEdge = EdgeDocking.DetectEdge(924, gap, BallWidth, BallHeight, PrimaryWorkArea, Threshold);
        DockEdge bottomEdge = EdgeDocking.DetectEdge(
            924, PrimaryWorkArea.Bottom - BallHeight - gap, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(expectedDock ? DockEdge.Top : DockEdge.None, topEdge);
        Assert.Equal(expectedDock ? DockEdge.Bottom : DockEdge.None, bottomEdge);
    }

    [Theory]
    [InlineData(5, 3, DockEdge.Top)]     // 两边都在阈值内但间距不同：取更近的一边
    [InlineData(3, 5, DockEdge.Left)]
    [InlineData(4, 4, DockEdge.Left)]    // 等距时按 左 → 右 → 上 → 下 优先级
    [InlineData(8, 8, DockEdge.Left)]    // 阈值边界上的等距同样按优先级
    [InlineData(8, 7, DockEdge.Top)]
    public void DetectEdge_TwoEdgesWithinThreshold_PicksNearestThenPriority(
        double leftGap, double topGap, DockEdge expected)
    {
        DockEdge edge = EdgeDocking.DetectEdge(leftGap, topGap, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(expected, edge);
    }

    [Fact]
    public void DetectEdge_RightAndBottomWithinThreshold_PicksNearestThenPriority()
    {
        // 右下角：右边距 6、下边距 2 → 取下边；两边都是 4 → 按优先级取右边（右在下之前）。
        DockEdge nearest = EdgeDocking.DetectEdge(
            PrimaryWorkArea.Right - BallWidth - 6,
            PrimaryWorkArea.Bottom - BallHeight - 2,
            BallWidth, BallHeight, PrimaryWorkArea, Threshold);
        DockEdge tied = EdgeDocking.DetectEdge(
            PrimaryWorkArea.Right - BallWidth - 4,
            PrimaryWorkArea.Bottom - BallHeight - 4,
            BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(DockEdge.Bottom, nearest);
        Assert.Equal(DockEdge.Right, tied);
    }

    [Fact]
    public void DetectEdge_InsideWorkArea_ReturnsNone()
    {
        DockEdge edge = EdgeDocking.DetectEdge(924, 480, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(DockEdge.None, edge);
    }

    [Fact]
    public void DetectEdge_TopLeftCorner_PrefersLeftAsDocumented()
    {
        // 两条边同时满足（Δ=0）时按 左 → 右 → 上 → 下 取第一条，结果确定。
        DockEdge edge = EdgeDocking.DetectEdge(0, 0, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(DockEdge.Left, edge);
    }

    [Fact]
    public void DetectEdge_BallPartlyOutsideWorkArea_TreatsGapAsZero()
    {
        // 钳制前可能已经越界（例如窗口比工作区还大），负间距按 0 处理，不会抛异常。
        DockEdge edge = EdgeDocking.DetectEdge(-30, 480, BallWidth, BallHeight, PrimaryWorkArea, Threshold);

        Assert.Equal(DockEdge.Left, edge);
    }

    [Theory]
    [InlineData(DockEdge.Left, 0, 480, -56, 480)]
    [InlineData(DockEdge.Right, 1848, 480, 1904, 480)]
    [InlineData(DockEdge.Top, 924, 0, 924, -56)]
    [InlineData(DockEdge.Bottom, 924, 960, 924, 1016)]
    public void ComputeDockedPosition_EachSide_LeavesVisibleStripOnScreen(
        DockEdge edge, double left, double top, double expectedLeft, double expectedTop)
    {
        Point docked = EdgeDocking.ComputeDockedPosition(edge, left, top, BallWidth, BallHeight, PrimaryWorkArea, VisiblePx);

        Assert.Equal(new Point(expectedLeft, expectedTop), docked);
    }

    [Fact]
    public void ComputeDockedPosition_Left_LeavesExactlyVisiblePxInsideWorkArea()
    {
        Point docked = EdgeDocking.ComputeDockedPosition(DockEdge.Left, 0, 480, BallWidth, BallHeight, PrimaryWorkArea, VisiblePx);

        Assert.Equal(VisiblePx, docked.X + BallWidth - PrimaryWorkArea.Left);
    }

    [Fact]
    public void ComputeDockedPosition_CrossAxisBeyondWorkArea_IsClampedInside()
    {
        Point tooHigh = EdgeDocking.ComputeDockedPosition(DockEdge.Left, 0, -500, BallWidth, BallHeight, PrimaryWorkArea, VisiblePx);
        Point tooLow = EdgeDocking.ComputeDockedPosition(DockEdge.Left, 0, 5000, BallWidth, BallHeight, PrimaryWorkArea, VisiblePx);

        Assert.Equal(PrimaryWorkArea.Top, tooHigh.Y);
        Assert.Equal(PrimaryWorkArea.Bottom - BallHeight, tooLow.Y);
    }

    [Fact]
    public void ComputeDockedPosition_NoneEdge_KeepsPosition()
    {
        Point docked = EdgeDocking.ComputeDockedPosition(DockEdge.None, 924, 480, BallWidth, BallHeight, PrimaryWorkArea, VisiblePx);

        Assert.Equal(new Point(924, 480), docked);
    }

    [Theory]
    [InlineData(0, 0, 480, 0, 480)]
    [InlineData(8, 0, 480, 8, 480)]
    public void ComputeExpandedPosition_LeftEdge_AppliesMargin(
        double margin, double left, double top, double expectedLeft, double expectedTop)
    {
        Point expanded = EdgeDocking.ComputeExpandedPosition(
            DockEdge.Left, left, top, BallWidth, BallHeight, PrimaryWorkArea, margin);

        Assert.Equal(new Point(expectedLeft, expectedTop), expanded);
    }

    [Fact]
    public void ComputeExpandedPosition_EachSide_PutsWholeBallInsideWorkArea()
    {
        foreach (DockEdge edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            Point expanded = EdgeDocking.ComputeExpandedPosition(
                edge, 924, 480, BallWidth, BallHeight, PrimaryWorkArea, EdgeDocking.ExpandMarginPx);

            var ball = new Rect(expanded.X, expanded.Y, BallWidth, BallHeight);
            Assert.True(PrimaryWorkArea.Contains(ball), $"{edge}: {ball} 不在 {PrimaryWorkArea} 内");
        }
    }

    [Fact]
    public void ComputeExpandedPosition_DefaultMargin_CoversWholeVisibleStrip()
    {
        // 展开必须盖住收起态的 16px 入口：否则鼠标悬停后窗口一滑走就落在窗口外，立刻又收起（闪缩）。
        foreach (DockEdge edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            Point docked = EdgeDocking.ComputeDockedPosition(
                edge, 924, 480, BallWidth, BallHeight, PrimaryWorkArea, VisiblePx);
            Point expanded = EdgeDocking.ComputeExpandedPosition(
                edge, 924, 480, BallWidth, BallHeight, PrimaryWorkArea, EdgeDocking.ExpandMarginPx);

            var dockedRect = new Rect(docked.X, docked.Y, BallWidth, BallHeight);
            var expandedRect = new Rect(expanded.X, expanded.Y, BallWidth, BallHeight);
            var visibleStrip = Rect.Intersect(dockedRect, PrimaryWorkArea);

            Assert.False(visibleStrip.IsEmpty);
            Assert.True(
                expandedRect.Contains(visibleStrip),
                $"{edge}: 展开窗口 {expandedRect} 未覆盖入口 {visibleStrip}");
        }
    }

    [Fact]
    public void ComputeDockedAndExpanded_SecondaryMonitorWithNegativeOrigin_UsesThatWorkArea()
    {
        // 副屏在主屏右侧且上移 51px：右侧收起必须落在副屏工作区上，纵坐标不超过副屏下边。
        Point docked = EdgeDocking.ComputeDockedPosition(
            DockEdge.Right, 3000, 1200, BallWidth, BallHeight, SecondaryWorkArea, VisiblePx);
        Point expanded = EdgeDocking.ComputeExpandedPosition(
            DockEdge.Right, 3000, 1200, BallWidth, BallHeight, SecondaryWorkArea, EdgeDocking.ExpandMarginPx);

        Assert.Equal(SecondaryWorkArea.Right - VisiblePx, docked.X);
        Assert.Equal(new Point(SecondaryWorkArea.Right - BallWidth, SecondaryWorkArea.Bottom - BallHeight), expanded);

        var expandedRect = new Rect(expanded.X, expanded.Y, BallWidth, BallHeight);
        Assert.True(SecondaryWorkArea.Contains(expandedRect));
    }

    [Fact]
    public void ComputeDockedPosition_WorkAreaSmallerThanBall_DoesNotThrow()
    {
        var tinyArea = new Rect(10, 20, 40, 40);

        Point docked = EdgeDocking.ComputeDockedPosition(DockEdge.Top, 30, 30, BallWidth, BallHeight, tinyArea, VisiblePx);
        Point expanded = EdgeDocking.ComputeExpandedPosition(DockEdge.Top, 30, 30, BallWidth, BallHeight, tinyArea, EdgeDocking.ExpandMarginPx);

        Assert.Equal(tinyArea.Top - (BallHeight - VisiblePx), docked.Y);
        Assert.Equal(tinyArea.Top, expanded.Y);
    }

    [Theory]
    [InlineData(DockEdge.Left, 4, 480, 0, 480)]         // 松手点距左边 4px → 归一化到贴齐
    [InlineData(DockEdge.Right, 1844, 480, 1848, 480)]  // 距右边 4px
    [InlineData(DockEdge.Top, 924, 4, 924, 0)]
    [InlineData(DockEdge.Bottom, 924, 956, 924, 960)]   // 距下边 4px（tester 观察到的 802/806 情形）
    public void DockedSaveValue_AfterNormalization_EqualsExpandedPosition(
        DockEdge edge, double releaseLeft, double releaseTop, double expectedLeft, double expectedTop)
    {
        // DockTo 的顺序：记下参考位置（松手点）→ 归一化为展开位 → 落盘。
        Point normalized = EdgeDocking.ComputeExpandedPosition(
            edge, releaseLeft, releaseTop, BallWidth, BallHeight, PrimaryWorkArea, EdgeDocking.ExpandMarginPx);
        Point docked = EdgeDocking.ComputeDockedPosition(
            edge, normalized.X, normalized.Y, BallWidth, BallHeight, PrimaryWorkArea, VisiblePx);

        var saved = WindowPositionStore.ComposeSavedPosition(edge, normalized.X, normalized.Y, docked.X, docked.Y);

        // 落盘值必须与悬停展开后的位置逐像素一致（O1），而不是松手点。
        Assert.Equal(new Point(expectedLeft, expectedTop), normalized);
        Assert.Equal(expectedLeft, saved.Left);
        Assert.Equal(expectedTop, saved.Top);
        Assert.Equal(edge, saved.Edge);
    }

    [Fact]
    public void ComputeExpandedPosition_IsIdempotent_ForAllEdges()
    {
        // DockTo 可能在「已贴边、再次收起」时重复归一化：结果必须不变。
        foreach (DockEdge edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            Point release = edge switch
            {
                DockEdge.Left => new Point(4, 480),
                DockEdge.Right => new Point(1844, 480),
                DockEdge.Top => new Point(924, 4),
                _ => new Point(924, 956)
            };

            Point once = EdgeDocking.ComputeExpandedPosition(
                edge, release.X, release.Y, BallWidth, BallHeight, PrimaryWorkArea, EdgeDocking.ExpandMarginPx);
            Point twice = EdgeDocking.ComputeExpandedPosition(
                edge, once.X, once.Y, BallWidth, BallHeight, PrimaryWorkArea, EdgeDocking.ExpandMarginPx);

            Assert.Equal(once, twice);
        }
    }
}
