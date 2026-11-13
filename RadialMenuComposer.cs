using System.Windows;

namespace AssistantTouch;

/// <summary>要绘制的圆类型。</summary>
internal enum RadialDrawKind
{
    Shortcut,
    Plus
}

/// <summary>一个待绘制的圆：类型 + 中心 + 快捷项（「+」为 null）。</summary>
internal sealed record RadialDrawItem(RadialDrawKind Kind, Point Center, ShortcutDefinition? Shortcut);

/// <summary>
/// 把布局结果映射为「实际要画的圆」列表（纯函数，便于单测）。
/// UI 层只负责根据列表创建控件，未来即使 RebuildRing 被改坏，测试也能发现。
/// </summary>
internal static class RadialMenuComposer
{
    /// <summary>
    /// - AllPlaced=false：不画任何快捷项，只画可选的一个「+」；
    /// - AllPlaced=true：按快捷项顺序画 Centers[0..n-1]，再画「+」（优先 Centers[n]，否则 fallback）；
    /// - 没有任何合法「+」位置时返回空列表。
    /// </summary>
    public static IReadOnlyList<RadialDrawItem> Build(
        RadialLayoutResult layout,
        IReadOnlyList<ShortcutDefinition> shortcuts,
        Point? plusFallback)
    {
        var items = new List<RadialDrawItem>();

        if (layout.AllPlaced)
        {
            int shortcutCount = Math.Min(shortcuts.Count, layout.Centers.Count);
            for (int i = 0; i < shortcutCount; i++)
            {
                items.Add(new RadialDrawItem(RadialDrawKind.Shortcut, layout.Centers[i], shortcuts[i]));
            }
        }

        Point? plusCenter = layout.AllPlaced && layout.Centers.Count > shortcuts.Count
            ? layout.Centers[shortcuts.Count]
            : plusFallback;

        if (plusCenter is { } center)
        {
            items.Add(new RadialDrawItem(RadialDrawKind.Plus, center, null));
        }

        return items;
    }
}
