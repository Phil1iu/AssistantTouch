using Xunit;

namespace AssistantTouch.Tests;

/// <summary>系统功能目录、默认项与添加判定的纯函数测试。</summary>
public class SystemShortcutCatalogTests
{
    private static readonly (string Action, string Name)[] ExpectedEntries =
    {
        ("showDesktop", "显示桌面"),
        ("screenshot", "截图"),
        ("taskManager", "任务管理器"),
        ("settings", "设置"),
        ("explorer", "文件资源管理器"),
        ("lock", "锁定电脑")
    };

    [Fact]
    public void Catalog_HasSixEntriesInDefaultOrder()
    {
        Assert.Equal(6, SystemShortcutCatalog.All.Count);

        for (int i = 0; i < ExpectedEntries.Length; i++)
        {
            Assert.Equal(ExpectedEntries[i].Action, SystemShortcutCatalog.All[i].Action);
            Assert.Equal(ExpectedEntries[i].Name, SystemShortcutCatalog.All[i].Name);
        }
    }

    [Fact]
    public void CreateDefaults_MatchesCatalogExactly()
    {
        List<ShortcutDefinition> defaults = ShortcutDefinition.CreateDefaults();

        Assert.Equal(6, defaults.Count);

        for (int i = 0; i < ExpectedEntries.Length; i++)
        {
            Assert.Equal(ShortcutKind.System, defaults[i].Kind);
            Assert.Equal(ExpectedEntries[i].Action, defaults[i].Action);
            Assert.Equal(ExpectedEntries[i].Name, defaults[i].Name);
        }
    }

    [Fact]
    public void ContainsSystemAction_OnlyMatchesSystemKind()
    {
        List<ShortcutDefinition> defaults = ShortcutDefinition.CreateDefaults();

        Assert.True(SystemShortcutCatalog.ContainsSystemAction(defaults, "showDesktop"));
        Assert.False(SystemShortcutCatalog.ContainsSystemAction(defaults, "unknown"));
        Assert.False(SystemShortcutCatalog.ContainsSystemAction(Array.Empty<ShortcutDefinition>(), "showDesktop"));

        var appWithSameAction = new ShortcutDefinition
        {
            Kind = ShortcutKind.App,
            Action = "showDesktop",
            Path = @"C:\app.exe",
            Name = "app"
        };

        Assert.False(SystemShortcutCatalog.ContainsSystemAction(new[] { appWithSameAction }, "showDesktop"));
    }

    private static List<ShortcutDefinition> Apps(int count)
    {
        var list = new List<ShortcutDefinition>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(new ShortcutDefinition
            {
                Kind = ShortcutKind.App,
                Path = $@"C:\app{i}.exe",
                Name = $"app{i}"
            });
        }

        return list;
    }

    [Fact]
    public void EvaluateAdd_EmptyAndOneItem()
    {
        Assert.Equal(
            SystemShortcutAddDecision.Allowed,
            SystemShortcutCatalog.EvaluateAdd(new List<ShortcutDefinition>(), "showDesktop"));

        List<ShortcutDefinition> one = ShortcutDefinition.CreateDefaults().Take(1).ToList();

        Assert.Equal(
            SystemShortcutAddDecision.AlreadyAdded,
            SystemShortcutCatalog.EvaluateAdd(one, "showDesktop"));
        Assert.Equal(
            SystemShortcutAddDecision.Allowed,
            SystemShortcutCatalog.EvaluateAdd(one, "screenshot"));
    }

    [Theory]
    [InlineData(0, 0)] // Allowed
    [InlineData(1, 0)] // Allowed
    [InlineData(7, 0)] // Allowed
    [InlineData(8, 2)] // LimitReached
    public void EvaluateAdd_RespectsLimit(int count, int expectedValue)
    {
        // 全部是 App 项，不含任何系统动作，因此目标动作一定是“未添加”。
        var expected = (SystemShortcutAddDecision)expectedValue;
        Assert.Equal(expected, SystemShortcutCatalog.EvaluateAdd(Apps(count), "showDesktop"));
    }

    [Fact]
    public void EvaluateAdd_AlreadyAddedTakesPrecedenceAtLimit()
    {
        // 8 项：5 个系统项 + 3 个 App；lock 未添加。
        List<ShortcutDefinition> list = ShortcutDefinition.CreateDefaults().Take(5).ToList();
        list.AddRange(Apps(3));
        Assert.Equal(8, list.Count);

        Assert.Equal(
            SystemShortcutAddDecision.AlreadyAdded,
            SystemShortcutCatalog.EvaluateAdd(list, "showDesktop"));

        Assert.Equal(
            SystemShortcutAddDecision.LimitReached,
            SystemShortcutCatalog.EvaluateAdd(list, "lock"));
    }

    [Fact]
    public void EvaluateAdd_UnknownAction_ReturnsUnknown()
    {
        Assert.Equal(
            SystemShortcutAddDecision.UnknownAction,
            SystemShortcutCatalog.EvaluateAdd(new List<ShortcutDefinition>(), "not-a-system-action"));
        Assert.Equal(
            SystemShortcutAddDecision.UnknownAction,
            SystemShortcutCatalog.EvaluateAdd(Apps(8), "not-a-system-action"));
    }
}
