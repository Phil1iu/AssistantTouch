namespace AssistantTouch;

/// <summary>快捷项名称的校验与规范化（纯函数，便于单测）。</summary>
internal static class ShortcutName
{
    public const int MaxLength = 20;

    /// <summary>去首尾空格后：非空且长度 ≤20。失败时返回中文提示。</summary>
    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = (raw ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            error = "名称不能为空";
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            error = $"名称不能超过 {MaxLength} 个字符";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
