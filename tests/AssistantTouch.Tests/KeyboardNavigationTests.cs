using System.Windows.Input;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>径向菜单键盘选择状态机（<see cref="KeyboardNavigation.Next"/>）的纯函数测试。</summary>
public class KeyboardNavigationTests
{
    [Theory]
    [InlineData(0, 5, Key.Right, 1)]
    [InlineData(3, 5, Key.Right, 4)]
    [InlineData(4, 5, Key.Right, 0)]
    [InlineData(0, 5, Key.Down, 1)]
    [InlineData(4, 5, Key.Down, 0)]
    [InlineData(0, 5, Key.Left, 4)]
    [InlineData(3, 5, Key.Left, 2)]
    [InlineData(0, 5, Key.Up, 4)]
    [InlineData(3, 5, Key.Home, 0)]
    [InlineData(1, 5, Key.End, 4)]
    public void Next_NavigatesAndWraps(int current, int count, Key key, int expected)
    {
        Assert.Equal((int?)expected, KeyboardNavigation.Next(current, count, key));
    }

    [Theory]
    [InlineData(Key.Left)]
    [InlineData(Key.Up)]
    [InlineData(Key.Right)]
    [InlineData(Key.Down)]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    public void Next_SingleItem_ReturnsZero(Key key)
    {
        Assert.Equal((int?)0, KeyboardNavigation.Next(0, 1, key));
    }

    [Theory]
    [InlineData(Key.Left)]
    [InlineData(Key.Up)]
    [InlineData(Key.Right)]
    [InlineData(Key.Down)]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    public void Next_NoItems_ReturnsNull(Key key)
    {
        Assert.Null(KeyboardNavigation.Next(0, 0, key));
    }

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    [InlineData(Key.Escape)]
    public void Next_NonNavigationKey_ReturnsNull(Key key)
    {
        Assert.Null(KeyboardNavigation.Next(2, 5, key));
    }

    [Fact]
    public void Next_OutOfRangeCurrent_NormalizesToFirst()
    {
        Assert.Equal((int?)1, KeyboardNavigation.Next(-1, 4, Key.Right));
        Assert.Equal((int?)3, KeyboardNavigation.Next(9, 4, Key.Left));
        Assert.Equal((int?)0, KeyboardNavigation.Next(9, 4, Key.Home));
    }
}
