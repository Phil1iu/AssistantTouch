using System.Windows;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>位置 JSON 解析（<see cref="WindowPositionStore.TryParse"/>）的纯函数测试。</summary>
public class WindowPositionStoreTests
{
    [Theory]
    [InlineData("{\"left\":924,\"top\":480}", 924, 480)]
    [InlineData("{\"left\":-12.5,\"top\":0}", -12.5, 0)]
    [InlineData("{\"left\":1,\"top\":2,\"extra\":\"ignored\"}", 1, 2)]
    public void TryParse_ValidJson_ReturnsExpectedPosition(string json, double expectedLeft, double expectedTop)
    {
        bool parsed = WindowPositionStore.TryParse(json, out double left, out double top);

        Assert.True(parsed);
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{bad")]
    [InlineData("{\"left\":924}")]
    [InlineData("{\"top\":480}")]
    [InlineData("{\"left\":\"924\",\"top\":480}")]
    [InlineData("{\"left\":null,\"top\":480}")]
    [InlineData("{\"left\":NaN,\"top\":480}")]
    [InlineData("{\"left\":1e999,\"top\":0}")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void TryParse_InvalidJson_ReturnsFalseAndZero(string json)
    {
        bool parsed = WindowPositionStore.TryParse(json, out double left, out double top);

        Assert.False(parsed);
        Assert.Equal(0, left);
        Assert.Equal(0, top);
    }
}

/// <summary>窗口钳制（<see cref="WindowClamp.Clamp"/>）的纯函数测试。</summary>
public class WindowClampTests
{
    private static readonly Rect PrimaryWorkArea = new(0, 0, 1920, 1032);
    private static readonly Rect SecondaryWorkArea = new(1920, -51, 1920, 1032);

    [Fact]
    public void Clamp_PositionInsideWorkArea_IsUnchanged()
    {
        Point result = WindowClamp.Clamp(924, 480, 72, 72, PrimaryWorkArea);

        Assert.Equal(new Point(924, 480), result);
    }

    [Fact]
    public void Clamp_PositionBeyondRightAndBottom_ClampsToBottomRight()
    {
        Point result = WindowClamp.Clamp(5000, 5000, 72, 72, PrimaryWorkArea);

        Assert.Equal(new Point(1848, 960), result);
    }

    [Fact]
    public void Clamp_NegativePosition_ClampsToTopLeft()
    {
        Point result = WindowClamp.Clamp(-50, -20, 72, 72, PrimaryWorkArea);

        Assert.Equal(new Point(0, 0), result);
    }

    [Fact]
    public void Clamp_WorkAreaSmallerThanWindow_ReturnsWorkAreaOrigin()
    {
        var tinyArea = new Rect(10, 20, 50, 50);

        Point result = WindowClamp.Clamp(500, 500, 72, 72, tinyArea);

        Assert.Equal(new Point(10, 20), result);
    }

    [Fact]
    public void Clamp_DeadZonePointWithOffsetWorkArea_MovesIntoThatWorkArea()
    {
        // 双屏错位死区：点 (0,-51) 不属于任何显示器；调用方传入最近显示器工作区后应完整落入。
        Point result = WindowClamp.Clamp(0, -51, 72, 72, SecondaryWorkArea);

        Assert.Equal(new Point(1920, -51), result);
    }

    [Fact]
    public void Clamp_NormalSecondaryPosition_IsUnchanged()
    {
        Point result = WindowClamp.Clamp(3000, 300, 72, 72, SecondaryWorkArea);

        Assert.Equal(new Point(3000, 300), result);
    }
}

/// <summary>位置 JSON 中贴边字段（sprint-24）的解析测试。</summary>
public class WindowPositionStoreEdgeTests
{
    [Fact]
    public void TryParse_OldFormatWithoutEdge_TreatsAsNotDocked()
    {
        bool parsed = WindowPositionStore.TryParse("{\"left\":924,\"top\":480}", out double left, out double top, out DockEdge edge);

        Assert.True(parsed);
        Assert.Equal(924, left);
        Assert.Equal(480, top);
        Assert.Equal(DockEdge.None, edge);
    }

    [Theory]
    [InlineData("left", DockEdge.Left)]
    [InlineData("right", DockEdge.Right)]
    [InlineData("top", DockEdge.Top)]
    [InlineData("bottom", DockEdge.Bottom)]
    [InlineData("RIGHT", DockEdge.Right)]
    [InlineData("Top", DockEdge.Top)]
    [InlineData("none", DockEdge.None)]
    public void TryParse_ValidEdge_ReturnsThatEdge(string rawEdge, DockEdge expected)
    {
        string json = $"{{\"left\":924,\"top\":480,\"edge\":\"{rawEdge}\"}}";

        bool parsed = WindowPositionStore.TryParse(json, out double left, out double top, out DockEdge edge);

        Assert.True(parsed);
        Assert.Equal(924, left);
        Assert.Equal(480, top);
        Assert.Equal(expected, edge);
    }

    [Theory]
    [InlineData("\"diagonal\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("3")]
    [InlineData("{\"side\":\"left\"}")]
    [InlineData("true")]
    public void TryParse_UnknownEdge_KeepsPositionAndFallsBackToNotDocked(string rawEdge)
    {
        string json = $"{{\"left\":924,\"top\":480,\"edge\":{rawEdge}}}";

        bool parsed = WindowPositionStore.TryParse(json, out double left, out double top, out DockEdge edge);

        Assert.True(parsed);
        Assert.Equal(924, left);
        Assert.Equal(480, top);
        Assert.Equal(DockEdge.None, edge);
    }

    [Fact]
    public void TryParse_InvalidPositionWithEdge_ReturnsFalseAndNotDocked()
    {
        bool parsed = WindowPositionStore.TryParse("{\"left\":\"924\",\"top\":480,\"edge\":\"left\"}", out double left, out double top, out DockEdge edge);

        Assert.False(parsed);
        Assert.Equal(0, left);
        Assert.Equal(0, top);
        Assert.Equal(DockEdge.None, edge);
    }
}

/// <summary>
/// 落盘内容的口径测试（rev-24 F1）：贴边态必须写「展开位置 + 边」，
/// 不能把屏外的收起坐标当普通位置写进 <c>window.json</c>。
/// </summary>
/// <remarks>
/// 窗口类的 <c>SavePosition()</c> 只做一件事：用当前状态调用 <see cref="WindowPositionStore.ComposeSavedPosition"/>。
/// 所以这里直接钉住那个纯函数与序列化结果，等价于钉住「贴边态落盘内容」这个不变量。
/// </remarks>
public class WindowPositionStoreSavedPositionTests
{
    [Theory]
    [InlineData(DockEdge.Left, 0, 480, -56, 480, 0, 480)]      // 左贴边收起时窗口在 x=-56
    [InlineData(DockEdge.Right, 1848, 480, 1904, 480, 1848, 480)] // 右贴边收起时窗口在 x=1904
    [InlineData(DockEdge.Top, 924, 0, 924, -56, 924, 0)]        // 上贴边收起时窗口在 y=-56
    [InlineData(DockEdge.Bottom, 924, 960, 924, 1016, 924, 960)] // 下贴边收起时窗口在 y=1016
    public void ComposeSavedPosition_Docked_SavesExpandedPositionNotOffscreenDock(
        DockEdge edge,
        double expandedLeft,
        double expandedTop,
        double currentLeft,
        double currentTop,
        double expectedLeft,
        double expectedTop)
    {
        // 传进去的 current 是屏外的收起坐标；落盘必须换成展开位置 + 边。
        var saved = WindowPositionStore.ComposeSavedPosition(edge, expandedLeft, expandedTop, currentLeft, currentTop);

        Assert.Equal(expectedLeft, saved.Left);
        Assert.Equal(expectedTop, saved.Top);
        Assert.Equal(edge, saved.Edge);
    }

    [Fact]
    public void ComposeSavedPosition_NotDocked_SavesCurrentPosition()
    {
        var saved = WindowPositionStore.ComposeSavedPosition(
            DockEdge.None, expandedLeft: 0, expandedTop: 0, currentLeft: 924, currentTop: 480);

        Assert.Equal(924, saved.Left);
        Assert.Equal(480, saved.Top);
        Assert.Equal(DockEdge.None, saved.Edge);
    }

    [Theory]
    [InlineData(DockEdge.Left, "left")]
    [InlineData(DockEdge.Right, "right")]
    [InlineData(DockEdge.Top, "top")]
    [InlineData(DockEdge.Bottom, "bottom")]
    public void Serialize_Docked_WritesEdgeAndRoundTrips(DockEdge edge, string rawEdge)
    {
        var saved = WindowPositionStore.ComposeSavedPosition(edge, 100, 200, -56, 200);

        string json = WindowPositionStore.Serialize(saved);

        Assert.Contains($"\"edge\":\"{rawEdge}\"", json);
        Assert.True(WindowPositionStore.TryParse(json, out double left, out double top, out DockEdge parsedEdge));
        Assert.Equal(100, left);
        Assert.Equal(200, top);
        Assert.Equal(edge, parsedEdge);
    }

    [Fact]
    public void Serialize_NotDocked_KeepsLegacyFormatWithoutEdge()
    {
        var saved = WindowPositionStore.ComposeSavedPosition(DockEdge.None, 0, 0, 924, 480);

        string json = WindowPositionStore.Serialize(saved);

        Assert.DoesNotContain("edge", json);
        Assert.True(WindowPositionStore.TryParse(json, out double left, out double top, out DockEdge edge));
        Assert.Equal(924, left);
        Assert.Equal(480, top);
        Assert.Equal(DockEdge.None, edge);
    }
}
