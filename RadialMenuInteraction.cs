using System.Windows;

namespace AssistantTouch;

/// <summary>径向菜单的交互判定纯函数（便于单测）。</summary>
internal static class RadialMenuInteraction
{
    /// <summary>背景右键菜单的响应半径（DIU）：覆盖四角双弧最大半径 167 + 小圆 22 并留余量。</summary>
    public const double BackgroundMenuRadius = 240;

    /// <summary>鼠标位置距球心 ≤ radius 时才弹出背景添加/恢复菜单；远处右键不响应。</summary>
    public static bool ShouldShowBackgroundMenu(
        Point ballCenter,
        Point pointer,
        double radius = BackgroundMenuRadius)
    {
        if (radius < 0)
        {
            return false;
        }

        return (pointer - ballCenter).Length <= radius;
    }
}
