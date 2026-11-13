using System.Windows;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>「要画哪些圆」映射（<see cref="RadialMenuComposer.Build"/>）的纯函数测试。</summary>
public class RadialMenuComposerTests
{
    private static readonly Rect FullHd = new(0, 0, 1920, 1080);

    private static List<ShortcutDefinition> CreateShortcuts(int count)
    {
        List<ShortcutDefinition> list = SystemShortcutCatalog.CreateDefaults();

        for (int i = list.Count; i < count; i++)
        {
            list.Add(new ShortcutDefinition
            {
                Kind = ShortcutKind.App,
                Path = $@"C:\app{i}.exe",
                Name = $"app{i}"
            });
        }

        return list.Take(count).ToList();
    }

    [Theory]
    [InlineData(6, 7)] // 6 快捷 + 「+」
    [InlineData(8, 9)] // 8 快捷 + 「+」（上限）
    public void Build_AllPlaced_DrawsShortcutsInOrderAndPlusLast(int shortcutCount, int total)
    {
        List<ShortcutDefinition> shortcuts = CreateShortcuts(shortcutCount);
        RadialLayoutResult layout = RadialLayout.Compute(new Point(960, 540), FullHd, total);

        Assert.True(layout.AllPlaced);
        Assert.Equal(total, layout.Centers.Count);

        IReadOnlyList<RadialDrawItem> items = RadialMenuComposer.Build(layout, shortcuts, plusFallback: null);

        Assert.Equal(total, items.Count);

        for (int i = 0; i < shortcutCount; i++)
        {
            Assert.Equal(RadialDrawKind.Shortcut, items[i].Kind);
            Assert.Same(shortcuts[i], items[i].Shortcut);
            Assert.Equal(layout.Centers[i], items[i].Center);
        }

        RadialDrawItem plus = items[^1];
        Assert.Equal(RadialDrawKind.Plus, plus.Kind);
        Assert.Null(plus.Shortcut);
        Assert.Equal(layout.Centers[shortcutCount], plus.Center);
    }

    [Fact]
    public void Build_Partial_DrawsOnlyPlus()
    {
        var area = new Rect(0, 0, 180, 180);
        var ball = new Point(90, 90);
        List<ShortcutDefinition> shortcuts = CreateShortcuts(8);
        RadialLayoutResult layout = RadialLayout.Compute(ball, area, 9);

        Assert.False(layout.AllPlaced);
        Assert.NotEmpty(layout.Centers);

        Point? fallback = RadialLayout.ChooseFallbackCenter(
            ball, area, RadialLayout.DefaultCircleSize, RadialLayout.DefaultMinBallDistance, layout.Centers, out _);
        Assert.NotNull(fallback);

        IReadOnlyList<RadialDrawItem> items = RadialMenuComposer.Build(layout, shortcuts, fallback);

        Assert.Single(items);
        Assert.Equal(RadialDrawKind.Plus, items[0].Kind);
        Assert.Null(items[0].Shortcut);
        Assert.Equal(fallback!.Value, items[0].Center);
    }

    [Fact]
    public void Build_PartialWithoutFallback_DrawsNothing()
    {
        var area = new Rect(0, 0, 40, 40);
        var ball = new Point(20, 20);
        List<ShortcutDefinition> shortcuts = CreateShortcuts(8);
        RadialLayoutResult layout = RadialLayout.Compute(ball, area, 9);

        Assert.False(layout.AllPlaced);
        Assert.Empty(layout.Centers);

        Point? fallback = RadialLayout.ChooseFallbackCenter(
            ball, area, RadialLayout.DefaultCircleSize, RadialLayout.DefaultMinBallDistance, layout.Centers, out _);
        Assert.Null(fallback);

        Assert.Empty(RadialMenuComposer.Build(layout, shortcuts, fallback));
    }

    [Fact]
    public void Build_DoubleArc_PreservesOrderAndPlus()
    {
        var ball = new Point(0, 0);
        List<ShortcutDefinition> shortcuts = CreateShortcuts(8);
        RadialLayoutResult layout = RadialLayout.Compute(ball, FullHd, 9);

        Assert.True(layout.AllPlaced);
        Assert.Equal(RadialLayoutMode.DoubleArc, layout.Mode);

        IReadOnlyList<RadialDrawItem> items = RadialMenuComposer.Build(layout, shortcuts, plusFallback: null);

        Assert.Equal(9, items.Count);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(RadialDrawKind.Shortcut, items[i].Kind);
            Assert.Same(shortcuts[i], items[i].Shortcut);
            Assert.Equal(layout.Centers[i], items[i].Center);
        }

        Assert.Equal(RadialDrawKind.Plus, items[^1].Kind);
        Assert.Equal(layout.Centers[8], items[^1].Center);
    }

    [Fact]
    public void Build_AllPlacedButNoPlusCenter_UsesFallback()
    {
        List<ShortcutDefinition> shortcuts = CreateShortcuts(2);
        var fakeLayout = new RadialLayoutResult(
            new[] { new Point(10, 10) },
            RadialLayoutMode.FullRing,
            allPlaced: true,
            diagnostic: "fake-single-center");

        IReadOnlyList<RadialDrawItem> items = RadialMenuComposer.Build(
            fakeLayout,
            shortcuts,
            new Point(99, 99));

        Assert.Equal(2, items.Count);
        Assert.Equal(RadialDrawKind.Shortcut, items[0].Kind);
        Assert.Same(shortcuts[0], items[0].Shortcut);
        Assert.Equal(new Point(10, 10), items[0].Center);
        Assert.Equal(RadialDrawKind.Plus, items[1].Kind);
        Assert.Equal(new Point(99, 99), items[1].Center);
    }
}
