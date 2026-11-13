using System.Windows;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>背景右键响应范围（球心 240px）的纯函数测试。</summary>
public class RadialMenuInteractionTests
{
    [Theory]
    [InlineData(0, 0, 239, 0, true)]
    [InlineData(0, 0, 240, 0, true)]
    [InlineData(0, 0, 241, 0, false)]
    [InlineData(0, 0, 0, 239, true)]
    [InlineData(0, 0, 0, 240, true)]
    [InlineData(0, 0, 0, 241, false)]
    public void ShouldShowBackgroundMenu_HorizontalAndVerticalBoundaries(
        double ballX, double ballY, double pointerX, double pointerY, bool expected)
    {
        Assert.Equal(
            expected,
            RadialMenuInteraction.ShouldShowBackgroundMenu(
                new Point(ballX, ballY),
                new Point(pointerX, pointerY)));
    }

    [Theory]
    [InlineData(-1920, -51, -1800, -51, true)]  // 距球心 120
    [InlineData(-1920, -51, -1600, -51, false)] // 距球心 320
    [InlineData(-1920, -51, -1920, -291, true)] // 正上方 240
    [InlineData(-1920, -51, -1920, -292, false)]
    public void ShouldShowBackgroundMenu_SupportsNegativeCoordinates(
        double ballX, double ballY, double pointerX, double pointerY, bool expected)
    {
        Assert.Equal(
            expected,
            RadialMenuInteraction.ShouldShowBackgroundMenu(
                new Point(ballX, ballY),
                new Point(pointerX, pointerY)));
    }

    [Fact]
    public void ShouldShowBackgroundMenu_CustomAndNegativeRadius()
    {
        var ball = new Point(10, 20);

        Assert.True(RadialMenuInteraction.ShouldShowBackgroundMenu(ball, new Point(110, 20), 100));
        Assert.False(RadialMenuInteraction.ShouldShowBackgroundMenu(ball, new Point(111, 20), 100));
        Assert.False(RadialMenuInteraction.ShouldShowBackgroundMenu(ball, ball, -1));
        Assert.True(RadialMenuInteraction.ShouldShowBackgroundMenu(ball, ball));
        Assert.Equal(240, RadialMenuInteraction.BackgroundMenuRadius);
    }
}
