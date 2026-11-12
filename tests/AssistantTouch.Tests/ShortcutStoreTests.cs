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
