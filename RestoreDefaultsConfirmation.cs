namespace AssistantTouch;

/// <summary>
/// 「恢复默认」二次确认的结果映射（纯逻辑，便于单测）：
/// 只有对话框返回 true（点「恢复」）才执行恢复；取消 / Esc / 关闭都不改数据。
/// </summary>
internal static class RestoreDefaultsConfirmation
{
    public static bool Apply(bool? dialogResult, Action resetToDefaults)
    {
        if (dialogResult != true)
        {
            return false;
        }

        resetToDefaults();
        return true;
    }
}
