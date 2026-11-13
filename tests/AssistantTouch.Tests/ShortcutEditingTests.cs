using System.Text;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>快捷项排序 / 交换 / 重命名的存储层测试（使用内存 Save 钩子，不写真实配置）。</summary>
public class ShortcutEditingTests
{
    private static List<ShortcutDefinition> CreateList(int count)
    {
        var list = ShortcutDefinition.CreateDefaults();
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

    private static string[] Names(IEnumerable<ShortcutDefinition> items)
    {
        return items.Select(item => item.Name).ToArray();
    }

    private sealed class SaveCapture
    {
        public int Count { get; private set; }

        public List<ShortcutDefinition> Last { get; private set; } = new();

        public Action<IReadOnlyList<ShortcutDefinition>> Hook => items =>
        {
            Count++;
            Last = items.ToList();
        };
    }

    [Fact]
    public void Move_DownThenUp_ChangesOrderAndSaves()
    {
        var capture = new SaveCapture();
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3), capture.Hook);
        ShortcutDefinition first = store.Shortcuts[0];

        Assert.True(store.Move(first, 1));
        Assert.Equal(new[] { "截图", "显示桌面", "任务管理器" }, Names(store.Shortcuts));
        Assert.Equal(1, capture.Count);
        Assert.Equal(Names(store.Shortcuts), Names(capture.Last));

        Assert.True(store.Move(first, -1));
        Assert.Equal("显示桌面", store.Shortcuts[0].Name);
        Assert.Equal(2, capture.Count);
    }

    [Fact]
    public void Move_BoundariesZeroAndUnknown_ReturnFalseWithoutSaving()
    {
        var capture = new SaveCapture();
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3), capture.Hook);
        var unknown = new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = @"C:\unknown.exe",
            Name = "unknown"
        };

        Assert.False(store.Move(store.Shortcuts[0], -1));
        Assert.False(store.Move(store.Shortcuts[^1], 1));
        Assert.False(store.Move(store.Shortcuts[1], 0));
        Assert.False(store.Move(unknown, 1));
        Assert.Equal(0, capture.Count);
        Assert.Equal(new[] { "显示桌面", "截图", "任务管理器" }, Names(store.Shortcuts));
    }

    [Fact]
    public void Move_EmptyAndSingleItem_ReturnFalse()
    {
        ShortcutStore empty = ShortcutStore.CreateForTests(Array.Empty<ShortcutDefinition>());
        var item = new ShortcutDefinition { Kind = ShortcutKind.App, Path = @"C:\a.exe", Name = "a" };
        Assert.False(empty.Move(item, 1));

        ShortcutStore single = ShortcutStore.CreateForTests(CreateList(1));
        Assert.False(single.Move(single.Shortcuts[0], -1));
        Assert.False(single.Move(single.Shortcuts[0], 1));
    }

    [Fact]
    public void Move_EightItems_WorksAtBothEnds()
    {
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(8));

        Assert.True(store.Move(store.Shortcuts[^1], -1));
        Assert.Equal("app7", store.Shortcuts[6].Name);
        Assert.Equal("app6", store.Shortcuts[7].Name);

        Assert.True(store.Move(store.Shortcuts[0], 1));
        Assert.Equal("截图", store.Shortcuts[0].Name);
        Assert.Equal("显示桌面", store.Shortcuts[1].Name);
    }

    [Fact]
    public void Swap_ExchangesItemsAndSaves()
    {
        var capture = new SaveCapture();
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3), capture.Hook);
        ShortcutDefinition first = store.Shortcuts[0];
        ShortcutDefinition last = store.Shortcuts[2];

        Assert.True(store.Swap(first, last));
        Assert.Equal(new[] { "任务管理器", "截图", "显示桌面" }, Names(store.Shortcuts));
        Assert.Equal(1, capture.Count);
        Assert.Equal(Names(store.Shortcuts), Names(capture.Last));
    }

    [Fact]
    public void Swap_SameOrUnknown_ReturnsFalse()
    {
        var capture = new SaveCapture();
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3), capture.Hook);
        var unknown = new ShortcutDefinition { Kind = ShortcutKind.App, Path = @"C:\u.exe", Name = "u" };

        Assert.False(store.Swap(store.Shortcuts[0], store.Shortcuts[0]));
        Assert.False(store.Swap(store.Shortcuts[0], unknown));
        Assert.False(store.Swap(unknown, store.Shortcuts[0]));
        Assert.Equal(0, capture.Count);
    }

    [Fact]
    public void Rename_TrimsAndPreservesIdentity()
    {
        var capture = new SaveCapture();
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3), capture.Hook);
        ShortcutDefinition item = store.Shortcuts[0];

        Assert.True(store.Rename(item, "  新桌面  "));

        ShortcutDefinition renamed = store.Shortcuts[0];
        Assert.Equal("新桌面", renamed.Name);
        Assert.Equal(ShortcutKind.System, renamed.Kind);
        Assert.Equal("showDesktop", renamed.Action);
        Assert.Equal(1, capture.Count);
    }

    [Fact]
    public void Rename_App_PreservesPath()
    {
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(7));
        ShortcutDefinition app = store.Shortcuts[6];
        string path = app.Path;

        Assert.True(store.Rename(app, "记事本"));
        Assert.Equal(ShortcutKind.App, store.Shortcuts[6].Kind);
        Assert.Equal(path, store.Shortcuts[6].Path);
        Assert.Equal("记事本", store.Shortcuts[6].Name);
    }

    [Fact]
    public void Rename_UnchangedName_ReturnsFalseWithoutSaving()
    {
        var capture = new SaveCapture();
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3), capture.Hook);
        ShortcutDefinition item = store.Shortcuts[0];

        Assert.False(store.Rename(item, item.Name));
        Assert.Equal(0, capture.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901")]
    public void Rename_InvalidNames_ReturnFalseAndKeepOldName(string? newName)
    {
        var capture = new SaveCapture();
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3), capture.Hook);
        ShortcutDefinition item = store.Shortcuts[0];
        string oldName = item.Name;

        Assert.False(store.Rename(item, newName));
        Assert.Equal(oldName, store.Shortcuts[0].Name);
        Assert.Equal(0, capture.Count);
    }

    [Fact]
    public void MovedOrder_RoundTripsThroughSerialize()
    {
        List<ShortcutDefinition>? saved = null;
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(4), items => saved = items.ToList());

        ShortcutDefinition first = store.Shortcuts[0];
        Assert.True(store.Move(first, 2));
        Assert.NotNull(saved);

        string json = Encoding.UTF8.GetString(ShortcutStore.Serialize(saved!));
        Assert.True(ShortcutStore.TryParse(json, out List<ShortcutDefinition>? parsed));

        Assert.Equal(Names(store.Shortcuts), Names(parsed!));
    }

    [Fact]
    public void EditMethods_AfterRename_OldReferenceReturnsFalse()
    {
        ShortcutStore store = ShortcutStore.CreateForTests(CreateList(3));
        ShortcutDefinition old = store.Shortcuts[0];

        Assert.True(store.Rename(old, "新名字"));

        ShortcutDefinition current = store.Shortcuts[0];
        Assert.NotSame(old, current);

        Assert.False(store.Rename(old, "再次改名"));
        Assert.False(store.Replace(old, new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Path = @"C:\replacement.exe",
            Name = "replacement"
        }));
        Assert.False(store.Remove(old));
        Assert.False(store.Move(old, 1));
        Assert.False(store.Swap(old, store.Shortcuts[1]));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("  abc  ", "abc")]
    [InlineData("  a b  ", "a b")]
    [InlineData("12345678901234567890", "12345678901234567890")]
    public void ShortcutName_TryNormalize_Valid(string raw, string expected)
    {
        Assert.True(ShortcutName.TryNormalize(raw, out string normalized, out string error));
        Assert.Equal(expected, normalized);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901")]
    public void ShortcutName_TryNormalize_Invalid(string? raw)
    {
        Assert.False(ShortcutName.TryNormalize(raw, out _, out string error));
        Assert.NotEmpty(error);
    }
}
