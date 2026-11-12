using System.Text;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>快捷方式配置的解析/序列化测试（与 Save 使用的 JSON 完全一致）。</summary>
public class ShortcutStoreTests
{
    private static ShortcutDefinition App(string name, string path)
    {
        return new ShortcutDefinition { Kind = ShortcutKind.App, Path = path, Name = name };
    }

    [Fact]
    public void TryParse_SevenEntries_SystemAndApp_Succeeds()
    {
        List<ShortcutDefinition> list = ShortcutDefinition.CreateDefaults();
        list.Add(App("notepad", @"C:\Windows\System32\notepad.exe"));

        string json = Encoding.UTF8.GetString(ShortcutStore.Serialize(list));

        Assert.True(ShortcutStore.TryParse(json, out List<ShortcutDefinition>? parsed));
        Assert.NotNull(parsed);
        Assert.Equal(7, parsed!.Count);
        Assert.Equal(ShortcutKind.App, parsed[6].Kind);
        Assert.Equal(@"C:\Windows\System32\notepad.exe", parsed[6].Path);
        Assert.Equal("notepad", parsed[6].Name);
    }

    [Fact]
    public void TryParse_EightEntries_Succeeds()
    {
        List<ShortcutDefinition> list = ShortcutDefinition.CreateDefaults();
        list.Add(App("notepad", @"C:\Windows\System32\notepad.exe"));
        list.Add(App("paint", @"C:\Windows\System32\mspaint.exe"));

        Assert.True(ShortcutStore.TryParse(Encoding.UTF8.GetString(ShortcutStore.Serialize(list)), out List<ShortcutDefinition>? parsed));
        Assert.Equal(8, parsed!.Count);
    }

    [Fact]
    public void TryParse_NineEntries_ReturnsFalse()
    {
        var list = new List<ShortcutDefinition>();
        for (int i = 0; i < 9; i++)
        {
            list.Add(App($"app{i}", $@"C:\app{i}.exe"));
        }

        Assert.False(ShortcutStore.TryParse(Encoding.UTF8.GetString(ShortcutStore.Serialize(list)), out _));
    }

    [Theory]
    [InlineData("{\"shortcuts\":[{\"kind\":\"system\",\"action\":\"unknown\",\"name\":\"X\"}]}")]
    [InlineData("{\"shortcuts\":[{\"kind\":\"app\",\"path\":\"\",\"name\":\"X\"}]}")]
    [InlineData("{\"shortcuts\":[{\"kind\":\"app\",\"path\":\"C:\\\\x.exe\",\"name\":\"\"}]}")]
    [InlineData("{\"shortcuts\":[]")]
    [InlineData("{bad json")]
    public void TryParse_InvalidEntries_ReturnsFalse(string json)
    {
        Assert.False(ShortcutStore.TryParse(json, out _));
    }

    /// <summary>手写的紧凑（单行）配置：6 个系统快捷 + 1 个 app，路径按 JSON 规则用双反斜杠。</summary>
    private const string CompactSevenEntryJson =
        "{\"shortcuts\":[" +
        "{\"kind\":\"system\",\"action\":\"showDesktop\",\"name\":\"显示桌面\"}," +
        "{\"kind\":\"system\",\"action\":\"screenshot\",\"name\":\"截图\"}," +
        "{\"kind\":\"system\",\"action\":\"taskManager\",\"name\":\"任务管理器\"}," +
        "{\"kind\":\"system\",\"action\":\"settings\",\"name\":\"设置\"}," +
        "{\"kind\":\"system\",\"action\":\"explorer\",\"name\":\"文件资源管理器\"}," +
        "{\"kind\":\"system\",\"action\":\"lock\",\"name\":\"锁定电脑\"}," +
        "{\"kind\":\"app\",\"path\":\"C:\\\\Windows\\\\System32\\\\notepad.exe\",\"name\":\"notepad\"}" +
        "]}";

    [Fact]
    public void TryParse_CompactSevenEntryJson_Succeeds()
    {
        Assert.True(ShortcutStore.TryParse(CompactSevenEntryJson, out List<ShortcutDefinition>? parsed, out string? error), error);
        Assert.NotNull(parsed);
        Assert.Equal(7, parsed!.Count);
        Assert.Equal(ShortcutKind.App, parsed[6].Kind);
        Assert.Equal(@"C:\Windows\System32\notepad.exe", parsed[6].Path);
        Assert.Equal("notepad", parsed[6].Name);
    }

    [Fact]
    public void TryParse_CompactNineEntries_ReturnsFalse()
    {
        var items = new List<string>();
        for (int i = 0; i < 9; i++)
        {
            items.Add($"{{\"kind\":\"app\",\"path\":\"C:\\\\app{i}.exe\",\"name\":\"app{i}\"}}");
        }

        string json = "{\"shortcuts\":[" + string.Join(",", items) + "]}";

        Assert.False(ShortcutStore.TryParse(json, out _, out string? error));
        Assert.Equal("too-many:9", error);
    }

    [Fact]
    public void TryParse_InvalidPathEscape_ReportsJsonException()
    {
        // 手写时常见的错误：JSON 里路径只写单反斜杠，\W 不是合法转义。
        const string json = "{\"shortcuts\":[{\"kind\":\"app\",\"path\":\"C:\\Windows\\notepad.exe\",\"name\":\"notepad\"}]}";

        Assert.False(ShortcutStore.TryParse(json, out _, out string? error));
        Assert.NotNull(error);
        Assert.Contains("JsonException", error);
    }

    [Fact]
    public void Serialize_RoundTrips_DefaultsPlusOneApp()
    {
        List<ShortcutDefinition> list = ShortcutDefinition.CreateDefaults();
        list.Add(App("notepad", @"C:\Windows\System32\notepad.exe"));

        byte[] bytes = ShortcutStore.Serialize(list);
        Assert.NotEmpty(bytes);

        string text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"shortcuts\"", text);
        Assert.Contains("\"kind\": \"app\"", text);

        Assert.True(ShortcutStore.TryParse(text, out List<ShortcutDefinition>? parsed));
        Assert.Equal(list.Count, parsed!.Count);
        Assert.Equal(list[6].Path, parsed[6].Path);
    }
}
