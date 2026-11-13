using Xunit;

namespace AssistantTouch.Tests;

/// <summary>按文本元素截断（emoji/组合字符不切半）的纯函数测试。</summary>
public class ShortcutTextTests
{
    [Theory]
    [InlineData("ab", "ab")]
    [InlineData("abcdef", "ab")]
    [InlineData("桌面", "桌面")]
    [InlineData("桌面图标", "桌面")]
    [InlineData("😀😀😀", "😀😀")]
    [InlineData("👨‍👩‍👧‍👦ABC", "👨‍👩‍👧‍👦A")]
    [InlineData("éx", "éx")]
    public void TruncateTextElements_TwoElements(string text, string expected)
    {
        Assert.Equal(expected, ShortcutText.TruncateTextElements(text, 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TruncateTextElements_Empty(string? text)
    {
        Assert.Equal(string.Empty, ShortcutText.TruncateTextElements(text, 2));
    }

    [Fact]
    public void TruncateTextElements_NonPositiveMax_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ShortcutText.TruncateTextElements("abc", 0));
        Assert.Equal(string.Empty, ShortcutText.TruncateTextElements("abc", -1));
    }
}
