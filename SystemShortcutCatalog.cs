namespace AssistantTouch;

/// <summary>系统快捷方式目录项：动作键 + 显示名称。</summary>
internal sealed record SystemShortcut(string Action, string Name);

/// <summary>系统动作的添加判定（纯逻辑，便于单测）。</summary>
internal enum SystemShortcutAddDecision
{
    Allowed,
    AlreadyAdded,
    LimitReached,
    UnknownAction
}

/// <summary>
/// 6 个系统功能的单一数据源：默认快捷项、菜单条目、解析校验都从这里取，
/// 避免动作/名称在多个文件里各写一份。
/// </summary>
internal static class SystemShortcutCatalog
{
    public static IReadOnlyList<SystemShortcut> All { get; } = new List<SystemShortcut>
    {
        new("showDesktop", "显示桌面"),
        new("screenshot", "截图"),
        new("taskManager", "任务管理器"),
        new("settings", "设置"),
        new("explorer", "文件资源管理器"),
        new("lock", "锁定电脑")
    }.AsReadOnly();

    /// <summary>默认 6 个系统快捷方式（顺序与目录一致）。</summary>
    public static List<ShortcutDefinition> CreateDefaults()
    {
        var shortcuts = new List<ShortcutDefinition>(All.Count);

        foreach (SystemShortcut entry in All)
        {
            shortcuts.Add(new ShortcutDefinition
            {
                Kind = ShortcutKind.System,
                Action = entry.Action,
                Name = entry.Name
            });
        }

        return shortcuts;
    }

    /// <summary>按动作取目录中的默认显示名；未知动作返回 null。</summary>
    public static string? GetDefaultName(string? action)
    {
        foreach (SystemShortcut entry in All)
        {
            if (string.Equals(entry.Action, action, StringComparison.Ordinal))
            {
                return entry.Name;
            }
        }

        return null;
    }

    /// <summary>列表中是否已有同 Action 的系统快捷项（App 项不参与）。</summary>
    public static bool ContainsSystemAction(IReadOnlyList<ShortcutDefinition> shortcuts, string? action)
    {
        foreach (ShortcutDefinition shortcut in shortcuts)
        {
            if (shortcut.Kind == ShortcutKind.System &&
                string.Equals(shortcut.Action, action, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>判定能否添加某系统动作；已添加优先于上限，未知动作最后判定。</summary>
    public static SystemShortcutAddDecision EvaluateAdd(
        IReadOnlyList<ShortcutDefinition> shortcuts,
        string? action)
    {
        bool known = false;
        foreach (SystemShortcut entry in All)
        {
            if (string.Equals(entry.Action, action, StringComparison.Ordinal))
            {
                known = true;
                break;
            }
        }

        if (!known)
        {
            return SystemShortcutAddDecision.UnknownAction;
        }

        if (ContainsSystemAction(shortcuts, action))
        {
            return SystemShortcutAddDecision.AlreadyAdded;
        }

        if (shortcuts.Count >= ShortcutStore.MaxShortcuts)
        {
            return SystemShortcutAddDecision.LimitReached;
        }

        return SystemShortcutAddDecision.Allowed;
    }
}
