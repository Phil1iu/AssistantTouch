using System.Windows.Input;

namespace AssistantTouch;

/// <summary>径向菜单的键盘选择状态机（纯函数，便于单测）。</summary>
internal static class KeyboardNavigation
{
    /// <summary>
    /// 返回方向键/Home/End 的目标索引；非导航键返回 null。
    /// ←/↑ 上一个、→/↓ 下一个，首尾环绕；0 项返回 null，1 项始终返回 0。
    /// </summary>
    public static int? Next(int current, int count, Key key)
    {
        if (count <= 0)
        {
            return null;
        }

        int normalized = current < 0 || current >= count ? 0 : current;

        return key switch
        {
            Key.Left or Key.Up => (normalized - 1 + count) % count,
            Key.Right or Key.Down => (normalized + 1) % count,
            Key.Home => 0,
            Key.End => count - 1,
            _ => null
        };
    }
}
