using System.Runtime.InteropServices;
using System.Windows;

namespace AssistantTouch;

/// <summary>
/// 取光标屏幕坐标（设备像素）的探针。只给贴边悬停的几何判定用；
/// 不能用 <see cref="System.Windows.Input.Mouse.GetPosition"/>：没有新输入消息时它拿到的是上一条消息的缓存位置，
/// 而贴边收起/展开期间窗口会从光标下方滑走，缓存位置会给出错误结论。
/// </summary>
internal static class CursorProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    /// <summary>光标在虚拟屏幕上的位置（设备像素）。取不到时返回 false，调用方回退到 <c>IsMouseOver</c>。</summary>
    public static bool TryGetScreenPosition(out Point point)
    {
        if (GetCursorPos(out NativePoint native))
        {
            point = new Point(native.X, native.Y);
            return true;
        }

        point = default;
        return false;
    }
}
