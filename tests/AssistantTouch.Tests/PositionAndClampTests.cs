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
