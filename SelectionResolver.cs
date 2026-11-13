namespace AssistantTouch;

/// <summary>
/// 重排/删除/重命名后重新定位键盘选中项（纯函数，便于单测）。
/// 槽位列表中的 <c>null</c> 表示「+」圆；快捷项按对象引用匹配。
/// </summary>
internal static class SelectionResolver
{
    /// <summary>
    /// - 无槽位 → -1；
    /// - 选中态未激活 → 仅把旧索引 clamp 进新槽位范围（视觉上仍不显示焦点框）；
    /// - 选中快捷项 → 按引用找到新位置；找不到（被删除/重命名替换）→ clamp 旧索引；
    /// - 选中「+」→ 跟随 null 槽位；没有「+」时 clamp。
    /// </summary>
    public static int Resolve(
        bool isSelectionActive,
        int previousIndex,
        ShortcutDefinition? previousItem,
        IReadOnlyList<ShortcutDefinition?> slots)
    {
        if (slots.Count == 0)
        {
            return -1;
        }

        if (!isSelectionActive)
        {
            return Math.Clamp(previousIndex, 0, slots.Count - 1);
        }

        if (previousItem is not null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (ReferenceEquals(slots[i], previousItem))
                {
                    return i;
                }
            }

            return Math.Clamp(previousIndex, 0, slots.Count - 1);
        }

        // 选中的是「+」：跟随 null 槽位。
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is null)
            {
                return i;
            }
        }

        return Math.Clamp(previousIndex, 0, slots.Count - 1);
    }
}
