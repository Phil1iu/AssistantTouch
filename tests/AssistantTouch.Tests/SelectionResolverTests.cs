using Xunit;

namespace AssistantTouch.Tests;

/// <summary>重排/删除后键盘选中项跟随（<see cref="SelectionResolver.Resolve"/>）的纯函数测试。</summary>
public class SelectionResolverTests
{
    private static ShortcutDefinition Item(string name)
    {
        return new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = $@"C:\{name}.exe",
            Name = name
        };
    }

    [Fact]
    public void Resolve_Reordered_FollowsOriginalItem()
    {
        ShortcutDefinition a = Item("a");
        ShortcutDefinition b = Item("b");
        ShortcutDefinition c = Item("c");

        var slots = new ShortcutDefinition?[] { b, c, a };

        Assert.Equal(2, SelectionResolver.Resolve(true, 0, a, slots));
    }

    [Fact]
    public void Resolve_SelectedItemDeleted_ClampsToPreviousIndex()
    {
        ShortcutDefinition a = Item("a");
        ShortcutDefinition b = Item("b");
        ShortcutDefinition c = Item("c");

        var slots = new ShortcutDefinition?[] { a, c };

        Assert.Equal(1, SelectionResolver.Resolve(true, 1, b, slots));
    }

    [Fact]
    public void Resolve_SelectedLastItemDeleted_ClampsToNewLast()
    {
        ShortcutDefinition a = Item("a");
        ShortcutDefinition b = Item("b");
        ShortcutDefinition c = Item("c");

        var slots = new ShortcutDefinition?[] { a, b };

        Assert.Equal(1, SelectionResolver.Resolve(true, 2, c, slots));
    }

    [Fact]
    public void Resolve_EmptySlots_ReturnsMinusOne()
    {
        ShortcutDefinition a = Item("a");

        Assert.Equal(-1, SelectionResolver.Resolve(true, 0, a, Array.Empty<ShortcutDefinition?>()));
        Assert.Equal(-1, SelectionResolver.Resolve(false, 3, null, Array.Empty<ShortcutDefinition?>()));
    }

    [Fact]
    public void Resolve_SelectionInactive_OnlyClampsIndex()
    {
        ShortcutDefinition a = Item("a");
        ShortcutDefinition b = Item("b");
        var slots = new ShortcutDefinition?[] { a, b };

        Assert.Equal(1, SelectionResolver.Resolve(false, 1, a, slots));
        Assert.Equal(1, SelectionResolver.Resolve(false, 9, a, slots));
        Assert.Equal(0, SelectionResolver.Resolve(false, -3, a, slots));
    }

    [Fact]
    public void Resolve_PlusSelected_FollowsNullSlot()
    {
        ShortcutDefinition a = Item("a");
        ShortcutDefinition b = Item("b");

        Assert.Equal(2, SelectionResolver.Resolve(true, 2, null, new ShortcutDefinition?[] { a, b, null }));
        Assert.Equal(3, SelectionResolver.Resolve(true, 2, null, new ShortcutDefinition?[] { a, b, Item("c"), null }));
        Assert.Equal(0, SelectionResolver.Resolve(true, 1, null, new ShortcutDefinition?[] { null }));
    }

    [Fact]
    public void Resolve_UsesReferenceIdentity_NotValueEquality()
    {
        ShortcutDefinition selected = Item("same");
        ShortcutDefinition replacement = Item("same");

        var slots = new ShortcutDefinition?[] { replacement };

        // 名称相同但实例不同（例如重命名替换后的新对象）：按引用找不到 → clamp。
        Assert.Equal(0, SelectionResolver.Resolve(true, 0, selected, slots));
    }

    [Fact]
    public void Resolve_PlusSelectedButNoPlusSlot_Clamps()
    {
        ShortcutDefinition a = Item("a");
        ShortcutDefinition b = Item("b");

        Assert.Equal(1, SelectionResolver.Resolve(true, 5, null, new ShortcutDefinition?[] { a, b }));
    }
}
