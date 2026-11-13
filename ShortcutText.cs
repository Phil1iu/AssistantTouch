using System.Globalization;
using System.Text;

namespace AssistantTouch;

/// <summary>按文本元素（grapheme cluster）截断，避免把 emoji/组合字符切成半个。</summary>
internal static class ShortcutText
{
    public static string TruncateTextElements(string? text, int maxElements)
    {
        if (string.IsNullOrEmpty(text) || maxElements <= 0)
        {
            return string.Empty;
        }

        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        var builder = new StringBuilder();

        int count = 0;
        while (count < maxElements && enumerator.MoveNext())
        {
            builder.Append(enumerator.GetTextElement());
            count++;
        }

        return builder.ToString();
    }
}
