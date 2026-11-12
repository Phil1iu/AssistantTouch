using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AssistantTouch;

/// <summary>
/// 执行快捷方式：系统动作（Win+D / Win+Shift+S / Ctrl+Shift+Esc / 设置 / 资源管理器 / 锁定）或本地程序。
/// </summary>
/// <remarks>所有失败静默记录到 Debug，不弹错误、不影响调用方。</remarks>
internal static class ShortcutActions
{
    private const byte VkLeftWindows = 0x5B;
    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkEscape = 0x1B;
    private const byte VkD = 0x44;
    private const byte VkS = 0x53;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    public static void Execute(ShortcutDefinition shortcut)
    {
        if (shortcut.Kind == ShortcutKind.App)
        {
            TryStart(shortcut.Path);
            return;
        }

        switch (shortcut.Action)
        {
            case "showDesktop":
                SendKeyChord(VkLeftWindows, VkD);
                break;
            case "screenshot":
                SendKeyChord(VkLeftWindows, VkShift, VkS);
                break;
            case "taskManager":
                SendKeyChord(VkControl, VkShift, VkEscape);
                break;
            case "settings":
                TryStart("ms-settings:");
                break;
            case "explorer":
                TryStart("explorer.exe");
                break;
            case "lock":
                LockComputer();
                break;
            default:
                Debug.WriteLine($"Unknown shortcut action: {shortcut.Action}");
                break;
        }
    }

    /// <summary>依次按下多个键后逆序释放。</summary>
    private static void SendKeyChord(params byte[] virtualKeys)
    {
        try
        {
            foreach (byte key in virtualKeys)
            {
                keybd_event(key, 0, 0, IntPtr.Zero);
            }

            for (int i = virtualKeys.Length - 1; i >= 0; i--)
            {
                keybd_event(virtualKeys[i], 0, 2, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shortcut key chord failed: {ex.Message}");
        }
    }

    private static void TryStart(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shortcut '{target}' failed: {ex.Message}");
        }
    }

    private static void LockComputer()
    {
        try
        {
            if (!LockWorkStation())
            {
                Debug.WriteLine("LockWorkStation returned false.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LockWorkStation failed: {ex.Message}");
        }
    }
}
