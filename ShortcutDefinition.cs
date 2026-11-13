namespace AssistantTouch;

/// <summary>快捷方式的种类。</summary>
internal enum ShortcutKind
{
    System,
    App
}

/// <summary>
/// 一个快捷方式定义：系统动作（showDesktop 等）或本地程序（exe/lnk）。
/// </summary>
internal sealed class ShortcutDefinition
{
    public ShortcutKind Kind { get; init; }

    /// <summary>系统动作键；仅 <see cref="ShortcutKind.System"/> 使用。</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>本地程序完整路径；仅 <see cref="ShortcutKind.App"/> 使用。</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>菜单/提示中显示的完整名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>默认 6 个系统快捷方式（由 <see cref="SystemShortcutCatalog"/> 单一数据源生成）。</summary>
    public static List<ShortcutDefinition> CreateDefaults()
    {
        return SystemShortcutCatalog.CreateDefaults();
    }
}
